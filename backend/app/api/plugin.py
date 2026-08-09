from __future__ import annotations

from fastapi import APIRouter, HTTPException

from app.domain.models import (
    DrawingPlan,
    ExecutionValidationRequest,
    PlanRequest,
    ReleaseGate,
)
from app.services.planner import generate_plan
from app.services.repository import plans
from app.services.validation import validate_execution
from app.standards.profiles import public_profiles


v2 = APIRouter(prefix="/api/v2", tags=["drawing-v2"])
v1 = APIRouter(prefix="/api/v1", tags=["drawing-v1-compatibility"])


@v2.get("/standards/profiles")
def standards_profiles() -> list[dict[str, object]]:
    return public_profiles()


@v2.post("/plugin/plan", response_model=DrawingPlan)
def plan_drawing(request: PlanRequest) -> DrawingPlan:
    try:
        plan = generate_plan(request)
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    plans.put(plan)
    return plan


@v2.post("/plugin/validate-execution", response_model=ReleaseGate)
def validate_drawing_execution(request: ExecutionValidationRequest) -> ReleaseGate:
    cached = plans.get(request.plan.plan_id)
    if cached is None:
        raise HTTPException(status_code=409, detail="immutable plan is not available; regenerate it")
    if cached.plan_digest != request.plan.plan_digest:
        raise HTTPException(status_code=409, detail="plan digest conflicts with cached plan")
    return validate_execution(cached, request.execution)


@v2.get("/plugin/plan/{plan_id}", response_model=DrawingPlan)
def get_plan(plan_id: str) -> DrawingPlan:
    plan = plans.get(plan_id)
    if plan is None:
        raise HTTPException(status_code=404, detail="plan not found")
    return plan


# Compatibility paths use the new v2 contract.  They intentionally do not
# accept legacy numeric-target plans because those cannot prove association.
@v1.post("/plugin/plan", response_model=DrawingPlan)
def plan_drawing_v1_alias(request: PlanRequest) -> DrawingPlan:
    return plan_drawing(request)


@v1.post("/plugin/validate-execution", response_model=ReleaseGate)
def validate_execution_v1_alias(request: ExecutionValidationRequest) -> ReleaseGate:
    return validate_drawing_execution(request)


@v1.post("/plugin/review", response_model=ReleaseGate)
def deterministic_review_alias(request: ExecutionValidationRequest) -> ReleaseGate:
    """Legacy endpoint retained without LLM-directed drawing mutations."""

    return validate_drawing_execution(request)
