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

// Single-shot run via command line argument
if (args.Length > 0)
{
    string question = string.Join(" ", args);
    await RunQuestionAsync(pipeline, question);
    return;
}

// Interactive Console Mode
Console.WriteLine("\nInteractive Mode active. Type your ERP question below, or 'exit' to quit.\n");

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("\nAsk ERP-GPT > ");
    Console.ResetColor();

    string? input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase) ||
        input.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Exiting ERP GPT Agent. Goodbye!");
        break;
    }

    await RunQuestionAsync(pipeline, input.Trim());
}

static async Task RunQuestionAsync(AgentPipeline pipeline, string question)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"\nProcessing Question: \"{question}\"");
    Console.ResetColor();

    try
    {
        var result = await pipeline.ExecuteAsync(question);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n=================================================");
        Console.WriteLine("RESULT SUMMARY:");
        Console.WriteLine($"Endpoint Picked: {result.RetrievedDocs.FirstOrDefault()?.EndpointName}");
        Console.WriteLine($"GraphQL Query:\n{result.GraphQLQuery}");
        Console.WriteLine("=================================================");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n[Pipeline Error]: {ex.Message}");
        Console.ResetColor();
    }
}
