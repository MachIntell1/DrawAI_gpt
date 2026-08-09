from __future__ import annotations

import hashlib
import json
from dataclasses import asdict, dataclass

from app.domain.enums import ProjectionMethod, StandardFamily, UnitSystem
from app.domain.models import DrawingPreferences, StandardsSnapshot


@dataclass(frozen=True)
class StandardProfile:
    profile_id: str
    family: StandardFamily
    edition_label: str
    default_projection: ProjectionMethod
    default_units: UnitSystem
    standard_references: tuple[str, ...]
    quantity_separator: str
    through_word: str
    depth_word: str
    diameter_symbol: str = "⌀"
    radius_symbol: str = "R"

    @property
    def digest(self) -> str:
        payload = json.dumps(asdict(self), sort_keys=True, default=str, separators=(",", ":"))
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()


PROFILES: dict[str, StandardProfile] = {
    "ISO_METRIC_2025": StandardProfile(
        profile_id="ISO_METRIC_2025",
        family=StandardFamily.ISO,
        edition_label="ISO TPD/GPS profile, controlled 2025 baseline",
        default_projection=ProjectionMethod.FIRST_ANGLE,
        default_units=UnitSystem.MM,
        standard_references=(
            "ISO 128-3:2022",
            "ISO 129-1:2018 + Amd 1:2020",
            "ISO 5455:1979",
            "ISO 5456-2:1996",
            "ISO 5457:1999 + Amd 1:2010",
            "ISO 7200:2004",
            "ISO 8015:2011",
            "ISO 1101:2017",
            "ISO 5458:2018",
            "ISO 5459:2024",
            "ISO 14405-1:2025",
            "ISO 21920-1:2021",
            "ISO 22081:2021",
        ),
        quantity_separator="×",
        through_word="THRU",
        depth_word="DEEP",
    ),
    "ASME_Y14_2018": StandardProfile(
        profile_id="ASME_Y14_2018",
        family=StandardFamily.ASME,
        edition_label="ASME Y14 controlled 2018 drawing baseline",
        default_projection=ProjectionMethod.THIRD_ANGLE,
        default_units=UnitSystem.INCH,
        standard_references=(
            "ASME Y14.1-2020",
            "ASME Y14.2-2014 (R2020)",
            "ASME Y14.3-2012 (R2018)",
            "ASME Y14.5-2018",
            "ASME Y14.6-2001 (R2018)",
            "ASME Y14.24-2020",
            "ASME Y14.36-2018",
            "ASME Y14.100-2017",
        ),
        quantity_separator="X",
        through_word="THRU",
        depth_word="DEEP",
    ),
}


def get_profile(profile_id: str) -> StandardProfile:
    try:
        return PROFILES[profile_id.upper()]
    except KeyError as exc:
        raise ValueError(f"unsupported standard_profile_id: {profile_id}") from exc


def snapshot(preferences: DrawingPreferences) -> StandardsSnapshot:
    profile = get_profile(preferences.standard_profile_id)
    projection = preferences.projection or profile.default_projection
    units = preferences.units or profile.default_units
    policy = preferences.company_policy_id
    digest_source = {
        "profile_digest": profile.digest,
        "projection": projection.value,
        "units": units.value,
        "policy_id": policy,
    }
    digest = hashlib.sha256(
        json.dumps(digest_source, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()
    return StandardsSnapshot(
        profile_id=profile.profile_id,
        standard_family=profile.family,
        edition_label=profile.edition_label,
        projection=projection,
        units=units,
        standard_references=list(profile.standard_references),
        policy_id=policy,
        digest=digest,
    )


def public_profiles() -> list[dict[str, object]]:
    return [
        {
            "profile_id": profile.profile_id,
            "family": profile.family,
            "edition_label": profile.edition_label,
            "default_projection": profile.default_projection,
            "default_units": profile.default_units,
            "standard_references": profile.standard_references,
        }
        for profile in PROFILES.values()
    ]

