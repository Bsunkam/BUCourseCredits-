"""
Transfer Credit agent service. Internal ingress only: the .NET API is the single caller.
No UI and no database logic; the API stores every run, decision and audit record.
"""

import logging

from fastapi import FastAPI, HTTPException
from fastapi.responses import RedirectResponse
from langgraph.types import Command

from app.models import EvaluateRequest, EvaluateResponse, ExtractRequest, ExtractResponse, ResumeRequest
from app.settings import load_settings
from graph.pipeline import build_graph, to_response
from graph.services import read_document_text

log = logging.getLogger("agent")
settings = load_settings()
graph = build_graph(settings)
app = FastAPI(title="Transfer Credit Agent Service", version="1.0")


def _config(thread_id: str) -> dict:
    return {"configurable": {"thread_id": thread_id}}


@app.get("/", include_in_schema=False)
def root() -> RedirectResponse:
    # No UI here; send people who open the base URL to the interactive API docs.
    return RedirectResponse("/docs")


@app.get("/health")
def health() -> dict:
    return {"status": "ok", "mode": settings.mode}


@app.post("/agent/evaluate", response_model=EvaluateResponse)
def evaluate(request: EvaluateRequest) -> EvaluateResponse:
    config = _config(request.threadId)
    graph.invoke({"request": request.model_dump()}, config)
    snapshot = graph.get_state(config)
    paused = bool(snapshot.next)  # waiting in human_review
    return to_response(request, snapshot.values, paused)


@app.post("/agent/resume")
def resume(request: ResumeRequest) -> dict:
    config = _config(request.threadId)
    snapshot = graph.get_state(config)
    if not snapshot.next:
        # Unknown or already finished (for example after a restart with the in-memory checkpointer).
        # The decision is already stored by the API, so this is not an error for the caller.
        return {"status": "not_paused"}
    graph.invoke(Command(resume=request.model_dump()), config)
    return {"status": "completed"}


@app.post("/agent/extract", response_model=ExtractResponse)
def extract(request: ExtractRequest) -> ExtractResponse:
    if not settings.use_azure:
        raise HTTPException(status_code=501, detail="Extraction needs Azure AI Document Intelligence (AGENT_MODE=azure).")
    text = read_document_text(settings, request.container, request.blobPath)
    if text is None:
        raise HTTPException(status_code=501, detail="Document Intelligence or storage is not configured.")
    # Course-field extraction from transcripts is done by staff review in the MVP; return the text.
    return ExtractResponse(text=text, courses=[], missingFields=[], model="prebuilt-layout")
