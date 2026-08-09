using System;
using System.Collections.Generic;
using MachIntellDrawAI.Infrastructure;
using MachIntellDrawAI.Models;
using Newtonsoft.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class CreatedAnnotationRecord
    {
        public string RequirementId { get; set; } = string.Empty;
        public string ViewId { get; set; } = string.Empty;
        public string MeasurandKey { get; set; } = string.Empty;
        public string SpecificationKey { get; set; } = string.Empty;
        public string AnnotationToken { get; set; } = string.Empty;
        public IAnnotation Annotation { get; set; } = null!;
        public Rect2 ConservativeBoundsMm { get; set; } = new Rect2();
    }

    internal sealed class AnnotationMetadataStore
    {
        private readonly IModelDoc2 _drawingModel;
        private readonly ICustomPropertyManager _properties;
        private readonly HashSet<string> _tokens = new HashSet<string>(StringComparer.Ordinal);

        public List<CreatedAnnotationRecord> Records { get; } = new List<CreatedAnnotationRecord>();
        public IReadOnlyCollection<string> Tokens => _tokens;

        public AnnotationMetadataStore(IModelDoc2 drawingModel)
        {
            _drawingModel = drawingModel;
            _properties = drawingModel.Extension.CustomPropertyManager[string.Empty];
        }

        public string Record(RequirementPlan requirement, string viewId, IAnnotation annotation, string displayText)
        {
            var bytes = (byte[]?)_drawingModel.Extension.GetPersistReference3(annotation)
                ?? throw new InvalidOperationException("Created drawing annotation has no persistent reference.");
            if (bytes.Length == 0) throw new InvalidOperationException("Created drawing annotation has an empty persistent reference.");
            var token = Convert.ToBase64String(bytes);
            if (!_tokens.Add(token)) throw new InvalidOperationException("Two requirements resolved to the same drawing annotation.");
            var position = annotation.GetPosition() as double[]
                ?? throw new InvalidOperationException("Created drawing annotation has no position.");
            // Conservative for the controlled 3.5 mm template font, including
            // prefix/symbol spacing and leader shoulder.
            var widthMm = Math.Max(18.0, LongestLine(displayText) * 2.5 + 8.0);
            var heightMm = Math.Max(7.0, CountLines(displayText) * 5.0 + 2.0);
            var centerX = position[0] * 1000.0;
            var centerY = position[1] * 1000.0;
            var record = new CreatedAnnotationRecord
            {
                RequirementId = requirement.RequirementId,
                ViewId = viewId,
                MeasurandKey = requirement.MeasurandKey,
                SpecificationKey = requirement.SpecificationKey,
                AnnotationToken = token,
                Annotation = annotation,
                ConservativeBoundsMm = new Rect2
                {
                    Left = centerX - widthMm / 2.0,
                    Right = centerX + widthMm / 2.0,
                    Bottom = centerY - heightMm / 2.0,
                    Top = centerY + heightMm / 2.0
                }
            };
            Records.Add(record);
            ComCall.Optional(annotation, "SetName", "MI_" + Short(requirement.RequirementId));
            var metadata = JsonConvert.SerializeObject(new
            {
                requirement_id = requirement.RequirementId,
                measurand_key = requirement.MeasurandKey,
                specification_key = requirement.SpecificationKey,
                annotation_persist_reference = token
            });
            _properties.Add3(
                "MI_REQ_" + Short(requirement.RequirementId),
                (int)swCustomInfoType_e.swCustomInfoText,
                metadata,
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            return token;
        }

        private static int CountLines(string value) => string.IsNullOrEmpty(value) ? 1 : value.Split('\n').Length;
        private static int LongestLine(string value)
        {
            var longest = 1;
            foreach (var line in (value ?? string.Empty).Split('\n')) longest = Math.Max(longest, line.Length);
            return longest;
        }
        private static string Short(string value) => value.Length <= 20 ? value : value.Substring(value.Length - 20);
    }
}
