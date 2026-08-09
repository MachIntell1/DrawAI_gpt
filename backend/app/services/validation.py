from __future__ import annotations

from collections import defaultdict

from app.domain.enums import (
    AssociationStatus,
    FeatureKind,
    ProjectionMethod,
    ReleaseStatus,
    RequirementKind,
    Severity,
)
from app.domain.models import (
    DrawingPlan,
    ExecutionReport,
    FeatureFamily,
    ModelManifest,
    ReleaseGate,
    RequirementPlan,
    ValidationFinding,
)
from app.services.identity import digest


def _finding(code: str, message: str, *item_ids: str, severity: Severity = Severity.BLOCKER) -> ValidationFinding:
    return ValidationFinding(code=code, severity=severity, message=message, item_ids=list(item_ids))


def _coverage_findings(
    families: list[FeatureFamily], requirements: list[RequirementPlan]
) -> list[ValidationFinding]:
    findings: list[ValidationFinding] = []
    by_feature: dict[str, list[RequirementPlan]] = defaultdict(list)
    for requirement in requirements:
        for feature_id in requirement.feature_ids:
            by_feature[feature_id].append(requirement)

    hole_kinds = {
        FeatureKind.PLAIN_HOLE,
        FeatureKind.COUNTERBORE_HOLE,
        FeatureKind.COUNTERSINK_HOLE,
        FeatureKind.TAPPED_HOLE,
        FeatureKind.ADVANCED_HOLE,
    }
    for family in families:
        linked = [requirement for feature_id in family.feature_ids for requirement in by_feature[feature_id]]
        linked_kinds = {requirement.kind for requirement in linked}
        if family.kind in hole_kinds:
            expected_size_kind = (
                RequirementKind.THREAD_CALLOUT
                if family.kind == FeatureKind.TAPPED_HOLE
                else RequirementKind.HOLE_CALLOUT
            )
            if expected_size_kind not in linked_kinds:
                findings.append(_finding("COV-001", "hole family has no complete size callout", family.family_id))
            location_count = sum(1 for requirement in linked if requirement.kind == RequirementKind.FEATURE_LOCATION)
            expected_locations = len(family.centers) * 2
            if not family.centers:
                findings.append(_finding("COV-002", "hole/notch instances have no extracted centers", family.family_id))
            elif location_count < expected_locations:
                findings.append(
                    _finding(
                        "COV-003",
                        f"feature family requires {expected_locations} rectangular location controls; found {location_count}",
                        family.family_id,
                    )
                )
        elif family.kind in {FeatureKind.FILLET, FeatureKind.EDGE_NOTCH}:
            if RequirementKind.RADIUS not in linked_kinds:
                findings.append(_finding("COV-004", "radius feature has no radius requirement", family.family_id))
        elif family.kind == FeatureKind.CHAMFER and RequirementKind.CHAMFER not in linked_kinds:
            findings.append(_finding("COV-005", "chamfer has no complete requirement", family.family_id))
    return findings


def validate_plan(
    plan: DrawingPlan,
    model: ModelManifest,
    planner_blockers: list[str] | None = None,
) -> ReleaseGate:
    blockers: list[ValidationFinding] = []
    warnings: list[ValidationFinding] = []

    if model.bounds_source != "EXACT_VERTICES":
        blockers.append(_finding("EXTRACT-001", "overall bounds were obtained from an approximate API"))
    for extraction in model.extraction_findings:
        finding = _finding(
            extraction.code,
            extraction.message,
            *[ref.identity for ref in extraction.entity_refs],
            severity=extraction.severity,
        )
        (blockers if extraction.severity == Severity.BLOCKER else warnings).append(finding)

    for feature in plan.classified_features:
        if feature.kind == FeatureKind.UNKNOWN:
            blockers.append(_finding("FEAT-001", "feature classification is unresolved", feature.feature_id))
        for conflict in feature.conflicts:
            blockers.append(_finding("FEAT-002", conflict, feature.feature_id))

    for text in planner_blockers or []:
        blockers.append(_finding("SPEC-001", text))

    for requirement in plan.requirements:
        if not requirement.geometry_refs:
            blockers.append(
                _finding(
                    "ASSOC-001",
                    "controlling requirement has no persistent target geometry reference",
                    requirement.requirement_id,
                )
            )
        if requirement.kind == RequirementKind.FEATURE_LOCATION and not requirement.reference_refs:
            blockers.append(
                _finding(
                    "ASSOC-002",
                    "feature location has no persistent origin/datum reference",
                    requirement.requirement_id,
                )
            )

    same_measurand: dict[str, set[str]] = defaultdict(set)
    counts: dict[tuple[str, str], int] = defaultdict(int)
    for requirement in plan.requirements:
        same_measurand[requirement.measurand_key].add(requirement.specification_key)
        counts[(requirement.measurand_key, requirement.specification_key)] += 1
    for key, specifications in same_measurand.items():
        if len(specifications) > 1:
            blockers.append(_finding("DIM-001", "one measurand has conflicting specifications", key))
    for key, count in counts.items():
        if count > 1:
            blockers.append(_finding("DIM-002", "true duplicate controlling requirement remains", key[0]))

    blockers.extend(_coverage_findings(plan.feature_families, plan.requirements))

    for sheet_index in sorted({view.sheet_index for view in plan.views}):
        sheet_views = [view for view in plan.views if view.sheet_index == sheet_index]
        for index, first in enumerate(sheet_views):
            bounds = first.reserved_annotation_bounds_mm
            if bounds.left < 0 or bounds.bottom < 0 or bounds.right > plan.sheet_width_mm or bounds.top > plan.sheet_height_mm:
                blockers.append(_finding("LAY-001", "view/annotation envelope exceeds sheet boundary", first.view_id))
            for second in sheet_views[index + 1 :]:
                if first.reserved_annotation_bounds_mm.intersects(second.reserved_annotation_bounds_mm):
                    blockers.append(_finding("LAY-002", "reserved view/annotation envelopes overlap", first.view_id, second.view_id))

    front = next((view for view in plan.views if view.view_id == "view-front"), None)
    top = next((view for view in plan.views if view.view_id == "view-top"), None)
    right = next((view for view in plan.views if view.view_id == "view-right"), None)
    if not (front and top and right):
        blockers.append(_finding("VIEW-001", "front/top/right drawing roles are incomplete"))
    elif plan.standards.projection == ProjectionMethod.FIRST_ANGLE:
        if not (top.center_y_mm < front.center_y_mm and right.center_x_mm < front.center_x_mm):
            blockers.append(_finding("STD-001", "view placement contradicts first-angle projection"))
    elif not (top.center_y_mm > front.center_y_mm and right.center_x_mm > front.center_x_mm):
        blockers.append(_finding("STD-002", "view placement contradicts third-angle projection"))

    if plan.reference_scheme.provisional:
        blockers.append(
            _finding(
                "INTENT-001",
                "location references are geometric/provisional; functional datum scheme is not approved",
            )
        )

    required_title_fields = ("PART_NUMBER", "DRAWING_NUMBER", "REVISION", "MATERIAL")
    for field in required_title_fields:
        if not plan.title_fields.get(field):
            blockers.append(_finding("META-001", f"required title field {field} is missing", field))

    explicit_tolerances = all(
        requirement.specification.tolerance_type or requirement.is_reference
        for requirement in plan.requirements
    )
    if not model.engineering_intent.general_tolerance_policy_id and not explicit_tolerances:
        blockers.append(
            _finding(
                "INTENT-002",
                "untoleranced requirements exist and no approved general-tolerance policy is identified",
            )
        )

    if model.engineering_intent.general_tolerance_policy_id and (
        plan.standards.policy_id != model.engineering_intent.general_tolerance_policy_id
    ):
        blockers.append(
            _finding(
                "STD-003",
                "engineering general-tolerance policy does not match the selected standards policy",
            )
        )

    if not model.engineering_intent.has_human_approval:
        blockers.append(_finding("APPROVAL-001", "recorded human engineering approval is missing"))

    if model.material_from_model and model.engineering_intent.material and (
        model.material_from_model.strip().lower() != model.engineering_intent.material.strip().lower()
    ):
        blockers.append(_finding("META-002", "model material conflicts with approved drawing material"))
    elif model.material_from_model and not model.engineering_intent.material:
        warnings.append(
            _finding(
                "META-003",
                "material was copied from the model but is not independently approved in engineering intent",
                severity=Severity.WARNING,
            )
        )

    checks = {
        "features_classified": not any(f.code.startswith("FEAT") for f in blockers),
        "requirements_associable": not any(f.code.startswith("ASSOC") for f in blockers),
        "feature_coverage_complete": not any(f.code.startswith("COV") for f in blockers),
        "no_requirement_conflicts": not any(f.code.startswith("DIM") for f in blockers),
        "projection_consistent": not any(f.code.startswith("STD-00") and f.code != "STD-003" for f in blockers),
        "layout_valid": not any(f.code.startswith("LAY") for f in blockers),
        "engineering_intent_complete": not any(f.code.startswith("INTENT") for f in blockers),
        "title_fields_complete": not any(f.code.startswith("META-001") for f in blockers),
        "human_approval_recorded": model.engineering_intent.has_human_approval,
    }

    only_approval = blockers and all(f.code == "APPROVAL-001" for f in blockers)
    status = ReleaseStatus.RELEASE_READY if not blockers else (
        ReleaseStatus.REVIEW_REQUIRED if only_approval else ReleaseStatus.DRAFT
    )
    return ReleaseGate(
        status=status,
        release_ready=not blockers,
        blockers=blockers,
        warnings=warnings,
        checks=checks,
    )


def validate_execution(plan: DrawingPlan, execution: ExecutionReport) -> ReleaseGate:
    blockers = list(plan.release_gate.blockers)
    warnings = list(plan.release_gate.warnings)

    if execution.plan_id != plan.plan_id or execution.plan_digest != plan.plan_digest:
        blockers.append(_finding("EXEC-001", "execution report does not belong to this exact plan"))
    if execution.model_hash != plan.model_hash:
        blockers.append(_finding("EXEC-002", "executed model hash differs from planned model"))
    if execution.standards_digest != plan.standards.digest:
        blockers.append(_finding("EXEC-003", "standards profile changed between planning and execution"))
    if execution.projection != plan.standards.projection:
        blockers.append(_finding("EXEC-004", "executed projection differs from the immutable plan"))

    planned_ids = {requirement.requirement_id for requirement in plan.requirements}
    executed_by_id = {item.requirement_id: item for item in execution.executed_requirements}
    for requirement_id in sorted(planned_ids):
        item = executed_by_id.get(requirement_id)
        if item is None:
            blockers.append(_finding("EXEC-005", "planned controlling requirement was not executed", requirement_id))
            continue
        if item.association_status != AssociationStatus.ASSOCIATIVE:
            blockers.append(
                _finding(
                    "EXEC-006",
                    f"requirement is not associative: {item.association_status.value}",
                    requirement_id,
                )
            )
        if item.measurand_key != next(r.measurand_key for r in plan.requirements if r.requirement_id == requirement_id):
            blockers.append(_finding("EXEC-007", "executed measurand identity changed", requirement_id))
        if item.resolved_geometry_ref_count != item.expected_geometry_ref_count:
            blockers.append(_finding("EXEC-008", "not all persistent geometry references resolved", requirement_id))

    extra_ids = set(executed_by_id) - planned_ids
    if extra_ids:
        blockers.append(_finding("EXEC-009", "unplanned controlling requirements were created", *sorted(extra_ids)))
    if execution.orphan_controlling_annotations:
        blockers.append(_finding("EXEC-010", "orphan controlling annotations exist", *execution.orphan_controlling_annotations))
    if execution.duplicate_measurand_keys:
        blockers.append(_finding("EXEC-011", "duplicate controlling measurands exist", *execution.duplicate_measurand_keys))
    if execution.annotation_layout_violations:
        blockers.append(
            _finding(
                "EXEC-020",
                "controlling annotation envelopes overlap or violate their reserved lane",
                *execution.annotation_layout_violations,
            )
        )

    planned_views = {view.view_id: view for view in plan.views}
    actual_views = {view.view_id: view for view in execution.executed_views}
    for view_id, planned in planned_views.items():
        actual = actual_views.get(view_id)
        if actual is None:
            blockers.append(_finding("EXEC-012", "planned view was not created", view_id))
            continue
        if abs(actual.actual_scale - planned.scale) > 1e-6:
            blockers.append(_finding("EXEC-013", "view scale differs from plan", view_id))
        if actual.orientation != planned.orientation:
            blockers.append(_finding("EXEC-014", "view orientation differs from plan", view_id))

    for index, first in enumerate(execution.executed_views):
        first_plan = planned_views.get(first.view_id)
        if first_plan is None:
            continue
        bounds = first.actual_bounds_mm
        if bounds.left < 0 or bounds.bottom < 0 or bounds.right > plan.sheet_width_mm or bounds.top > plan.sheet_height_mm:
            blockers.append(_finding("EXEC-015", "actual view/annotation envelope is off sheet", first.view_id))
        reserved = first_plan.reserved_annotation_bounds_mm
        if (
            bounds.left < reserved.left - 0.1
            or bounds.bottom < reserved.bottom - 0.1
            or bounds.right > reserved.right + 0.1
            or bounds.top > reserved.top + 0.1
        ):
            blockers.append(_finding("EXEC-021", "actual annotations exceed the planned reserved lane", first.view_id))
        for second in execution.executed_views[index + 1 :]:
            second_plan = planned_views.get(second.view_id)
            if second_plan and second_plan.sheet_index == first_plan.sheet_index and bounds.intersects(second.actual_bounds_mm):
                blockers.append(_finding("EXEC-016", "actual view/annotation envelopes overlap", first.view_id, second.view_id))

    for field, expected in plan.title_fields.items():
        if expected and execution.title_fields_written.get(field) != expected:
            blockers.append(_finding("EXEC-017", f"title field {field} was not written exactly", field))
    if not execution.cad_artifacts_hidden:
        blockers.append(_finding("EXEC-018", "origins/sketches/selection artifacts were not verified hidden"))
    if not execution.human_approval_confirmed:
        blockers.append(_finding("EXEC-019", "human release approval was not confirmed in SolidWorks"))

    # Remove duplicate findings inherited from plan when execution supplies the
    # same evidence; do not remove any blocker by quality score or screenshot.
    unique: dict[str, ValidationFinding] = {}
    for finding in blockers:
        key = digest({"code": finding.code, "message": finding.message, "items": finding.item_ids})
        unique.setdefault(key, finding)
    blockers = list(unique.values())
    status = ReleaseStatus.RELEASE_READY if not blockers else ReleaseStatus.DRAFT
    checks = dict(plan.release_gate.checks)
    checks.update(
        {
            "execution_associative": not any(f.code in {"EXEC-005", "EXEC-006", "EXEC-007", "EXEC-008", "EXEC-009", "EXEC-010", "EXEC-011"} for f in blockers),
            "actual_layout_valid": not any(f.code in {"EXEC-015", "EXEC-016", "EXEC-020", "EXEC-021"} for f in blockers),
            "execution_metadata_complete": not any(f.code == "EXEC-017" for f in blockers),
            "cad_artifacts_hidden": execution.cad_artifacts_hidden,
            "human_approval_confirmed": execution.human_approval_confirmed,
        }
    )
    return ReleaseGate(
        status=status,
        release_ready=not blockers,
        blockers=blockers,
        warnings=warnings,
        checks=checks,
    )
