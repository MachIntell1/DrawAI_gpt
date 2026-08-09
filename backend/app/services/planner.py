from __future__ import annotations

from datetime import datetime, timezone

from app.domain.enums import AnnotationKind, ReleaseStatus
from app.domain.models import (
    AnnotationPlan,
    DrawingPlan,
    PlanRequest,
    ReleaseGate,
)
from app.services.classifier import classify_all
from app.services.families import group_families
from app.services.identity import digest
from app.services.layout import plan_views
from app.services.references import select_reference_scheme
from app.services.requirements import build_requirements
from app.services.validation import validate_plan
from app.standards.profiles import get_profile, snapshot


def _scale_ratio(scale: float) -> tuple[int, int]:
    mapping = {
        10.0: (10, 1),
        5.0: (5, 1),
        2.0: (2, 1),
        1.0: (1, 1),
        0.5: (1, 2),
        0.2: (1, 5),
        0.1: (1, 10),
        0.05: (1, 20),
        0.02: (1, 50),
    }
    return mapping[scale]


def _title_fields(request: PlanRequest) -> dict[str, str]:
    model = request.model_data
    intent = model.engineering_intent
    properties = {key.upper(): value for key, value in model.custom_properties.items()}
    return {
        "PART_NUMBER": intent.part_number or properties.get("PART_NUMBER", ""),
        "DRAWING_NUMBER": intent.drawing_number or properties.get("DRAWING_NUMBER", ""),
        "REVISION": intent.revision or properties.get("REVISION", ""),
        "DESCRIPTION": intent.description or properties.get("DESCRIPTION", model.model_name),
        "MATERIAL": intent.material or model.material_from_model or properties.get("MATERIAL", ""),
        "HEAT_TREATMENT": intent.heat_treatment or "",
        "COATING": intent.coating or "",
        "UNITS": (request.preferences.units or get_profile(request.preferences.standard_profile_id).default_units).value,
    }


def generate_plan(request: PlanRequest) -> DrawingPlan:
    model = request.model_data
    preferences = request.preferences
    standards = snapshot(preferences)
    profile = get_profile(preferences.standard_profile_id)
    features = classify_all(model.features, model.bounds.dimensions)
    families = group_families(features)
    reference_scheme = select_reference_scheme(model)
    views, scale, (sheet_width, sheet_height) = plan_views(
        model, families, preferences, standards.projection
    )
    requirements, annotations, planner_blockers = build_requirements(
        model,
        families,
        reference_scheme,
        views,
        standards,
        profile,
        preferences,
    )
    numerator, denominator = _scale_ratio(scale)
    title_fields = _title_fields(request)
    plan_id = digest(
        {
            "model_hash": model.model_hash,
            "configuration": model.configuration,
            "standards": standards.digest,
            "preferences": preferences,
        },
        "plan:",
    )
    plan_digest = digest(
        {
            "plan_id": plan_id,
            "features": features,
            "families": families,
            "reference_scheme": reference_scheme,
            "views": views,
            "requirements": requirements,
            "annotations": annotations,
            "title_fields": title_fields,
        },
        "pd:",
    )
    placeholder = ReleaseGate(status=ReleaseStatus.DRAFT, release_ready=False)
    plan = DrawingPlan(
        plan_id=plan_id,
        plan_digest=plan_digest,
        model_hash=model.model_hash,
        configuration=model.configuration,
        standards=standards,
        sheet_size=preferences.sheet_size,
        sheet_width_mm=sheet_width,
        sheet_height_mm=sheet_height,
        scale_numerator=numerator,
        scale_denominator=denominator,
        classified_features=features,
        feature_families=families,
        reference_scheme=reference_scheme,
        views=views,
        requirements=requirements,
        annotations=annotations,
        title_fields=title_fields,
        release_gate=placeholder,
        generated_at=datetime.now(timezone.utc),
    )
    plan.release_gate = validate_plan(plan, model, planner_blockers)
    # A plan-ready drawing is still only a generated draft.  The plugin removes
    # this non-controlling watermark only after executed associations and the
    # separate human release confirmation also pass.
    plan.annotations.append(
        AnnotationPlan(
            annotation_id=digest({"plan": plan.plan_id, "kind": "DRAFT_WATERMARK"}, "ann:"),
            kind=AnnotationKind.DRAFT_WATERMARK,
            text="DRAFT - NOT FOR MANUFACTURING",
            position_x_mm=plan.sheet_width_mm / 2,
            position_y_mm=plan.sheet_height_mm / 2,
        )
    )
    # Bind the immutable digest to the final executable content, including the
    # release watermark.  Timestamps and diagnostic wording are excluded so
    # identical inputs remain idempotent.
    plan.plan_digest = digest(
        {
            "plan_id": plan.plan_id,
            "standards": plan.standards,
            "features": plan.classified_features,
            "families": plan.feature_families,
            "reference_scheme": plan.reference_scheme,
            "views": plan.views,
            "requirements": plan.requirements,
            "annotations": plan.annotations,
            "title_fields": plan.title_fields,
        },
        "pd:",
    )
    return plan
