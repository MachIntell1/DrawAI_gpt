from fastapi.testclient import TestClient

from app.main import app
from app.domain.models import DrawingPlan
from tests.test_execution import _passing_execution
from tests.fixtures import drawing2_request


client = TestClient(app)


def test_health_and_plan_contract():
    health = client.get("/health")
    assert health.status_code == 200
    assert health.json()["llm_controls_dimensions"] is False

    response = client.post(
        "/api/v2/plugin/plan",
        json=drawing2_request().model_dump(mode="json"),
    )
    assert response.status_code == 200, response.text
    body = response.json()
    assert body["schema_version"] == "2.0"
    assert body["release_gate"]["release_ready"] is True
    assert body["requirements"]


def test_v1_alias_rejects_legacy_unverifiable_contract():
    response = client.post(
        "/api/v1/plugin/plan",
        json={
            "model_data": {
                "model_name": "legacy",
                "model_hash": "12345678",
                "file_path": "legacy.SLDPRT",
                "bounding_box": {"width": 10, "height": 10, "depth": 2},
            },
            "preferences": {"standard": "ISO"},
        },
    )
    assert response.status_code == 422


def test_execution_uses_cached_immutable_plan_not_client_release_gate():
    response = client.post(
        "/api/v2/plugin/plan",
        json=drawing2_request(approved=False).model_dump(mode="json"),
    )
    assert response.status_code == 200
    body = response.json()
    plan = DrawingPlan.model_validate(body)
    execution = _passing_execution(plan)

    # A client cannot erase the recorded approval blocker in the plan it sends
    # back; validation uses the cached immutable plan for this plan ID/digest.
    body["release_gate"] = {
        "status": "RELEASE_READY",
        "release_ready": True,
        "blockers": [],
        "warnings": [],
        "checks": {},
    }
    validation = client.post(
        "/api/v2/plugin/validate-execution",
        json={"plan": body, "execution": execution.model_dump(mode="json")},
    )
    assert validation.status_code == 200
    gate = validation.json()
    assert gate["release_ready"] is False
    assert any(item["code"] == "APPROVAL-001" for item in gate["blockers"])
