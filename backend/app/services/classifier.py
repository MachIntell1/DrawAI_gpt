from __future__ import annotations

from copy import deepcopy

from app.domain.enums import EvidenceSource, FeatureKind
from app.domain.models import ClassifiedFeature, FeatureSpecification, RawFeature


NATIVE_TYPES = {
    "fillet": FeatureKind.FILLET,
    "round": FeatureKind.FILLET,
    "chamfer": FeatureKind.CHAMFER,
    "holewzd": FeatureKind.PLAIN_HOLE,
    "hole wizard": FeatureKind.PLAIN_HOLE,
    "advancedhole": FeatureKind.ADVANCED_HOLE,
}


def _normalized(value: str | None) -> str:
    return (value or "").strip().lower().replace("_", " ").replace("-", " ")


def _native_kind(raw: RawFeature) -> FeatureKind | None:
    native = _normalized(raw.native_type)
    subtype = _normalized(raw.native_subtype)
    spec = raw.specification

    if "fillet" in native or native in {"round", "constant radius"}:
        return FeatureKind.FILLET
    if "chamfer" in native:
        return FeatureKind.CHAMFER
    if "slot" in native or "slot" in subtype:
        return FeatureKind.SLOT
    if "hole" in native or "hole" in subtype or native == "holewzd":
        if spec.thread_designation or "tap" in subtype or "thread" in subtype:
            return FeatureKind.TAPPED_HOLE
        if spec.counterbore_diameter or "counterbore" in subtype or "cbore" in subtype:
            return FeatureKind.COUNTERBORE_HOLE
        if spec.countersink_diameter or "countersink" in subtype or "csink" in subtype:
            return FeatureKind.COUNTERSINK_HOLE
        if "advanced" in native:
            return FeatureKind.ADVANCED_HOLE
        return FeatureKind.PLAIN_HOLE
    if "boss" in native or "extrude" in native and "cut" not in native:
        return FeatureKind.BOSS
    if "cut" in native or "pocket" in native:
        return None
    return NATIVE_TYPES.get(native)


def _topology_kind(raw: RawFeature, model_shortest_size: float) -> tuple[FeatureKind | None, float]:
    topology = raw.topology
    if topology is None or _normalized(topology.surface_kind) not in {"cylinder", "cylindrical"}:
        return None, 0.0

    sweep = topology.sweep_angle_deg
    full_sweep = sweep is None or sweep >= 359.0
    is_open = topology.opens_to_outer_boundary is True or topology.closed_profile is False

    # Tangency is decisive for a blend.  It takes precedence over concavity;
    # concave fillets are otherwise easily misread as small holes.
    if topology.tangent_face_count >= 2 and topology.radius:
        if topology.radius <= max(model_shortest_size * 0.5, 0.5):
            return FeatureKind.FILLET, 0.98

    if is_open or not full_sweep:
        return FeatureKind.EDGE_NOTCH, 0.98
    if full_sweep and topology.closed_profile is True and topology.is_internal is True:
        return FeatureKind.PLAIN_HOLE, 0.96
    if full_sweep and topology.closed_profile is True and topology.is_internal is False:
        return FeatureKind.BOSS, 0.94
    return None, 0.0


def _compare_native_and_topology(
    native: FeatureKind | None, topology: FeatureKind | None
) -> list[str]:
    if native is None or topology is None:
        return []
    compatible_holes = {
        FeatureKind.PLAIN_HOLE,
        FeatureKind.COUNTERBORE_HOLE,
        FeatureKind.COUNTERSINK_HOLE,
        FeatureKind.TAPPED_HOLE,
        FeatureKind.ADVANCED_HOLE,
    }
    if native == topology or native in compatible_holes and topology == FeatureKind.PLAIN_HOLE:
        return []
    return [f"native classification {native.value} conflicts with topology {topology.value}"]


def _validate_specification(kind: FeatureKind, spec: FeatureSpecification) -> list[str]:
    conflicts: list[str] = []
    if kind in {
        FeatureKind.PLAIN_HOLE,
        FeatureKind.COUNTERBORE_HOLE,
        FeatureKind.COUNTERSINK_HOLE,
    } and spec.diameter is None:
        conflicts.append("hole diameter is missing")
    if kind == FeatureKind.COUNTERBORE_HOLE and (
        spec.counterbore_diameter is None or spec.counterbore_depth is None
    ):
        conflicts.append("counterbore diameter/depth is incomplete")
    if kind == FeatureKind.COUNTERSINK_HOLE and (
        spec.countersink_diameter is None or spec.countersink_angle_deg is None
    ):
        conflicts.append("countersink diameter/angle is incomplete")
    if kind == FeatureKind.TAPPED_HOLE and not spec.thread_designation:
        conflicts.append("thread designation is missing")
    if kind in {FeatureKind.FILLET, FeatureKind.EDGE_NOTCH} and spec.radius is None:
        conflicts.append("radius is missing")
    if kind == FeatureKind.CHAMFER and spec.chamfer_distance is None:
        conflicts.append("chamfer size is missing")
    return conflicts


def classify_feature(raw: RawFeature, model_shortest_size: float) -> ClassifiedFeature | None:
    if raw.suppressed:
        return None
    native = _native_kind(raw)
    topology, topology_confidence = _topology_kind(raw, model_shortest_size)
    conflicts = _compare_native_and_topology(native, topology)

    # Native Hole Wizard/fillet/chamfer data is authoritative when present.
    # B-rep is authoritative for otherwise untyped cylindrical surfaces.
    if native is not None:
        kind = native
        source = raw.source
        confidence = 1.0 if raw.source == EvidenceSource.SOLIDWORKS_NATIVE else 0.95
    elif topology is not None:
        kind = topology
        source = EvidenceSource.BREP_TOPOLOGY
        confidence = topology_confidence
    else:
        kind = FeatureKind.UNKNOWN
        source = raw.source
        confidence = 0.0
        conflicts.append("feature cannot be classified deterministically")

    spec = deepcopy(raw.specification)
    if spec.radius is None and raw.topology and raw.topology.radius:
        spec.radius = raw.topology.radius
    conflicts.extend(_validate_specification(kind, spec))
    count = raw.pattern_count or max(len(raw.centers), 1)

    return ClassifiedFeature(
        feature_id=raw.feature_id,
        name=raw.name,
        kind=kind,
        source=source,
        confidence=confidence,
        feature_ref=raw.feature_ref,
        entity_refs=raw.entity_refs or (raw.topology.entity_refs if raw.topology else []),
        centers=raw.centers,
        axis=raw.axis,
        specification=spec,
        topology=raw.topology,
        instance_count=count,
        conflicts=conflicts,
    )


def classify_all(features: list[RawFeature], model_dimensions: dict[str, float]) -> list[ClassifiedFeature]:
    shortest = min(model_dimensions.values())
    return [
        classified
        for raw in features
        if (classified := classify_feature(raw, shortest)) is not None
    ]

