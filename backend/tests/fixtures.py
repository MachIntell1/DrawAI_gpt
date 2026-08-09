from __future__ import annotations

from datetime import datetime, timezone

from app.domain.enums import EvidenceSource, ProjectionMethod, UnitSystem
from app.domain.geometry import Bounds3, EntityRef, Point3, Vector3
from app.domain.models import (
    DatumDefinition,
    DrawingPreferences,
    EngineeringIntent,
    FeatureSpecification,
    ModelManifest,
    PlanRequest,
    RawFeature,
    TopologyEvidence,
)


def ref(name: str, entity_type: str = "EDGE") -> EntityRef:
    # Test tokens are opaque stand-ins; production values are base64 persistent refs.
    return EntityRef(token=f"dGVzdC1yZWYt{name}", entity_type=entity_type)


def drawing2_request(
    *,
    profile: str = "ISO_METRIC_2025",
    projection: ProjectionMethod | None = ProjectionMethod.FIRST_ANGLE,
    approved: bool = True,
) -> PlanRequest:
    extremes = {
        "X_MIN": ref("xmin", "FACE"),
        "X_MAX": ref("xmax", "FACE"),
        "Y_MIN": ref("ymin", "FACE"),
        "Y_MAX": ref("ymax", "FACE"),
        "Z_MIN": ref("zmin", "FACE"),
        "Z_MAX": ref("zmax", "FACE"),
    }
    features = [
        RawFeature(
            feature_id="notches",
            name="2x edge relief",
            native_type="Cut-Extrude",
            source=EvidenceSource.BREP_TOPOLOGY,
            entity_refs=[ref("notch-1"), ref("notch-2")],
            centers=[Point3(x=87, y=100, z=8), Point3(x=87, y=0, z=8)],
            axis=Vector3(x=0, y=0, z=1),
            specification=FeatureSpecification(radius=17.5),
            topology=TopologyEvidence(
                surface_kind="cylindrical",
                sweep_angle_deg=180,
                closed_profile=False,
                opens_to_outer_boundary=True,
                is_internal=True,
                radius=17.5,
                entity_refs=[ref("notch-1"), ref("notch-2")],
            ),
            patterned=True,
            pattern_count=2,
        ),
        RawFeature(
            feature_id="edge-fillets",
            name="R2 edge fillets",
            native_type="Fillet",
            entity_refs=[ref("fillet-1"), ref("fillet-2")],
            specification=FeatureSpecification(radius=2),
            topology=TopologyEvidence(
                surface_kind="cylindrical",
                sweep_angle_deg=90,
                tangent_face_count=2,
                radius=2,
                opens_to_outer_boundary=True,
            ),
            pattern_count=2,
        ),
        RawFeature(
            feature_id="plain-11",
            name="4x diameter 11 through",
            native_type="HoleWzd",
            native_subtype="through_hole",
            entity_refs=[ref(f"h11-{index}") for index in range(4)],
            centers=[
                Point3(x=10, y=10, z=8),
                Point3(x=310, y=10, z=8),
                Point3(x=10, y=90, z=8),
                Point3(x=310, y=90, z=8),
            ],
            axis=Vector3(x=0, y=0, z=1),
            specification=FeatureSpecification(diameter=11, through=True),
            patterned=True,
            pattern_count=4,
        ),
        RawFeature(
            feature_id="dowel-8",
            name="2x diameter 8 through",
            native_type="HoleWzd",
            native_subtype="through_hole",
            entity_refs=[ref("h8-1"), ref("h8-2")],
            centers=[Point3(x=30, y=50, z=8), Point3(x=290, y=50, z=8)],
            axis=Vector3(x=0, y=0, z=1),
            specification=FeatureSpecification(diameter=8, through=True),
            patterned=True,
            pattern_count=2,
        ),
        RawFeature(
            feature_id="m6-holes",
            name="6x M6 tapped",
            native_type="HoleWzd",
            native_subtype="tapped_hole",
            entity_refs=[ref(f"m6-{index}") for index in range(6)],
            centers=[Point3(x=50 + 44 * index, y=50, z=8) for index in range(6)],
            axis=Vector3(x=0, y=0, z=1),
            specification=FeatureSpecification(
                thread_designation="M6×1",
                thread_class="6H",
                thread_depth=16,
                drill_depth=18,
                through=False,
            ),
            patterned=True,
            pattern_count=6,
        ),
    ]
    intent = EngineeringIntent(
        part_number="PLATE-320-001",
        drawing_number="DWG-PLATE-320-001",
        revision="A",
        description="PRISMATIC MOUNTING PLATE",
        material="AISI 1018",
        material_specification="COMPANY-MAT-1018",
        edge_requirement="COMPANY-EDGE-001",
        general_tolerance_policy_id="POLICY-ISO-001" if profile.startswith("ISO") else "POLICY-ASME-001",
        internal_thread_class="6H" if profile.startswith("ISO") else "2B",
        datums=[
            DatumDefinition(label="A", role="PRIMARY", feature_ref=extremes["Z_MIN"], approved=True),
            DatumDefinition(label="B", role="X_ORIGIN", feature_ref=extremes["X_MIN"], approved=True),
            DatumDefinition(label="C", role="Y_ORIGIN", feature_ref=extremes["Y_MIN"], approved=True),
            DatumDefinition(label="D", role="Z_ORIGIN", feature_ref=extremes["Z_MIN"], approved=True),
        ],
        approved_by="Test Engineer" if approved else None,
        approved_at=datetime(2026, 8, 8, tzinfo=timezone.utc) if approved else None,
        approval_id="ENG-APPROVAL-001" if approved else None,
    )
    manifest = ModelManifest(
        model_name="prismatic-plate",
        model_hash="0123456789abcdef",
        file_path=r"C:\models\prismatic-plate.SLDPRT",
        units=UnitSystem.MM,
        bounds=Bounds3(
            minimum=Point3(x=0, y=0, z=0),
            maximum=Point3(x=320, y=100, z=16),
            extreme_refs=extremes,
        ),
        material_from_model="AISI 1018",
        features=features,
        engineering_intent=intent,
    )
    return PlanRequest(
        model_data=manifest,
        preferences=DrawingPreferences(
            standard_profile_id=profile,
            projection=projection,
            sheet_size="A3" if profile.startswith("ISO") else "B",
            units=UnitSystem.MM,
            include_isometric=True,
            include_section_view=True,
            company_policy_id=intent.general_tolerance_policy_id,
        ),
    )

