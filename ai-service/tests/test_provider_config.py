"""A missing API key or an unknown AI_PROVIDER must not crash the service (stage B is optional)."""
from __future__ import annotations

import pytest

import providers
from main import app
from providers import AiProvider, ProviderConfigError, ProviderRequest, ProviderResult, create_provider


class KeyedProvider(AiProvider):
    """Stands for a real model provider that needs an API key."""

    name = "keyed"
    required_env = ("KEYED_API_KEY",)

    async def generate(self, request: ProviderRequest) -> ProviderResult:  # pragma: no cover - never reached
        raise NotImplementedError


@pytest.fixture
def keyed_registry(monkeypatch):
    monkeypatch.setitem(providers._PROVIDERS, KeyedProvider.name, KeyedProvider)


def test_mock_needs_no_api_key():
    assert create_provider("mock").name == "mock"


def test_unknown_provider_is_a_config_error_with_a_hebrew_message():
    with pytest.raises(ProviderConfigError) as ex:
        create_provider("openai")
    assert "openai" in str(ex.value) and "AI_PROVIDER" in str(ex.value)


def test_missing_api_key_is_a_config_error_naming_the_variable(keyed_registry, monkeypatch):
    monkeypatch.delenv("KEYED_API_KEY", raising=False)
    with pytest.raises(ProviderConfigError) as ex:
        create_provider("keyed")
    assert "KEYED_API_KEY" in str(ex.value) and "חסר מפתח API" in str(ex.value)


def test_blank_api_key_counts_as_missing(keyed_registry, monkeypatch):
    monkeypatch.setenv("KEYED_API_KEY", "   ")
    with pytest.raises(ProviderConfigError):
        create_provider("keyed")


def test_api_key_present_creates_the_provider(keyed_registry, monkeypatch):
    monkeypatch.setenv("KEYED_API_KEY", "test-value")
    assert create_provider("keyed").name == "keyed"


@pytest.fixture
def not_configured():
    """The app as it starts when the provider cannot be created."""
    saved = (app.state.service, app.state.config_error, app.state.provider_name)
    app.state.service = None
    app.state.config_error = 'חסר מפתח API לספק ה-AI "keyed": יש להגדיר את משתנה הסביבה KEYED_API_KEY ולהפעיל מחדש את השירות.'
    app.state.provider_name = "keyed"
    yield app.state.config_error
    app.state.service, app.state.config_error, app.state.provider_name = saved


def test_health_reports_not_configured_with_the_reason(client, not_configured):
    response = client.get("/health")
    assert response.status_code == 503
    assert response.json() == {"status": "not_configured", "provider": "keyed", "detail": not_configured}


@pytest.mark.parametrize("path", ["/generate-control", "/prompt-preview"])
def test_endpoints_answer_503_problem_with_the_reason(client, catalog_json, not_configured, path):
    response = client.post(path, json={"text": "רחפן פעיל במוקד סגור", "catalog": catalog_json})
    assert response.status_code == 503
    assert response.headers["content-type"].startswith("application/problem+json")
    body = response.json()
    assert body["title"] == "שירות ה-AI אינו מוגדר"
    assert body["detail"] == not_configured


def test_health_is_ok_when_configured(client):
    assert client.get("/health").json() == {"status": "ok", "provider": "mock"}
