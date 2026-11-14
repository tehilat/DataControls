import os

from providers.base import AiProvider, ProviderConfigError, ProviderError, ProviderRequest, ProviderResult
from providers.mock_provider import MockProvider

# Registry of available providers. A real model provider is added here and selected via AI_PROVIDER.
_PROVIDERS: dict[str, type[AiProvider]] = {
    MockProvider.name: MockProvider,
}


def create_provider(name: str) -> AiProvider:
    """Creates the provider named by AI_PROVIDER.

    Raises ProviderConfigError (with a Hebrew message for the user) when the name is unknown or when the
    provider's API key / settings (`required_env`) are missing. The caller keeps the service running and
    reports the message instead of crashing.
    """
    key = (name or "").strip().lower()
    provider_type = _PROVIDERS.get(key)
    if provider_type is None:
        available = ", ".join(sorted(_PROVIDERS))
        raise ProviderConfigError(
            f'ספק ה-AI "{name}" אינו מוכר. ספקים זמינים: {available}. יש לבדוק את משתנה הסביבה AI_PROVIDER.'
        )

    missing = [var for var in provider_type.required_env if not os.getenv(var, "").strip()]
    if missing:
        raise ProviderConfigError(
            f'חסר מפתח API לספק ה-AI "{key}": יש להגדיר את משתנה הסביבה {", ".join(missing)} ולהפעיל מחדש את השירות.'
        )
    return provider_type()


__all__ = [
    "AiProvider",
    "MockProvider",
    "ProviderConfigError",
    "ProviderError",
    "ProviderRequest",
    "ProviderResult",
    "create_provider",
]
