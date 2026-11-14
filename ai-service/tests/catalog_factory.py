"""Builds the drone-fleet test catalog the way the .NET API is expected to: from database/03_metadata.sql
(entities, fields, relations), flattened into the reachable field paths of each entity."""
from __future__ import annotations

from typing import Any

_ENTITIES = {
    "Delivery": ("משלוח", "משלוחים", [
        ("DeliveryId", "מספר משלוח", "number", None),
        ("PackageWeightKg", "משקל חבילה", "number", 'ק"ג'),
        ("DistanceKm", "מרחק משלוח", "number", 'ק"מ'),
        ("DispatchedAt", "זמן יציאה", "datetime", None),
        ("DeliveredAt", "זמן מסירה", "datetime", None),
        ("Status", "סטטוס משלוח", "enum", None),
    ]),
    "Drone": ("רחפן", "רחפנים", [
        ("DroneId", "מספר רחפן", "number", None),
        ("SerialNumber", "מספר סידורי", "text", None),
        ("Model", "דגם", "text", None),
        ("MaxPayloadKg", "כושר נשיאה", "number", 'ק"ג'),
        ("MaxRangeKm", "טווח טיסה", "number", 'ק"מ'),
        ("LastMaintenance", "תחזוקה אחרונה", "date", None),
        ("NextMaintenanceDue", "מועד תחזוקה הבא", "date", None),
        ("Status", "סטטוס רחפן", "enum", None),
    ]),
    "Hub": ("מוקד", "מוקדים", [
        ("HubId", "מספר מוקד", "number", None),
        ("Name", "שם מוקד", "text", None),
        ("City", "עיר מוקד", "text", None),
        ("IsActive", "מוקד פעיל", "boolean", None),
    ]),
    "Pilot": ("מפעיל", "מפעילים", [
        ("PilotId", "מספר מפעיל", "number", None),
        ("FullName", "שם מפעיל", "text", None),
        ("LicenseNumber", "מספר רישיון", "text", None),
        ("LicenseExpiry", "תוקף רישיון", "date", None),
        ("IsActive", "מפעיל פעיל", "boolean", None),
    ]),
    "Customer": ("לקוח", "לקוחות", [
        ("CustomerId", "מספר לקוח", "number", None),
        ("FullName", "שם לקוח", "text", None),
        ("Phone", "טלפון", "text", None),
        ("Email", "אימייל", "text", None),
        ("City", "עיר", "text", None),
        ("Address", "כתובת", "text", None),
    ]),
}

_OPTIONS = {
    ("Delivery", "Status"): [("Pending", "ממתין"), ("InTransit", "בדרך"), ("Delivered", "נמסר"), ("Failed", "נכשל")],
    ("Drone", "Status"): [("Active", "בשירות"), ("Maintenance", "בתחזוקה"), ("Retired", "הוצא משירות")],
}

# (from entity, relation key, to entity)
_RELATIONS = [("Delivery", "Drone", "Drone"), ("Delivery", "Pilot", "Pilot"),
              ("Delivery", "Customer", "Customer"), ("Drone", "Hub", "Hub")]

_OPERATORS = [("eq", "שווה ל", True), ("neq", "שונה מ", True), ("gt", "גדול מ", True),
              ("gte", "גדול או שווה ל", True), ("lt", "קטן מ", True), ("lte", "קטן או שווה ל", True),
              ("contains", "מכיל", True), ("isEmpty", "ריק", False), ("isNotEmpty", "לא ריק", False)]

_DATE_NAMES = {"eq": "בתאריך", "neq": "לא בתאריך", "gt": "אחרי", "gte": "באותו מועד או אחרי",
               "lt": "לפני", "lte": "באותו מועד או לפני"}
_COMPARE = ["eq", "neq", "gt", "gte", "lt", "lte"]


def _type_operators() -> list[dict[str, Any]]:
    rows = []
    for op in _COMPARE:
        rows.append({"dataType": "number", "operator": op, "allowFieldCompare": True})
    rows += [{"dataType": "number", "operator": op, "allowFieldCompare": False} for op in ("isEmpty", "isNotEmpty")]
    rows += [{"dataType": "text", "operator": op, "allowFieldCompare": True} for op in ("eq", "neq")]
    rows += [{"dataType": "text", "operator": op, "allowFieldCompare": False} for op in ("contains", "isEmpty", "isNotEmpty")]
    for dt in ("date", "datetime"):
        rows += [{"dataType": dt, "operator": op, "displayName": _DATE_NAMES[op], "allowFieldCompare": True}
                 for op in _COMPARE]
        rows += [{"dataType": dt, "operator": op, "allowFieldCompare": False} for op in ("isEmpty", "isNotEmpty")]
    for dt in ("boolean", "enum"):
        rows += [{"dataType": dt, "operator": "eq", "displayName": "הוא", "allowFieldCompare": False},
                 {"dataType": dt, "operator": "neq", "displayName": "אינו", "allowFieldCompare": False}]
    return rows


def _fields(entity: str, prefix: str = "", suffix: str = "", depth: int = 0) -> list[dict[str, Any]]:
    _, _, fields = _ENTITIES[entity]
    out = []
    for key, name, data_type, unit in fields:
        f = {"path": prefix + key, "displayName": name + suffix, "dataType": data_type}
        if unit:
            f["unit"] = unit
        if (entity, key) in _OPTIONS:
            f["options"] = [{"value": v, "displayName": d} for v, d in _OPTIONS[(entity, key)]]
        out.append(f)
    if depth < 3:
        for src, rel, target in _RELATIONS:
            if src == entity:
                out += _fields(target, f"{prefix}{rel}.", f" ({_ENTITIES[target][0]})", depth + 1)
    return out


def build_catalog() -> dict[str, Any]:
    return {
        "version": 1,
        "entities": [
            {"key": key, "displayName": name, "displayNamePlural": plural, "fields": _fields(key)}
            for key, (name, plural, _) in _ENTITIES.items()
        ],
        "operators": [{"key": k, "displayName": d, "requiresValue": r} for k, d, r in _OPERATORS],
        "typeOperators": _type_operators(),
    }
