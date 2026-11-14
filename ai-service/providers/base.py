"""The AI provider abstraction.

A provider turns a free-text request into a *raw* model answer. It does not validate anything:
ControlGenerationService validates every answer the same way (schema + catalog), so swapping the
mock for a real model (OpenAI, Azure OpenAI, Anthropic, a local model...) means adding one class
here and selecting it with the AI_PROVIDER setting — nothing else changes.
"""
from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import Any

from prompt_builder import Prompt
from schemas import Catalog


@dataclass(frozen=True)
class ProviderRequest:
    text: str
    catalog: Catalog
    prompt: Prompt  # the full prompt a real model would receive


@dataclass
class ProviderResult:
    """Raw answer. `definition` is untrusted JSON (dict) — the service parses and validates it."""

    definition: dict[str, Any] | None
    explanation: str
    confidence: float
    warnings: list[str] = field(default_factory=list)


class ProviderError(Exception):
    """The provider could not produce an answer (network, quota, malformed model output...)."""


class AiProvider(ABC):
    name: str

    @abstractmethod
    async def generate(self, request: ProviderRequest) -> ProviderResult: ...
