using System;
using System.Collections.Generic;
using System.IO;
using MachIntellDrawAI.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class DrawingContext
    {
        public IModelDoc2 Model { get; set; } = null!;
        public IDrawingDoc Drawing { get; set; } = null!;
        public Dictionary<int, string> SheetNames { get; set; } = new Dictionary<int, string>();
        public TemplateProfile Template { get; set; } = null!;
    }

    internal sealed class DrawingDocumentFactory
    {
        private readonly ISldWorks _app;

        public DrawingDocumentFactory(ISldWorks app) => _app = app;

        public DrawingContext Create(DrawingPlan plan, PluginConfig config)
        {
            var template = config.GetTemplate(plan.Standards.ProfileId);
            if (!string.Equals(template.ExpectedProjection, plan.Standards.Projection, StringComparison.Ordinal))
                throw new InvalidOperationException("The controlled template projection does not match the immutable standards snapshot.");
            if (!File.Exists(template.TemplatePath))
                throw new FileNotFoundException("Controlled drawing template not found.", template.TemplatePath);

            var model = _app.NewDocument(
                template.TemplatePath,
                PaperSize(plan.SheetSize),
                plan.SheetWidthMm / 1000.0,
                plan.SheetHeightMm / 1000.0) as IModelDoc2
                ?? throw new InvalidOperationException("SolidWorks failed to create a drawing from the controlled template.");
            var drawing = model as IDrawingDoc
                ?? throw new InvalidOperationException("The controlled template did not create a drawing document.");
            var context = new DrawingContext { Model = model, Drawing = drawing, Template = template };

            var current = (ISheet)drawing.GetCurrentSheet();
            current.SetName("MI_SHEET_1");
            context.SheetNames[1] = "MI_SHEET_1";
            ConfigureAndVerifySheet(current, plan, template, 1);

            var maximumSheet = 1;
            foreach (var view in plan.Views) maximumSheet = Math.Max(maximumSheet, view.SheetIndex);
            for (var index = 2; index <= maximumSheet; index++)
            {
                var name = "MI_SHEET_" + index;
                var firstAngle = plan.Standards.Projection == "FIRST_ANGLE";
                var created = drawing.NewSheet3(
                    name,
                    PaperSize(plan.SheetSize),
                    (int)swDwgTemplates_e.swDwgTemplateCustom,
                    plan.ScaleNumerator,
                    plan.ScaleDenominator,
                    firstAngle,
                    template.TemplatePath,
                    plan.SheetWidthMm / 1000.0,
                    plan.SheetHeightMm / 1000.0,
                    string.Empty);
                if (!created) throw new InvalidOperationException("SolidWorks failed to create controlled sheet " + index);
                drawing.ActivateSheet(name);
                var sheet = (ISheet)drawing.GetCurrentSheet();
                ConfigureAndVerifySheet(sheet, plan, template, index);
                context.SheetNames[index] = name;
            }

            SetDocumentUnits(model, plan.Standards.Units);
            WriteTitleFields(model, plan);
            WriteAuditProperties(model, plan, template);
            drawing.ActivateSheet(context.SheetNames[1]);
            return context;
        }

        private static void ConfigureAndVerifySheet(ISheet sheet, DrawingPlan plan, TemplateProfile template, int index)
        {
            var firstAngle = plan.Standards.Projection == "FIRST_ANGLE";
            sheet.SetScale(plan.ScaleNumerator, plan.ScaleDenominator, true, true);
            var properties = sheet.GetProperties2() as double[]
                ?? throw new InvalidOperationException("SolidWorks returned no sheet properties for sheet " + index);
            if (properties.Length < 7)
                throw new InvalidOperationException("SolidWorks sheet properties are incomplete for sheet " + index);
            var actualFirstAngle = Convert.ToBoolean(properties[4]);
            if (actualFirstAngle != firstAngle)
                throw new InvalidOperationException($"Sheet {index} projection disagrees with {plan.Standards.Projection}.");
            var actualScale = properties[2] / properties[3];
            var expectedScale = (double)plan.ScaleNumerator / plan.ScaleDenominator;
            if (Math.Abs(actualScale - expectedScale) > 1e-9)
                throw new InvalidOperationException($"Sheet {index} scale did not accept {plan.ScaleNumerator}:{plan.ScaleDenominator}.");
            double width = 0, height = 0;
            sheet.GetSize(ref width, ref height);
            if (Math.Abs(width * 1000.0 - plan.SheetWidthMm) > 0.1 || Math.Abs(height * 1000.0 - plan.SheetHeightMm) > 0.1)
                throw new InvalidOperationException($"Sheet {index} size differs from the plan.");
            var actualTemplate = sheet.GetTemplateName() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(actualTemplate) &&
                !string.Equals(Path.GetFullPath(actualTemplate), Path.GetFullPath(template.TemplatePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Sheet {index} is not using controlled template revision {template.Revision}.");
        }

        private static void SetDocumentUnits(IModelDoc2 model, string units)
        {
            var linear = units == "INCH" ? swLengthUnit_e.swINCHES : swLengthUnit_e.swMM;
            if (!model.Extension.SetUserPreferenceInteger(
                (int)swUserPreferenceIntegerValue_e.swUnitsLinear,
                (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified,
                (int)linear))
                throw new InvalidOperationException("SolidWorks rejected the planned drawing units.");
            model.Extension.SetUserPreferenceInteger(
                (int)swUserPreferenceIntegerValue_e.swUnitsAngular,
                (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified,
                (int)swAngleUnit_e.swDEGREES);
        }

        private static void WriteTitleFields(IModelDoc2 model, DrawingPlan plan)
        {
            var manager = model.Extension.CustomPropertyManager[string.Empty];
            foreach (var pair in plan.TitleFields)
            {
                manager.Add3(pair.Key, (int)swCustomInfoType_e.swCustomInfoText, pair.Value, (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
                if (ReadProperty(manager, pair.Key) != pair.Value)
                    throw new InvalidOperationException("Title field could not be written exactly: " + pair.Key);
            }
        }

        private static void WriteAuditProperties(IModelDoc2 model, DrawingPlan plan, TemplateProfile template)
        {
            var manager = model.Extension.CustomPropertyManager[string.Empty];
            Add(manager, "MI_SCHEMA_VERSION", plan.SchemaVersion);
            Add(manager, "MI_PLAN_ID", plan.PlanId);
            Add(manager, "MI_PLAN_DIGEST", plan.PlanDigest);
            Add(manager, "MI_MODEL_HASH", plan.ModelHash);
            Add(manager, "MI_STANDARDS_DIGEST", plan.Standards.Digest);
            Add(manager, "MI_TEMPLATE_REVISION", template.Revision);
            Add(manager, "MI_RELEASE_STATUS", "DRAFT");
        }

        private static void Add(ICustomPropertyManager manager, string name, string value) =>
            manager.Add3(name, (int)swCustomInfoType_e.swCustomInfoText, value, (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);

        private static string ReadProperty(ICustomPropertyManager manager, string name)
        {
            manager.Get6(name, false, out _, out var resolved, out _, out _);
            return resolved ?? string.Empty;
        }

        private static int PaperSize(string sheetSize)
        {
            switch (sheetSize.ToUpperInvariant())
            {
                case "A4": return (int)swDwgPaperSizes_e.swDwgPaperA4size;
                case "A3": return (int)swDwgPaperSizes_e.swDwgPaperA3size;
                case "A2": return (int)swDwgPaperSizes_e.swDwgPaperA2size;
                case "A1": return (int)swDwgPaperSizes_e.swDwgPaperA1size;
                case "A0": return (int)swDwgPaperSizes_e.swDwgPaperA0size;
                case "A": return (int)swDwgPaperSizes_e.swDwgPaperAsize;
                case "B": return (int)swDwgPaperSizes_e.swDwgPaperBsize;
                case "C": return (int)swDwgPaperSizes_e.swDwgPaperCsize;
                case "D": return (int)swDwgPaperSizes_e.swDwgPaperDsize;
                default: throw new NotSupportedException("Unsupported sheet size: " + sheetSize);
            }
        }
    }
}
