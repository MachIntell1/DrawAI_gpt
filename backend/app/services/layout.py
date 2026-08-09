from __future__ import annotations

from dataclasses import dataclass

from app.domain.enums import Orientation, ProjectionMethod, ViewKind
from app.domain.geometry import Rect2
from app.domain.models import DrawingPreferences, FeatureFamily, ModelManifest, ViewPlan


SHEET_SIZES_MM: dict[str, tuple[float, float]] = {
    "A4": (297.0, 210.0),
    "A3": (420.0, 297.0),
    "A2": (594.0, 420.0),
    "A1": (841.0, 594.0),
    "A0": (1189.0, 841.0),
    "A": (279.4, 215.9),
    "B": (431.8, 279.4),
    "C": (558.8, 431.8),
    "D": (863.6, 558.8),
}

STANDARD_SCALES = (10.0, 5.0, 2.0, 1.0, 0.5, 0.2, 0.1, 0.05, 0.02)


@dataclass(frozen=True)
class ViewAxes:
    u: str
    v: str
    normal: str
    solidworks_name: str


BASE_AXES: dict[str, ViewAxes] = {
    "Z": ViewAxes("X", "Y", "Z", "*Front"),
    "Y": ViewAxes("X", "Z", "Y", "*Top"),
    "X": ViewAxes("Z", "Y", "X", "*Right"),
}


def choose_base_axes(model: ModelManifest, families: list[FeatureFamily]) -> ViewAxes:
    scores = {"X": 0.0, "Y": 0.0, "Z": 0.0}
    for family in families:
        if family.axis:
            scores[family.axis.dominant_axis] += max(family.instance_count, 1) * 10.0
    dims = model.bounds.dimensions
    for normal in scores:
        shown = [axis for axis in ("X", "Y", "Z") if axis != normal]
        scores[normal] += dims[shown[0]] * dims[shown[1]] / max(max(dims.values()), 1.0)
    return BASE_AXES[max(scores, key=scores.get)]


def _projected_axes(base: ViewAxes) -> tuple[ViewAxes, ViewAxes]:
    # Roles are relative to the chosen base view.  The SolidWorks plugin creates
    # these through CreateUnfoldedViewAt3 so alignment is API-controlled.
    top = ViewAxes(base.u, base.normal, base.v, "PROJECTED_TOP")
    right = ViewAxes(base.normal, base.v, base.u, "PROJECTED_RIGHT")
    return top, right


def _parse_scale(scale: str) -> float | None:
    normalized = scale.strip().upper()
    if normalized == "AUTO":
        return None
    if ":" not in normalized:
        raise ValueError("scale must be AUTO or N:D")
    numerator, denominator = normalized.split(":", 1)
    value = float(numerator) / float(denominator)
    if not any(abs(value - candidate) < 1e-9 for candidate in STANDARD_SCALES):
        raise ValueError(f"non-standard scale: {scale}")
    return value


def _rect(center_x: float, center_y: float, width: float, height: float) -> Rect2:
    return Rect2(
        left=center_x - width / 2,
        bottom=center_y - height / 2,
        right=center_x + width / 2,
        top=center_y + height / 2,
    )


def plan_views(
    model: ModelManifest,
    families: list[FeatureFamily],
    preferences: DrawingPreferences,
    projection: ProjectionMethod,
) -> tuple[list[ViewPlan], float, tuple[float, float]]:
    sheet_width, sheet_height = SHEET_SIZES_MM[preferences.sheet_size]
    left, right, bottom, top = 12.0, sheet_width - 12.0, 55.0, sheet_height - 12.0
    usable_width, usable_height = right - left, top - bottom
    gap = preferences.view_clearance_mm
    # Reserve real, asymmetric annotation bands, not only text-height
    # clearance. Hole callouts need more horizontal space; stacked rectangular
    # locations need more vertical space. Larger configured clearances can
    # increase both bands and force a smaller standard scale.
    horizontal_lane = max(preferences.annotation_clearance_mm * 3.0, 60.0)
    vertical_lane = max(preferences.annotation_clearance_mm * 2.0, 40.0)

    base = choose_base_axes(model, families)
    projected_top, projected_right = _projected_axes(base)
    dims = model.bounds.dimensions

    # Four-cell layout reserves the empty quadrant for an isometric reference.
    cell_width = (usable_width - gap) / 2.0
    cell_height = (usable_height - gap) / 2.0
    axes_to_size = lambda axes, scale: (dims[axes.u] * scale, dims[axes.v] * scale)
    requested_scale = _parse_scale(preferences.scale)
    candidates = (requested_scale,) if requested_scale is not None else STANDARD_SCALES

    scale = STANDARD_SCALES[-1]
    for candidate in candidates:
        if candidate is None:
            continue
        sizes = [axes_to_size(axes, candidate) for axes in (base, projected_top, projected_right)]
        iso_size = max(dims.values()) * candidate * 0.85
        if all(
            width + 2 * horizontal_lane <= cell_width
            and height + 2 * vertical_lane <= cell_height
            for width, height in sizes
        ) and (
            not preferences.include_isometric
            or (
                iso_size + 2 * horizontal_lane <= cell_width
                and iso_size * 0.75 + 2 * vertical_lane <= cell_height
            )
        ):
            scale = candidate
            break

    x_left = left + cell_width / 2.0
    x_right = right - cell_width / 2.0
    y_low = bottom + cell_height / 2.0
    y_high = top - cell_height / 2.0

    if projection == ProjectionMethod.THIRD_ANGLE:
        centers = {
            "base": (x_left, y_low),
            "top": (x_left, y_high),
            "right": (x_right, y_low),
            "iso": (x_right, y_high),
        }
    else:
        centers = {
            "base": (x_right, y_high),
            "top": (x_right, y_low),
            "right": (x_left, y_high),
            "iso": (x_left, y_low),
        }

    definitions = [
        ("view-front", ViewKind.BASE, Orientation.FRONT, base, "base", None),
        ("view-top", ViewKind.PROJECTED, Orientation.TOP, projected_top, "top", "view-front"),
        ("view-right", ViewKind.PROJECTED, Orientation.RIGHT, projected_right, "right", "view-front"),
    ]
    views: list[ViewPlan] = []
    for view_id, kind, orientation, axes, cell, parent in definitions:
        center_x, center_y = centers[cell]
        width, height = axes_to_size(axes, scale)
        model_rect = _rect(center_x, center_y, max(width, 1.0), max(height, 1.0))
        views.append(
            ViewPlan(
                view_id=view_id,
                sheet_index=1,
                kind=kind,
                orientation=orientation,
                solidworks_view_name=axes.solidworks_name,
                center_x_mm=center_x,
                center_y_mm=center_y,
                scale=scale,
                display_style=(
                    "HIDDEN_LINES_VISIBLE" if preferences.show_hidden_lines else "HIDDEN_LINES_REMOVED"
                ),
                expected_model_bounds_mm=model_rect,
                reserved_annotation_bounds_mm=Rect2(
                    left=model_rect.left - horizontal_lane,
                    bottom=model_rect.bottom - vertical_lane,
                    right=model_rect.right + horizontal_lane,
                    top=model_rect.top + vertical_lane,
                ),
                parent_view_id=parent,
                model_u_axis=axes.u,
                model_v_axis=axes.v,
                model_normal_axis=axes.normal,
            )
        )

    if preferences.include_isometric:
        center_x, center_y = centers["iso"]
        iso_size = max(dims.values()) * scale * 0.75
        model_rect = _rect(center_x, center_y, max(iso_size, 1.0), max(iso_size * 0.75, 1.0))
        views.append(
            ViewPlan(
                view_id="view-isometric",
                sheet_index=1,
                kind=ViewKind.ISOMETRIC,
                orientation=Orientation.ISOMETRIC,
                solidworks_view_name="*Isometric",
                center_x_mm=center_x,
                center_y_mm=center_y,
                scale=scale * 0.75,
                display_style="SHADED",
                expected_model_bounds_mm=model_rect,
                reserved_annotation_bounds_mm=Rect2(
                    left=model_rect.left - horizontal_lane,
                    bottom=model_rect.bottom - vertical_lane,
                    right=model_rect.right + horizontal_lane,
                    top=model_rect.top + vertical_lane,
                ),
                model_u_axis=base.u,
                model_v_axis=base.v,
                model_normal_axis=base.normal,
            )
        )

    stepped_or_blind = any(
        family.kind.value in {"COUNTERBORE_HOLE", "COUNTERSINK_HOLE", "ADVANCED_HOLE"}
        or family.specification.through is False
        for family in families
    )
    if preferences.include_section_view and stepped_or_blind:
        # A section is planned on Sheet 2 with an explicit source view.  A
        # section cannot legally use a parent view on another SolidWorks sheet.
        source_rect = _rect(
            sheet_width * 0.25,
            (bottom + top) / 2,
            dims[base.u] * scale,
            dims[base.v] * scale,
        )
        section_rect = _rect(
            sheet_width * 0.75,
            (bottom + top) / 2,
            dims[base.u] * scale,
            dims[base.v] * scale,
        )
        views.append(
            ViewPlan(
                view_id="view-section-source",
                sheet_index=2,
                kind=ViewKind.BASE,
                orientation=Orientation.FRONT,
                solidworks_view_name=base.solidworks_name,
                center_x_mm=sheet_width * 0.25,
                center_y_mm=(bottom + top) / 2,
                scale=scale,
                display_style="HIDDEN_LINES_REMOVED",
                expected_model_bounds_mm=source_rect,
                reserved_annotation_bounds_mm=Rect2(
                    left=source_rect.left - horizontal_lane,
                    bottom=source_rect.bottom - vertical_lane,
                    right=source_rect.right + horizontal_lane,
                    top=source_rect.top + vertical_lane,
                ),
                model_u_axis=base.u,
                model_v_axis=base.v,
                model_normal_axis=base.normal,
            )
        )
        views.append(
            ViewPlan(
                view_id="view-section-a",
                sheet_index=2,
                kind=ViewKind.SECTION,
                orientation=Orientation.FRONT,
                solidworks_view_name="SECTION_A",
                center_x_mm=sheet_width * 0.75,
                center_y_mm=(bottom + top) / 2,
                scale=scale,
                display_style="HIDDEN_LINES_REMOVED",
                expected_model_bounds_mm=section_rect,
                reserved_annotation_bounds_mm=Rect2(
                    left=section_rect.left - horizontal_lane,
                    bottom=section_rect.bottom - vertical_lane,
                    right=section_rect.right + horizontal_lane,
                    top=section_rect.top + vertical_lane,
                ),
                parent_view_id="view-section-source",
                section_axis=base.u,  # cutting line direction in the base view
                model_u_axis=base.u,
                model_v_axis=base.v,
                model_normal_axis=base.normal,
            )
        )
    return views, scale, (sheet_width, sheet_height)
