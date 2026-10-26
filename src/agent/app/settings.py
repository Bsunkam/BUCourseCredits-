import os
from dataclasses import dataclass


@dataclass(frozen=True)
class Settings:
    """Environment configuration. Secrets come from Key Vault references on the container app."""

    mode: str  # "azure" uses Azure AI services; "mock" uses the keyword ranker (tests and local runs)
    openai_endpoint: str
    openai_api_key: str
    openai_api_version: str
    chat_deployment: str
    extraction_deployment: str
    search_endpoint: str
    search_index: str
    search_api_key: str
    docintel_endpoint: str
    storage_account_url: str
    max_syllabus_chars: int = 12000

    @property
    def use_azure(self) -> bool:
        return self.mode == "azure"


def load_settings() -> Settings:
    endpoint = os.getenv("AZURE_OPENAI_ENDPOINT", "")
    mode = os.getenv("AGENT_MODE") or ("azure" if endpoint else "mock")
    return Settings(
        mode=mode,
        openai_endpoint=endpoint,
        openai_api_key=os.getenv("AZURE_OPENAI_API_KEY", ""),
        openai_api_version=os.getenv("AZURE_OPENAI_API_VERSION", "2024-10-21"),
        chat_deployment=os.getenv("AZURE_OPENAI_CHAT_DEPLOYMENT", "gpt-4o"),
        # A smaller model for extraction keeps cost down (design 9.3, "other savings").
        extraction_deployment=os.getenv("AZURE_OPENAI_EXTRACTION_DEPLOYMENT", "gpt-4o-mini"),
        search_endpoint=os.getenv("AZURE_SEARCH_ENDPOINT", ""),
        search_index=os.getenv("AZURE_SEARCH_INDEX", "bu-courses"),
        search_api_key=os.getenv("AZURE_SEARCH_API_KEY", ""),
        docintel_endpoint=os.getenv("AZURE_DOCINTEL_ENDPOINT", ""),
        storage_account_url=os.getenv("STORAGE_ACCOUNT_URL", ""),
    )
