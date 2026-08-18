using ErpGpt.Agent.Contracts;
using ErpGpt.Agent.Services;

namespace ErpGpt.Agent;

/// <summary>
/// Proves the validation engine against fixtures. Runs with NO database, NO pgvector,
/// NO Ollama and NO knowledge base — validation is the one part of the pipeline that can
/// be finished and proven while retrieval is still being built.
///
///   dotnet run --project api/ErpGpt.Agent -- --selftest
/// </summary>
public static class ValidatorSelfTest
{
    private sealed record Case(string Name, string Json, PlanDecision Expect, string? MustMention = null);

    public static int Run()
    {
        // Fixed reference date so preset cases are deterministic, and so THIS_YEAR lands
        // inside the demo dataset (which ends mid-2025) instead of returning nothing.
        var validator = new QueryPlanValidator(EndpointCatalog.Load(), new DateOnly(2024, 6, 15));

        Case[] cases =
        [
            // ---------------------------------------------------------- should execute
            new("aggregation, literal dates",
                """{"endpoint":"topCustomers","dateRange":{"from":"2024-01-01","to":"2024-12-31"},"limit":5}""",
                PlanDecision.Execute),
            new("aggregation, preset",
                """{"endpoint":"topCustomers","dateRange":{"preset":"LAST_QUARTER"},"limit":10}""",
                PlanDecision.Execute),
            new("aggregation returning one object",
                """{"endpoint":"salesSummary","dateRange":{"preset":"THIS_YEAR"}}""",
                PlanDecision.Execute),
            new("browse with filter and sort",
                """{"endpoint":"products","filters":[{"field":"listPrice","operator":"gt","value":100}],"sort":{"field":"listPrice","direction":"DESC"},"limit":20}""",
                PlanDecision.Execute),
            new("detail by id",
                """{"endpoint":"customer","id":29641}""", PlanDecision.Execute),

            // ------------------------------------------- missing input -> ask the user
            new("aggregation with no date range",
                """{"endpoint":"topCustomers","limit":5}""",
                PlanDecision.Clarification, "date range"),
            new("detail with no id",
                """{"endpoint":"order"}""", PlanDecision.Clarification, "id"),
            new("date range missing its end",
                """{"endpoint":"topProducts","dateRange":{"from":"2024-01-01"}}""",
                PlanDecision.Clarification, "end date"),

            // ------------------------------------ structurally impossible -> unsupported
            new("THE LIVE BUG: browse args on an aggregation endpoint",
                """{"endpoint":"topCustomers","limit":3,"filters":[{"field":"territory","operator":"eq","value":"Canada"}],"dateRange":{"from":"2024-01-01","to":"2024-12-31"}}""",
                PlanDecision.Unsupported, "does not accept filters"),
            new("endpoint that does not exist",
                """{"endpoint":"topSuppliers","dateRange":{"preset":"THIS_YEAR"}}""",
                PlanDecision.Unsupported, "not an endpoint"),
            new("near-miss endpoint gets a suggestion",
                """{"endpoint":"topCustomer","dateRange":{"preset":"THIS_YEAR"}}""",
                PlanDecision.Unsupported, "Did you mean"),
            new("reversed dates",
                """{"endpoint":"topCustomers","dateRange":{"from":"2025-01-01","to":"2024-01-01"}}""",
                PlanDecision.Unsupported, "after end date"),
            new("limit above the server cap",
                """{"endpoint":"topCustomers","dateRange":{"preset":"THIS_YEAR"},"limit":500}""",
                PlanDecision.Unsupported, "capped at 100"),
            new("filter on a field that does not exist",
                """{"endpoint":"products","filters":[{"field":"colour","operator":"eq","value":"red"}]}""",
                PlanDecision.Unsupported, "not a field"),
            new("sorting an aggregation",
                """{"endpoint":"salesByTerritory","dateRange":{"preset":"THIS_YEAR"},"sort":{"field":"revenue","direction":"DESC"}}""",
                PlanDecision.Unsupported, "fixed order"),
            new("unknown preset",
                """{"endpoint":"topCustomers","dateRange":{"preset":"SINCE_FOREVER"}}""",
                PlanDecision.Unsupported, "not a known period"),
            new("preset and literal dates together",
                """{"endpoint":"topCustomers","dateRange":{"preset":"THIS_YEAR","from":"2024-01-01","to":"2024-12-31"}}""",
                PlanDecision.Unsupported, "not both"),
            new("id sent to a browse endpoint",
                """{"endpoint":"customers","id":5}""", PlanDecision.Unsupported, "does not take an id"),
            new("malformed json",
                """{"endpoint":"topCustomers",,}""", PlanDecision.Unsupported, "not valid JSON"),
        ];

        int pass = 0, fail = 0;
        Console.WriteLine("\nValidation engine self-test\n" + new string('-', 78));

        foreach (var c in cases)
        {
            var plan = QueryPlan.Parse(c.Json, out var parseError);
            var result = parseError is not null
                ? new ValidationResult(PlanDecision.Unsupported,
                    [new ValidationIssue("PLAN_MALFORMED", parseError)], [], null)
                : validator.Validate(plan);

            var text = string.Join(" ", result.Issues.Select(i => i.Message));
            var ok = result.Decision == c.Expect
                     && (c.MustMention is null || text.Contains(c.MustMention, StringComparison.OrdinalIgnoreCase));

            if (ok) { pass++; Console.WriteLine($"  ok    {c.Name}  ->  {result.Decision}"); }
            else
            {
                fail++;
                Console.WriteLine($"  FAIL  {c.Name}");
                Console.WriteLine($"        expected {c.Expect}, got {result.Decision}");
                if (c.MustMention is not null) Console.WriteLine($"        wanted text containing: \"{c.MustMention}\"");
                if (text.Length > 0) Console.WriteLine($"        issues: {text}");
            }
        }

        // Gate A — retrieval confidence, independent of any plan.
        Console.WriteLine("\n  Gate A (pre-inference retrieval confidence)");
        var gateA = new (string Name, double Distance, PlanDecision Expect)[]
        {
            ("strong match  (distance 0.21)", 0.21, PlanDecision.Execute),
            ("weak match    (distance 0.72)", 0.72, PlanDecision.Clarification),
        };
        foreach (var (name, distance, expect) in gateA)
        {
            var docs = new List<EndpointSearchResult> { new() { EndpointName = "topCustomers", Distance = distance } };
            var r = validator.ValidateRetrieval(docs);
            if (r.Decision == expect) { pass++; Console.WriteLine($"  ok    {name}  ->  {r.Decision}"); }
            else { fail++; Console.WriteLine($"  FAIL  {name}: expected {expect}, got {r.Decision}"); }
        }
        var empty = validator.ValidateRetrieval([]);
        if (empty.Decision == PlanDecision.Unsupported) { pass++; Console.WriteLine("  ok    no matches at all  ->  Unsupported"); }
        else { fail++; Console.WriteLine("  FAIL  no matches at all"); }

        Console.WriteLine(new string('-', 78));
        Console.WriteLine($"  {pass} passed, {fail} failed\n");
        return fail == 0 ? 0 : 1;
    }
}
