from __future__ import annotations

from collections import defaultdict

from app.domain.models import ClassifiedFeature, FeatureFamily
from app.services.identity import digest


def _axis_signature(feature: ClassifiedFeature) -> str:
    if feature.axis is None:
        return "NONE"
    vector = feature.axis.normalized()
    sign = 1 if getattr(vector, vector.dominant_axis.lower()) >= 0 else -1
    return f"{vector.dominant_axis}:{sign}"


def _family_signature(feature: ClassifiedFeature) -> str:
    return digest(
        {
            "kind": feature.kind,
            "specification": feature.specification,
            "axis": _axis_signature(feature),
        },
        "fam:",
    )


def group_families(features: list[ClassifiedFeature]) -> list[FeatureFamily]:
    grouped: dict[str, list[ClassifiedFeature]] = defaultdict(list)
    for feature in features:
        grouped[_family_signature(feature)].append(feature)

    families: list[FeatureFamily] = []
    for signature in sorted(grouped):
        members = grouped[signature]
        entity_refs = []
        centers = []
        seen_ref_ids: set[str] = set()
        for member in members:
            for index, entity_ref in enumerate(member.entity_refs):
                if entity_ref.identity in seen_ref_ids:
                    continue
                seen_ref_ids.add(entity_ref.identity)
                entity_refs.append(entity_ref)
                if index < len(member.centers):
                    centers.append(member.centers[index])
            # Non-instance geometry (for example chamfer faces) can have no center.
            if not member.entity_refs:
                centers.extend(member.centers)
        instance_count = len(entity_refs) if centers and len(centers) == len(entity_refs) else sum(
            member.instance_count for member in members
        )
        families.append(
            FeatureFamily(
                family_id=signature,
                kind=members[0].kind,
                feature_ids=sorted(member.feature_id for member in members),
                instance_count=max(instance_count, 1),
                specification=members[0].specification,
                axis=members[0].axis,
                centers=centers,
                entity_refs=entity_refs,
            )
        )
    return families
