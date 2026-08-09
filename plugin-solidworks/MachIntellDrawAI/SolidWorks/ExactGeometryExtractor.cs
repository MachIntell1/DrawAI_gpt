using System;
using System.Collections.Generic;
using MachIntellDrawAI.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class ExactGeometryResult
    {
        public Bounds3 Bounds { get; set; } = new Bounds3();
        public string Source { get; set; } = "EXACT_VERTICES";
        public int BodyCount { get; set; }
        public List<ExtractionFinding> Findings { get; } = new List<ExtractionFinding>();
    }

    internal sealed class ExactGeometryExtractor
    {
        private readonly IPartDoc _part;
        private readonly PersistentReferenceService _refs;

        public ExactGeometryExtractor(IPartDoc part, PersistentReferenceService refs)
        {
            _part = part;
            _refs = refs;
        }

        public ExactGeometryResult Extract()
        {
            var result = new ExactGeometryResult();
            var bodies = ToObjects(_part.GetBodies2((int)swBodyType_e.swSolidBody, false));
            result.BodyCount = bodies.Count;
            if (bodies.Count == 0)
                throw new InvalidOperationException("The active configuration contains no unsuppressed solid body.");

            var points = new List<(Point3 point, object vertex)>();
            foreach (var item in bodies)
            {
                if (!(item is IBody2 body)) continue;
                foreach (var vertexItem in ToObjects(body.GetVertices()))
                {
                    if (!(vertexItem is IVertex vertex)) continue;
                    var coordinates = vertex.GetPoint() as double[];
                    if (coordinates == null || coordinates.Length < 3) continue;
                    points.Add((new Point3
                    {
                        X = coordinates[0] * 1000.0,
                        Y = coordinates[1] * 1000.0,
                        Z = coordinates[2] * 1000.0
                    }, vertex));
                }
            }

            if (points.Count == 0)
            {
                var box = _part.GetPartBox(true) as double[];
                if (box == null || box.Length < 6)
                    throw new InvalidOperationException("SolidWorks returned neither exact vertices nor an approximate part box.");
                result.Source = "APPROXIMATE_BODY_BOX";
                result.Bounds = new Bounds3
                {
                    Minimum = new Point3 { X = box[0] * 1000.0, Y = box[1] * 1000.0, Z = box[2] * 1000.0 },
                    Maximum = new Point3 { X = box[3] * 1000.0, Y = box[4] * 1000.0, Z = box[5] * 1000.0 }
                };
                result.Findings.Add(new ExtractionFinding
                {
                    Code = "GEOM-APPROX-BOUNDS",
                    Severity = "BLOCKER",
                    Message = "Exact vertex bounds were unavailable; GetPartBox is approximate and cannot drive manufacturing dimensions."
                });
                return result;
            }

            var xMin = Min(points, p => p.point.X); var xMax = Max(points, p => p.point.X);
            var yMin = Min(points, p => p.point.Y); var yMax = Max(points, p => p.point.Y);
            var zMin = Min(points, p => p.point.Z); var zMax = Max(points, p => p.point.Z);
            result.Bounds = new Bounds3
            {
                Minimum = new Point3 { X = xMin.point.X, Y = yMin.point.Y, Z = zMin.point.Z },
                Maximum = new Point3 { X = xMax.point.X, Y = yMax.point.Y, Z = zMax.point.Z },
                ExtremeRefs = new Dictionary<string, EntityRef>
                {
                    ["X_MIN"] = _refs.Create(xMin.vertex, "VERTEX"),
                    ["X_MAX"] = _refs.Create(xMax.vertex, "VERTEX"),
                    ["Y_MIN"] = _refs.Create(yMin.vertex, "VERTEX"),
                    ["Y_MAX"] = _refs.Create(yMax.vertex, "VERTEX"),
                    ["Z_MIN"] = _refs.Create(zMin.vertex, "VERTEX"),
                    ["Z_MAX"] = _refs.Create(zMax.vertex, "VERTEX")
                }
            };
            return result;
        }

        private static (Point3 point, object vertex) Min(List<(Point3 point, object vertex)> values, Func<(Point3 point, object vertex), double> selector)
        {
            var best = values[0];
            for (var i = 1; i < values.Count; i++) if (selector(values[i]) < selector(best)) best = values[i];
            return best;
        }

        private static (Point3 point, object vertex) Max(List<(Point3 point, object vertex)> values, Func<(Point3 point, object vertex), double> selector)
        {
            var best = values[0];
            for (var i = 1; i < values.Count; i++) if (selector(values[i]) > selector(best)) best = values[i];
            return best;
        }

        internal static List<object> ToObjects(object? value)
        {
            var result = new List<object>();
            if (value is Array array) foreach (var item in array) if (item != null) result.Add(item);
            return result;
        }
    }
}
