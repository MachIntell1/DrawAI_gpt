using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MachIntellDrawAI.Models;
using Newtonsoft.Json;

namespace MachIntellDrawAI.Infrastructure
{
    internal sealed class ApiClient : IDisposable
    {
        private readonly HttpClient _http;

        public ApiClient(PluginConfig config)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(config.BackendUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(config.RequestTimeoutSeconds)
            };
            var key = config.GetApiKey();
            if (!string.IsNullOrWhiteSpace(key))
                _http.DefaultRequestHeaders.Add("X-API-Key", key);
            _http.DefaultRequestHeaders.Add("X-MachIntell-Contract", "2.0");
        }

        public Task<DrawingPlan> CreatePlanAsync(PlanRequest request, CancellationToken cancellationToken) =>
            PostAsync<PlanRequest, DrawingPlan>("api/v2/plugin/plan", request, cancellationToken);

        public Task<ReleaseGate> ValidateExecutionAsync(ExecutionValidationRequest request, CancellationToken cancellationToken) =>
            PostAsync<ExecutionValidationRequest, ReleaseGate>("api/v2/plugin/validate-execution", request, cancellationToken);

        private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken)
        {
            var json = JsonConvert.SerializeObject(request, JsonContract.Settings);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var response = await _http.PostAsync(path, content, cancellationToken).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Drawing backend rejected {path} ({(int)response.StatusCode}): {SafeBody(body)}");
                return JsonConvert.DeserializeObject<TResponse>(body, JsonContract.Settings)
                    ?? throw new InvalidOperationException("Drawing backend returned an empty response.");
            }
        }

        private static string SafeBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "no error detail";
            body = body.Replace("\r", " ").Replace("\n", " ");
            return body.Length <= 500 ? body : body.Substring(0, 500);
        }

        public void Dispose() => _http.Dispose();
    }
}
