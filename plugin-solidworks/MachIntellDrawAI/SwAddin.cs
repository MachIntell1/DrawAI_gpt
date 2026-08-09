using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using MachIntellDrawAI.Infrastructure;
using MachIntellDrawAI.SolidWorks;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace MachIntellDrawAI
{
    [ComVisible(true)]
    [Guid(AddinGuid)]
    [ProgId("MachIntellDrawAI.SwAddin")]
    public sealed class SwAddin : ISwAddin
    {
        private const string AddinGuid = "2DB49368-A588-4CA4-8A5C-2699E2D3D446";
        private const int CommandGroupId = 93271;
        private ISldWorks? _app;
        private ICommandManager? _commands;
        private DrawingGenerationService? _generation;
        private AuditLog? _log;

        public bool ConnectToSW(object thisSw, int cookie)
        {
            try
            {
                _app = (ISldWorks)thisSw;
                _app.SetAddinCallbackInfo2(0, this, cookie);
                _log = new AuditLog();
                _generation = new DrawingGenerationService(_app, _log);
                CreateCommands(cookie);
                _log.Info("addin-connected", "MachIntell drawing contract 2.0");
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "MachIntell add-in failed to load", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try
            {
                _generation?.Dispose();
                if (_commands != null)
                {
                    // Remove the ribbon tabs we created so they don't persist or duplicate on reload.
                    foreach (var docType in new[] { (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocDRAWING })
                    {
                        try
                        {
                            var tab = _commands.GetCommandTab(docType, "MachIntell Manufacturing Drawing");
                            if (tab != null) _commands.RemoveCommandTab(tab);
                        }
                        catch { }
                    }
                }
                _commands?.RemoveCommandGroup2(CommandGroupId, false);
                if (_commands != null) Marshal.FinalReleaseComObject(_commands);
                if (_app != null) Marshal.FinalReleaseComObject(_app);
                _commands = null;
                _app = null;
                return true;
            }
            catch { return false; }
        }

        public void GenerateVerifiedDraft()
        {
            try
            {
                var outcome = RequiredGeneration().GenerateDraft(CancellationToken.None);
                var message = outcome.Gate.Blockers.Count == 0
                    ? "Verified draft created. Inspect it, then use Validate and Approve Release."
                    : $"Draft created with {outcome.Gate.Blockers.Count} release blocker(s):\n\n{FindingSummary(outcome.Gate)}";
                MessageBox.Show(message, "MachIntell manufacturing drawing", MessageBoxButtons.OK,
                    outcome.Gate.Blockers.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                _log?.Error("generation-failed", ex);
                MessageBox.Show(Infrastructure.ExceptionText.Describe(ex), "Drawing generation stopped", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ValidateApproveRelease()
        {
            var confirmation = MessageBox.Show(
                "I have checked the drawing against the controlled engineering intent, applicable company standard, and current purchased ISO/ASME requirements. Continue with release validation?",
                "Explicit drawing approval",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes) return;
            try
            {
                var gate = RequiredGeneration().ValidateAndApprove(CancellationToken.None);
                MessageBox.Show(
                    gate.ReleaseReady ? "All deterministic gates passed. The drawing is marked RELEASE_READY."
                                      : $"Release rejected with {gate.Blockers.Count} blocker(s):\n\n{FindingSummary(gate)}",
                    "MachIntell release validation",
                    MessageBoxButtons.OK,
                    gate.ReleaseReady ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                _log?.Error("release-validation-failed", ex);
                MessageBox.Show(ex.Message, "Release validation stopped", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ShowSettingsLocation()
        {
            MessageBox.Show(
                "Edit the controlled settings file and restart SolidWorks:\n\n" + PluginConfig.SettingsPath +
                "\n\nAn example is installed beside MachIntellDrawAI.dll. API keys belong in the configured Windows user environment variable, not the JSON file.",
                "MachIntell settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        public int GenerateEnable() => _app?.ActiveDoc is IModelDoc2 model && model.GetType() == (int)swDocumentTypes_e.swDocPART ? 1 : 0;
        public int ApproveEnable() => _app?.ActiveDoc is IModelDoc2 model && model.GetType() == (int)swDocumentTypes_e.swDocDRAWING ? 1 : 0;
        public int SettingsEnable() => 1;

        private void CreateCommands(int cookie)
        {
            if (_app == null) throw new InvalidOperationException("SolidWorks application is unavailable.");
            _commands = _app.GetCommandManager(cookie);

            // Discard any cached command group with this ID before recreating (prevents error 1)
            try { _commands.RemoveCommandGroup2(CommandGroupId, false); } catch { }

            var errors = 0;
            var group = _commands.CreateCommandGroup2(
                CommandGroupId,
                "MachIntell Manufacturing Drawing",
                "Generate deterministic associative manufacturing drawings",
                "MachIntell Manufacturing Drawing",
                -1,
                true,
                ref errors);
            // Only a null group is fatal; a nonzero 'errors' can be a benign cache/ID notice
            if (group == null)
                throw new InvalidOperationException("SolidWorks command group could not be created (error " + errors + ").");
            var generateIndex = group.AddCommandItem2(
                "Generate Verified Draft", -1,
                "Create a new drawing from the exact v2 plan",
                "Generate Verified Draft", 0,
                nameof(GenerateVerifiedDraft), nameof(GenerateEnable), 0,
                (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem);
            var approveIndex = group.AddCommandItem2(
                "Validate and Approve Release", -1,
                "Revalidate associations, layout, metadata and approval",
                "Validate and Approve", 1,
                nameof(ValidateApproveRelease), nameof(ApproveEnable), 1,
                (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem);
            var settingsIndex = group.AddCommandItem2(
                "Settings Location", -1,
                "Show the controlled settings path",
                "Settings Location", 2,
                nameof(ShowSettingsLocation), nameof(SettingsEnable), 2,
                (int)swCommandItemType_e.swMenuItem);
            group.HasMenu = true;
            group.HasToolbar = true;
            if (!group.Activate()) throw new InvalidOperationException("SolidWorks command group activation failed.");

            // Resolve the global command IDs assigned to each item; these are needed to place
            // the buttons on a CommandManager ribbon tab (a toolbar/menu alone is not shown as a tab).
            var generateId = group.CommandID[generateIndex];
            var approveId = group.CommandID[approveIndex];
            var settingsId = group.CommandID[settingsIndex];

            // Build a visible CommandManager tab for each relevant document type. Without an
            // explicit CommandTab, modern SolidWorks does not surface the group as a ribbon tab.
            AddRibbonTab((int)swDocumentTypes_e.swDocPART, generateId, approveId, settingsId);
            AddRibbonTab((int)swDocumentTypes_e.swDocDRAWING, generateId, approveId, settingsId);
        }

        private void AddRibbonTab(int docType, int generateId, int approveId, int settingsId)
        {
            if (_commands == null) return;

            const string tabTitle = "MachIntell Manufacturing Drawing";

            // Remove any stale tab with this title so we don't stack duplicates across reloads.
            var existing = _commands.GetCommandTab(docType, tabTitle);
            if (existing != null) _commands.RemoveCommandTab(existing);

            var tab = _commands.AddCommandTab(docType, tabTitle);
            if (tab == null) return;

            var box = tab.AddCommandTabBox();
            if (box == null) return;

            var commandIds = new[] { generateId, approveId, settingsId };
            var textTypes = new[]
            {
                (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
                (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
                (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow
            };
            box.AddCommands(commandIds, textTypes);
        }

        private DrawingGenerationService RequiredGeneration() => _generation ?? throw new InvalidOperationException("Add-in services are not initialized.");

        private static string FindingSummary(Models.ReleaseGate gate)
        {
            var lines = gate.Blockers.Take(8).Select(f => $"• {f.Code}: {f.Message}").ToList();
            if (gate.Blockers.Count > lines.Count) lines.Add($"• …and {gate.Blockers.Count - lines.Count} more");
            return string.Join(System.Environment.NewLine, lines);
        }

        [ComRegisterFunction]
        public static void Register(Type type)
        {
            var guid = type.GUID.ToString("B");
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SolidWorks\Addins\" + guid))
            {
                key.SetValue(null, 1, RegistryValueKind.DWord);
                key.SetValue("Title", "MachIntell Manufacturing Drawing");
                key.SetValue("Description", "Deterministic associative manufacturing drawing generator");
            }
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\SolidWorks\AddInsStartup\" + guid))
                key.SetValue(null, 1, RegistryValueKind.DWord);
        }

        [ComUnregisterFunction]
        public static void Unregister(Type type)
        {
            var guid = type.GUID.ToString("B");
            Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\SolidWorks\Addins\" + guid, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\SolidWorks\AddInsStartup\" + guid, false);
        }
    }
}
