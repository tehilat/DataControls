from __future__ import annotations

import re
from pathlib import Path

import pytest
from pydantic import ValidationError

from definition_validator import MAX_DEPTH, MAX_RULES, CatalogIndex, validate_definition
from schemas import Catalog, ControlDefinition

SAMPLE_CONTROLS = Path(__file__).resolve().parents[2] / "database" / "04_sample_controls.sql"


def _rule(field, op, compare_to=None):
    r = {"type": "rule", "field": field, "operator": op}
    if compare_to is not None:
        r["compareTo"] = compare_to
    return r


def _def(condition, root="Delivery"):
    return ControlDefinition.model_validate({"version": 1, "rootEntity": root, "condition": condition})


def _validate(catalog: Catalog, condition, root="Delivery"):
    return validate_definition(_def(condition, root), CatalogIndex(catalog))


def _messages(issues):
    return " | ".join(i.message for i in issues)


# ------------------------------------------------------------------ stage A compatibility

def _stage_a_definitions() -> list[str]:
    sql = SAMPLE_CONTROLS.read_text(encoding="utf-8-sig")
    return re.findall(r"N'(\{\"version\".*?\})',\s*\r?\nN'", sql, flags=re.S)


@pytest.mark.skipif(not SAMPLE_CONTROLS.exists(), reason="database scripts not available")
def test_all_14_stage_a_sample_controls_are_valid(catalog):
    definitions = _stage_a_definitions()
    assert len(definitions) == 14
    for text in definitions:
        definition = ControlDefinition.model_validate_json(text)
        result, issues = validate_definition(definition, CatalogIndex(catalog))
        assert issues == [], text
        assert result is not None


# ------------------------------------------------------------------ valid cases

def test_field_compare_across_relation(catalog):
    result, issues = _validate(catalog, _rule("PackageWeightKg", "gt", {"kind": "field", "field": "Drone.MaxPayloadKg"}))
    assert issues == []
    assert result.condition.compare_to.field == "Drone.MaxPayloadKg"


def test_keys_are_case_insensitive_and_canonicalized(catalog):
    result, issues = _validate(
        catalog,
        _rule("drone.hub.isactive", "EQ", {"kind": "value", "value": False}),
        root="delivery",
    )
    assert issues == []
    assert result.root_entity == "Delivery"
    assert result.condition.field == "Drone.Hub.IsActive"
    assert result.condition.operator == "eq"


def test_enum_value_is_canonicalized(catalog):
    result, issues = _validate(catalog, _rule("Status", "eq", {"kind": "value", "value": "intransit"}))
    assert issues == []
    assert result.condition.compare_to.value == "InTransit"


def test_date_and_datetime_are_comparable(catalog):
    _, issues = _validate(catalog, _rule("Pilot.LicenseExpiry", "lt", {"kind": "field", "field": "DispatchedAt"}))
    assert issues == []


def test_nested_groups(catalog):
    condition = {"type": "group", "logic": "OR", "children": [
        {"type": "group", "logic": "AND", "children": [
            _rule("Status", "eq", {"kind": "value", "value": "Delivered"}),
            _rule("DeliveredAt", "isEmpty"),
        ]},
        {"type": "group", "logic": "AND", "children": [
            _rule("Status", "eq", {"kind": "value", "value": "Failed"}),
            _rule("DeliveredAt", "isNotEmpty"),
        ]},
    ]}
    result, issues = _validate(catalog, condition)
    assert issues == []
    assert result.model_dump(by_alias=True)["condition"]["children"][0]["children"][1] == {
        "type": "rule", "field": "DeliveredAt", "operator": "isEmpty"
    }


def test_valid_literal_values(catalog):
    cases = [
        _rule("DistanceKm", "gte", {"kind": "value", "value": 20.5}),
        _rule("Drone.Model", "contains", {"kind": "value", "value": "X"}),
        _rule("Drone.NextMaintenanceDue", "lt", {"kind": "value", "value": "2026-01-31"}),
        _rule("DispatchedAt", "gt", {"kind": "value", "value": "2026-01-31T10:00:00"}),
        _rule("DispatchedAt", "gt", {"kind": "now"}),
        _rule("Drone.LastMaintenance", "lt", {"kind": "today", "offsetDays": -180}),
    ]
    for c in cases:
        _, issues = _validate(catalog, c)
        assert issues == [], c


# ------------------------------------------------------------------ catalog whitelist

def test_unknown_root_entity(catalog):
    result, issues = _validate(catalog, _rule("Phone", "isEmpty"), root="Employees")
    assert result is None
    assert issues[0].path == "rootEntity"


def test_unknown_field_path(catalog):
    _, issues = _validate(catalog, _rule("Salary", "gt", {"kind": "value", "value": 1}))
    assert "Salary" in _messages(issues)
    assert issues[0].path == "condition.field"


def test_field_of_other_root_is_rejected(catalog):
    # Phone belongs to Customer; from Delivery it is reachable only as Customer.Phone
    _, issues = _validate(catalog, _rule("Phone", "isEmpty"))
    assert issues


def test_relation_not_navigable_from_root(catalog):
    # Drone has no relation to Pilot
    _, issues = _validate(catalog, _rule("Pilot.IsActive", "eq", {"kind": "value", "value": True}), root="Drone")
    assert issues


def test_unknown_operator(catalog):
    _, issues = _validate(catalog, _rule("DistanceKm", "between", {"kind": "value", "value": 1}))
    assert issues[0].path == "condition.operator"


@pytest.mark.parametrize("field,op", [("Drone.Model", "gt"), ("DistanceKm", "contains"),
                                       ("Status", "gt"), ("Drone.Hub.IsActive", "isEmpty")])
def test_operator_not_allowed_for_type(catalog, field, op):
    compare_to = None if op == "isEmpty" else {"kind": "value", "value": "1"}
    _, issues = _validate(catalog, _rule(field, op, compare_to))
    assert "אינו מתאים" in _messages(issues)


@pytest.mark.parametrize("path", [
    "PackageWeightKg; DROP TABLE ops.Deliveries --",
    "PackageWeightKg]",
    "[PackageWeightKg]",
    "Drone..MaxPayloadKg",
    "a.b.c.d.e.f",
    "ops.Deliveries.PackageWeightKg",
])
def test_injection_like_paths_are_rejected(catalog, path):
    _, issues = _validate(catalog, _rule(path, "isEmpty"))
    assert issues


def test_injection_in_operator_is_rejected(catalog):
    _, issues = _validate(catalog, _rule("DistanceKm", "= 1 OR 1=1 --", {"kind": "value", "value": 1}))
    assert issues


# ------------------------------------------------------------------ operands

def test_missing_compare_to(catalog):
    _, issues = _validate(catalog, _rule("DistanceKm", "gt"))
    assert issues[0].path == "condition.compareTo"


def test_compare_to_not_allowed_for_is_empty(catalog):
    _, issues = _validate(catalog, _rule("DeliveredAt", "isEmpty", {"kind": "value", "value": "x"}))
    assert "אינו מקבל ערך" in _messages(issues)


def test_field_compare_not_allowed_for_enum(catalog):
    _, issues = _validate(catalog, _rule("Status", "eq", {"kind": "field", "field": "Drone.Status"}))
    assert "לשדה אחר" in _messages(issues)


def test_field_compare_incompatible_types(catalog):
    _, issues = _validate(catalog, _rule("DistanceKm", "gt", {"kind": "field", "field": "DispatchedAt"}))
    assert "לא ניתן להשוות" in _messages(issues)


def test_today_on_non_date_field(catalog):
    _, issues = _validate(catalog, _rule("DistanceKm", "gt", {"kind": "today", "offsetDays": 0}))
    assert issues


def test_offset_days_out_of_range(catalog):
    _, issues = _validate(catalog, _rule("DispatchedAt", "lt", {"kind": "today", "offsetDays": 99_999}))
    assert issues[0].path == "condition.compareTo.offsetDays"


@pytest.mark.parametrize("field,value", [
    ("DistanceKm", "20"), ("DistanceKm", True), ("Drone.Hub.IsActive", "false"), ("Drone.Hub.IsActive", 0),
    ("Status", "Lost"), ("Status", "נמסר"), ("Drone.NextMaintenanceDue", "31/01/2026"),
    ("Drone.NextMaintenanceDue", "2026-02-30"), ("DispatchedAt", "yesterday"), ("Drone.Model", 5),
    ("Drone.Model", "x" * 4001),
])
def test_invalid_literal_values(catalog, field, value):
    _, issues = _validate(catalog, _rule(field, "eq", {"kind": "value", "value": value}))
    assert issues, (field, value)


def test_all_errors_are_collected(catalog):
    condition = {"type": "group", "logic": "AND", "children": [
        _rule("Nope", "isEmpty"),
        _rule("DistanceKm", "gt"),
    ]}
    _, issues = _validate(catalog, condition)
    assert [i.path for i in issues] == ["condition.children[0].field", "condition.children[1].compareTo"]


# ------------------------------------------------------------------ limits

def test_depth_limit(catalog):
    node = _rule("DeliveredAt", "isEmpty")
    for _ in range(MAX_DEPTH):
        node = {"type": "group", "logic": "AND", "children": [node]}
    _, issues = _validate(catalog, node)
    assert "עומק" in _messages(issues)


def test_rule_limit(catalog):
    condition = {"type": "group", "logic": "OR", "children": [_rule("DeliveredAt", "isEmpty")] * (MAX_RULES + 5)}
    _, issues = _validate(catalog, condition)
    assert len(issues) == 1
    assert "יותר מדי תנאים" in issues[0].message


# ------------------------------------------------------------------ schema (DefinitionJson shape)

@pytest.mark.parametrize("payload", [
    {"version": 2, "rootEntity": "Delivery", "condition": _rule("DeliveredAt", "isEmpty")},
    {"version": 1, "rootEntity": "Delivery"},
    {"version": 1, "rootEntity": "Delivery", "condition": {"type": "group", "logic": "XOR", "children": []}},
    {"version": 1, "rootEntity": "Delivery", "condition": {"type": "group", "logic": "AND", "children": []}},
    {"version": 1, "rootEntity": "Delivery", "condition": {"type": "sql", "text": "SELECT 1"}},
    {"version": 1, "rootEntity": "Delivery", "condition": _rule("DistanceKm", "gt", {"kind": "sql", "value": "1"})},
    {"version": 1, "rootEntity": "Delivery", "condition": {**_rule("DeliveredAt", "isEmpty"), "sql": "x"}},
])
def test_definition_schema_rejects_malformed(payload):
    with pytest.raises(ValidationError):
        ControlDefinition.model_validate(payload)


def test_serialization_matches_definition_json():
    d = _def(_rule("Drone.NextMaintenanceDue", "lt", {"kind": "today", "offsetDays": 0}), root="Delivery")
    assert d.model_dump(by_alias=True) == {
        "version": 1,
        "rootEntity": "Delivery",
        "condition": {"type": "rule", "field": "Drone.NextMaintenanceDue", "operator": "lt",
                      "compareTo": {"kind": "today", "offsetDays": 0}},
    }


# ------------------------------------------------------------------ catalog consistency

def test_catalog_rejects_duplicates_and_unknown_operators(catalog_json):
    dup = {**catalog_json, "entities": catalog_json["entities"] + catalog_json["entities"][:1]}
    with pytest.raises(ValidationError):
        Catalog.model_validate(dup)

    bad_op = {**catalog_json, "typeOperators": catalog_json["typeOperators"] + [
        {"dataType": "number", "operator": "between", "allowFieldCompare": False}]}
    with pytest.raises(ValidationError):
        Catalog.model_validate(bad_op)


def test_catalog_rejects_enum_without_options(catalog_json):
    field = next(f for f in catalog_json["entities"][0]["fields"] if f["dataType"] == "enum")
    field["options"] = []
    with pytest.raises(ValidationError):
        Catalog.model_validate(catalog_json)
