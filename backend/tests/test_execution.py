from app.domain.enums import AssociationStatus
from app.domain.models import ExecutedRequirement, ExecutedView, ExecutionReport
from app.services.planner import generate_plan
from app.services.validation import validate_execution
from tests.fixtures import drawing2_request


def _passing_execution(plan):
    return ExecutionReport(
        plan_id=plan.plan_id,
        plan_digest=plan.plan_digest,
        model_hash=plan.model_hash,
        standards_digest=plan.standards.digest,
        projection=plan.standards.projection,
        executed_views=[
            ExecutedView(
                view_id=view.view_id,
                orientation=view.orientation,
                actual_bounds_mm=view.expected_model_bounds_mm,
                actual_scale=view.scale,
                parent_view_id=view.parent_view_id,
            )
            for view in plan.views
        ],
        executed_requirements=[
            ExecutedRequirement(
                requirement_id=requirement.requirement_id,
                measurand_key=requirement.measurand_key,
                specification_key=requirement.specification_key,
                association_status=AssociationStatus.ASSOCIATIVE,
                created_annotation_id=f"sw-{index}",
                resolved_geometry_ref_count=len(requirement.geometry_refs),
                expected_geometry_ref_count=len(requirement.geometry_refs),
            )
            for index, requirement in enumerate(plan.requirements)
        ],
        title_fields_written=plan.title_fields,
        cad_artifacts_hidden=True,
        human_approval_confirmed=True,
    )


def test_execution_gate_rejects_one_missing_association():
    plan = generate_plan(drawing2_request())
    execution = _passing_execution(plan)
    execution.executed_requirements[0].association_status = AssociationStatus.ORPHAN
    gate = validate_execution(plan, execution)
    assert not gate.release_ready
    assert any(f.code == "EXEC-006" for f in gate.blockers)


def test_execution_gate_can_pass_complete_approved_fixture():
    plan = generate_plan(drawing2_request())
    assert plan.release_gate.release_ready, [f.model_dump() for f in plan.release_gate.blockers]
    gate = validate_execution(plan, _passing_execution(plan))
    assert gate.release_ready, [f.model_dump() for f in gate.blockers]

