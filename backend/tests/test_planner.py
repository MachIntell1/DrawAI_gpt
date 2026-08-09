from app.domain.enums import FeatureKind, ProjectionMethod, ReleaseStatus, RequirementKind
from app.services.planner import generate_plan
from tests.fixtures import drawing2_request


def test_failed_drawing_2_regression_is_feature_complete_and_not_diagonal():
    plan = generate_plan(drawing2_request())
    by_kind = {family.kind for family in plan.feature_families}
    assert FeatureKind.EDGE_NOTCH in by_kind
    assert FeatureKind.FILLET in by_kind
    overall = [r for r in plan.requirements if r.kind == RequirementKind.OVERALL_SIZE]
    assert {r.characteristic for r in overall} == {"OVERALL_X", "OVERALL_Y", "OVERALL_Z"}
    assert any(r.specification.nominal_value_mm == 16 for r in overall)
    location = [r for r in plan.requirements if r.kind == RequirementKind.FEATURE_LOCATION]
    assert location
    assert {r.measurement_axis for r in location} <= {"X", "Y", "Z"}
    assert all(r.measurement_axis != "RADIAL" for r in location)
    assert not [f for f in plan.release_gate.blockers if f.code.startswith("COV")]


def test_iso_and_asme_projection_are_preserved_end_to_end():
    iso = generate_plan(drawing2_request())
    assert iso.standards.projection == ProjectionMethod.FIRST_ANGLE
    iso_views = {view.view_id: view for view in iso.views}
    assert iso_views["view-top"].center_y_mm < iso_views["view-front"].center_y_mm
    assert iso_views["view-right"].center_x_mm < iso_views["view-front"].center_x_mm

    asme = generate_plan(
        drawing2_request(profile="ASME_Y14_2018", projection=ProjectionMethod.THIRD_ANGLE)
    )
    assert asme.standards.projection == ProjectionMethod.THIRD_ANGLE
    asme_views = {view.view_id: view for view in asme.views}
    assert asme_views["view-top"].center_y_mm > asme_views["view-front"].center_y_mm
    assert asme_views["view-right"].center_x_mm > asme_views["view-front"].center_x_mm


def test_identical_input_is_idempotent():
    request = drawing2_request()
    first = generate_plan(request)
    second = generate_plan(request)
    assert first.plan_id == second.plan_id
    assert first.plan_digest == second.plan_digest
    assert [r.requirement_id for r in first.requirements] == [r.requirement_id for r in second.requirements]


def test_missing_approval_never_reports_release_ready():
    plan = generate_plan(drawing2_request(approved=False))
    assert plan.release_gate.status != ReleaseStatus.RELEASE_READY
    assert not plan.release_gate.release_ready
    assert any(f.code == "APPROVAL-001" for f in plan.release_gate.blockers)
    assert any(annotation.kind.value == "DRAFT_WATERMARK" for annotation in plan.annotations)


def test_plan_ready_output_is_still_watermarked_until_execution_approval():
    plan = generate_plan(drawing2_request(approved=True))
    assert plan.release_gate.release_ready
    assert any(annotation.kind.value == "DRAFT_WATERMARK" for annotation in plan.annotations)
