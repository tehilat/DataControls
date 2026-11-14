"""MockProvider — canned answers for known Hebrew requests, no external calls.

The scenarios are data (mock_scenarios.json), not code: each one lists keyword groups that must all
appear in the request (any term per group, regex allowed) and the answer to return. The most specific
matching scenario (most groups) wins. The answer is returned as raw JSON, exactly like a real model's,
and goes through the same validation as any other provider — so a scenario that does not fit the
catalog that was sent is rejected, not trusted.
"""
from __future__ import annotations

import copy
import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from providers.base import AiProvider, ProviderRequest, ProviderResult

_SCENARIOS_FILE = Path(__file__).with_name("mock_scenarios.json")
_NIQQUD = re.compile(r"[\u0591-\u05C7]")
_QUOTES = re.compile(r"[\"'\u05F3\u05F4\u201C\u201D\u2018\u2019]")
_SPACES = re.compile(r"\s+")
_FINAL_LETTERS = str.maketrans("ךםןףץ", "כמנפצ")  # רחפן / רחפנים match the same pattern


def normalize(text: str) -> str:
    """Lower-case, drop niqqud and quote marks (דוא"ל → דואל), unify final letters, collapse whitespace."""
    text = _NIQQUD.sub("", text).translate(_FINAL_LETTERS)
    text = _QUOTES.sub("", text)
    return _SPACES.sub(" ", text).strip().lower()


@dataclass(frozen=True)
class Scenario:
    id: str
    example: str
    groups: tuple[tuple[re.Pattern[str], ...], ...]
    definition: dict[str, Any]
    explanation: str
    confidence: float
    warnings: tuple[str, ...]

    def matches(self, normalized_text: str) -> bool:
        return all(any(p.search(normalized_text) for p in group) for group in self.groups)


def load_scenarios(path: Path = _SCENARIOS_FILE) -> list[Scenario]:
    raw = json.loads(path.read_text(encoding="utf-8"))
    scenarios = []
    for s in raw["scenarios"]:
        groups = tuple(tuple(re.compile(normalize(term)) for term in group) for group in s["match"])
        if not groups or any(not g for g in groups):
            raise ValueError(f"mock scenario '{s['id']}' has an empty match group")
        scenarios.append(
            Scenario(
                id=s["id"],
                example=s["example"],
                groups=groups,
                definition=s["definition"],
                explanation=s["explanation"],
                confidence=float(s["confidence"]),
                warnings=tuple(s.get("warnings", [])),
            )
        )
    return scenarios


class MockProvider(AiProvider):
    name = "mock"

    def __init__(self, scenarios: list[Scenario] | None = None) -> None:
        self.scenarios = scenarios if scenarios is not None else load_scenarios()

    def match(self, text: str) -> Scenario | None:
        normalized = normalize(text)
        best: Scenario | None = None
        for s in self.scenarios:
            if s.matches(normalized) and (best is None or len(s.groups) > len(best.groups)):
                best = s
        return best

    async def generate(self, request: ProviderRequest) -> ProviderResult:
        scenario = self.match(request.text)
        if scenario is None:
            examples = "; ".join(f'"{s.example}"' for s in self.scenarios[:5])
            return ProviderResult(
                definition=None,
                explanation="לא הצלחתי לתרגם את הבקשה להגדרת בקרה.",
                confidence=0.0,
                warnings=[
                    "הבקשה לא זוהתה. במצב הדגמה (Mock) מזוהות רק בקשות לדוגמה, למשל: " + examples + ".",
                    "ניתן להגדיר את הבקרה ידנית במסך הגדרת הבקרות.",
                ],
            )
        return ProviderResult(
            definition=copy.deepcopy(scenario.definition),
            explanation=scenario.explanation,
            confidence=scenario.confidence,
            warnings=list(scenario.warnings),
        )
