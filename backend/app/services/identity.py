from __future__ import annotations

import hashlib
import json
from typing import Any

from pydantic import BaseModel


def canonical(value: Any) -> Any:
    if isinstance(value, BaseModel):
        return canonical(value.model_dump(mode="json", exclude_none=True))
    if isinstance(value, dict):
        return {key: canonical(value[key]) for key in sorted(value)}
    if isinstance(value, (list, tuple, set)):
        normalized = [canonical(item) for item in value]
        if isinstance(value, set):
            return sorted(normalized, key=lambda item: json.dumps(item, sort_keys=True))
        return normalized
    if isinstance(value, float):
        return round(value, 9)
    return value


def digest(value: Any, prefix: str = "") -> str:
    payload = json.dumps(canonical(value), sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return f"{prefix}{hashlib.sha256(payload.encode('utf-8')).hexdigest()}"


def measurand_key(
    *,
    configuration: str,
    feature_ids: list[str],
    characteristic: str,
    geometry_ref_ids: list[str],
    reference_ref_ids: list[str],
    measurement_axis: str,
    controlled_extent: str,
) -> str:
    return digest(
        {
            "configuration": configuration,
            "feature_ids": sorted(feature_ids),
            "characteristic": characteristic,
            "geometry_refs": sorted(geometry_ref_ids),
            "reference_refs": sorted(reference_ref_ids),
            "measurement_axis": measurement_axis,
            "controlled_extent": controlled_extent,
        },
        "mk:",
    )


def specification_key(specification: Any) -> str:
    return digest(specification, "sk:")

