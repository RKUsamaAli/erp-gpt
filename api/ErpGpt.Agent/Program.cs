using ErpGpt.Agent;
using ErpGpt.Agent.Services;

Console.WriteLine("=================================================");
Console.WriteLine("        ERP GPT - Semantic Kernel RAG Agent      ");
Console.WriteLine("=================================================");

var vectorStore = new PgVectorMemoryStore();
var contextResolver = new ContextResolver(vectorStore);
var planGenerator = new QueryPlanGenerator();
var queryBuilder = new GraphQLValidatorAndBuilder();

var pipeline = new AgentPipeline(contextResolver, planGenerator, queryBuilder);

string sampleQuestion = "Who are our top 3 customers in Canada?";
if (args.Length > 0)
{
    sampleQuestion = string.Join(" ", args);
}

Console.WriteLine($"Question: \"{sampleQuestion}\"");

try
{
    var result = await pipeline.ExecuteAsync(sampleQuestion);
    Console.WriteLine("\n=================================================");
    Console.WriteLine("RESULT SUMMARY:");
    Console.WriteLine($"Endpoint Picked: {result.RetrievedDocs.FirstOrDefault()?.EndpointName}");
    Console.WriteLine($"GraphQL Query:\n{result.GraphQLQuery}");
    Console.WriteLine("=================================================");
}
catch (Exception ex)
{
    Console.WriteLine($"\n[Pipeline Error]: {ex.Message}");
}
