using System.Text;
using System.Text.Json;

namespace ErpGpt.Agent.Services;

public class QueryPlanGenerator
{
    private readonly HttpClient _httpClient;
    private readonly string _ollamaEndpoint;
    private readonly string _modelName;

    public QueryPlanGenerator(string ollamaEndpoint = "http://localhost:11434", string modelName = "llama3.1")
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5) // Increased timeout for local LLM inference
        };
        _ollamaEndpoint = ollamaEndpoint;
        _modelName = modelName;
    }

    public async Task<string> GenerateQueryPlanJsonAsync(string userPrompt, List<EndpointSearchResult> contextDocs)
    {
        var docsSummary = new StringBuilder();
        foreach (var doc in contextDocs)
        {
            docsSummary.AppendLine($"### Endpoint: {doc.EndpointName}");
            docsSummary.AppendLine(doc.Payload.GetRawText());
            docsSummary.AppendLine();
        }

        var systemPrompt = $@"You are an ERP GraphQL AI Query Planner.
Choose the single best GraphQL endpoint from the provided Documentation and construct a JSON Query Plan.

DOCUMENTATION:
{docsSummary}

Output format required (JSON):
{{
  ""endpoint"": ""<endpoint_name>"",
  ""take"": 10,
  ""filters"": {{
    ""field"": ""<field_name>"",
    ""value"": ""<value>""
  }},
  ""sort"": {{
    ""field"": ""<field_name>"",
    ""direction"": ""ASC""
  }}
}}";

        var requestBody = new
        {
            model = _modelName,
            prompt = $"{systemPrompt}\n\nUser Question: {userPrompt}\n\nOutput Query Plan JSON:",
            format = "json", // Force JSON mode for 5x-10x faster generation
            stream = false,
            options = new
            {
                temperature = 0.0,
                num_predict = 150 // Cap token generation so it finishes in seconds
            }
        };

        var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_ollamaEndpoint}/api/generate", jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Ollama LLM call failed with status: {response.StatusCode}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(responseString);
        var rawOutput = jsonDoc.RootElement.GetProperty("response").GetString() ?? string.Empty;

        return ExtractJson(rawOutput);
    }

    private static string ExtractJson(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return text.Substring(start, end - start + 1);
        }
        return text.Trim();
    }
}
