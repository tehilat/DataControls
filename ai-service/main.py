"""AI service — free text → control definition (stage B).

Run: uvicorn main:app --reload --port 8001
Settings (environment): AI_PROVIDER (default "mock").

The service never touches the database: the .NET API sends the business catalog with each request.
"""
from __future__ import annotations

import logging
import os

from fastapi import Depends, FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from providers import ProviderConfigError, ProviderError, create_provider
from schemas import GenerateRequest, GenerateResponse, PromptPreview
from service import ControlGenerationService

logging.basicConfig(level=logging.INFO)
log = logging.getLogger("ai-service")

app = FastAPI(
    title="DataControls AI service",
    version="1.0.0",
    description="Translates a free-text (Hebrew) control description into a DefinitionJson control definition.",
)

PROBLEM_JSON = "application/problem+json"
NOT_CONFIGURED_TITLE = "שירות ה-AI אינו מוגדר"

# A wrong AI_PROVIDER or a missing API key must not crash the service: it starts "not configured",
# /health says why (503), and the endpoints answer 503 with the same Hebrew message, which the .NET API
# passes on to the user. The rest of the system keeps working.
app.state.provider_name = os.getenv("AI_PROVIDER", "mock")
app.state.config_error = None
try:
    app.state.service = ControlGenerationService(create_provider(app.state.provider_name))
except ProviderConfigError as config_error:
    app.state.service = None
    app.state.config_error = str(config_error)
    log.error("AI service is NOT configured: %s", config_error)


class ServiceNotConfigured(Exception):
    pass


def get_service(request: Request) -> ControlGenerationService:
    service = request.app.state.service
    if service is None:
        raise ServiceNotConfigured()
    return service


def _config_error(request: Request) -> str:
    return request.app.state.config_error or "ספק ה-AI לא הוגדר."


@app.exception_handler(ServiceNotConfigured)
async def _not_configured(request: Request, _: ServiceNotConfigured) -> JSONResponse:
    return JSONResponse(
        status_code=503,
        media_type=PROBLEM_JSON,
        content={"type": "about:blank", "title": NOT_CONFIGURED_TITLE, "status": 503, "detail": _config_error(request)},
    )


@app.exception_handler(RequestValidationError)
async def _validation_error(_: Request, ex: RequestValidationError) -> JSONResponse:
    errors = [
        {"path": ".".join(str(p) for p in e.get("loc", ())), "message": e.get("msg", "")}
        for e in ex.errors()
    ]
    return JSONResponse(
        status_code=422,
        media_type=PROBLEM_JSON,
        content={"type": "about:blank", "title": "הבקשה אינה תקינה", "status": 422, "errors": errors},
    )


@app.exception_handler(ProviderError)
async def _provider_error(_: Request, ex: ProviderError) -> JSONResponse:
    logging.getLogger(__name__).error("AI provider failed: %s", ex)
    return JSONResponse(
        status_code=502,
        media_type=PROBLEM_JSON,
        content={"type": "about:blank", "title": "שירות ה-AI אינו זמין כרגע", "status": 502},
    )


@app.get("/health")
async def health(request: Request) -> JSONResponse:
    service = request.app.state.service
    if service is None:
        return JSONResponse(
            status_code=503,
            content={"status": "not_configured", "provider": request.app.state.provider_name, "detail": _config_error(request)},
        )
    return JSONResponse(content={"status": "ok", "provider": service.provider.name})


@app.post("/generate-control", response_model=GenerateResponse)
async def generate_control(
    body: GenerateRequest, service: ControlGenerationService = Depends(get_service)
) -> GenerateResponse:
    return await service.generate(body)


@app.post("/prompt-preview", response_model=PromptPreview)
async def prompt_preview(
    body: GenerateRequest, service: ControlGenerationService = Depends(get_service)
) -> PromptPreview:
    prompt = service.build_prompt(body)
    return PromptPreview(
        provider=service.provider.name,
        system=prompt.system,
        user=prompt.user,
        response_schema=prompt.response_schema,
    )
