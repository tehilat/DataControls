from __future__ import annotations

import asyncio

import pytest

from definition_validator import CatalogIndex, validate_definition
from prompt_builder import PromptBuilder
from providers import MockProvider, ProviderRequest
from providers.mock_provider import load_scenarios, normalize
from schemas import ControlDefinition


@pytest.fixture(scope="module")
def mock() -> MockProvider:
    return MockProvider()


@pytest.mark.parametrize("text,scenario", [
    ("משקל מעל כושר נשיאה", "weight-over-payload"),
    ("מצא משלוחים שבהם משקל החבילה גבוה מכושר הנשיאה של הרחפן", "weight-over-payload"),
    ("רחפן פעיל במוקד סגור", "active-drone-closed-hub"),
    ("רחפנים בשירות שמשויכים למוקד שאינו פעיל", "active-drone-closed-hub"),
    ("לקוח בלי טלפון ובלי אימייל", "customer-without-contact"),
    ('לקוחות ללא טלפון וללא דוא"ל', "customer-without-contact"),
    ("משלוח באיחור", "late-delivery"),
    ("אילו חבילות מתעכבות?", "late-delivery"),
    ("מפעיל עם רישיון שפג", "pilot-expired-license"),
    ("טייסים שהרשיון שלהם לא בתוקף", "pilot-expired-license"),
    ("משלוחים שבוצעו ע\"י מפעיל עם רישיון שפג", "delivery-by-expired-license"),
    ("משלוחים עם משקל שלילי", "negative-weight"),
    ("מַפְעִיל עִם רִישָׁיוֹן שֶׁפָּג", "pilot-expired-license"),  # with niqqud
])
def test_recognizes_hebrew_requests(mock, text, scenario):
    matched = mock.match(text)
    assert matched is not None, text
    assert matched.id == scenario


@pytest.mark.parametrize("text", ["מה מזג האוויר מחר?", "עובדים שגילם מתחת ל-18", "רחפן", "שלום"])
def test_unknown_request_returns_warning_without_definition(mock, catalog, text):
    result = asyncio.run(mock.generate(ProviderRequest(text, catalog, PromptBuilder().build(text, catalog))))
    assert result.definition is None
    assert result.confidence == 0
    assert result.warnings


def test_every_scenario_is_valid_against_the_catalog(catalog):
    index = CatalogIndex(catalog)
    scenarios = load_scenarios()
    assert len(scenarios) >= 5
    assert len({s.id for s in scenarios}) == len(scenarios)
    for s in scenarios:
        definition = ControlDefinition.model_validate(s.definition)
        _, issues = validate_definition(definition, index)
        assert issues == [], (s.id, issues)
        assert 0 < s.confidence <= 1
        assert s.explanation


def test_each_scenario_matches_its_own_example(mock):
    for s in mock.scenarios:
        assert mock.match(s.example).id == s.id


def test_answers_are_copies(mock, catalog):
    request = ProviderRequest("משקל מעל כושר נשיאה", catalog, PromptBuilder().build("x", catalog))
    first = asyncio.run(mock.generate(request))
    first.definition["rootEntity"] = "Tampered"
    second = asyncio.run(mock.generate(request))
    assert second.definition["rootEntity"] == "Delivery"


def test_normalize():
    assert normalize('  דוא"ל   ו״ל  ') == "דואל ול"
    assert normalize("רחפן") in normalize("רחפנים")
    assert normalize("שָׁלוֹם EMAIL") == normalize("שלום email") == "שלומ email"
