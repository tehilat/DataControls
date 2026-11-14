"""Wire contracts of the AI service.

* Catalog          — the business catalog the .NET API sends (business terms only, no physical names).
* ControlDefinition — the DefinitionJson of stage A (docs/db-instructions.md), version 1.
* GenerateRequest / GenerateResponse — POST /generate-control.
* PromptPreview    — POST /prompt-preview.

JSON uses camelCase (to match .NET); Python attributes are snake_case via an alias generator.
"""
from __future__ import annotations

from typing import Annotated, Any, Literal, Union

from pydantic import (
    BaseModel,
    ConfigDict,
    Field,
    model_serializer,
    model_validator,
)
from pydantic.alias_generators import to_camel

DataType = Literal["number", "text", "date", "datetime", "boolean", "enum"]

MAX_REQUEST_TEXT = 2000


class _Wire(BaseModel):
    """Base for all wire models: camelCase JSON, snake_case Python."""

    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class _StrictWire(_Wire):
    """Unknown properties are an error (used for the control definition)."""

    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True, extra="forbid")


# --------------------------------------------------------------------------- catalog

class FieldOption(_Wire):
    value: str = Field(min_length=1, description="Stored value, e.g. 'Active'")
    display_name: str = Field(min_length=1, description="Hebrew display name, e.g. 'בשירות'")


class CatalogField(_Wire):
    path: str = Field(
        min_length=1,
        description="Path from the entity: 'FieldKey' or 'RelationKey(.RelationKey)*.FieldKey'",
    )
    display_name: str = Field(min_length=1)
    description: str | None = None
    data_type: DataType
    unit: str | None = None
    options: list[FieldOption] = Field(default_factory=list, description="Allowed values (enum fields)")


class CatalogEntity(_Wire):
    key: str = Field(min_length=1)
    display_name: str = Field(min_length=1)
    display_name_plural: str | None = None
    description: str | None = None
    fields: list[CatalogField] = Field(min_length=1, description="Every field path reachable from this entity")

    @model_validator(mode="after")
    def _unique_paths(self) -> "CatalogEntity":
        seen: set[str] = set()
        for f in self.fields:
            k = f.path.casefold()
            if k in seen:
                raise ValueError(f"duplicate field path '{f.path}' in entity '{self.key}'")
            seen.add(k)
            if f.data_type == "enum" and not f.options:
                raise ValueError(f"enum field '{f.path}' in entity '{self.key}' has no options")
        return self


class CatalogOperator(_Wire):
    key: str = Field(min_length=1)
    display_name: str = Field(min_length=1)
    requires_value: bool


class TypeOperator(_Wire):
    data_type: DataType
    operator: str = Field(min_length=1)
    display_name: str | None = Field(default=None, description="Type-specific wording, e.g. 'לפני' for dates")
    allow_field_compare: bool = False


class Catalog(_Wire):
    version: int = 1
    entities: list[CatalogEntity] = Field(min_length=1)
    operators: list[CatalogOperator] = Field(min_length=1)
    type_operators: list[TypeOperator] = Field(min_length=1)

    @model_validator(mode="after")
    def _consistent(self) -> "Catalog":
        keys = [e.key.casefold() for e in self.entities]
        if len(keys) != len(set(keys)):
            raise ValueError("duplicate entity key in catalog")
        ops = {o.key.casefold() for o in self.operators}
        if len(ops) != len(self.operators):
            raise ValueError("duplicate operator key in catalog")
        for t in self.type_operators:
            if t.operator.casefold() not in ops:
                raise ValueError(f"typeOperators references unknown operator '{t.operator}'")
        return self


# --------------------------------------------------------------------------- control definition (DefinitionJson v1)

class ValueOperand(_StrictWire):
    kind: Literal["value"]
    value: bool | int | float | str


class FieldOperand(_StrictWire):
    kind: Literal["field"]
    field: str = Field(min_length=1)


class TodayOperand(_StrictWire):
    kind: Literal["today"]
    offset_days: int = 0


class NowOperand(_StrictWire):
    kind: Literal["now"]


Operand = Annotated[
    Union[ValueOperand, FieldOperand, TodayOperand, NowOperand],
    Field(discriminator="kind"),
]


class RuleNode(_StrictWire):
    type: Literal["rule"]
    field: str = Field(min_length=1)
    operator: str = Field(min_length=1)
    compare_to: Operand | None = None

    @model_serializer(mode="wrap")
    def _omit_empty_compare_to(self, handler: Any) -> dict[str, Any]:
        # DefinitionJson omits compareTo for isEmpty / isNotEmpty
        data = handler(self)
        for key in ("compareTo", "compare_to"):
            if key in data and data[key] is None:
                del data[key]
        return data


class GroupNode(_StrictWire):
    type: Literal["group"]
    logic: Literal["AND", "OR"]
    children: list["Node"] = Field(min_length=1)


Node = Annotated[Union[RuleNode, GroupNode], Field(discriminator="type")]
GroupNode.model_rebuild()


class ControlDefinition(_StrictWire):
    version: Literal[1] = 1
    root_entity: str = Field(min_length=1)
    condition: Node


# --------------------------------------------------------------------------- API

class GenerateRequest(_Wire):
    text: str = Field(min_length=1, max_length=MAX_REQUEST_TEXT, description="Free-text control description (Hebrew)")
    catalog: Catalog

    @model_validator(mode="after")
    def _not_blank(self) -> "GenerateRequest":
        self.text = self.text.strip()
        if not self.text:
            raise ValueError("text must not be blank")
        return self


class GenerateResponse(_Wire):
    """Also the exact JSON a real model is asked to return (see PromptBuilder)."""

    definition: ControlDefinition | None = Field(
        default=None, description="Proposed control, or null when the request could not be translated"
    )
    explanation: str = Field(description="Hebrew explanation of the proposed control, for the approving user")
    confidence: float = Field(ge=0.0, le=1.0)
    warnings: list[str] = Field(default_factory=list, description="Hebrew warnings / assumptions")


class PromptPreview(_Wire):
    provider: str
    system: str
    user: str
    response_schema: dict[str, Any]
