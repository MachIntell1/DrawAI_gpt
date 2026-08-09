from app.domain.enums import RequirementKind
from app.services.planner import generate_plan
from tests.fixtures import drawing2_request


def test_callouts_group_exact_families_without_orphan_notes():
    plan = generate_plan(drawing2_request())
    callouts = [
        requirement
        for requirement in plan.requirements
        if requirement.kind in {RequirementKind.HOLE_CALLOUT, RequirementKind.THREAD_CALLOUT}
    ]
    texts = [requirement.specification.display_text for requirement in callouts]
    assert any(text and "4×" in text and "⌀11" in text for text in texts)
    assert any(text and "6× M6×1-6H" in text for text in texts)
    assert all(requirement.geometry_refs for requirement in callouts)

