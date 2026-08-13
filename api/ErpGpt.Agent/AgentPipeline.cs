using System.Text;
using System.Text.Json;
using ErpGpt.Agent.Services;

namespace ErpGpt.Agent;

public class AgentPipelineResult
{
    public string Question { get; set; } = string.Empty;
    public List<EndpointSearchResult> RetrievedDocs { get; set; } = new();
    public string QueryPlanJson { get; set; } = string.Empty;
    public string GraphQLQuery { get; set; } = string.Empty;
    public string RawApiResponse { get; set; } = string.Empty;
    public string FinalAnswer { get; set; } = string.Empty;
}

public class AgentPipeline
{
    private readonly ContextResolver _contextResolver;
    private readonly QueryPlanGenerator _planGenerator;
    private readonly GraphQLValidatorAndBuilder _queryBuilder;
    private readonly HttpClient _httpClient;
    private readonly string _graphQlApiUrl;

    public AgentPipeline(
        ContextResolver contextResolver,
        QueryPlanGenerator planGenerator,
        GraphQLValidatorAndBuilder queryBuilder,
        string graphQlApiUrl = "http://localhost:5000/graphql")
    {
        _contextResolver = contextResolver;
        _planGenerator = planGenerator;
        _queryBuilder = queryBuilder;
        _httpClient = new HttpClient();
        _graphQlApiUrl = graphQlApiUrl;
    }

    public async Task<AgentPipelineResult> ExecuteAsync(string userPrompt)
    {
        Console.WriteLine($"\n[1/5] RAG Retrieval: Finding matching endpoint docs in pgvector...");
        var retrievedDocs = await _contextResolver.ResolveContextAsync(userPrompt, topK: 3);

        foreach (var doc in retrievedDocs)
        {
            Console.WriteLine($"  -> Endpoint: {doc.EndpointName} | Dist: {doc.Distance:F4} | Match Q: {doc.Question}");
        }

        Console.WriteLine($"\n[2/5] LLM Query Planner: Prompting Llama 3.1 8B via Semantic Kernel...");
        var queryPlanJson = await _planGenerator.GenerateQueryPlanJsonAsync(userPrompt, retrievedDocs);
        Console.WriteLine($"  -> Query Plan: {queryPlanJson}");

        Console.WriteLine($"\n[3/5] Validation Gate & GraphQL Construction...");
        var graphQlQuery = _queryBuilder.BuildGraphQLQuery(queryPlanJson, retrievedDocs.FirstOrDefault());
        Console.WriteLine($"  -> Generated GraphQL:\n{graphQlQuery}");

        Console.WriteLine($"\n[4/5] Executing GraphQL against API ({_graphQlApiUrl})...");
        string apiResponse = "{}";
        try
        {
            var reqObj = new { query = graphQlQuery };
            var content = new StringContent(JsonSerializer.Serialize(reqObj), Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync(_graphQlApiUrl, content);
            apiResponse = await resp.Content.ReadAsStringAsync();
            Console.WriteLine($"  -> Raw Response: {apiResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  -> Execution notice: {ex.Message} (Ensure GraphQL API is running on {_graphQlApiUrl})");
        }

        Console.WriteLine($"\n[5/5] Synthesizing final natural language response...");
        var result = new AgentPipelineResult
        {
            Question = userPrompt,
            RetrievedDocs = retrievedDocs,
            QueryPlanJson = queryPlanJson,
            GraphQLQuery = graphQlQuery,
            RawApiResponse = apiResponse,
            FinalAnswer = $"Retrieved endpoint '{retrievedDocs.FirstOrDefault()?.EndpointName}' and generated GraphQL query to fetch answer from live database."
        };

        return result;
    }
}
