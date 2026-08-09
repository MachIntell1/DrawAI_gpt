using System;
using System.Collections.Generic;

namespace MachIntellDrawAI.Models
{
    public sealed class Point3
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public sealed class Vector3
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public string DominantAxis
        {
            get
            {
                double x = Math.Abs(X), y = Math.Abs(Y), z = Math.Abs(Z);
                return x >= y && x >= z ? "X" : y >= z ? "Y" : "Z";
            }
        }
    }

    public sealed class EntityRef
    {
        public string Token { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string ModelConfiguration { get; set; } = "Default";
    }

    public sealed class Bounds3
    {
        public Point3 Minimum { get; set; } = new Point3();
        public Point3 Maximum { get; set; } = new Point3();
        public Dictionary<string, EntityRef> ExtremeRefs { get; set; } = new Dictionary<string, EntityRef>();
    }

    public sealed class Rect2
    {
        public double Left { get; set; }
        public double Bottom { get; set; }
        public double Right { get; set; }
        public double Top { get; set; }
    }

    public sealed class TopologyEvidence
    {
        public string? SurfaceKind { get; set; }
        public double? SweepAngleDeg { get; set; }
        public bool? ClosedProfile { get; set; }
        public bool? OpensToOuterBoundary { get; set; }
        public int TangentFaceCount { get; set; }
        public bool? IsInternal { get; set; }
        public double? Radius { get; set; }
        public double? AxialLength { get; set; }
        public int? LoopCount { get; set; }
        public List<EntityRef> EntityRefs { get; set; } = new List<EntityRef>();
    }

    public sealed class FeatureSpecification
    {
        public double? Diameter { get; set; }
        public double? Depth { get; set; }
        public bool? Through { get; set; }
        public double? CounterboreDiameter { get; set; }
        public double? CounterboreDepth { get; set; }
        public double? CountersinkDiameter { get; set; }
        public double? CountersinkAngleDeg { get; set; }
        public double? Radius { get; set; }
        public double? Width { get; set; }
        public double? Length { get; set; }
        public double? ChamferDistance { get; set; }
        public double? ChamferAngleDeg { get; set; }
        public string? ThreadDesignation { get; set; }
        public double? ThreadPitch { get; set; }
        public string? ThreadClass { get; set; }
        public double? ThreadDepth { get; set; }
        public double? DrillDepth { get; set; }
        public Dictionary<string, object>? Tolerance { get; set; }
    }

    public class RawFeature
    {
        public string FeatureId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string NativeType { get; set; } = string.Empty;
        public string? NativeSubtype { get; set; }
        public string Source { get; set; } = "SOLIDWORKS_NATIVE";
        public EntityRef? FeatureRef { get; set; }
        public List<EntityRef> EntityRefs { get; set; } = new List<EntityRef>();
        public List<Point3> Centers { get; set; } = new List<Point3>();
        public Vector3? Axis { get; set; }
        public FeatureSpecification Specification { get; set; } = new FeatureSpecification();
        public TopologyEvidence? Topology { get; set; }
        public bool Patterned { get; set; }
        public int? PatternCount { get; set; }
        public bool Suppressed { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }

    public sealed class DatumDefinition
    {
        public string Label { get; set; } = string.Empty;
        public EntityRef FeatureRef { get; set; } = new EntityRef();
        public string Role { get; set; } = "OTHER";
        public bool Approved { get; set; }
    }

    public sealed class EngineeringIntent
    {
        public string? PartNumber { get; set; }
        public string? DrawingNumber { get; set; }
        public string? Revision { get; set; }
        public string? Description { get; set; }
        public string? Material { get; set; }
        public string? MaterialSpecification { get; set; }
        public string? HeatTreatment { get; set; }
        public string? Coating { get; set; }
        public string? EdgeRequirement { get; set; }
        public List<Dictionary<string, object>> SurfaceTextureRequirements { get; set; } = new List<Dictionary<string, object>>();
        public List<DatumDefinition> Datums { get; set; } = new List<DatumDefinition>();
        public List<Dictionary<string, object>> GeometricTolerances { get; set; } = new List<Dictionary<string, object>>();
        public string? GeneralTolerancePolicyId { get; set; }
        public string? InternalThreadClass { get; set; }
        public string? ExternalThreadClass { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string? ApprovalId { get; set; }
    }

    public sealed class ExtractionFinding
    {
        public string Code { get; set; } = string.Empty;
        public string Severity { get; set; } = "BLOCKER";
        public string Message { get; set; } = string.Empty;
        public List<EntityRef> EntityRefs { get; set; } = new List<EntityRef>();
    }

    public sealed class ModelManifest
    {
        public string SchemaVersion { get; set; } = "2.0";
        public string ModelName { get; set; } = string.Empty;
        public string ModelHash { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Configuration { get; set; } = "Default";
        public string DocumentType { get; set; } = "PART";
        public string Units { get; set; } = "MM";
        public Bounds3 Bounds { get; set; } = new Bounds3();
        public string? MaterialFromModel { get; set; }
        public double? MassKg { get; set; }
        public int BodyCount { get; set; } = 1;
        public string BoundsSource { get; set; } = "EXACT_VERTICES";
        public List<RawFeature> Features { get; set; } = new List<RawFeature>();
        public List<ExtractionFinding> ExtractionFindings { get; set; } = new List<ExtractionFinding>();
        public EngineeringIntent EngineeringIntent { get; set; } = new EngineeringIntent();
        public Dictionary<string, string> CustomProperties { get; set; } = new Dictionary<string, string>();
    }

    public sealed class DrawingPreferences
    {
        public string StandardProfileId { get; set; } = "ISO_METRIC_2025";
        public string? Projection { get; set; }
        public string SheetSize { get; set; } = "A3";
        public string Scale { get; set; } = "AUTO";
        public string? Units { get; set; }
        public bool IncludeIsometric { get; set; } = true;
        public bool IncludeSectionView { get; set; } = true;
        public bool IncludeHoleTable { get; set; }
        public bool ShowHiddenLines { get; set; }
        public double AnnotationClearanceMm { get; set; } = 8.0;
        public double ViewClearanceMm { get; set; } = 20.0;
        public string? CompanyPolicyId { get; set; }
    }

    public sealed class PlanRequest
    {
        public ModelManifest ModelData { get; set; } = new ModelManifest();
        public DrawingPreferences Preferences { get; set; } = new DrawingPreferences();
    }

    public sealed class StandardsSnapshot
    {
        public string ProfileId { get; set; } = string.Empty;
        public string StandardFamily { get; set; } = string.Empty;
        public string EditionLabel { get; set; } = string.Empty;
        public string Projection { get; set; } = string.Empty;
        public string Units { get; set; } = string.Empty;
        public List<string> StandardReferences { get; set; } = new List<string>();
        public string? PolicyId { get; set; }
        public string Digest { get; set; } = string.Empty;
    }

    public sealed class ClassifiedFeature : RawFeature
    {
        public string Kind { get; set; } = "UNKNOWN";
        public double Confidence { get; set; }
        public int InstanceCount { get; set; } = 1;
        public List<string> Conflicts { get; set; } = new List<string>();
    }

    public sealed class FeatureFamily
    {
        public string FamilyId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public List<string> FeatureIds { get; set; } = new List<string>();
        public int InstanceCount { get; set; }
        public FeatureSpecification Specification { get; set; } = new FeatureSpecification();
        public Vector3? Axis { get; set; }
        public List<Point3> Centers { get; set; } = new List<Point3>();
        public List<EntityRef> EntityRefs { get; set; } = new List<EntityRef>();
    }

    public sealed class ReferenceScheme
    {
        public string ReferenceType { get; set; } = string.Empty;
        public EntityRef? XOriginRef { get; set; }
        public EntityRef? YOriginRef { get; set; }
        public EntityRef? ZOriginRef { get; set; }
        public Dictionary<string, string> DatumLabels { get; set; } = new Dictionary<string, string>();
        public bool Provisional { get; set; }
    }

    public sealed class ViewPlan
    {
        public string ViewId { get; set; } = string.Empty;
        public int SheetIndex { get; set; } = 1;
        public string Kind { get; set; } = string.Empty;
        public string Orientation { get; set; } = string.Empty;
        public string SolidworksViewName { get; set; } = string.Empty;
        public double CenterXMm { get; set; }
        public double CenterYMm { get; set; }
        public double Scale { get; set; }
        public string DisplayStyle { get; set; } = string.Empty;
        public Rect2 ExpectedModelBoundsMm { get; set; } = new Rect2();
        public Rect2 ReservedAnnotationBoundsMm { get; set; } = new Rect2();
        public string? ParentViewId { get; set; }
        public string? SectionAxis { get; set; }
        public string ModelUAxis { get; set; } = string.Empty;
        public string ModelVAxis { get; set; } = string.Empty;
        public string ModelNormalAxis { get; set; } = string.Empty;
    }

    public sealed class RequirementSpecification
    {
        public double? NominalValueMm { get; set; }
        public double? DisplayValue { get; set; }
        public string? DisplayText { get; set; }
        public string? ToleranceType { get; set; }
        public double? Upper { get; set; }
        public double? Lower { get; set; }
        public List<string> Modifiers { get; set; } = new List<string>();
        public int? Quantity { get; set; }
        public string Unit { get; set; } = "MM";
    }

    public sealed class RequirementPlan
    {
        public string RequirementId { get; set; } = string.Empty;
        public string MeasurandKey { get; set; } = string.Empty;
        public string SpecificationKey { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public List<string> FeatureIds { get; set; } = new List<string>();
        public List<EntityRef> GeometryRefs { get; set; } = new List<EntityRef>();
        public List<EntityRef> ReferenceRefs { get; set; } = new List<EntityRef>();
        public string Characteristic { get; set; } = string.Empty;
        public string MeasurementAxis { get; set; } = string.Empty;
        public string ControlledExtent { get; set; } = string.Empty;
        public string ViewId { get; set; } = string.Empty;
        public RequirementSpecification Specification { get; set; } = new RequirementSpecification();
        public string PlacementLane { get; set; } = string.Empty;
        public bool AssociativeRequired { get; set; }
        public bool IsReference { get; set; }
    }

    public sealed class AnnotationPlan
    {
        public string AnnotationId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string? ViewId { get; set; }
        public List<EntityRef> GeometryRefs { get; set; } = new List<EntityRef>();
        public string? Text { get; set; }
        public double? PositionXMm { get; set; }
        public double? PositionYMm { get; set; }
        public bool Controlling { get; set; }
    }

    public sealed class ValidationFinding
    {
        public string Code { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<string> ItemIds { get; set; } = new List<string>();
        public string? StandardReference { get; set; }
    }

    public sealed class ReleaseGate
    {
        public string Status { get; set; } = string.Empty;
        public bool ReleaseReady { get; set; }
        public List<ValidationFinding> Blockers { get; set; } = new List<ValidationFinding>();
        public List<ValidationFinding> Warnings { get; set; } = new List<ValidationFinding>();
        public Dictionary<string, bool> Checks { get; set; } = new Dictionary<string, bool>();
    }

    public sealed class DrawingPlan
    {
        public string SchemaVersion { get; set; } = "2.0";
        public string PlanId { get; set; } = string.Empty;
        public string PlanDigest { get; set; } = string.Empty;
        public string ModelHash { get; set; } = string.Empty;
        public string Configuration { get; set; } = string.Empty;
        public StandardsSnapshot Standards { get; set; } = new StandardsSnapshot();
        public string SheetSize { get; set; } = string.Empty;
        public double SheetWidthMm { get; set; }
        public double SheetHeightMm { get; set; }
        public int ScaleNumerator { get; set; }
        public int ScaleDenominator { get; set; }
        public List<ClassifiedFeature> ClassifiedFeatures { get; set; } = new List<ClassifiedFeature>();
        public List<FeatureFamily> FeatureFamilies { get; set; } = new List<FeatureFamily>();
        public ReferenceScheme ReferenceScheme { get; set; } = new ReferenceScheme();
        public List<ViewPlan> Views { get; set; } = new List<ViewPlan>();
        public List<RequirementPlan> Requirements { get; set; } = new List<RequirementPlan>();
        public List<AnnotationPlan> Annotations { get; set; } = new List<AnnotationPlan>();
        public Dictionary<string, string> TitleFields { get; set; } = new Dictionary<string, string>();
        public ReleaseGate ReleaseGate { get; set; } = new ReleaseGate();
        public DateTimeOffset GeneratedAt { get; set; }
    }

    public sealed class ExecutedRequirement
    {
        public string RequirementId { get; set; } = string.Empty;
        public string MeasurandKey { get; set; } = string.Empty;
        public string SpecificationKey { get; set; } = string.Empty;
        public string AssociationStatus { get; set; } = "MISSING";
        public string? CreatedAnnotationId { get; set; }
        public int ResolvedGeometryRefCount { get; set; }
        public int ExpectedGeometryRefCount { get; set; }
        public double? ActualValue { get; set; }
        public string? Message { get; set; }
    }

    public sealed class ExecutedView
    {
        public string ViewId { get; set; } = string.Empty;
        public string Orientation { get; set; } = string.Empty;
        public Rect2 ActualBoundsMm { get; set; } = new Rect2();
        public double ActualScale { get; set; }
        public string? ParentViewId { get; set; }
    }

    public sealed class ExecutionReport
    {
        public string SchemaVersion { get; set; } = "2.0";
        public string PlanId { get; set; } = string.Empty;
        public string PlanDigest { get; set; } = string.Empty;
        public string ModelHash { get; set; } = string.Empty;
        public string StandardsDigest { get; set; } = string.Empty;
        public string Projection { get; set; } = string.Empty;
        public List<ExecutedView> ExecutedViews { get; set; } = new List<ExecutedView>();
        public List<ExecutedRequirement> ExecutedRequirements { get; set; } = new List<ExecutedRequirement>();
        public List<string> OrphanControllingAnnotations { get; set; } = new List<string>();
        public List<string> DuplicateMeasurandKeys { get; set; } = new List<string>();
        public List<string> AnnotationLayoutViolations { get; set; } = new List<string>();
        public Dictionary<string, string> TitleFieldsWritten { get; set; } = new Dictionary<string, string>();
        public bool CadArtifactsHidden { get; set; }
        public bool HumanApprovalConfirmed { get; set; }
    }

    public sealed class ExecutionValidationRequest
    {
        public DrawingPlan Plan { get; set; } = new DrawingPlan();
        public ExecutionReport Execution { get; set; } = new ExecutionReport();
    }
}
