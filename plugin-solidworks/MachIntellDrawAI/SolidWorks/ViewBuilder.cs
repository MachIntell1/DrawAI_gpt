using System;
using System.Collections.Generic;
using System.Linq;
using MachIntellDrawAI.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class ViewBuildResult
    {
        public Dictionary<string, IView> Views { get; } = new Dictionary<string, IView>(StringComparer.Ordinal);
    }

    internal sealed class ViewBuilder
    {
        private readonly DrawingContext _context;
        private readonly string _modelPath;

        public ViewBuilder(DrawingContext context, string modelPath)
        {
            _context = context;
            _modelPath = modelPath;
        }

        public ViewBuildResult Build(DrawingPlan plan)
        {
            var result = new ViewBuildResult();
            foreach (var sheetGroup in plan.Views.GroupBy(v => v.SheetIndex).OrderBy(g => g.Key))
            {
                _context.Drawing.ActivateSheet(_context.SheetNames[sheetGroup.Key]);
                foreach (var planned in sheetGroup.OrderBy(ViewOrder))
                {
                    IView? view;
                    if (planned.Kind == "PROJECTED") view = CreateProjected(planned, result.Views);
                    else if (planned.Kind == "SECTION") view = CreateSection(planned, result.Views);
                    else view = _context.Drawing.CreateDrawViewFromModelView3(
                        _modelPath,
                        planned.SolidworksViewName,
                        planned.CenterXMm / 1000.0,
                        planned.CenterYMm / 1000.0,
                        0) as IView;
                    if (view == null) throw new InvalidOperationException("SolidWorks failed to create planned view " + planned.ViewId);
                    view.ReferencedConfiguration = plan.Configuration;
                    view.UseSheetScale = 0;
                    view.ScaleRatio = new[] { planned.Scale, 1.0 };
                    ApplyDisplayStyle(view, planned.DisplayStyle);
                    _context.Model.EditRebuild3();
                    VerifyView(planned, view, plan.Configuration);
                    result.Views.Add(planned.ViewId, view);
                }
            }
            _context.Drawing.ActivateSheet(_context.SheetNames[1]);
            return result;
        }

        private IView? CreateProjected(ViewPlan planned, Dictionary<string, IView> views)
        {
            if (planned.ParentViewId == null || !views.TryGetValue(planned.ParentViewId, out var parent))
                throw new InvalidOperationException("Projected view is missing its planned parent: " + planned.ViewId);
            _context.Model.ClearSelection2(true);
            if (!_context.Model.Extension.SelectByID2(parent.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0))
                throw new InvalidOperationException("Projected parent view could not be selected: " + planned.ParentViewId);
            return _context.Drawing.CreateUnfoldedViewAt3(
                planned.CenterXMm / 1000.0,
                planned.CenterYMm / 1000.0,
                0,
                false) as IView;
        }

        private IView? CreateSection(ViewPlan planned, Dictionary<string, IView> views)
        {
            if (planned.ParentViewId == null || !views.TryGetValue(planned.ParentViewId, out var parent))
                throw new InvalidOperationException("Section view is missing its same-sheet source view: " + planned.ViewId);
            var outline = parent.GetOutline() as double[];
            if (outline == null || outline.Length < 4)
                throw new InvalidOperationException("Section source view has no exact drawing outline.");
            _context.Drawing.ActivateView(parent.Name);
            _context.Model.ClearSelection2(true);
            var marginX = Math.Max((outline[2] - outline[0]) * 0.05, 0.001);
            var marginY = Math.Max((outline[3] - outline[1]) * 0.05, 0.001);
            double sx, sy, ex, ey;
            if (planned.SectionAxis == planned.ModelUAxis)
            {
                sx = outline[0] + marginX; ex = outline[2] - marginX;
                sy = ey = (outline[1] + outline[3]) / 2.0;
            }
            else
            {
                sx = ex = (outline[0] + outline[2]) / 2.0;
                sy = outline[1] + marginY; ey = outline[3] - marginY;
            }
            _context.Model.SketchManager.InsertSketch(true);
            var segment = _context.Model.SketchManager.CreateLine(sx, sy, 0, ex, ey, 0) as IEntity;
            _context.Model.SketchManager.InsertSketch(true);
            if (segment == null || !segment.Select4(false, null))
                throw new InvalidOperationException("The exact section cutting line could not be selected.");
            return _context.Drawing.CreateSectionViewAt5(
                planned.CenterXMm / 1000.0,
                planned.CenterYMm / 1000.0,
                0,
                "A",
                0,
                null,
                0) as IView;
        }

        private static int ViewOrder(ViewPlan view)
        {
            if (view.Kind == "BASE") return 0;
            if (view.Kind == "PROJECTED") return 1;
            if (view.Kind == "ISOMETRIC") return 2;
            if (view.Kind == "SECTION") return 3;
            return 4;
        }

        private static void ApplyDisplayStyle(IView view, string style)
        {
            int mode;
            switch (style)
            {
                case "HIDDEN_LINES_VISIBLE": mode = (int)swDisplayMode_e.swHIDDEN_GREYED; break;
                case "HIDDEN_LINES_REMOVED": mode = (int)swDisplayMode_e.swHIDDEN; break;
                case "SHADED": mode = (int)swDisplayMode_e.swSHADED; break;
                default: throw new InvalidOperationException("Unsupported display style " + style);
            }
            if (!view.SetDisplayMode3(false, mode, false, false))
                throw new InvalidOperationException("SolidWorks rejected display style " + style + " for " + view.Name);
        }

        private static void VerifyView(ViewPlan planned, IView actual, string configuration)
        {
            if (!string.Equals(actual.ReferencedConfiguration, configuration, StringComparison.Ordinal))
                throw new InvalidOperationException("View references the wrong model configuration: " + planned.ViewId);
            var ratio = actual.ScaleRatio as double[];
            if (ratio == null || ratio.Length < 2 || ratio[1] <= 0 || Math.Abs(ratio[0] / ratio[1] - planned.Scale) > 1e-6)
                throw new InvalidOperationException("SolidWorks did not retain the exact scale for " + planned.ViewId);
            var outline = actual.GetOutline() as double[];
            if (outline == null || outline.Length < 4 || outline[2] <= outline[0] || outline[3] <= outline[1])
                throw new InvalidOperationException("SolidWorks returned an invalid outline for " + planned.ViewId);
            var position = actual.Position as double[];
            if (position == null || position.Length < 2 ||
                Math.Abs(position[0] * 1000.0 - planned.CenterXMm) > 0.1 ||
                Math.Abs(position[1] * 1000.0 - planned.CenterYMm) > 0.1)
                throw new InvalidOperationException("View position differs from the deterministic plan: " + planned.ViewId);
        }
    }
}
