from __future__ import annotations

from decimal import Decimal

from app.domain.enums import FeatureKind, StandardFamily, UnitSystem
from app.domain.models import EngineeringIntent, FeatureFamily
from app.standards.profiles import StandardProfile


def to_display(value_mm: float | None, units: UnitSystem) -> float | None:
    if value_mm is None:
        return None
    return value_mm / 25.4 if units == UnitSystem.INCH else value_mm


def format_number(value: float | None, units: UnitSystem = UnitSystem.MM) -> str:
    if value is None:
        return "?"
    decimal = Decimal(str(value)).normalize()
    text = format(decimal, "f")
    if "." in text:
        text = text.rstrip("0").rstrip(".")
    if units == UnitSystem.INCH and text.startswith("0."):
        return text[1:]
    return text


def quantity_prefix(quantity: int, profile: StandardProfile) -> str:
    if quantity <= 1:
        return ""
    return f"{quantity}{profile.quantity_separator} "


def hole_callout(
    family: FeatureFamily,
    profile: StandardProfile,
    intent: EngineeringIntent,
    units: UnitSystem,
) -> tuple[str, list[str]]:
    spec = family.specification
    prefix = quantity_prefix(family.instance_count, profile)
    dia = f"{profile.diameter_symbol}{format_number(to_display(spec.diameter, units), units)}"
    end_condition = profile.through_word if spec.through else (
        f"{format_number(to_display(spec.depth, units), units)} {profile.depth_word}" if spec.depth else "END CONDITION REQUIRED"
    )
    blockers: list[str] = []

    if family.kind == FeatureKind.TAPPED_HOLE:
        thread = spec.thread_designation or "THREAD REQUIRED"
        thread_class = spec.thread_class or intent.internal_thread_class
        if not thread_class:
            blockers.append(f"{family.family_id}: internal thread class is not approved")
        depth = spec.thread_depth or spec.depth
        if spec.through:
            end = profile.through_word
        elif depth:
            end = f"{format_number(to_display(depth, units), units)} {profile.depth_word}"
        else:
            end = "THREAD DEPTH REQUIRED"
            blockers.append(f"{family.family_id}: thread depth/through condition is missing")
        class_text = f"-{thread_class}" if thread_class else ""
        return f"{prefix}{thread}{class_text} {end}", blockers

    if spec.diameter is None:
        blockers.append(f"{family.family_id}: primary diameter is missing")
    if not spec.through and spec.depth is None:
        blockers.append(f"{family.family_id}: hole depth/through condition is missing")

    if family.kind == FeatureKind.COUNTERBORE_HOLE:
        if spec.counterbore_diameter is None or spec.counterbore_depth is None:
            blockers.append(f"{family.family_id}: counterbore definition is incomplete")
        second = (
            f"⌴ {profile.diameter_symbol}{format_number(to_display(spec.counterbore_diameter, units), units)} × "
            f"{format_number(to_display(spec.counterbore_depth, units), units)} {profile.depth_word}"
        )
        return f"{prefix}{dia} {end_condition}\n{second}", blockers

    if family.kind == FeatureKind.COUNTERSINK_HOLE:
        if spec.countersink_diameter is None or spec.countersink_angle_deg is None:
            blockers.append(f"{family.family_id}: countersink definition is incomplete")
        second = (
            f"⌵ {profile.diameter_symbol}{format_number(to_display(spec.countersink_diameter, units), units)} × "
            f"{format_number(spec.countersink_angle_deg)}°"
        )
        return f"{prefix}{dia} {end_condition}\n{second}", blockers

    return f"{prefix}{dia} {end_condition}", blockers


def profile_callout(
    family: FeatureFamily, profile: StandardProfile, units: UnitSystem
) -> tuple[str, list[str]]:
    spec = family.specification
    prefix = quantity_prefix(family.instance_count, profile)
    if family.kind in {FeatureKind.FILLET, FeatureKind.EDGE_NOTCH}:
        if spec.radius is None:
            return f"{prefix}RADIUS REQUIRED", [f"{family.family_id}: radius is missing"]
        return f"{prefix}{profile.radius_symbol}{format_number(to_display(spec.radius, units), units)}", []
    if family.kind == FeatureKind.CHAMFER:
        if spec.chamfer_distance is None:
            return f"{prefix}CHAMFER SIZE REQUIRED", [f"{family.family_id}: chamfer size is missing"]
        if spec.chamfer_angle_deg is None:
            return f"{prefix}{format_number(to_display(spec.chamfer_distance, units), units)} CHAMFER", [
                f"{family.family_id}: chamfer angle/second distance is missing"
            ]
        separator = " × " if profile.family == StandardFamily.ISO else " X "
        return (
            f"{prefix}{format_number(to_display(spec.chamfer_distance, units), units)}{separator}"
            f"{format_number(spec.chamfer_angle_deg)}°",
            [],
        )
    return "", []
