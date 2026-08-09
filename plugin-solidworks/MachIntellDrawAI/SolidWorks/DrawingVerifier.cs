using System;
using System.Collections.Generic;
using System.Linq;
using MachIntellDrawAI.Models;
using SolidWorks.Interop.sldworks;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class DrawingVerifier
    {
        private readonly DrawingContext _drawing;
        private readonly IReadOnlyDictionary<string, IView> _views;
        private readonly RequirementExecutionResult _execution;

        public DrawingVerifier(
            DrawingContext drawing,
            IReadOnlyDictionary<string, IView> views,
            RequirementExecutionResult execution)
        {
            _drawing = drawing;
            _views = views;
            _execution = execution;
        }

        public ExecutionReport Verify(DrawingPlan plan, bool humanApprovalConfirmed)
        {
            VerifyProjectionAndScale(plan);
            var report = new ExecutionReport
            {
                PlanId = plan.PlanId,
                PlanDigest = plan.PlanDigest,
                ModelHash = plan.ModelHash,
                StandardsDigest = plan.Standards.Digest,
                Projection = plan.Standards.Projection,
                ExecutedRequirements = new List<ExecutedRequirement>(_execution.Requirements),
                TitleFieldsWritten = ReadTitleFields(plan),
                HumanApprovalConfirmed = humanApprovalConfirmed
            };
            foreach (var planned in plan.Views)
            {
                if (!_views.TryGetValue(planned.ViewId, out var view)) continue;
                report.ExecutedViews.Add(new ExecutedView
                {
                    ViewId = planned.ViewId,
                    Orientation = planned.Orientation,
                    ParentViewId = planned.ParentViewId,
                    ActualScale = ReadScale(view),
                    ActualBoundsMm = ActualEnvelope(planned.ViewId, view)
                });
            }
            report.OrphanControllingAnnotations = FindOrphanDimensions();
            report.DuplicateMeasurandKeys = _execution.Metadata.Records
                .GroupBy(r => r.MeasurandKey, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            report.AnnotationLayoutViolations = FindAnnotationLayoutViolations(plan);
            _drawing.Model.ClearSelection2(true);
            report.CadArtifactsHidden = _execution.CadArtifactsHidden &&
                                        report.OrphanControllingAnnotations.Count == 0 &&
                                        report.AnnotationLayoutViolations.Count == 0 &&
                                        report.ExecutedViews.Count == plan.Views.Count;
            return report;
        }

        private void VerifyProjectionAndScale(DrawingPlan plan)
        {
            foreach (var pair in _drawing.SheetNames)
            {
                _drawing.Drawing.ActivateSheet(pair.Value);
                var sheet = (ISheet)_drawing.Drawing.GetCurrentSheet();
                var props = sheet.GetProperties2() as double[]
                    ?? throw new InvalidOperationException("Sheet properties are unavailable during final verification.");
                var expectedFirst = plan.Standards.Projection == "FIRST_ANGLE";
                if (Convert.ToBoolean(props[4]) != expectedFirst)
                    throw new InvalidOperationException("Projection changed after view generation on sheet " + pair.Key);
                var scale = props[2] / props[3];
                var expected = (double)plan.ScaleNumerator / plan.ScaleDenominator;
                if (Math.Abs(scale - expected) > 1e-9)
                    throw new InvalidOperationException("Sheet scale changed after view generation on sheet " + pair.Key);
            }
            foreach (var planned in plan.Views)
            {
                if (!_views.TryGetValue(planned.ViewId, out var view)) continue;
                if (!string.Equals(view.ReferencedConfiguration, plan.Configuration, StringComparison.Ordinal))
                    throw new InvalidOperationException("A drawing view changed model configuration after planning: " + planned.ViewId);
                var position = view.Position as double[];
                if (position == null || position.Length < 2 ||
                    Math.Abs(position[0] * 1000.0 - planned.CenterXMm) > 0.1 ||
                    Math.Abs(position[1] * 1000.0 - planned.CenterYMm) > 0.1)
                    throw new InvalidOperationException("A drawing view moved after deterministic placement: " + planned.ViewId);
            }
            _drawing.Drawing.ActivateSheet(_drawing.SheetNames[1]);
        }

        private Rect2 ActualEnvelope(string viewId, IView view)
        {
            var outline = view.GetOutline() as double[]
                ?? throw new InvalidOperationException("View outline is unavailable during final verification: " + viewId);
            var result = new Rect2
            {
                Left = outline[0] * 1000.0,
                Bottom = outline[1] * 1000.0,
                Right = outline[2] * 1000.0,
                Top = outline[3] * 1000.0
            };
            foreach (var annotation in _execution.Metadata.Records.Where(r => r.ViewId == viewId))
            {
                result.Left = Math.Min(result.Left, annotation.ConservativeBoundsMm.Left);
                result.Bottom = Math.Min(result.Bottom, annotation.ConservativeBoundsMm.Bottom);
                result.Right = Math.Max(result.Right, annotation.ConservativeBoundsMm.Right);
                result.Top = Math.Max(result.Top, annotation.ConservativeBoundsMm.Top);
            }
            return result;
        }

        private List<string> FindOrphanDimensions()
        {
            var orphans = new List<string>();
            var plannedTokens = new HashSet<string>(_execution.Metadata.Tokens, StringComparer.Ordinal);
            foreach (var pair in _views)
            {
                var display = pair.Value.GetFirstDisplayDimension5() as IDisplayDimension;
                while (display != null)
                {
                    var annotation = display.GetAnnotation() as IAnnotation;
                    if (annotation == null)
                    {
                        orphans.Add(pair.Key + ":annotation-missing");
                    }
                    else
                    {
                        var bytes = _drawing.Model.Extension.GetPersistReference3(annotation) as byte[];
                        var token = bytes == null ? string.Empty : Convert.ToBase64String(bytes);
                        if (string.IsNullOrEmpty(token) || !plannedTokens.Contains(token))
                            orphans.Add(pair.Key + ":" + (string.IsNullOrEmpty(token) ? "unidentified" : token));
                    }
                    display = display.GetNext5() as IDisplayDimension;
                }
            }
            return orphans;
        }

        private List<string> FindAnnotationLayoutViolations(DrawingPlan plan)
        {
            var violations = new List<string>();
            foreach (var group in _execution.Metadata.Records.GroupBy(record => record.ViewId, StringComparer.Ordinal))
            {
                var records = group.ToList();
                var plannedView = plan.Views.First(view => view.ViewId == group.Key);
                for (var index = 0; index < records.Count; index++)
                {
                    var bounds = records[index].ConservativeBoundsMm;
                    var reserved = plannedView.ReservedAnnotationBoundsMm;
                    if (bounds.Left < reserved.Left || bounds.Bottom < reserved.Bottom ||
                        bounds.Right > reserved.Right || bounds.Top > reserved.Top)
                        violations.Add(records[index].RequirementId + ":outside-reserved-lane");
                    for (var second = index + 1; second < records.Count; second++)
                    {
                        if (Intersects(bounds, records[second].ConservativeBoundsMm, 1.0))
                            violations.Add(records[index].RequirementId + ":overlaps:" + records[second].RequirementId);
                    }
                }
            }
            return violations;
        }

        private static bool Intersects(Rect2 first, Rect2 second, double clearance) =>
            !(first.Right + clearance <= second.Left || second.Right + clearance <= first.Left ||
              first.Top + clearance <= second.Bottom || second.Top + clearance <= first.Bottom);

        private Dictionary<string, string> ReadTitleFields(DrawingPlan plan)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var manager = _drawing.Model.Extension.CustomPropertyManager[string.Empty];
            foreach (var name in plan.TitleFields.Keys)
            {
                manager.Get6(name, false, out _, out var resolved, out _, out _);
                result[name] = resolved ?? string.Empty;
            }
            return result;
        }

        private static double ReadScale(IView view)
        {
            var ratio = view.ScaleRatio as double[]
                ?? throw new InvalidOperationException("View scale is unavailable during final verification.");
            return ratio[0] / ratio[1];
        }
    }
}
