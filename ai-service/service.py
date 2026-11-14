"""ControlGenerationService — orchestrates prompt → provider → validation.

Every provider answer, mock or real, is treated as untrusted: it is parsed into the DefinitionJson
schema and validated against the catalog. An invalid answer never reaches the caller as a definition;
the reasons are returned as warnings instead.
"""
from __future__ import annotations

import logging

from pydantic import ValidationError

from definition_validator import CatalogIndex, validate_definition
from prompt_builder import Prompt, PromptBuilder
from providers import AiProvider, ProviderRequest
from schemas import ControlDefinition, GenerateRequest, GenerateResponse

log = logging.getLogger(__name__)

INVALID_ANSWER = "ההצעה שהתקבלה אינה תקינה ולכן לא ניתן להשתמש בה. ניתן לנסח את הבקשה מחדש או להגדיר את הבקרה ידנית."


class ControlGenerationService:
    def __init__(self, provider: AiProvider, prompt_builder: PromptBuilder | None = None) -> None:
        self.provider = provider
        self.prompt_builder = prompt_builder or PromptBuilder()

    def build_prompt(self, request: GenerateRequest) -> Prompt:
        return self.prompt_builder.build(request.text, request.catalog)

    async def generate(self, request: GenerateRequest) -> GenerateResponse:
        prompt = self.build_prompt(request)
        result = await self.provider.generate(ProviderRequest(request.text, request.catalog, prompt))

        warnings = list(result.warnings)
        confidence = min(max(result.confidence, 0.0), 1.0)

        if result.definition is None:
            return GenerateResponse(definition=None, explanation=result.explanation, confidence=0.0, warnings=warnings)

        try:
            parsed = ControlDefinition.model_validate(result.definition)
        except ValidationError as ex:
            log.warning("provider %s returned a definition that does not match the schema: %s", self.provider.name, ex)
            return GenerateResponse(
                definition=None,
                explanation=result.explanation,
                confidence=0.0,
                warnings=[INVALID_ANSWER, "מבנה ההגדרה אינו תואם את הפורמט הנדרש."] + warnings,
            )

        definition, issues = validate_definition(parsed, CatalogIndex(request.catalog))
        if issues:
            log.warning("provider %s returned a definition that does not match the catalog: %s",
                        self.provider.name, [f"{i.path}: {i.message}" for i in issues])
            return GenerateResponse(
                definition=None,
                explanation=result.explanation,
                confidence=0.0,
                warnings=[INVALID_ANSWER] + [i.message for i in issues] + warnings,
            )

        return GenerateResponse(
            definition=definition, explanation=result.explanation, confidence=confidence, warnings=warnings
        )
