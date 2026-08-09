using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MachIntellDrawAI.Infrastructure;
using MachIntellDrawAI.Models;
using Newtonsoft.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class ModelManifestExtractor
    {
        private static readonly string[] ControlledProperties =
        {
            "MI_PART_NUMBER", "MI_DRAWING_NUMBER", "MI_REVISION", "MI_DESCRIPTION", "MI_MATERIAL_SPEC",
            "MI_HEAT_TREATMENT", "MI_COATING", "MI_EDGE_REQUIREMENT", "MI_GENERAL_TOLERANCE_POLICY_ID",
            "MI_INTERNAL_THREAD_CLASS", "MI_EXTERNAL_THREAD_CLASS", "MI_DATUMS_JSON", "MI_GDT_JSON",
            "MI_SURFACE_TEXTURE_JSON", "MI_APPROVED_BY", "MI_APPROVED_AT", "MI_APPROVAL_ID"
        };

        private readonly IModelDoc2 _model;

        public ModelManifestExtractor(IModelDoc2 model) => _model = model;

        public ModelManifest Extract()
        {
            if (_model.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new NotSupportedException("Version 2.0 generates drawings only from a saved SolidWorks part document.");
            var path = _model.GetPathName();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidOperationException("Save the part before generating a drawing.");
            if (_model.GetSaveFlag())
                throw new InvalidOperationException("The part has unsaved changes. Rebuild and save it before planning.");
            if (!path.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The active document is not a .SLDPRT file.");

            var configuration = _model.ConfigurationManager.ActiveConfiguration.Name;
            var refs = new PersistentReferenceService(_model, configuration);
            var part = (IPartDoc)_model;
            var exact = new ExactGeometryExtractor(part, refs).Extract();
            var topology = new TopologyEvidenceExtractor(refs);
            var native = new NativeFeatureExtractor(_model, refs, topology).Extract();
            var features = new List<RawFeature>(native.Features);
            var findings = new List<ExtractionFinding>(exact.Findings);
            findings.AddRange(native.Findings);
            new BrepFeatureExtractor(part, refs, topology).Extract(native.OwnedFaceTokens, features, findings);

            var properties = ReadControlledProperties(configuration);
            var materialFromModel = ReadModelMaterial(part, configuration);
            var intent = ReadEngineeringIntent(properties, materialFromModel, findings);
            return new ModelManifest
            {
                ModelName = Path.GetFileNameWithoutExtension(path),
                ModelHash = StableHash.File(path),
                FilePath = path,
                Configuration = configuration,
                DocumentType = "PART",
                Units = "MM",
                Bounds = exact.Bounds,
                BoundsSource = exact.Source,
                BodyCount = exact.BodyCount,
                Features = features,
                ExtractionFindings = findings,
                EngineeringIntent = intent,
                MaterialFromModel = materialFromModel,
                CustomProperties = properties
            };
        }

        private Dictionary<string, string> ReadControlledProperties(string configuration)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var configurationManager = _model.Extension.CustomPropertyManager[configuration];
            var documentManager = _model.Extension.CustomPropertyManager[string.Empty];
            foreach (var property in ControlledProperties)
            {
                var value = ReadProperty(configurationManager, property) ?? ReadProperty(documentManager, property);
                if (!string.IsNullOrWhiteSpace(value)) values[property] = value.Trim();
            }
            return values;
        }

        private static string? ReadProperty(ICustomPropertyManager manager, string property)
        {
            // Call the strongly-typed interop directly. Late binding (InvokeMember) cannot marshal the
            // ByRef 'out' parameters and throws DISP_E_TYPEMISMATCH (0x80020005). Get6 is the overload
            // used elsewhere in this project (DrawingVerifier / DrawingDocumentFactory) and is available.
            manager.Get6(property, false, out var valOut, out var resolvedOut, out _, out _);
            return PickValue(resolvedOut, valOut);
        }

        // Prefer the resolved value (evaluated expressions); fall back to the raw stored value.
        private static string? PickValue(string resolved, string raw)
        {
            if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }

        private static string? ReadModelMaterial(IPartDoc part, string configuration)
        {
            var args = new object?[] { configuration, string.Empty };
            if (!ComCall.Try(part, "GetMaterialPropertyName2", args, out var result)) return null;
            var value = Convert.ToString(result, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static EngineeringIntent ReadEngineeringIntent(
            Dictionary<string, string> values,
            string? modelMaterial,
            List<ExtractionFinding> findings)
        {
            var intent = new EngineeringIntent
            {
                PartNumber = Get(values, "MI_PART_NUMBER"),
                DrawingNumber = Get(values, "MI_DRAWING_NUMBER"),
                Revision = Get(values, "MI_REVISION"),
                Description = Get(values, "MI_DESCRIPTION"),
                Material = Get(values, "MI_MATERIAL_SPEC"),
                MaterialSpecification = Get(values, "MI_MATERIAL_SPEC"),
                HeatTreatment = Get(values, "MI_HEAT_TREATMENT"),
                Coating = Get(values, "MI_COATING"),
                EdgeRequirement = Get(values, "MI_EDGE_REQUIREMENT"),
                GeneralTolerancePolicyId = Get(values, "MI_GENERAL_TOLERANCE_POLICY_ID"),
                InternalThreadClass = Get(values, "MI_INTERNAL_THREAD_CLASS"),
                ExternalThreadClass = Get(values, "MI_EXTERNAL_THREAD_CLASS"),
                ApprovedBy = Get(values, "MI_APPROVED_BY"),
                ApprovalId = Get(values, "MI_APPROVAL_ID")
            };
            if (DateTimeOffset.TryParse(Get(values, "MI_APPROVED_AT"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var approvedAt))
                intent.ApprovedAt = approvedAt;
            intent.Datums = Parse<List<DatumDefinition>>(values, "MI_DATUMS_JSON", findings) ?? new List<DatumDefinition>();
            intent.GeometricTolerances = Parse<List<Dictionary<string, object>>>(values, "MI_GDT_JSON", findings) ?? new List<Dictionary<string, object>>();
            intent.SurfaceTextureRequirements = Parse<List<Dictionary<string, object>>>(values, "MI_SURFACE_TEXTURE_JSON", findings) ?? new List<Dictionary<string, object>>();
            if (string.IsNullOrWhiteSpace(intent.Material) && !string.IsNullOrWhiteSpace(modelMaterial))
                findings.Add(new ExtractionFinding
                {
                    Code = "INTENT-MATERIAL-NOT-APPROVED",
                    Severity = "WARNING",
                    Message = "The model has a material, but MI_MATERIAL_SPEC is not independently approved."
                });
            return intent;
        }

        private static T? Parse<T>(Dictionary<string, string> values, string key, List<ExtractionFinding> findings) where T : class
        {
            var json = Get(values, key);
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonConvert.DeserializeObject<T>(json, JsonContract.Settings); }
            catch (JsonException ex)
            {
                findings.Add(new ExtractionFinding
                {
                    Code = "INTENT-JSON-INVALID",
                    Severity = "BLOCKER",
                    Message = $"{key} is invalid: {ex.Message}"
                });
                return null;
            }
        }

        private static string? Get(Dictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? value : null;
    }
}
