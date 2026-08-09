using System;
using System.IO;
using System.Linq;
using System.Threading;
using MachIntellDrawAI.Infrastructure;
using MachIntellDrawAI.Models;
using Newtonsoft.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class GenerationOutcome
    {
        public DrawingPlan Plan { get; set; } = null!;
        public ReleaseGate Gate { get; set; } = null!;
        public IModelDoc2 Drawing { get; set; } = null!;
    }

    internal sealed class DrawingGenerationService : IDisposable
    {
        private sealed class Session
        {
            public IModelDoc2 SourceModel { get; set; } = null!;
            public ModelManifest Manifest { get; set; } = null!;
            public DrawingPlan Plan { get; set; } = null!;
            public DrawingContext Drawing { get; set; } = null!;
            public ViewBuildResult Views { get; set; } = null!;
            public RequirementExecutionResult Execution { get; set; } = null!;
            public ApiClient Api { get; set; } = null!;
        }

        private readonly ISldWorks _app;
        private readonly AuditLog _log;
        private Session? _session;

        public DrawingGenerationService(ISldWorks app, AuditLog log)
        {
            _app = app;
            _log = log;
        }

        public GenerationOutcome GenerateDraft(CancellationToken cancellationToken)
        {
            var source = _app.ActiveDoc as IModelDoc2
                ?? throw new InvalidOperationException("Open a saved SolidWorks part before generating a drawing.");
            var config = PluginConfig.Load();
            _log.Info("generation-started", source.GetTitle());
            var manifest = new ModelManifestExtractor(source).Extract();
            var refs = new PersistentReferenceService(source, manifest.Configuration);
            var api = new ApiClient(config);
            DrawingContext? drawing = null;
            try
            {
                var plan = api.CreatePlanAsync(
                    new PlanRequest { ModelData = manifest, Preferences = config.Preferences },
                    cancellationToken).GetAwaiter().GetResult();
                if (plan.SchemaVersion != "2.0" || plan.ModelHash != manifest.ModelHash)
                    throw new InvalidOperationException("Backend returned a plan for a different contract or model hash.");
                drawing = new DrawingDocumentFactory(_app).Create(plan, config);
                var views = new ViewBuilder(drawing, manifest.FilePath).Build(plan);
                var execution = new AssociativeRequirementExecutor(drawing, source, refs, views.Views).Execute(plan);
                execution.CadArtifactsHidden = new CadArtifactController(source, drawing, views.Views).HideAndVerify();
                var report = new DrawingVerifier(drawing, views.Views, execution).Verify(plan, false);
                var gate = api.ValidateExecutionAsync(
                    new ExecutionValidationRequest { Plan = plan, Execution = report },
                    cancellationToken).GetAwaiter().GetResult();
                WriteGate(drawing.Model, gate, "DRAFT");
                _session?.Api.Dispose();
                _session = new Session
                {
                    SourceModel = source,
                    Manifest = manifest,
                    Plan = plan,
                    Drawing = drawing,
                    Views = views,
                    Execution = execution,
                    Api = api
                };
                api = null!;
                _log.Info("generation-completed", $"plan={plan.PlanId}; status={gate.Status}; blockers={gate.Blockers.Count}");
                return new GenerationOutcome { Plan = plan, Gate = gate, Drawing = drawing.Model };
            }
            catch
            {
                if (drawing != null) WriteStatus(drawing.Model, "DRAFT_GENERATION_FAILED");
                throw;
            }
            finally
            {
                api?.Dispose();
            }
        }

        public ReleaseGate ValidateAndApprove(CancellationToken cancellationToken)
        {
            var session = _session ?? throw new InvalidOperationException("Generate and inspect a verified draft in this SolidWorks session first.");
            if ((_app.ActiveDoc as IModelDoc2)?.GetTitle() != session.Drawing.Model.GetTitle())
                throw new InvalidOperationException("Activate the generated drawing before approval.");
            if (session.SourceModel.GetSaveFlag())
                throw new InvalidOperationException("The source model has unsaved changes; approval is invalid until it is saved and regenerated.");
            if (StableHash.File(session.Manifest.FilePath) != session.Plan.ModelHash)
                throw new InvalidOperationException("The source model file changed after planning; regenerate the drawing.");
            if (session.SourceModel.ConfigurationManager.ActiveConfiguration.Name != session.Plan.Configuration)
                throw new InvalidOperationException("The source configuration changed after planning; regenerate the drawing.");

            var report = new DrawingVerifier(session.Drawing, session.Views.Views, session.Execution).Verify(session.Plan, true);
            var gate = session.Api.ValidateExecutionAsync(
                new ExecutionValidationRequest { Plan = session.Plan, Execution = report },
                cancellationToken).GetAwaiter().GetResult();
            if (!gate.ReleaseReady)
            {
                WriteGate(session.Drawing.Model, gate, gate.Status);
                _log.Info("release-rejected", $"plan={session.Plan.PlanId}; blockers={gate.Blockers.Count}");
                return gate;
            }
            if (session.Execution.DraftWatermark != null)
            {
                session.Drawing.Model.ClearSelection2(true);
                if (!session.Execution.DraftWatermark.Select3(false, null) ||
                    !session.Drawing.Model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed))
                    throw new InvalidOperationException("Release validation passed, but the draft watermark could not be removed. Release status was not changed.");
                session.Execution.DraftWatermark = null;
            }
            WriteGate(session.Drawing.Model, gate, "RELEASE_READY");
            session.Drawing.Model.ForceRebuild3(true);
            _log.Info("release-approved", "plan=" + session.Plan.PlanId);
            return gate;
        }

        private static void WriteGate(IModelDoc2 model, ReleaseGate gate, string status)
        {
            var manager = model.Extension.CustomPropertyManager[string.Empty];
            manager.Add3("MI_RELEASE_STATUS", (int)swCustomInfoType_e.swCustomInfoText, status, (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            manager.Add3(
                "MI_RELEASE_FINDINGS",
                (int)swCustomInfoType_e.swCustomInfoText,
                JsonConvert.SerializeObject(gate.Blockers.Select(f => new { f.Code, f.Message })),
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
        }

        private static void WriteStatus(IModelDoc2 model, string status)
        {
            model.Extension.CustomPropertyManager[string.Empty].Add3(
                "MI_RELEASE_STATUS",
                (int)swCustomInfoType_e.swCustomInfoText,
                status,
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
        }

        public void Dispose()
        {
            _session?.Api.Dispose();
            _session = null;
        }
    }
}
