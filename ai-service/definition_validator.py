"""Validates a ControlDefinition against the business catalog.

Every key a definition uses (root entity, field paths, operators, enum values) must come from the
catalog the .NET API sent. Lookups are case-insensitive (as in the .NET DefinitionBinder) and the
returned definition is rewritten with the catalog's canonical spelling.

This is a first line of defence that lets the service reject an unusable AI answer early and explain
why; the .NET DefinitionBinder remains the authoritative validator before anything is saved or run.
Error messages are user-facing, hence Hebrew.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field
from datetime import date, datetime

from schemas import (
    Catalog,
    CatalogEntity,
    CatalogField,
    CatalogOperator,
    ControlDefinition,
    FieldOperand,
    GroupNode,
    NowOperand,
    RuleNode,
    TodayOperand,
    TypeOperator,
    ValueOperand,
)

# Same limits as DataControls.Application DefinitionLimits.
MAX_DEPTH = 10
MAX_RULES = 100
MAX_PATH_SEGMENTS = 5
MAX_TEXT_LENGTH = 4000
MAX_OFFSET_DAYS = 36_500

TEMPORAL = {"date", "datetime"}
TYPE_NAMES_HE = {
    "number": "מספר",
    "text": "טקסט",
    "date": "תאריך",
    "datetime": "תאריך ושעה",
    "boolean": "כן/לא",
    "enum": "רשימת ערכים",
}


@dataclass(frozen=True)
class ValidationIssue:
    path: str
    message: str


class CatalogIndex:
    """Case-insensitive lookups over a Catalog."""

    def __init__(self, catalog: Catalog) -> None:
        self.catalog = catalog
        self._entities = {e.key.casefold(): e for e in catalog.entities}
        self._fields = {
            e.key.casefold(): {f.path.casefold(): f for f in e.fields} for e in catalog.entities
        }
        self._operators = {o.key.casefold(): o for o in catalog.operators}
        self._type_ops = {(t.data_type, t.operator.casefold()): t for t in catalog.type_operators}

    def entity(self, key: str) -> CatalogEntity | None:
        return self._entities.get(key.casefold())

    def field(self, entity_key: str, path: str) -> CatalogField | None:
        return self._fields.get(entity_key.casefold(), {}).get(path.casefold())

    def operator(self, key: str) -> CatalogOperator | None:
        return self._operators.get(key.casefold())

    def type_operator(self, data_type: str, operator_key: str) -> TypeOperator | None:
        return self._type_ops.get((data_type, operator_key.casefold()))

    def operators_for(self, data_type: str) -> list[TypeOperator]:
        return [t for t in self.catalog.type_operators if t.data_type == data_type]


@dataclass
class _Ctx:
    index: CatalogIndex
    root: CatalogEntity
    rules: int = 0
    issues: list[ValidationIssue] = field(default_factory=list)

    def error(self, path: str, message: str) -> None:
        self.issues.append(ValidationIssue(path, message))


def validate_definition(
    definition: ControlDefinition, index: CatalogIndex
) -> tuple[ControlDefinition | None, list[ValidationIssue]]:
    """Returns (canonical definition, []) when valid, otherwise (None, issues)."""
    root = index.entity(definition.root_entity)
    if root is None:
        return None, [ValidationIssue("rootEntity", f'הישות "{_clip(definition.root_entity)}" אינה קיימת בקטלוג.')]

    ctx = _Ctx(index=index, root=root)
    condition = _node(definition.condition, "condition", 1, ctx)
    if ctx.issues or condition is None:
        return None, ctx.issues
    return ControlDefinition(version=1, root_entity=root.key, condition=condition), []


def _node(node: RuleNode | GroupNode, path: str, depth: int, ctx: _Ctx) -> RuleNode | GroupNode | None:
    if depth > MAX_DEPTH:
        ctx.error(path, f"הבקרה מקוננת לעומק רב מדי (מותר עד {MAX_DEPTH} רמות).")
        return None
    if isinstance(node, GroupNode):
        children = [_node(c, f"{path}.children[{i}]", depth + 1, ctx) for i, c in enumerate(node.children)]
        if any(c is None for c in children):
            return None
        return GroupNode(type="group", logic=node.logic, children=children)
    return _rule(node, path, ctx)


def _rule(rule: RuleNode, path: str, ctx: _Ctx) -> RuleNode | None:
    ctx.rules += 1
    if ctx.rules > MAX_RULES:
        if ctx.rules == MAX_RULES + 1:
            ctx.error(path, f"יותר מדי תנאים בבקרה (מותר עד {MAX_RULES}).")
        return None

    field_ = _resolve(rule.field, f"{path}.field", ctx)
    op = ctx.index.operator(rule.operator)
    if op is None:
        ctx.error(f"{path}.operator", f'אופרטור לא מוכר: "{_clip(rule.operator)}".')
        return None
    if field_ is None:
        return None

    type_op = ctx.index.type_operator(field_.data_type, op.key)
    if type_op is None:
        ctx.error(
            f"{path}.operator",
            f'האופרטור "{op.display_name}" אינו מתאים לשדה "{field_.display_name}" '
            f"(סוג: {TYPE_NAMES_HE[field_.data_type]}).",
        )
        return None
    op_display = type_op.display_name or op.display_name

    if op.requires_value and rule.compare_to is None:
        ctx.error(f"{path}.compareTo", f'חסר ערך להשוואה בתנאי על "{field_.display_name}".')
        return None
    if not op.requires_value and rule.compare_to is not None:
        ctx.error(f"{path}.compareTo", f'האופרטור "{op_display}" אינו מקבל ערך להשוואה.')
        return None

    operand = None
    if rule.compare_to is not None:
        operand = _operand(rule.compare_to, field_, type_op, op_display, f"{path}.compareTo", ctx)
        if operand is None:
            return None

    return RuleNode(type="rule", field=field_.path, operator=op.key, compare_to=operand)


def _operand(operand, left: CatalogField, type_op: TypeOperator, op_display: str, path: str, ctx: _Ctx):
    if isinstance(operand, ValueOperand):
        value = _value(operand.value, left, f"{path}.value", ctx)
        return None if value is None else ValueOperand(kind="value", value=value)

    if isinstance(operand, FieldOperand):
        if not type_op.allow_field_compare:
            ctx.error(path, f'לא ניתן להשוות את "{left.display_name}" לשדה אחר באופרטור "{op_display}".')
            return None
        right = _resolve(operand.field, f"{path}.field", ctx)
        if right is None:
            return None
        if not (left.data_type == right.data_type or {left.data_type, right.data_type} <= TEMPORAL):
            ctx.error(
                path,
                f'לא ניתן להשוות את "{left.display_name}" ({TYPE_NAMES_HE[left.data_type]}) '
                f'ל"{right.display_name}" ({TYPE_NAMES_HE[right.data_type]}).',
            )
            return None
        return FieldOperand(kind="field", field=right.path)

    if isinstance(operand, (TodayOperand, NowOperand)):
        label = "היום" if isinstance(operand, TodayOperand) else "עכשיו"
        if left.data_type not in TEMPORAL:
            ctx.error(path, f'השוואה ל"{label}" אפשרית רק בשדה תאריך, ו"{left.display_name}" אינו כזה.')
            return None
        if isinstance(operand, TodayOperand) and abs(operand.offset_days) > MAX_OFFSET_DAYS:
            ctx.error(
                f"{path}.offsetDays",
                f"היסט של {operand.offset_days} ימים חורג מהטווח המותר (בין {-MAX_OFFSET_DAYS} ל-{MAX_OFFSET_DAYS}).",
            )
            return None
        return operand

    ctx.error(path, "סוג ערך השוואה לא מוכר.")
    return None


def _value(value, left: CatalogField, path: str, ctx: _Ctx):
    name = left.display_name
    match left.data_type:
        case "number":
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
                ctx.error(path, f'הערך של "{name}" חייב להיות מספר.')
                return None
            return value
        case "boolean":
            if not isinstance(value, bool):
                ctx.error(path, f'הערך של "{name}" חייב להיות כן או לא.')
                return None
            return value
        case "text":
            if not isinstance(value, str):
                ctx.error(path, f'הערך של "{name}" חייב להיות טקסט.')
                return None
            if len(value) > MAX_TEXT_LENGTH:
                ctx.error(path, f'הערך של "{name}" ארוך מדי (עד {MAX_TEXT_LENGTH} תווים).')
                return None
            return value
        case "enum":
            if isinstance(value, str):
                for opt in left.options:
                    if opt.value.casefold() == value.casefold():
                        return opt.value
            allowed = ", ".join(o.display_name for o in left.options)
            ctx.error(path, f'הערך "{_clip(str(value))}" אינו ערך מותר עבור "{name}". ערכים מותרים: {allowed}.')
            return None
        case "date":
            if isinstance(value, str) and len(value) == 10:
                try:
                    date.fromisoformat(value)
                    return value
                except ValueError:
                    pass
            ctx.error(path, f'הערך של "{name}" חייב להיות תאריך בפורמט yyyy-MM-dd.')
            return None
        case "datetime":
            if isinstance(value, str):
                try:
                    datetime.fromisoformat(value)
                    return value
                except ValueError:
                    pass
            ctx.error(path, f'הערך של "{name}" חייב להיות תאריך ושעה (ISO 8601).')
            return None
    ctx.error(path, f'סוג השדה "{name}" אינו נתמך.')
    return None


def _resolve(path_text: str, json_path: str, ctx: _Ctx) -> CatalogField | None:
    segments = path_text.split(".")
    if len(segments) > MAX_PATH_SEGMENTS or any(not s.strip() for s in segments):
        ctx.error(json_path, f'נתיב השדה "{_clip(path_text)}" אינו תקין.')
        return None
    found = ctx.index.field(ctx.root.key, path_text)
    if found is None:
        ctx.error(json_path, f'השדה "{_clip(path_text)}" אינו קיים עבור הישות "{ctx.root.display_name}".')
    return found


def _clip(text: str, limit: int = 60) -> str:
    return text if len(text) <= limit else text[:limit] + "…"
