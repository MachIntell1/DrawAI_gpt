from __future__ import annotations

from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, Field, model_validator

from app.domain.enums import (
    AnnotationKind,
    AssociationStatus,
    DocumentType,
    EvidenceSource,
    FeatureKind,
    Orientation,
    ProjectionMethod,
    ReleaseStatus,
    RequirementKind,
    Severity,
    StandardFamily,
    UnitSystem,
    ViewKind,
)
from app.domain.geometry import Bounds3, EntityRef, Point3, Rect2, Vector3


class TopologyEvidence(BaseModel):
    surface_kind: str | None = None
    sweep_angle_deg: float | None = None
    closed_profile: bool | None = None
    opens_to_outer_boundary: bool | None = None
    tangent_face_count: int = 0
    is_internal: bool | None = None
    radius: float | None = None
    axial_length: float | None = None
    loop_count: int | None = None
    entity_refs: list[EntityRef] = Field(default_factory=list)


class FeatureSpecification(BaseModel):
    diameter: float | None = Field(default=None, gt=0)
    depth: float | None = Field(default=None, gt=0)
    through: bool | None = None
    counterbore_diameter: float | None = Field(default=None, gt=0)
    counterbore_depth: float | None = Field(default=None, gt=0)
    countersink_diameter: float | None = Field(default=None, gt=0)
    countersink_angle_deg: float | None = Field(default=None, gt=0, lt=180)
    radius: float | None = Field(default=None, gt=0)
    width: float | None = Field(default=None, gt=0)
    length: float | None = Field(default=None, gt=0)
    chamfer_distance: float | None = Field(default=None, gt=0)
    chamfer_angle_deg: float | None = Field(default=None, gt=0, lt=180)
    thread_designation: str | None = None
    thread_pitch: float | None = Field(default=None, gt=0)
    thread_class: str | None = None
    thread_depth: float | None = Field(default=None, gt=0)
    drill_depth: float | None = Field(default=None, gt=0)
    tolerance: dict[str, Any] | None = None


class RawFeature(BaseModel):
    feature_id: str = Field(min_length=1)
    name: str
    native_type: str
    native_subtype: str | None = None
    source: EvidenceSource = EvidenceSource.SOLIDWORKS_NATIVE
    feature_ref: EntityRef | None = None
    entity_refs: list[EntityRef] = Field(default_factory=list)
    centers: list[Point3] = Field(default_factory=list)
    axis: Vector3 | None = None
    specification: FeatureSpecification = Field(default_factory=FeatureSpecification)
    topology: TopologyEvidence | None = None
    patterned: bool = False
    pattern_count: int | None = Field(default=None, ge=1)
    suppressed: bool = False
    parameters: dict[str, Any] = Field(default_factory=dict)


class ClassifiedFeature(BaseModel):
    feature_id: str
    name: str
    kind: FeatureKind
    source: EvidenceSource
    confidence: float = Field(ge=0, le=1)
    feature_ref: EntityRef | None = None
    entity_refs: list[EntityRef] = Field(default_factory=list)
    centers: list[Point3] = Field(default_factory=list)
    axis: Vector3 | None = None
    specification: FeatureSpecification
    topology: TopologyEvidence | None = None
    instance_count: int = Field(ge=1)
    conflicts: list[str] = Field(default_factory=list)


class DatumDefinition(BaseModel):
    label: str = Field(pattern=r"^[A-Z][A-Z0-9]?$", min_length=1, max_length=2)
    feature_ref: EntityRef
    role: Literal["PRIMARY", "X_ORIGIN", "Y_ORIGIN", "Z_ORIGIN", "OTHER"] = "OTHER"
    approved: bool = False


class EngineeringIntent(BaseModel):
    part_number: str | None = None
    drawing_number: str | None = None
    revision: str | None = None
    description: str | None = None
    material: str | None = None
    material_specification: str | None = None
    heat_treatment: str | None = None
    coating: str | None = None
    edge_requirement: str | None = None
    surface_texture_requirements: list[dict[str, Any]] = Field(default_factory=list)
    datums: list[DatumDefinition] = Field(default_factory=list)
    geometric_tolerances: list[dict[str, Any]] = Field(default_factory=list)
    general_tolerance_policy_id: str | None = None
    internal_thread_class: str | None = None
    external_thread_class: str | None = None
    approved_by: str | None = None
    approved_at: datetime | None = None
    approval_id: str | None = None

    @property
    def has_human_approval(self) -> bool:
        return bool(self.approved_by and self.approved_at and self.approval_id)


class ExtractionFinding(BaseModel):
    code: str
    severity: Severity
    message: str
    entity_refs: list[EntityRef] = Field(default_factory=list)


class ModelManifest(BaseModel):
    schema_version: Literal["2.0"] = "2.0"
    model_name: str
    model_hash: str = Field(min_length=8)
    file_path: str
    configuration: str = "Default"
    document_type: DocumentType = DocumentType.PART
    units: UnitSystem = UnitSystem.MM
    bounds: Bounds3
    material_from_model: str | None = None
    mass_kg: float | None = Field(default=None, ge=0)
    body_count: int = Field(default=1, ge=1)
    bounds_source: Literal["EXACT_VERTICES", "APPROXIMATE_BODY_BOX"] = "EXACT_VERTICES"
    features: list[RawFeature] = Field(default_factory=list)
    extraction_findings: list[ExtractionFinding] = Field(default_factory=list)
    engineering_intent: EngineeringIntent = Field(default_factory=EngineeringIntent)
    custom_properties: dict[str, str] = Field(default_factory=dict)

    @model_validator(mode="after")
    def unique_feature_ids(self) -> "ModelManifest":
        ids = [feature.feature_id for feature in self.features if not feature.suppressed]
        if len(ids) != len(set(ids)):
            raise ValueError("feature_id values must be unique")
        return self


class DrawingPreferences(BaseModel):
    standard_profile_id: str = "ISO_METRIC_2025"
    projection: ProjectionMethod | None = None
    sheet_size: Literal["A4", "A3", "A2", "A1", "A0", "A", "B", "C", "D"] = "A3"
    scale: str = "AUTO"
    units: UnitSystem | None = None
    include_isometric: bool = True
    include_section_view: bool = True
    include_hole_table: bool = False
    show_hidden_lines: bool = False
    annotation_clearance_mm: float = Field(default=8.0, ge=4.0, le=25.0)
    view_clearance_mm: float = Field(default=20.0, ge=10.0, le=60.0)
    company_policy_id: str | None = None


class PlanRequest(BaseModel):
    model_data: ModelManifest
    preferences: DrawingPreferences = Field(default_factory=DrawingPreferences)


class StandardsSnapshot(BaseModel):
    profile_id: str
    standard_family: StandardFamily
    edition_label: str
    projection: ProjectionMethod
    units: UnitSystem
    standard_references: list[str]
    policy_id: str | None = None
    digest: str


class ReferenceScheme(BaseModel):
    reference_type: Literal["APPROVED_DATUM", "PROVISIONAL_GEOMETRIC"]
    x_origin_ref: EntityRef | None = None
    y_origin_ref: EntityRef | None = None
    z_origin_ref: EntityRef | None = None
    datum_labels: dict[str, str] = Field(default_factory=dict)
    provisional: bool = True


class FeatureFamily(BaseModel):
    family_id: str
    kind: FeatureKind
    feature_ids: list[str]
    instance_count: int
    specification: FeatureSpecification
    axis: Vector3 | None = None
    centers: list[Point3] = Field(default_factory=list)
    entity_refs: list[EntityRef] = Field(default_factory=list)


class ViewPlan(BaseModel):
    view_id: str
    sheet_index: int = Field(default=1, ge=1)
    kind: ViewKind
    orientation: Orientation
    solidworks_view_name: str
    center_x_mm: float
    center_y_mm: float
    scale: float = Field(gt=0)
    display_style: Literal["HIDDEN_LINES_REMOVED", "HIDDEN_LINES_VISIBLE", "SHADED"]
    expected_model_bounds_mm: Rect2
    reserved_annotation_bounds_mm: Rect2
    parent_view_id: str | None = None
    section_axis: Literal["X", "Y", "Z"] | None = None
    model_u_axis: Literal["X", "Y", "Z"]
    model_v_axis: Literal["X", "Y", "Z"]
    model_normal_axis: Literal["X", "Y", "Z"]


class RequirementSpecification(BaseModel):
    nominal_value_mm: float | None = None
    display_value: float | None = None
    display_text: str | None = None
    tolerance_type: str | None = None
    upper: float | None = None
    lower: float | None = None
    modifiers: list[str] = Field(default_factory=list)
    quantity: int | None = Field(default=None, ge=1)
    unit: UnitSystem = UnitSystem.MM


class RequirementPlan(BaseModel):
    requirement_id: str
    measurand_key: str
    specification_key: str
    kind: RequirementKind
    feature_ids: list[str] = Field(default_factory=list)
    geometry_refs: list[EntityRef]
    reference_refs: list[EntityRef] = Field(default_factory=list)
    characteristic: str
    measurement_axis: Literal["X", "Y", "Z", "RADIAL", "ANGULAR", "NORMAL"]
    controlled_extent: str
    view_id: str
    specification: RequirementSpecification
    placement_lane: str
    associative_required: Literal[True] = True
    is_reference: bool = False


class AnnotationPlan(BaseModel):
    annotation_id: str
    kind: AnnotationKind
    view_id: str | None = None
    geometry_refs: list[EntityRef] = Field(default_factory=list)
    text: str | None = None
    position_x_mm: float | None = None
    position_y_mm: float | None = None
    controlling: Literal[False] = False


class ValidationFinding(BaseModel):
    code: str
    severity: Severity
    message: str
    item_ids: list[str] = Field(default_factory=list)
    standard_reference: str | None = None


class ReleaseGate(BaseModel):
    status: ReleaseStatus
    release_ready: bool
    blockers: list[ValidationFinding] = Field(default_factory=list)
    warnings: list[ValidationFinding] = Field(default_factory=list)
    checks: dict[str, bool] = Field(default_factory=dict)


class DrawingPlan(BaseModel):
    schema_version: Literal["2.0"] = "2.0"
    plan_id: str
    plan_digest: str
    model_hash: str
    configuration: str
    standards: StandardsSnapshot
    sheet_size: str
    sheet_width_mm: float
    sheet_height_mm: float
    scale_numerator: int
    scale_denominator: int
    classified_features: list[ClassifiedFeature]
    feature_families: list[FeatureFamily]
    reference_scheme: ReferenceScheme
    views: list[ViewPlan]
    requirements: list[RequirementPlan]
    annotations: list[AnnotationPlan]
    title_fields: dict[str, str]
    release_gate: ReleaseGate
    generated_at: datetime


class ExecutedRequirement(BaseModel):
    requirement_id: str
    measurand_key: str
    specification_key: str
    association_status: AssociationStatus
    created_annotation_id: str | None = None
    resolved_geometry_ref_count: int = Field(default=0, ge=0)
    expected_geometry_ref_count: int = Field(default=0, ge=0)
    actual_value: float | None = None
    message: str | None = None


class ExecutedView(BaseModel):
    view_id: str
    orientation: Orientation
    actual_bounds_mm: Rect2
    actual_scale: float = Field(gt=0)
    parent_view_id: str | None = None


class ExecutionReport(BaseModel):
    schema_version: Literal["2.0"] = "2.0"
    plan_id: str
    plan_digest: str
    model_hash: str
    standards_digest: str
    projection: ProjectionMethod
    executed_views: list[ExecutedView]
    executed_requirements: list[ExecutedRequirement]
    orphan_controlling_annotations: list[str] = Field(default_factory=list)
    duplicate_measurand_keys: list[str] = Field(default_factory=list)
    annotation_layout_violations: list[str] = Field(default_factory=list)
    title_fields_written: dict[str, str] = Field(default_factory=dict)
    cad_artifacts_hidden: bool = False
    human_approval_confirmed: bool = False


class ExecutionValidationRequest(BaseModel):
    plan: DrawingPlan
    execution: ExecutionReport
