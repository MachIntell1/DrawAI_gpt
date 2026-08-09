using System;
using System.Collections.Generic;
using MachIntellDrawAI.Models;
using SolidWorks.Interop.sldworks;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class CylinderEvidence
    {
        public IFace2 Face { get; set; } = null!;
        public Point3 Center { get; set; } = new Point3();
        public Vector3 Axis { get; set; } = new Vector3();
        public double RadiusMm { get; set; }
        public double SweepAngleDeg { get; set; }
        public double AxialLengthMm { get; set; }
        public bool OpensToOuterBoundary { get; set; }
        public bool IsInternal { get; set; }
        public object? RepresentativeCircularEdge { get; set; }
        public List<EntityRef> Refs { get; set; } = new List<EntityRef>();
    }

    internal sealed class TopologyEvidenceExtractor
    {
        private const double FullSweepToleranceDeg = 0.25;
        private readonly PersistentReferenceService _refs;

        public TopologyEvidenceExtractor(PersistentReferenceService refs) => _refs = refs;

        public CylinderEvidence? ReadCylinder(IFace2 face)
        {
            var surface = face.GetSurface() as ISurface;
            if (surface == null || !surface.IsCylinder()) return null;
            var cylinder = surface.CylinderParams as double[];
            if (cylinder == null || cylinder.Length < 7) return null;
            var bounds = face.GetUVBounds() as double[];
            if (bounds == null || bounds.Length < 4) return null;

            double sweep = Math.Abs(bounds[1] - bounds[0]) * 180.0 / Math.PI;
            sweep = Math.Min(sweep, 360.0);
            var evidence = new CylinderEvidence
            {
                Face = face,
                Center = new Point3 { X = cylinder[0] * 1000.0, Y = cylinder[1] * 1000.0, Z = cylinder[2] * 1000.0 },
                Axis = Normalize(new Vector3 { X = cylinder[3], Y = cylinder[4], Z = cylinder[5] }),
                RadiusMm = Math.Abs(cylinder[6]) * 1000.0,
                SweepAngleDeg = sweep,
                AxialLengthMm = Math.Abs(bounds[3] - bounds[2]) * 1000.0,
                IsInternal = face.FaceInSurfaceSense()
            };

            var hasNonCircularBoundary = false;
            foreach (var item in ExactGeometryExtractor.ToObjects(face.GetEdges()))
            {
                if (!(item is IEdge edge)) continue;
                var curve = edge.GetCurve() as ICurve;
                var circular = curve != null && curve.IsCircle();
                if (circular && evidence.RepresentativeCircularEdge == null)
                    evidence.RepresentativeCircularEdge = edge;
                if (!circular) hasNonCircularBoundary = true;
            }
            evidence.OpensToOuterBoundary = sweep < 360.0 - FullSweepToleranceDeg || hasNonCircularBoundary;
            evidence.Refs.Add(_refs.Create(face, "FACE"));
            if (evidence.RepresentativeCircularEdge != null)
                evidence.Refs.Insert(0, _refs.Create(evidence.RepresentativeCircularEdge, "CIRCULAR_EDGE"));
            return evidence;
        }

        public TopologyEvidence ToContract(CylinderEvidence evidence, int tangentFaceCount = 0) => new TopologyEvidence
        {
            SurfaceKind = "CYLINDER",
            SweepAngleDeg = evidence.SweepAngleDeg,
            ClosedProfile = !evidence.OpensToOuterBoundary,
            OpensToOuterBoundary = evidence.OpensToOuterBoundary,
            TangentFaceCount = tangentFaceCount,
            IsInternal = evidence.IsInternal,
            Radius = evidence.RadiusMm,
            AxialLength = evidence.AxialLengthMm,
            EntityRefs = evidence.Refs
        };

        private static Vector3 Normalize(Vector3 value)
        {
            var magnitude = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
            if (magnitude < 1e-9) throw new InvalidOperationException("Cylindrical surface has a zero axis vector.");
            return new Vector3 { X = value.X / magnitude, Y = value.Y / magnitude, Z = value.Z / magnitude };
        }
    }
}
