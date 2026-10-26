"""Azure AI service clients: Azure OpenAI (chat), Azure AI Search (retrieval), Document Intelligence (OCR)."""

from functools import lru_cache
from pathlib import Path
from typing import Optional

from pydantic import BaseModel, Field

from app.models import CatalogueCourse, Confidence, MatchLevel
from app.settings import Settings

PROMPTS = Path(__file__).resolve().parent.parent / "prompts"


def load_prompt(name: str, version: str) -> str:
    """Prompts are versioned files; the version is part of the reuse cache key."""
    path = PROMPTS / f"{name}_{version}.md"
    if not path.exists():
        path = PROMPTS / f"{name}_p-001.md"
    return path.read_text(encoding="utf-8")


# ---------- structured outputs the model must return ----------

class RankedCandidate(BaseModel):
    buCourseCode: str
    matchLevel: MatchLevel
    confidence: Confidence
    matchedAttributes: list[str] = Field(default_factory=list, description="Short facts: topics, level, credit hours")


class Ranking(BaseModel):
    candidates: list[RankedCandidate] = Field(max_length=5)


class CitedEvidence(BaseModel):
    snippet: str = Field(description="An exact quotation from the syllabus text")
    matchesOutcome: str = Field(description="Which BU course outcome or topic it supports")


class EvidenceReport(BaseModel):
    explanation: str = Field(description="2-4 sentences for the evaluator. Advisory only.")
    evidence: list[CitedEvidence] = Field(max_length=4)
    insufficientEvidence: bool


# ---------- clients (created lazily, only in azure mode) ----------

def _token_provider():
    from azure.identity import DefaultAzureCredential, get_bearer_token_provider

    return get_bearer_token_provider(DefaultAzureCredential(), "https://cognitiveservices.azure.com/.default")


@lru_cache
def chat_model(settings: Settings, deployment: str):
    from langchain_openai import AzureChatOpenAI

    kwargs = dict(
        azure_endpoint=settings.openai_endpoint,
        azure_deployment=deployment,
        api_version=settings.openai_api_version,
        temperature=0,
        timeout=45,
        max_retries=2,
    )
    if settings.openai_api_key:
        kwargs["api_key"] = settings.openai_api_key
    else:
        kwargs["azure_ad_token_provider"] = _token_provider()
    return AzureChatOpenAI(**kwargs)


def search_candidates(settings: Settings, query: str, provided: list[CatalogueCourse], top: int = 8) -> list[CatalogueCourse]:
    """Vector/hybrid search over the BU course index. Falls back to the catalogue the API sent."""
    if not settings.search_endpoint:
        return provided
    from azure.core.credentials import AzureKeyCredential
    from azure.identity import DefaultAzureCredential
    from azure.search.documents import SearchClient
    from azure.search.documents.models import VectorizableTextQuery

    credential = AzureKeyCredential(settings.search_api_key) if settings.search_api_key else DefaultAzureCredential()
    client = SearchClient(settings.search_endpoint, settings.search_index, credential)
    results = client.search(
        search_text=query,
        vector_queries=[VectorizableTextQuery(text=query, k_nearest_neighbors=top, fields="contentVector")],
        select=["code", "title", "credits", "description", "verified"],
        top=top,
    )
    found = [
        CatalogueCourse(code=r["code"], title=r["title"], credits=r.get("credits") or 0,
                        description=r.get("description"), verified=r.get("verified") or "Check")
        for r in results
    ]
    # Only codes the API knows are allowed back; the API rejects unknown codes anyway.
    known = {c.code for c in provided}
    return [c for c in found if c.code in known] or provided


def read_document_text(settings: Settings, container: str, blob_path: str) -> Optional[str]:
    """OCR/layout extraction with Azure AI Document Intelligence (prebuilt-layout)."""
    if not (settings.docintel_endpoint and settings.storage_account_url):
        return None
    from azure.ai.documentintelligence import DocumentIntelligenceClient
    from azure.ai.documentintelligence.models import AnalyzeDocumentRequest
    from azure.identity import DefaultAzureCredential
    from azure.storage.blob import BlobServiceClient

    credential = DefaultAzureCredential()
    blob = BlobServiceClient(settings.storage_account_url, credential).get_blob_client(container, blob_path)
    data = blob.download_blob().readall()
    client = DocumentIntelligenceClient(settings.docintel_endpoint, credential)
    poller = client.begin_analyze_document("prebuilt-layout", AnalyzeDocumentRequest(bytes_source=data))
    return poller.result().content
