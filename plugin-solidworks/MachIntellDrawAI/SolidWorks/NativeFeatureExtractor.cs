using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MachIntellDrawAI.Infrastructure;
using MachIntellDrawAI.Models;
using SolidWorks.Interop.sldworks;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class NativeFeatureResult
    {
        public List<RawFeature> Features { get; } = new List<RawFeature>();
        public List<ExtractionFinding> Findings { get; } = new List<ExtractionFinding>();
        public HashSet<string> OwnedFaceTokens { get; } = new HashSet<string>(StringComparer.Ordinal);
    }

    internal sealed class NativeFeatureExtractor
    {
        private readonly IModelDoc2 _model;
        private readonly PersistentReferenceService _refs;
        private readonly TopologyEvidenceExtractor _topology;

        public NativeFeatureExtractor(IModelDoc2 model, PersistentReferenceService refs, TopologyEvidenceExtractor topology)
        {
            _model = model;
            _refs = refs;
            _topology = topology;
        }

        public NativeFeatureResult Extract()
        {
            var result = new NativeFeatureResult();
            var feature = _model.FirstFeature() as IFeature;
            while (feature != null)
            {
                try
                {
                    if (!feature.IsSuppressed())
                    {
                        var type = feature.GetTypeName2() ?? string.Empty;
                        if (IsHole(type)) ExtractHole(feature, type, result);
                        else if (IsFillet(type)) ExtractRadiusFeature(feature, type, "FILLET", 2, result);
                        else if (IsChamfer(type)) ExtractChamfer(feature, type, result);
                        else if (IsPattern(type)) ExtractPattern(feature, type, result);
                    }
                }
                catch (Exception ex)
                {
                    EntityRef? reference = null;
                    try { reference = _refs.Create(feature, "FEATURE"); } catch { }
                    result.Findings.Add(new ExtractionFinding
                    {
                        Code = "FEATURE-EXTRACTION-FAILED",
                        Severity = "BLOCKER",
                        Message = $"Feature {feature.Name} could not be extracted deterministically: {ex.Message}",
                        EntityRefs = reference == null ? new List<EntityRef>() : new List<EntityRef> { reference }
                    });
                }
                feature = feature.GetNextFeature() as IFeature;
            }
            return result;
        }

        private void ExtractHole(IFeature feature, string type, NativeFeatureResult result)
        {
            var featureRef = _refs.Create(feature, "FEATURE");
            var specific = feature.GetSpecificFeature2()
                ?? throw new InvalidOperationException("Hole feature exposes no feature data.");
            var selectionsAccessed = ComCall.Optional(specific, "AccessSelections", _model, null) as bool? ?? false;
            try
            {
                var specification = new FeatureSpecification
                {
                    Diameter = Millimetres(ComCall.Double(specific, "HoleDiameter", "Diameter")),
                    Depth = Millimetres(ComCall.Double(specific, "HoleDepth", "Depth")),
                    CounterboreDiameter = Millimetres(ComCall.Double(specific, "CounterBoreDiameter")),
                    CounterboreDepth = Millimetres(ComCall.Double(specific, "CounterBoreDepth")),
                    CountersinkDiameter = Millimetres(ComCall.Double(specific, "CounterSinkDiameter")),
                    CountersinkAngleDeg = RadiansToDegrees(ComCall.Double(specific, "CounterSinkAngle")),
                    ThreadDepth = Millimetres(ComCall.Double(specific, "ThreadDepth")),
                    ThreadClass = Clean(ComCall.Property<string>(specific, "ThreadClass")),
                    ThreadDesignation = Clean(ComCall.Property<string>(specific, "FastenerSize", "HoleSize")),
                    ThreadPitch = Millimetres(ComCall.Double(specific, "ThreadPitch")),
                    DrillDepth = Millimetres(ComCall.Double(specific, "DrillDepth"))
                };
                var endConditionValue = ComCall.Property<object>(specific, "EndCondition");
                int? endCondition = endConditionValue == null ? (int?)null : Convert.ToInt32(endConditionValue, CultureInfo.InvariantCulture);
                specification.Through = endCondition.HasValue ? IsThroughEndCondition(endCondition.Value) : (bool?)null;

                var cylinders = GetOwnedCylinders(feature, result).ToList();
                var primary = SelectPrimaryCylinders(cylinders, specification.Diameter);
                if (primary.Count == 0)
                    throw new InvalidOperationException("Hole feature has no cylindrical face matching its native diameter.");

                var raw = new RawFeature
                {
                    FeatureId = _refs.StableId(featureRef, "feat:"),
                    Name = feature.Name,
                    NativeType = type,
                    NativeSubtype = NativeHoleSubtype(specification),
                    Source = "SOLIDWORKS_NATIVE",
                    FeatureRef = featureRef,
                    Specification = specification,
                    Patterned = primary.Count > 1,
                    PatternCount = primary.Count,
                    Axis = primary[0].Axis,
                    Topology = _topology.ToContract(primary[0])
                };
                foreach (var cylinder in primary)
                {
                    if (cylinder.RepresentativeCircularEdge == null)
                        throw new InvalidOperationException("Hole cylinder has no circular edge for an associative callout.");
                    raw.Centers.Add(cylinder.Center);
                    raw.EntityRefs.Add(_refs.Create(cylinder.RepresentativeCircularEdge, "CIRCULAR_EDGE"));
                }
                result.Features.Add(raw);
            }
            finally
            {
                if (selectionsAccessed) ComCall.Optional(specific, "ReleaseSelectionAccess");
            }
        }

        private void ExtractRadiusFeature(IFeature feature, string type, string subtype, int tangentFaces, NativeFeatureResult result)
        {
            var featureRef = _refs.Create(feature, "FEATURE");
            var specific = feature.GetSpecificFeature2();
            var nativeRadius = specific == null ? null : Millimetres(ComCall.Double(specific, "DefaultRadius", "Radius"));
            var cylinders = GetOwnedCylinders(feature, result).ToList();
            if (cylinders.Count == 0)
                throw new InvalidOperationException("Fillet feature has no cylindrical face evidence.");
            var raw = new RawFeature
            {
                FeatureId = _refs.StableId(featureRef, "feat:"),
                Name = feature.Name,
                NativeType = type,
                NativeSubtype = subtype,
                Source = "SOLIDWORKS_NATIVE",
                FeatureRef = featureRef,
                Specification = new FeatureSpecification { Radius = nativeRadius ?? cylinders[0].RadiusMm },
                Axis = cylinders[0].Axis,
                Topology = _topology.ToContract(cylinders[0], tangentFaces),
                Patterned = cylinders.Count > 1,
                PatternCount = cylinders.Count
            };
            foreach (var cylinder in cylinders)
            {
                raw.Centers.Add(cylinder.Center);
                raw.EntityRefs.Add(cylinder.Refs[0]);
            }
            result.Features.Add(raw);
        }

        private void ExtractChamfer(IFeature feature, string type, NativeFeatureResult result)
        {
            var featureRef = _refs.Create(feature, "FEATURE");
            var specific = feature.GetSpecificFeature2()
                ?? throw new InvalidOperationException("Chamfer feature exposes no feature data.");
            var distance = Millimetres(ComCall.Double(specific, "Distance", "Distance1"));
            var angle = RadiansToDegrees(ComCall.Double(specific, "Angle"));
            var ownedRefs = new List<EntityRef>();
            foreach (var item in ExactGeometryExtractor.ToObjects(feature.GetFaces()))
            {
                if (!(item is IFace2 face)) continue;
                var faceRef = _refs.Create(face, "FACE");
                result.OwnedFaceTokens.Add(faceRef.Token);
                ownedRefs.Add(faceRef);
            }
            if (ownedRefs.Count == 0 || !distance.HasValue)
                throw new InvalidOperationException("Chamfer feature lacks an owned face or native distance.");
            result.Features.Add(new RawFeature
            {
                FeatureId = _refs.StableId(featureRef, "feat:"),
                Name = feature.Name,
                NativeType = type,
                NativeSubtype = "CHAMFER",
                Source = "SOLIDWORKS_NATIVE",
                FeatureRef = featureRef,
                EntityRefs = ownedRefs,
                Specification = new FeatureSpecification { ChamferDistance = distance, ChamferAngleDeg = angle }
            });
        }

        private void ExtractPattern(IFeature feature, string type, NativeFeatureResult result)
        {
            var specific = feature.GetSpecificFeature2()
                ?? throw new InvalidOperationException("Pattern feature exposes no feature data.");
            var seed = ComCall.Optional(specific, "GetSeedFeature") as IFeature
                ?? ComCall.Property<IFeature>(specific, "SeedFeature");
            if (seed == null)
                throw new InvalidOperationException("Pattern seed feature cannot be resolved.");
            var seedRef = _refs.Create(seed, "FEATURE");
            var seedId = _refs.StableId(seedRef, "feat:");
            var seedRaw = result.Features.FirstOrDefault(item => item.FeatureId == seedId)
                ?? throw new InvalidOperationException("Pattern seed is not a supported native manufacturing feature.");
            var cylinders = GetOwnedCylinders(feature, result).ToList();
            if (cylinders.Count == 0)
                throw new InvalidOperationException("Pattern has no owned cylindrical instance faces.");
            var patternRef = _refs.Create(feature, "FEATURE");
            var raw = new RawFeature
            {
                FeatureId = _refs.StableId(patternRef, "feat:"),
                Name = feature.Name,
                NativeType = seedRaw.NativeType,
                NativeSubtype = seedRaw.NativeSubtype,
                Source = "SOLIDWORKS_NATIVE",
                FeatureRef = patternRef,
                Specification = seedRaw.Specification,
                Patterned = true,
                PatternCount = cylinders.Count,
                Axis = cylinders[0].Axis,
                Topology = _topology.ToContract(cylinders[0])
            };
            var requiresCircularEdge = IsHole(seedRaw.NativeType);
            foreach (var cylinder in cylinders)
            {
                if (requiresCircularEdge && cylinder.RepresentativeCircularEdge == null)
                    throw new InvalidOperationException("Patterned hole instance has no circular edge for an associative callout.");
                raw.Centers.Add(cylinder.Center);
                raw.EntityRefs.Add(cylinder.RepresentativeCircularEdge == null
                    ? _refs.Create(cylinder.Face, "FACE")
                    : _refs.Create(cylinder.RepresentativeCircularEdge, "CIRCULAR_EDGE"));
            }
            result.Features.Add(raw);
        }

        private IEnumerable<CylinderEvidence> GetOwnedCylinders(IFeature feature, NativeFeatureResult result)
        {
            foreach (var item in ExactGeometryExtractor.ToObjects(feature.GetFaces()))
            {
                if (!(item is IFace2 face)) continue;
                var faceRef = _refs.Create(face, "FACE");
                result.OwnedFaceTokens.Add(faceRef.Token);
                var cylinder = _topology.ReadCylinder(face);
                if (cylinder != null) yield return cylinder;
            }
        }

        private static List<CylinderEvidence> SelectPrimaryCylinders(List<CylinderEvidence> cylinders, double? diameter)
        {
            if (!diameter.HasValue) return cylinders.Where(c => c.RepresentativeCircularEdge != null).ToList();
            var tolerance = Math.Max(0.01, diameter.Value * 0.001);
            return cylinders.Where(c => Math.Abs(c.RadiusMm * 2.0 - diameter.Value) <= tolerance && c.RepresentativeCircularEdge != null).ToList();
        }

        private static string NativeHoleSubtype(FeatureSpecification specification)
        {
            if (!string.IsNullOrWhiteSpace(specification.ThreadDesignation)) return "TAPPED_HOLE";
            if (specification.CounterboreDiameter.HasValue) return "COUNTERBORE_HOLE";
            if (specification.CountersinkDiameter.HasValue) return "COUNTERSINK_HOLE";
            return "PLAIN_HOLE";
        }

        private static bool IsHole(string type) => type.IndexOf("HoleWzd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   type.IndexOf("WizardHole", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   type.IndexOf("AdvHole", StringComparison.OrdinalIgnoreCase) >= 0;
        private static bool IsFillet(string type) => type.IndexOf("Fillet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                     type.IndexOf("Round", StringComparison.OrdinalIgnoreCase) >= 0;
        private static bool IsChamfer(string type) => type.IndexOf("Chamfer", StringComparison.OrdinalIgnoreCase) >= 0;
        private static bool IsPattern(string type) => type.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                      type.IndexOf("LPattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                      type.IndexOf("CirPattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                      type.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0;
        private static bool IsThroughEndCondition(int value) => value == 1 || value == 2 || value == 3;
        private static double? Millimetres(double? metres) => metres.HasValue && metres.Value > 0 ? metres.Value * 1000.0 : (double?)null;
        private static double? RadiansToDegrees(double? radians) => radians.HasValue && radians.Value > 0 ? radians.Value * 180.0 / Math.PI : (double?)null;
        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
