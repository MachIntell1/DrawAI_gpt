from __future__ import annotations

from threading import RLock

from app.domain.models import DrawingPlan


class PlanRepository:
    """Small in-memory cache; production deployments should replace this adapter."""

    def __init__(self) -> None:
        self._lock = RLock()
        self._plans: dict[str, DrawingPlan] = {}

    def put(self, plan: DrawingPlan) -> None:
        with self._lock:
            self._plans[plan.plan_id] = plan.model_copy(deep=True)

    def get(self, plan_id: str) -> DrawingPlan | None:
        with self._lock:
            plan = self._plans.get(plan_id)
            return plan.model_copy(deep=True) if plan else None


plans = PlanRepository()

