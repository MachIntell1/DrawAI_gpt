from __future__ import annotations

from app.domain.enums import AnnotationKind, FeatureKind, RequirementKind, UnitSystem
from app.domain.geometry import EntityRef
from app.domain.models import (
    AnnotationPlan,
    DrawingPreferences,
    FeatureFamily,
    ModelManifest,
    ReferenceScheme,
    RequirementPlan,
    RequirementSpecification,
    StandardsSnapshot,
    ViewPlan,
)
from app.services.callouts import hole_callout, profile_callout, to_display
from app.services.identity import digest, measurand_key, specification_key
from app.standards.profiles import StandardProfile


HOLE_KINDS = {
    FeatureKind.PLAIN_HOLE,
    FeatureKind.COUNTERBORE_HOLE,
    FeatureKind.COUNTERSINK_HOLE,
    FeatureKind.TAPPED_HOLE,
    FeatureKind.ADVANCED_HOLE,
}


def _refs_identity(refs: list[EntityRef]) -> list[str]:
    return [ref.identity for ref in refs]


def _make_requirement(
    *,
    model: ModelManifest,
    kind: RequirementKind,
    feature_ids: list[str],
    geometry_refs: list[EntityRef],
    reference_refs: list[EntityRef],
    characteristic: str,
    measurement_axis: str,
    extent: str,
    view_id: str,
    spec: RequirementSpecification,
    lane: str,
) -> RequirementPlan:
    key = measurand_key(
        configuration=model.configuration,
        feature_ids=feature_ids,
        characteristic=characteristic,
        geometry_ref_ids=_refs_identity(geometry_refs),
        reference_ref_ids=_refs_identity(reference_refs),
        measurement_axis=measurement_axis,
        controlled_extent=extent,
    )
    spec_key = specification_key(spec)
    return RequirementPlan(
        requirement_id=digest({"measurand": key, "specification": spec_key}, "req:"),
        measurand_key=key,
        specification_key=spec_key,
        kind=kind,
        feature_ids=feature_ids,
        geometry_refs=geometry_refs,
        reference_refs=reference_refs,
        characteristic=characteristic,
        measurement_axis=measurement_axis,  # type: ignore[arg-type]
        controlled_extent=extent,
        view_id=view_id,
        specification=spec,
        placement_lane=lane,
    )


def _view_for_normal(views: list[ViewPlan], normal_axis: str) -> ViewPlan:
    return next((view for view in views if view.model_normal_axis == normal_axis and view.kind.value != "ISOMETRIC"), views[0])


def _view_showing_axis(views: list[ViewPlan], axis: str) -> ViewPlan:
    return _view_for_normal(views, axis)


def build_requirements(
    model: ModelManifest,
    families: list[FeatureFamily],
    references: ReferenceScheme,
    views: list[ViewPlan],
    standards: StandardsSnapshot,
    profile: StandardProfile,
    preferences: DrawingPreferences,
) -> tuple[list[RequirementPlan], list[AnnotationPlan], list[str]]:
    requirements: list[RequirementPlan] = []
    annotations: list[AnnotationPlan] = []
    blockers: list[str] = []
    units = standards.units
    extremes = {key.upper(): ref for key, ref in model.bounds.extreme_refs.items()}

    base_view = next(view for view in views if view.view_id == "view-front")
    axis_view = {
        base_view.model_u_axis: base_view,
        base_view.model_v_axis: base_view,
    }
    for view in views:
        if view.kind.value != "ISOMETRIC":
            axis_view.setdefault(view.model_u_axis, view)
            axis_view.setdefault(view.model_v_axis, view)

    for axis in ("X", "Y", "Z"):
        geometry_refs = [ref for key in (f"{axis}_MIN", f"{axis}_MAX") if (ref := extremes.get(key))]
        view = axis_view.get(axis, base_view)
        lane = "BOTTOM_OUTER" if view.model_u_axis == axis else "LEFT_OUTER"
        spec = RequirementSpecification(
            nominal_value_mm=model.bounds.size(axis),
            display_value=to_display(model.bounds.size(axis), units),
            quantity=1,
            unit=units,
        )
        requirements.append(
            _make_requirement(
                model=model,
                kind=RequirementKind.OVERALL_SIZE,
                feature_ids=["BODY"],
                geometry_refs=geometry_refs,
                reference_refs=[],
                characteristic=f"OVERALL_{axis}",
                measurement_axis=axis,
                extent="FULL_BODY",
                view_id=view.view_id,
                spec=spec,
                lane=lane,
            )
        )

    for family in families:
        if family.kind == FeatureKind.UNKNOWN:
            continue
        axis = family.axis.dominant_axis if family.axis else base_view.model_normal_axis
        true_shape_view = _view_showing_axis(views, axis)

        if family.kind in HOLE_KINDS:
            text, callout_blockers = hole_callout(
                family, profile, model.engineering_intent, units
            )
            blockers.extend(callout_blockers)
            spec = RequirementSpecification(
                nominal_value_mm=family.specification.diameter,
                display_value=to_display(family.specification.diameter, units),
                display_text=text,
                tolerance_type=(family.specification.tolerance or {}).get("type"),
                upper=(family.specification.tolerance or {}).get("upper"),
                lower=(family.specification.tolerance or {}).get("lower"),
                quantity=family.instance_count,
                unit=units,
            )
            requirements.append(
                _make_requirement(
                    model=model,
                    kind=(RequirementKind.THREAD_CALLOUT if family.kind == FeatureKind.TAPPED_HOLE else RequirementKind.HOLE_CALLOUT),
                    feature_ids=family.feature_ids,
                    geometry_refs=family.entity_refs,
                    reference_refs=[],
                    characteristic=family.kind.value,
                    measurement_axis="RADIAL",
                    extent="EXACT_FEATURE_FAMILY",
                    view_id=true_shape_view.view_id,
                    spec=spec,
                    lane="RIGHT_CALLOUT",
                )
            )
            for index, entity_ref in enumerate(family.entity_refs):
                annotations.append(
                    AnnotationPlan(
                        annotation_id=digest(
                            {"kind": "CENTER_MARK", "ref": entity_ref.identity, "view": true_shape_view.view_id},
                            "ann:",
                        ),
                        kind=AnnotationKind.CENTER_MARK,
                        view_id=true_shape_view.view_id,
                        geometry_refs=[entity_ref],
                    )
                )

        elif family.kind in {FeatureKind.FILLET, FeatureKind.EDGE_NOTCH, FeatureKind.CHAMFER}:
            text, profile_blockers = profile_callout(family, profile, units)
            blockers.extend(profile_blockers)
            kind = RequirementKind.CHAMFER if family.kind == FeatureKind.CHAMFER else RequirementKind.RADIUS
            value = (
                family.specification.chamfer_distance
                if family.kind == FeatureKind.CHAMFER
                else family.specification.radius
            )
            requirements.append(
                _make_requirement(
                    model=model,
                    kind=kind,
                    feature_ids=family.feature_ids,
                    geometry_refs=family.entity_refs,
                    reference_refs=[],
                    characteristic=family.kind.value,
                    measurement_axis="RADIAL" if kind == RequirementKind.RADIUS else "ANGULAR",
                    extent="EXACT_FEATURE_FAMILY",
                    view_id=true_shape_view.view_id,
                    spec=RequirementSpecification(
                        nominal_value_mm=value,
                        display_value=to_display(value, units),
                        display_text=text,
                        quantity=family.instance_count,
                        unit=units,
                    ),
                    lane="RIGHT_CALLOUT",
                )
            )

        # Every center is located independently in rectangular coordinates.
        # Equal values are retained because the entity and axis differ.
        if family.centers and family.kind not in {FeatureKind.FILLET, FeatureKind.CHAMFER}:
            origin_refs = {"X": references.x_origin_ref, "Y": references.y_origin_ref, "Z": references.z_origin_ref}
            display_axes = (true_shape_view.model_u_axis, true_shape_view.model_v_axis)
            for index, center in enumerate(family.centers):
                feature_ref = family.entity_refs[index] if index < len(family.entity_refs) else None
                for coordinate_axis, lane in zip(display_axes, ("BOTTOM_ORDINATE", "LEFT_ORDINATE")):
                    origin_ref = origin_refs.get(coordinate_axis)
                    geometry_refs = [feature_ref] if feature_ref else []
                    reference_refs = [origin_ref] if origin_ref else []
                    nominal = center.coordinate(coordinate_axis) - model.bounds.minimum.coordinate(coordinate_axis)
                    requirements.append(
                        _make_requirement(
                            model=model,
                            kind=RequirementKind.FEATURE_LOCATION,
                            feature_ids=family.feature_ids,
                            geometry_refs=geometry_refs,
                            reference_refs=reference_refs,
                            characteristic=f"CENTER_{coordinate_axis}",
                            measurement_axis=coordinate_axis,
                            extent=f"INSTANCE_{index + 1}",
                            view_id=true_shape_view.view_id,
                            spec=RequirementSpecification(
                                nominal_value_mm=nominal,
                                display_value=to_display(nominal, units),
                                unit=units,
                            ),
                            lane=lane,
                        )
                    )

    # Idempotent exact-key consolidation.  Different specification for the same
    # measurand remains visible to the validator as a conflict.
    unique: dict[tuple[str, str], RequirementPlan] = {}
    for requirement in requirements:
        unique.setdefault((requirement.measurand_key, requirement.specification_key), requirement)
    requirements = list(unique.values())
    return requirements, annotations, blockers
