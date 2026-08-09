using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MachIntellDrawAI.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class RequirementExecutionResult
    {
        public List<ExecutedRequirement> Requirements { get; } = new List<ExecutedRequirement>();
        public AnnotationMetadataStore Metadata { get; set; } = null!;
        public IAnnotation? DraftWatermark { get; set; }
        public bool CadArtifactsHidden { get; set; }
    }

    internal sealed class AssociativeRequirementExecutor
    {
        private readonly DrawingContext _drawing;
        private readonly IModelDoc2 _sourceModel;
        private readonly PersistentReferenceService _refs;
        private readonly IReadOnlyDictionary<string, IView> _views;
        private readonly Dictionary<string, int> _laneCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _laneTotals = new Dictionary<string, int>(StringComparer.Ordinal);

        public AssociativeRequirementExecutor(
            DrawingContext drawing,
            IModelDoc2 sourceModel,
            PersistentReferenceService refs,
            IReadOnlyDictionary<string, IView> views)
        {
            _drawing = drawing;
            _sourceModel = sourceModel;
            _refs = refs;
            _views = views;
        }

        public RequirementExecutionResult Execute(DrawingPlan plan)
        {
            var result = new RequirementExecutionResult { Metadata = new AnnotationMetadataStore(_drawing.Model) };
            foreach (var group in plan.Requirements.GroupBy(item => item.ViewId + ":" + item.PlacementLane, StringComparer.Ordinal))
                _laneTotals[group.Key] = group.Count();
            foreach (var requirement in plan.Requirements)
                result.Requirements.Add(ExecuteOne(requirement, result.Metadata));
            result.DraftWatermark = ExecuteNonControllingAnnotations(plan.Annotations);
            _drawing.Model.EditRebuild3();
            return result;
        }

        private ExecutedRequirement ExecuteOne(RequirementPlan requirement, AnnotationMetadataStore metadata)
        {
            var report = new ExecutedRequirement
            {
                RequirementId = requirement.RequirementId,
                MeasurandKey = requirement.MeasurandKey,
                SpecificationKey = requirement.SpecificationKey,
                ExpectedGeometryRefCount = requirement.GeometryRefs.Count,
                AssociationStatus = "MISSING"
            };
            try
            {
                if (!_views.TryGetValue(requirement.ViewId, out var view))
                    throw new InvalidOperationException("Planned view is missing.");
                _drawing.Drawing.ActivateView(view.Name);
                _drawing.Model.ClearSelection2(true);
                var geometry = ResolveCorresponding(view, requirement.GeometryRefs);
                report.ResolvedGeometryRefCount = geometry.Count;
                if (geometry.Count != requirement.GeometryRefs.Count)
                    throw new InvalidOperationException("Not every persistent geometry reference maps into the planned drawing view.");
                var references = ResolveCorresponding(view, requirement.ReferenceRefs);
                if (references.Count != requirement.ReferenceRefs.Count)
                    throw new InvalidOperationException("Not every reference/datum maps into the planned drawing view.");

                var position = Placement(requirement.ViewId, view, requirement.PlacementLane);
                IDisplayDimension display;
                if (requirement.Kind == "HOLE_CALLOUT" || requirement.Kind == "THREAD_CALLOUT")
                    display = CreateNativeHoleCallout(requirement, geometry, position.x, position.y);
                else
                    display = CreateAssociativeDimension(requirement, geometry, references, position.x, position.y);

                ApplyTolerance(display, requirement.Specification);
                var actual = ReadActualValue(display, requirement);
                report.ActualValue = actual;
                if (requirement.Specification.NominalValueMm.HasValue && actual.HasValue &&
                    Math.Abs(actual.Value - requirement.Specification.NominalValueMm.Value) > ValueTolerance(requirement))
                    throw new InvalidOperationException($"Associative dimension measures {actual.Value:0.######}, expected {requirement.Specification.NominalValueMm.Value:0.######}.");
                var annotation = display.GetAnnotation() as IAnnotation
                    ?? throw new InvalidOperationException("SolidWorks created a dimension without an annotation.");
                var text = display.GetText((int)swDimensionTextParts_e.swDimensionTextAll) ?? string.Empty;
                report.CreatedAnnotationId = metadata.Record(requirement, requirement.ViewId, annotation, text);
                report.AssociationStatus = "ASSOCIATIVE";
            }
            catch (Exception ex)
            {
                report.Message = ex.Message;
                report.AssociationStatus = "MISSING";
            }
            finally
            {
                _drawing.Model.ClearSelection2(true);
            }
            return report;
        }

        private List<object> ResolveCorresponding(IView view, List<EntityRef> references)
        {
            var result = new List<object>();
            foreach (var reference in references)
            {
                var modelEntity = _refs.Resolve(reference);
                var drawingEntity = view.GetCorrespondingEntity(modelEntity);
                if (drawingEntity == null) continue;
                result.Add(drawingEntity);
            }
            return result;
        }

        private IDisplayDimension CreateNativeHoleCallout(RequirementPlan requirement, List<object> geometry, double x, double y)
        {
            if (geometry.Count == 0 || !(geometry[0] is IEntity entity) || !entity.Select4(false, null))
                throw new InvalidOperationException("The exact circular edge could not be selected for a native hole callout.");
            var display = _drawing.Drawing.AddHoleCallout2(x, y, 0) as IDisplayDimension
                ?? throw new InvalidOperationException("SolidWorks AddHoleCallout2 failed; no note fallback is permitted.");
            if (!display.IsHoleCallout())
                throw new InvalidOperationException("SolidWorks did not identify the created annotation as a native hole callout.");
            if ((requirement.Specification.Quantity ?? 1) > 1)
            {
                var separator = requirement.Specification.DisplayText != null && requirement.Specification.DisplayText.Contains("×") ? "×" : "X";
                display.SetText(
                    (int)swDimensionTextParts_e.swDimensionTextPrefix,
                    requirement.Specification.Quantity.Value.ToString(CultureInfo.InvariantCulture) + separator + " ");
            }
            return display;
        }

        private IDisplayDimension CreateAssociativeDimension(
            RequirementPlan requirement,
            List<object> geometry,
            List<object> references,
            double x,
            double y)
        {
            var selections = new List<object>(geometry);
            selections.AddRange(references);
            if (requirement.Kind == "RADIUS" || requirement.Kind == "CHAMFER" || requirement.Kind == "FEATURE_SIZE")
                selections = new List<object> { geometry[0] };
            if (selections.Count == 0) throw new InvalidOperationException("No exact entities were available for the requirement.");
            for (var index = 0; index < selections.Count; index++)
            {
                if (!(selections[index] is IEntity entity) || !entity.Select4(index > 0, null))
                    throw new InvalidOperationException("A corresponding drawing entity could not be selected.");
            }
            return _drawing.Model.AddDimension2(x, y, 0) as IDisplayDimension
                ?? throw new InvalidOperationException("SolidWorks AddDimension2 failed; no plain-note fallback is permitted.");
        }

        private static void ApplyTolerance(IDisplayDimension display, RequirementSpecification specification)
        {
            if (string.IsNullOrWhiteSpace(specification.ToleranceType)) return;
            var dimension = display.GetDimension2(0) as IDimension
                ?? throw new InvalidOperationException("Created display dimension has no model dimension for tolerance application.");
            var tolerance = dimension.Tolerance as IDimensionTolerance
                ?? throw new InvalidOperationException("Created dimension has no SolidWorks tolerance object.");
            var type = specification.ToleranceType.Trim().ToUpperInvariant();
            if (type == "BILATERAL" || type == "BILAT") tolerance.Type = (int)swTolType_e.swTolBILAT;
            else if (type == "SYMMETRIC") tolerance.Type = (int)swTolType_e.swTolSYMMETRIC;
            else if (type == "LIMIT" || type == "LIMITS") tolerance.Type = (int)swTolType_e.swTolLIMIT;
            else throw new NotSupportedException("Explicit tolerance type is unsupported: " + specification.ToleranceType);
            if (!specification.Upper.HasValue || !specification.Lower.HasValue)
                throw new InvalidOperationException("Explicit tolerance requires both upper and lower values.");
            tolerance.SetValues(specification.Upper.Value / 1000.0, specification.Lower.Value / 1000.0);
        }

        private static double? ReadActualValue(IDisplayDimension display, RequirementPlan requirement)
        {
            var dimension = display.GetDimension2(0) as IDimension;
            if (dimension == null) return null;
            var value = dimension.GetSystemValue3(
                (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration,
                null);
            if (!(value is double number)) return null;
            if (requirement.Kind == "ANGLE") return number * 180.0 / Math.PI;
            return number * 1000.0;
        }

        private static double ValueTolerance(RequirementPlan requirement)
        {
            if (requirement.Kind == "ANGLE") return 0.01;
            return Math.Max(0.005, (requirement.Specification.NominalValueMm ?? 1.0) * 1e-5);
        }

        private (double x, double y) Placement(string viewId, IView view, string lane)
        {
            var outline = view.GetOutline() as double[]
                ?? throw new InvalidOperationException("View outline is unavailable for deterministic placement.");
            var key = view.Name + ":" + lane;
            var totalKey = viewId + ":" + lane;
            _laneCounts.TryGetValue(key, out var index);
            _laneCounts[key] = index + 1;
            _laneTotals.TryGetValue(totalKey, out var total);
            total = Math.Max(total, 1);
            if (lane.StartsWith("RIGHT", StringComparison.Ordinal))
                return (outline[2] + 0.012, Distributed(outline[1] - 0.030, outline[3] + 0.030, index, total));
            if (lane.StartsWith("LEFT", StringComparison.Ordinal))
            {
                var minimumY = outline[1] - 0.030;
                var maximumY = outline[3] + 0.030;
                var rows = Math.Min(total, Math.Max(1, (int)Math.Floor((maximumY - minimumY) / 0.009) - 1));
                var row = index % rows;
                var column = index / rows;
                return (outline[0] - 0.010 - 0.020 * column, Distributed(minimumY, maximumY, row, rows));
            }
            if (lane.StartsWith("BOTTOM", StringComparison.Ordinal))
            {
                var minimumX = outline[0] - 0.025;
                var maximumX = outline[2] + 0.025;
                var columns = Math.Min(total, Math.Max(1, (int)Math.Floor((maximumX - minimumX) / 0.019) - 1));
                var column = index % columns;
                var row = index / columns;
                return (Distributed(minimumX, maximumX, column, columns), outline[1] - 0.012 - 0.009 * row);
            }
            return (outline[2] + 0.012, Distributed(outline[1] - 0.030, outline[3] + 0.030, index, total));
        }

        private static double Distributed(double minimum, double maximum, int index, int count) =>
            minimum + (maximum - minimum) * (index + 1.0) / (count + 1.0);

        private IAnnotation? ExecuteNonControllingAnnotations(List<AnnotationPlan> annotations)
        {
            IAnnotation? watermark = null;
            foreach (var planned in annotations)
            {
                if (planned.Controlling)
                    throw new InvalidOperationException("The v2 plugin never creates controlling plain annotations.");
                if (planned.Kind == "CENTER_MARK") CreateCenterMark(planned);
                else if (planned.Kind == "DRAFT_WATERMARK") watermark = CreateWatermark(planned);
                else throw new NotSupportedException("Non-controlling annotation is not implemented: " + planned.Kind);
            }
            return watermark;
        }

        private void CreateCenterMark(AnnotationPlan planned)
        {
            if (planned.ViewId == null || !_views.TryGetValue(planned.ViewId, out var view))
                throw new InvalidOperationException("Center mark has no valid planned view.");
            var entities = ResolveCorresponding(view, planned.GeometryRefs);
            if (entities.Count != planned.GeometryRefs.Count)
                throw new InvalidOperationException("Center-mark reference did not map into its drawing view.");
            _drawing.Drawing.ActivateView(view.Name);
            _drawing.Model.ClearSelection2(true);
            if (!(entities[0] is IEntity entity) || !entity.Select4(false, null))
                throw new InvalidOperationException("Center-mark circular edge could not be selected.");
            var created = _drawing.Drawing.InsertCenterMark2(0, true);
            if (created == null || (created is bool success && !success))
                throw new InvalidOperationException("SolidWorks failed to create an associative center mark.");
        }

        private IAnnotation CreateWatermark(AnnotationPlan planned)
        {
            _drawing.Drawing.ActivateSheet(_drawing.SheetNames[1]);
            var note = _drawing.Model.InsertNote(planned.Text ?? "DRAFT — NOT FOR MANUFACTURE") as INote
                ?? throw new InvalidOperationException("SolidWorks failed to create the non-controlling draft watermark.");
            var annotation = note.GetAnnotation() as IAnnotation
                ?? throw new InvalidOperationException("Draft watermark has no annotation.");
            annotation.SetPosition2(
                (planned.PositionXMm ?? 25.0) / 1000.0,
                (planned.PositionYMm ?? 25.0) / 1000.0,
                0);
            Infrastructure.ComCall.Optional(annotation, "SetName", "MI_DRAFT_WATERMARK");
            return annotation;
        }
    }
}
