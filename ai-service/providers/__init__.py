from providers.base import AiProvider, ProviderError, ProviderRequest, ProviderResult
from providers.mock_provider import MockProvider

# Registry of available providers. A real model provider is added here and selected via AI_PROVIDER.
_PROVIDERS: dict[str, type[AiProvider]] = {
    MockProvider.name: MockProvider,
}


def create_provider(name: str) -> AiProvider:
    try:
        return _PROVIDERS[name.strip().lower()]()
    except KeyError:
        raise ValueError(f"Unknown AI provider '{name}'. Available: {', '.join(sorted(_PROVIDERS))}") from None


__all__ = ["AiProvider", "MockProvider", "ProviderError", "ProviderRequest", "ProviderResult", "create_provider"]
