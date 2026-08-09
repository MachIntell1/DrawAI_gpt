from __future__ import annotations

import logging
import hmac

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from app import __version__
from app.api.plugin import v1, v2
from app.config import get_settings


settings = get_settings()
logging.basicConfig(level=getattr(logging, settings.log_level.upper(), logging.INFO))

app = FastAPI(
    title=settings.app_name,
    version=__version__,
    description="Deterministic, associative ISO/ASME drawing planner and release gate.",
)
app.include_router(v2)
if settings.allow_v1_compatibility:
    app.include_router(v1)


@app.middleware("http")
async def request_size_limit(request: Request, call_next):
    length = request.headers.get("content-length")
    if length:
        try:
            if int(length) > settings.max_request_bytes:
                return JSONResponse(status_code=413, content={"detail": "request body too large"})
        except ValueError:
            return JSONResponse(status_code=400, content={"detail": "invalid content-length"})
    if settings.api_key and request.url.path.startswith("/api/"):
        supplied = request.headers.get("x-api-key", "")
        if not hmac.compare_digest(supplied, settings.api_key):
            return JSONResponse(status_code=401, content={"detail": "invalid API key"})
    return await call_next(request)


@app.get("/health")
def health() -> dict[str, object]:
    return {
        "status": "healthy",
        "version": __version__,
        "planner": "deterministic-v2",
        "llm_controls_dimensions": False,
    }
