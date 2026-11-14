from __future__ import annotations

import pytest

from main import app
from providers import AiProvider, ProviderError, ProviderRequest, ProviderResult
from service import ControlGenerationService

EXPECTED = {
    "משקל מעל כושר נשיאה": {
        "version": 1, "rootEntity": "Delivery",
        "condition": {"type": "rule", "field": "PackageWeightKg", "operator": "gt",
                      "compareTo": {"kind": "field", "field": "Drone.MaxPayloadKg"}},
    },
    "רחפן פעיל במוקד סגור": {
        "version": 1, "rootEntity": "Drone",
        "condition": {"type": "group", "logic": "AND", "children": [
            {"type": "rule", "field": "Status", "operator": "eq", "compareTo": {"kind": "value", "value": "Active"}},
            {"type": "rule", "field": "Hub.IsActive", "operator": "eq", "compareTo": {"kind": "value", "value": False}},
        ]},
    },
    "לקוח בלי טלפון ובלי אימייל": {
        "version": 1, "rootEntity": "Customer",
        "condition": {"type": "group", "logic": "AND", "children": [
            {"type": "rule", "field": "Phone", "operator": "isEmpty"},
            {"type": "rule", "field": "Email", "operator": "isEmpty"},
        ]},
    },
    "משלוח באיחור": {
        "version": 1, "rootEntity": "Delivery",
        "condition": {"type": "group", "logic": "AND", "children": [
            {"type": "rule", "field": "Status", "operator": "eq", "compareTo": {"kind": "value", "value": "InTransit"}},
            {"type": "rule", "field": "DispatchedAt", "operator": "lt", "compareTo": {"kind": "today", "offsetDays": -1}},
        ]},
    },
    "מפעיל עם רישיון שפג": {
        "version": 1, "rootEntity": "Pilot",
        "condition": {"type": "rule", "field": "LicenseExpiry", "operator": "lt",
                      "compareTo": {"kind": "today", "offsetDays": 0}},
    },
}


class _FakeProvider(AiProvider):
    name = "fake"

    def __init__(self, result: ProviderResult | Exception) -> None:
        self.result = result
        self.seen: ProviderRequest | None = None

    async def generate(self, request: ProviderRequest) -> ProviderResult:
        self.seen = request
        if isinstance(self.result, Exception):
            raise self.result
        return self.result


@pytest.fixture
def use_provider():
    original = app.state.service

    def _use(provider: AiProvider) -> AiProvider:
        app.state.service = ControlGenerationService(provider)
        return provider

    yield _use
    app.state.service = original


def test_health(client):
    assert client.get("/health").json() == {"status": "ok", "provider": "mock"}


@pytest.mark.parametrize("text", list(EXPECTED))
def test_generate_control_for_the_five_sample_requests(client, catalog_json, text):
    response = client.post("/generate-control", json={"text": text, "catalog": catalog_json})
    assert response.status_code == 200
    body = response.json()
    assert body["definition"] == EXPECTED[text]
    assert body["explanation"]
    assert 0 < body["confidence"] <= 1
    assert isinstance(body["warnings"], list)


def test_late_delivery_reports_its_assumptions(client, catalog_json):
    body = client.post("/generate-control", json={"text": "משלוח באיחור", "catalog": catalog_json}).json()
    assert body["confidence"] < 0.8
    assert any("פורש" in w for w in body["warnings"])


def test_unrecognized_request(client, catalog_json):
    response = client.post("/generate-control", json={"text": "תביא לי פיצה", "catalog": catalog_json})
    assert response.status_code == 200
    body = response.json()
    assert body["definition"] is None
    assert body["confidence"] == 0
    assert body["warnings"]


def test_mock_answer_not_in_catalog_is_rejected(client, catalog_json):
    """A catalog without Pilot → the canned answer fails validation and is not returned."""
    catalog_json["entities"] = [e for e in catalog_json["entities"] if e["key"] != "Pilot"]
    body = client.post("/generate-control", json={"text": "מפעיל עם רישיון שפג", "catalog": catalog_json}).json()
    assert body["definition"] is None
    assert body["confidence"] == 0
    assert any("Pilot" in w for w in body["warnings"])


def test_provider_answer_with_invalid_schema(client, catalog_json, use_provider):
    use_provider(_FakeProvider(ProviderResult(
        definition={"version": 1, "rootEntity": "Delivery", "condition": {"type": "sql", "text": "DROP TABLE x"}},
        explanation="x", confidence=0.99)))
    body = client.post("/generate-control", json={"text": "בקשה", "catalog": catalog_json}).json()
    assert body["definition"] is None
    assert body["confidence"] == 0
    assert body["warnings"]


def test_provider_answer_with_unknown_field(client, catalog_json, use_provider):
    use_provider(_FakeProvider(ProviderResult(
        definition={"version": 1, "rootEntity": "Delivery",
                    "condition": {"type": "rule", "field": "Price", "operator": "gt",
                                  "compareTo": {"kind": "value", "value": 1}}},
        explanation="x", confidence=0.99, warnings=["הנחה"])))
    body = client.post("/generate-control", json={"text": "בקשה", "catalog": catalog_json}).json()
    assert body["definition"] is None
    assert any("Price" in w for w in body["warnings"])
    assert "הנחה" in body["warnings"]


def test_provider_answer_is_canonicalized_and_confidence_clamped(client, catalog_json, use_provider):
    use_provider(_FakeProvider(ProviderResult(
        definition={"version": 1, "rootEntity": "delivery",
                    "condition": {"type": "rule", "field": "status", "operator": "EQ",
                                  "compareTo": {"kind": "value", "value": "failed"}}},
        explanation="x", confidence=7)))
    body = client.post("/generate-control", json={"text": "בקשה", "catalog": catalog_json}).json()
    assert body["definition"] == {"version": 1, "rootEntity": "Delivery",
                                  "condition": {"type": "rule", "field": "Status", "operator": "eq",
                                                "compareTo": {"kind": "value", "value": "Failed"}}}
    assert body["confidence"] == 1


def test_provider_receives_the_full_prompt(client, catalog_json, use_provider):
    fake = use_provider(_FakeProvider(ProviderResult(definition=None, explanation="x", confidence=0)))
    client.post("/generate-control", json={"text": "  משלוח באיחור  ", "catalog": catalog_json})
    assert fake.seen.text == "משלוח באיחור"
    assert "Drone.MaxPayloadKg" in fake.seen.prompt.system
    assert "משלוח באיחור" in fake.seen.prompt.user


def test_provider_failure_returns_502(client, catalog_json, use_provider):
    use_provider(_FakeProvider(ProviderError("timeout")))
    response = client.post("/generate-control", json={"text": "בקשה", "catalog": catalog_json})
    assert response.status_code == 502
    assert response.headers["content-type"].startswith("application/problem+json")


@pytest.mark.parametrize("body", [
    {"text": "   ", "catalog": "CATALOG"},
    {"text": "x" * 2001, "catalog": "CATALOG"},
    {"text": "משלוח באיחור"},
    {"text": "משלוח באיחור", "catalog": {"entities": [], "operators": [], "typeOperators": []}},
])
def test_invalid_requests_return_problem_details(client, catalog_json, body):
    if body.get("catalog") == "CATALOG":
        body = {**body, "catalog": catalog_json}
    response = client.post("/generate-control", json=body)
    assert response.status_code == 422
    assert response.headers["content-type"].startswith("application/problem+json")
    assert response.json()["errors"]


def test_prompt_preview(client, catalog_json):
    response = client.post("/prompt-preview", json={"text": "משלוח באיחור", "catalog": catalog_json})
    assert response.status_code == 200
    body = response.json()
    assert body["provider"] == "mock"
    assert "Drone.Hub.IsActive" in body["system"]
    assert "<request>" in body["user"]
    assert "definition" in body["responseSchema"]["properties"]
