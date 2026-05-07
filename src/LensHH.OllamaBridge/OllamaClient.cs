using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LensHH.OllamaBridge
{
    public class OllamaClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public OllamaClient(string baseUrl = "http://localhost:11434")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        }

        public async Task<OllamaChatResponse> ChatAsync(OllamaChatRequest request)
        {
            var json = JsonConvert.SerializeObject(request,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_baseUrl}/api/chat", content);
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<OllamaChatResponse>(responseJson)!;
        }

        public async IAsyncEnumerable<OllamaChatResponse> ChatStreamAsync(OllamaChatRequest request)
        {
            request.Stream = true;
            var json = JsonConvert.SerializeObject(request,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat") { Content = content };
            using var response = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                OllamaChatResponse? chunk;
                try { chunk = JsonConvert.DeserializeObject<OllamaChatResponse>(line); }
                catch { continue; }
                if (chunk != null) yield return chunk;
            }
        }

        public async Task<List<string>> ListModelsAsync()
        {
            var response = await _http.GetAsync($"{_baseUrl}/api/tags");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var obj = JObject.Parse(json);
            var models = new List<string>();
            if (obj["models"] is JArray arr)
            {
                foreach (var m in arr)
                {
                    var name = m["name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                        models.Add(name);
                }
            }
            return models;
        }

        public void Dispose() => _http.Dispose();
    }
}
