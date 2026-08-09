using System;
using System.Collections.Generic;
using System.IO;
using MachIntellDrawAI.Models;
using Newtonsoft.Json;

namespace MachIntellDrawAI
{
    public sealed class TemplateProfile
    {
        public string ProfileId { get; set; } = string.Empty;
        public string TemplatePath { get; set; } = string.Empty;
        public string ExpectedProjection { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
    }

    public sealed class PluginConfig
    {
        public string BackendUrl { get; set; } = "https://localhost:8443";
        public string ApiKeyEnvironmentVariable { get; set; } = "MACHINTELL_DRAWING_API_KEY";
        public DrawingPreferences Preferences { get; set; } = new DrawingPreferences();
        public List<TemplateProfile> Templates { get; set; } = new List<TemplateProfile>();
        public int RequestTimeoutSeconds { get; set; } = 90;

        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MachIntell", "DrawingAddin", "settings.json");

        public static PluginConfig Load()
        {
            if (!File.Exists(SettingsPath))
                throw new InvalidOperationException("Add-in settings are missing: " + SettingsPath);
            var config = JsonConvert.DeserializeObject<PluginConfig>(File.ReadAllText(SettingsPath))
                ?? throw new InvalidOperationException("Add-in settings could not be read.");
            config.Validate();
            return config;
        }

        public TemplateProfile GetTemplate(string profileId)
        {
            var template = Templates.Find(t => string.Equals(t.ProfileId, profileId, StringComparison.Ordinal));
            if (template == null)
                throw new InvalidOperationException("No controlled drawing template configured for profile " + profileId);
            return template;
        }

        public string? GetApiKey() => Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable, EnvironmentVariableTarget.Process);

        private void Validate()
        {
            if (!Uri.TryCreate(BackendUrl, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("BackendUrl must be an absolute URL.");
            // Require HTTPS for real hosts; allow plain HTTP only for local loopback (development/testing).
            var isLoopback = uri.IsLoopback; // matches localhost, 127.0.0.1, ::1
            if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && isLoopback))
                throw new InvalidOperationException("BackendUrl must be an absolute HTTPS URL (plain HTTP is allowed only for localhost).");
            if (RequestTimeoutSeconds < 10 || RequestTimeoutSeconds > 300)
                throw new InvalidOperationException("RequestTimeoutSeconds must be between 10 and 300.");
            var template = GetTemplate(Preferences.StandardProfileId);
            if (!File.Exists(template.TemplatePath))
                throw new InvalidOperationException("Controlled drawing template does not exist: " + template.TemplatePath);
            if (template.ExpectedProjection != "FIRST_ANGLE" && template.ExpectedProjection != "THIRD_ANGLE")
                throw new InvalidOperationException("Template ExpectedProjection must be FIRST_ANGLE or THIRD_ANGLE.");
        }
    }
}
