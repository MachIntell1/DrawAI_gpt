using System;
using System.Collections.Generic;
using MachIntellDrawAI.Models;
using SolidWorks.Interop.sldworks;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class BrepFeatureExtractor
    {
        private readonly IPartDoc _part;
        private readonly PersistentReferenceService _refs;
        private readonly TopologyEvidenceExtractor _topology;

        public BrepFeatureExtractor(IPartDoc part, PersistentReferenceService refs, TopologyEvidenceExtractor topology)
        {
            _part = part;
            _refs = refs;
            _topology = topology;
        }

        public void Extract(HashSet<string> nativeOwnedFaceTokens, List<RawFeature> features, List<ExtractionFinding> findings)
        {
            foreach (var item in ExactGeometryExtractor.ToObjects(_part.GetBodies2(0, false)))
            {
                if (!(item is IBody2 body)) continue;
                foreach (var faceItem in ExactGeometryExtractor.ToObjects(body.GetFaces()))
                {
                    if (!(faceItem is IFace2 face)) continue;
                    var faceRef = _refs.Create(face, "FACE");
                    if (nativeOwnedFaceTokens.Contains(faceRef.Token)) continue;
                    var cylinder = _topology.ReadCylinder(face);
                    if (cylinder == null) continue;

                    var reference = cylinder.RepresentativeCircularEdge == null
                        ? faceRef
                        : _refs.Create(cylinder.RepresentativeCircularEdge, "CIRCULAR_EDGE");
                    var raw = new RawFeature
                    {
                        FeatureId = _refs.StableId(faceRef, "brep:"),
                        Name = "B-rep cylindrical face",
                        NativeType = "IMPORTED_CYLINDRICAL_FACE",
                        NativeSubtype = null,
                        Source = "BREP_TOPOLOGY",
                        FeatureRef = faceRef,
                        EntityRefs = new List<EntityRef> { reference },
                        Centers = new List<Point3> { cylinder.Center },
                        Axis = cylinder.Axis,
                        Specification = new FeatureSpecification
                        {
                            Diameter = cylinder.IsInternal && !cylinder.OpensToOuterBoundary ? cylinder.RadiusMm * 2.0 : (double?)null,
                            Radius = cylinder.OpensToOuterBoundary ? cylinder.RadiusMm : (double?)null
                        },
                        Topology = _topology.ToContract(cylinder),
                        PatternCount = 1
                    };
                    features.Add(raw);

                    if (cylinder.RepresentativeCircularEdge == null)
                        findings.Add(Blocker("BREP-NO-CIRCULAR-EDGE", "Cylindrical topology has no circular edge that can drive an associative callout.", faceRef));
                    if (cylinder.OpensToOuterBoundary)
                        findings.Add(Blocker(
                            "BREP-OPEN-CYLINDER-INTENT",
                            "An imported/open cylindrical face could be a notch or blend. Native feature provenance or approved classification is required.",
                            faceRef));
                    if (!cylinder.OpensToOuterBoundary && !cylinder.IsInternal)
                        findings.Add(Blocker(
                            "BREP-EXTERNAL-CYLINDER-INTENT",
                            "An imported external cylinder has no approved manufacturing intent in the current contract.",
                            faceRef));
                    if (!cylinder.OpensToOuterBoundary && cylinder.IsInternal)
                        findings.Add(Blocker(
                            "BREP-HOLE-END-CONDITION",
                            "An imported cylindrical hole lacks a native through/blind end condition; specify the manufacturing intent before release.",
                            faceRef));
                }
            }
        }

        private static ExtractionFinding Blocker(string code, string message, EntityRef entityRef) => new ExtractionFinding
        {
            Code = code,
            Severity = "BLOCKER",
            Message = message,
            EntityRefs = new List<EntityRef> { entityRef }
        };
    }
}
