using System;
using System.Collections.Generic;
using MachIntellDrawAI.Infrastructure;
using SolidWorks.Interop.sldworks;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class CadArtifactController
    {
        private readonly IModelDoc2 _source;
        private readonly DrawingContext _drawing;
        private readonly IReadOnlyDictionary<string, IView> _views;

        public CadArtifactController(IModelDoc2 source, DrawingContext drawing, IReadOnlyDictionary<string, IView> views)
        {
            _source = source;
            _drawing = drawing;
            _views = views;
        }

        public bool HideAndVerify()
        {
            var feature = _source.FirstFeature() as IFeature;
            while (feature != null)
            {
                var type = feature.GetTypeName2() ?? string.Empty;
                var referenceGeometry = IsReferenceGeometry(type);
                var sketch = !referenceGeometry && IsSketch(type);
                if (referenceGeometry || sketch)
                {
                    foreach (var view in _views.Values)
                    {
                        _drawing.Drawing.ActivateView(view.Name);
                        _drawing.Model.ClearSelection2(true);
                        var corresponding = view.GetCorrespondingEntity(feature);
                        var selected = corresponding is IEntity entity
                            ? entity.Select4(false, null)
                            : view.SelectEntity(feature, false);
                        if (!selected) continue; // The feature is not displayed in this view.
                        if (sketch) _drawing.Model.BlankSketch();
                        else _drawing.Model.BlankRefGeom();
                        _drawing.Model.ClearSelection2(true);
                    }
                }
                feature = feature.GetNextFeature() as IFeature;
            }
            _drawing.Model.ClearSelection2(true);
            return true;
        }

        private static bool IsSketch(string type) =>
            type.IndexOf("ProfileFeature", StringComparison.OrdinalIgnoreCase) >= 0 ||
            type.IndexOf("Sketch", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsReferenceGeometry(string type) =>
            type.IndexOf("Origin", StringComparison.OrdinalIgnoreCase) >= 0 ||
            type.IndexOf("RefPlane", StringComparison.OrdinalIgnoreCase) >= 0 ||
            type.IndexOf("RefAxis", StringComparison.OrdinalIgnoreCase) >= 0 ||
            type.IndexOf("CoordSys", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
