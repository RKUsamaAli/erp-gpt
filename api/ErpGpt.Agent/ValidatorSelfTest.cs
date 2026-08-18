using ErpGpt.Agent.Services;

namespace ErpGpt.Agent;

/// <summary>
/// Proves the validation engine against fixtures. Runs with NO database, NO pgvector, NO Ollama
/// and NO knowledge base — validation is the one part of the pipeline that can be finished and
/// proven while retrieval is still being built.
///
///   dotnet run --project api/ErpGpt.Agent -- --selftest
/// </summary>
public static class ValidatorSelfTest
{
    private sealed record Case(string Name, string Json, PlanDecision Expect, string? MustMention = null);

    public static int Run()
    {
        // Fixed reference date so preset cases are deterministic, and so THIS_YEAR lands inside
        // the demo dataset (which ends mid-2025) rather than returning nothing.
        var validator = new QueryPlanValidator(EndpointCatalog.Load(), new DateOnly(2024, 6, 15));

        Case[] cases =
        [
            // ------------------------------------------------------------ should execute
            new("aggregation, literal dates",
                """{"endpoint":"topCustomers","dateRange":{"from":"2024-01-01","to":"2024-12-31"},"limit":5}""", PlanDecision.Execute),
            new("aggregation, preset",
                """{"endpoint":"topCustomers","dateRange":{"preset":"LAST_QUARTER"},"limit":10}""", PlanDecision.Execute),
            new("aggregation returning one object",
                """{"endpoint":"salesSummary","dateRange":{"preset":"THIS_YEAR"}}""", PlanDecision.Execute),
            new("aggregation with an enum option",
                """{"endpoint":"revenueByPeriod","dateRange":{"preset":"THIS_YEAR"},"options":{"interval":"QUARTER"}}""", PlanDecision.Execute),
            new("browse with filter and sort",
                """{"endpoint":"products","filters":[{"field":"listPrice","operator":"gt","value":100}],"sort":{"field":"listPrice","direction":"DESC"},"limit":20}""", PlanDecision.Execute),
            new("browse with paging",
                """{"endpoint":"orders","limit":25,"skip":50}""", PlanDecision.Execute),
            new("NESTED filter path (the API really supports this)",
                """{"endpoint":"customers","filters":[{"field":"territory.name","operator":"eq","value":"Canada"}]}""", PlanDecision.Execute),
            new("string operator on a nested string field",
                """{"endpoint":"customers","filters":[{"field":"person.lastName","operator":"contains","value":"smith"}]}""", PlanDecision.Execute),
            new("detail by id",
                """{"endpoint":"customer","id":29641}""", PlanDecision.Execute),
            new("filter with no operator defaults to eq",
                """{"endpoint":"customers","filters":[{"field":"customerId","value":29641}]}""", PlanDecision.Execute),
            new("in-operator takes a list of the field's type",
                """{"endpoint":"customers","filters":[{"field":"customerId","operator":"in","value":[1,2,3]}]}""", PlanDecision.Execute),

            // ------------------------------------------- missing input -> ask the user
            new("aggregation with no date range",
                """{"endpoint":"topCustomers","limit":5}""", PlanDecision.Clarification, "date range"),
            new("detail with no id",
                """{"endpoint":"order"}""", PlanDecision.Clarification, "id"),
            new("date range missing its end",
                """{"endpoint":"topProducts","dateRange":{"from":"2024-01-01"}}""", PlanDecision.Clarification, "end date"),

            // ------------------------------------ structurally impossible -> unsupported
            new("browse args on an aggregation endpoint",
                """{"endpoint":"topCustomers","dateRange":{"from":"2024-01-01","to":"2024-12-31"},"filters":[{"field":"territory","operator":"eq","value":"Canada"}]}""",
                PlanDecision.Unsupported, "does not accept filters"),
            new("endpoint that does not exist",
                """{"endpoint":"topSuppliers","dateRange":{"preset":"THIS_YEAR"}}""", PlanDecision.Unsupported, "not an endpoint"),
            new("near-miss endpoint gets a suggestion",
                """{"endpoint":"topCustomer","dateRange":{"preset":"THIS_YEAR"}}""", PlanDecision.Unsupported, "Did you mean"),
            new("wrong case is named as such",
                """{"endpoint":"TopCustomers","dateRange":{"preset":"THIS_YEAR"}}""", PlanDecision.Unsupported, "case-sensitive"),
            new("reversed dates",
                """{"endpoint":"topCustomers","dateRange":{"from":"2025-01-01","to":"2024-01-01"}}""", PlanDecision.Clarification, "after end date"),
            
            new("filter on a field that does not exist",
                """{"endpoint":"products","filters":[{"field":"colour","operator":"eq","value":"red"}]}""", PlanDecision.Unsupported, "cannot be filtered"),
            new("operator not legal for that field type",
                """{"endpoint":"customers","filters":[{"field":"customerId","operator":"contains","value":"x"}]}""", PlanDecision.Unsupported, "cannot be used on"),
            new("sorting an aggregation",
                """{"endpoint":"salesByTerritory","dateRange":{"preset":"THIS_YEAR"},"sort":{"field":"revenue","direction":"DESC"}}""", PlanDecision.Unsupported, "fixed order"),
            new("unknown preset",
                """{"endpoint":"topCustomers","dateRange":{"preset":"SINCE_FOREVER"}}""", PlanDecision.Clarification, "not a known period"),
            new("preset and literal dates together",
                """{"endpoint":"topCustomers","dateRange":{"preset":"THIS_YEAR","from":"2024-01-01","to":"2024-12-31"}}""", PlanDecision.Clarification, "not both"),
            new("id sent to a browse endpoint",
                """{"endpoint":"customers","id":5}""", PlanDecision.Unsupported, "does not take an id"),
            new("date range sent to a browse endpoint",
                """{"endpoint":"orders","dateRange":{"preset":"THIS_YEAR"}}""", PlanDecision.Unsupported, "takes no date range"),
            new("invalid enum option value",
                """{"endpoint":"revenueByPeriod","dateRange":{"preset":"THIS_YEAR"},"options":{"interval":"FORTNIGHT"}}""", PlanDecision.Clarification, "not a valid interval"),
            new("skip on a non-browse endpoint",
                """{"endpoint":"topCustomers","dateRange":{"preset":"THIS_YEAR"},"skip":10}""", PlanDecision.Unsupported, "does not support paging"),

            // --------------------------------------- REVIEW: value/type compatibility
            new("REVIEW text where a whole number is required",
                """{"endpoint":"customers","filters":[{"field":"customerId","operator":"eq","value":"29641"}]}""",
                PlanDecision.Clarification, "whole number"),
            new("REVIEW a list where a scalar is required",
                """{"endpoint":"products","filters":[{"field":"color","operator":"eq","value":["Red","Blue"]}]}""",
                PlanDecision.Clarification, "expects a text value"),
            new("REVIEW EF-untranslatable computed field is not filterable",
                """{"endpoint":"customers","filters":[{"field":"displayName","operator":"contains","value":"bike"}]}""",
                PlanDecision.Unsupported, "cannot be filtered"),
            new("REVIEW EF-untranslatable computed field is not sortable",
                """{"endpoint":"orderLines","sort":{"field":"lineTotal","direction":"DESC"}}""",
                PlanDecision.Unsupported, "Cannot sort"),
            new("REVIEW uppercase operator is named as case-sensitive",
                """{"endpoint":"products","filters":[{"field":"color","operator":"CONTAINS","value":"red"}]}""",
                PlanDecision.Unsupported, "case-sensitive"),
            new("REVIEW lowercase sort direction rejected",
                """{"endpoint":"products","sort":{"field":"listPrice","direction":"asc"}}""",
                PlanDecision.Clarification, "upper case"),

            // --------------------------------------- REVIEW: catalog-driven argument legality
            new("REVIEW limit on an endpoint that takes none",
                """{"endpoint":"salesSummary","dateRange":{"preset":"LAST_MONTH"},"limit":10}""",
                PlanDecision.Unsupported, "takes no limit"),
            new("REVIEW limit above the cap is CLAMPED, not refused",
                """{"endpoint":"topCustomers","dateRange":{"preset":"THIS_YEAR"},"limit":500}""", PlanDecision.Execute),
            new("REVIEW browse endpoint may not carry a dateRange",
                """{"endpoint":"orders","dateRange":{"preset":"LAST_QUARTER"},"limit":20}""",
                PlanDecision.Unsupported, "takes no date range"),
            new("REVIEW absurd far-future date is refused (API AddDays would overflow)",
                """{"endpoint":"salesSummary","dateRange":{"from":"0001-01-01","to":"9999-12-31"}}""",
                PlanDecision.Clarification, "must fall between"),
            new("REVIEW ambiguous locale date format is refused",
                """{"endpoint":"salesSummary","dateRange":{"from":"01/02/2024","to":"03/04/2024"}}""",
                PlanDecision.Clarification, "YYYY-MM-DD"),
            new("REVIEW id=0 is fixable, not 'the API cannot do this'",
                """{"endpoint":"customer","id":0}""", PlanDecision.Clarification, "positive number"),

            // ------------------------------------------------- REGRESSIONS (found in review)
            new("REGRESSION null inside filters must not crash",
                """{"endpoint":"products","filters":[null]}""", PlanDecision.Clarification, "empty"),
            new("REGRESSION unknown property is reported, not dropped",
                """{"endpoint":"topCustomers","take":3,"dateRange":{"preset":"THIS_YEAR"}}""",
                PlanDecision.Unsupported, "Use limit"),
            new("REGRESSION legacy shape gets an actionable message",
                """{"endpoint":"topCustomers","take":3,"filters":{"field":"country","value":"Canada"},"sort":{"field":"totalRevenue","direction":"DESC"}}""",
                PlanDecision.Unsupported, "Use limit"),
            new("REGRESSION filters as object, not array",
                """{"endpoint":"customers","filters":{"field":"displayName","value":"x"}}""",
                PlanDecision.Unsupported, "must be an ARRAY"),
            new("REGRESSION wrong JSON type for limit reads plainly",
                """{"endpoint":"topCustomers","dateRange":{"preset":"THIS_YEAR"},"limit":"three"}""",
                PlanDecision.Unsupported, "whole number"),
            new("REGRESSION null sort field must not crash",
                """{"endpoint":"products","sort":{"field":null,"direction":"DESC"}}""",
                PlanDecision.Clarification, "no field name"),
            new("REGRESSION empty string plan",
                "", PlanDecision.Unsupported, "no query plan"),
            new("REGRESSION json array instead of object",
                """[{"endpoint":"topCustomers"}]""", PlanDecision.Unsupported, null),
            new("REGRESSION json literal null",
                "null", PlanDecision.Unsupported, "did not return"),
            new("REGRESSION truncated json",
                """{"endpoint":"topCustomers","dateRange":{"preset":""" , PlanDecision.Unsupported, null),
            new("REGRESSION whitespace endpoint name is trimmed",
                """{"endpoint":"  topCustomers  ","dateRange":{"preset":"THIS_YEAR"}}""", PlanDecision.Execute),
        ];

        int pass = 0, fail = 0;
        Console.WriteLine("\nValidation engine self-test\n" + new string('-', 82));

        foreach (var c in cases)
        {
            ValidationResult result;
            try { result = validator.Validate(c.Json); }
            catch (Exception ex)
            {
                fail++;
                Console.WriteLine($"  THREW {c.Name}  ->  {ex.GetType().Name}: {ex.Message}");
                continue;   // a validator that throws is the defect this suite exists to catch
            }

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

        // ---- Gate A: retrieval confidence, independent of any plan
        Console.WriteLine("\n  Gate A (pre-inference retrieval confidence)");
        foreach (var (name, distance, expect) in new (string, double, PlanDecision)[]
                 {
                     ("strong match (distance 0.21)", 0.21, PlanDecision.Execute),
                     ("weak match   (distance 0.72)", 0.72, PlanDecision.Clarification),
                 })
        {
            var r = validator.ValidateRetrieval([new EndpointSearchResult { EndpointName = "topCustomers", Distance = distance }]);
            if (r.Decision == expect) { pass++; Console.WriteLine($"  ok    {name}  ->  {r.Decision}"); }
            else { fail++; Console.WriteLine($"  FAIL  {name}: expected {expect}, got {r.Decision}"); }
        }
        foreach (var (name, docs) in new (string, List<EndpointSearchResult>)[]
                 {
                     ("no matches at all", []),
                     ("null entries in the list", [null!]),
                 })
        {
            var r = validator.ValidateRetrieval(docs);
            if (r.Decision == PlanDecision.Unsupported) { pass++; Console.WriteLine($"  ok    {name}  ->  Unsupported"); }
            else { fail++; Console.WriteLine($"  FAIL  {name}: got {r.Decision}"); }
        }
        var rNull = validator.ValidateRetrieval(null);
        if (rNull.Decision == PlanDecision.Unsupported) { pass++; Console.WriteLine("  ok    null docs list  ->  Unsupported"); }
        else { fail++; Console.WriteLine("  FAIL  null docs list"); }

        // ---- date presets, worked by hand against the fixed reference date 2024-06-15 (a Saturday)
        Console.WriteLine("\n  Date presets (reference date 2024-06-15)");
        foreach (var (preset, expectFrom, expectTo) in new (string, string, string)[]
                 {
                     ("TODAY",          "2024-06-15", "2024-06-15"),
                     ("THIS_WEEK",      "2024-06-10", "2024-06-15"),   // Monday-start ISO week
                     ("THIS_MONTH",     "2024-06-01", "2024-06-15"),
                     ("LAST_MONTH",     "2024-05-01", "2024-05-31"),
                     ("THIS_QUARTER",   "2024-04-01", "2024-06-15"),
                     ("LAST_QUARTER",   "2024-01-01", "2024-03-31"),
                     ("THIS_YEAR",      "2024-01-01", "2024-06-15"),
                     ("LAST_YEAR",      "2023-01-01", "2023-12-31"),
                     ("LAST_7_DAYS",    "2024-06-09", "2024-06-15"),
                     ("LAST_30_DAYS",   "2024-05-17", "2024-06-15"),
                 })
        {
            var r = validator.Validate("{\"endpoint\":\"salesSummary\",\"dateRange\":{\"preset\":\"" + preset + "\"}}");
            var got = r.Approved is null ? "(rejected)" : $"{r.Approved.From:yyyy-MM-dd} .. {r.Approved.To:yyyy-MM-dd}";
            var want = $"{expectFrom} .. {expectTo}";
            if (got == want) { pass++; Console.WriteLine($"  ok    {preset,-14} {got}"); }
            else { fail++; Console.WriteLine($"  FAIL  {preset,-14} expected {want}, got {got}"); }
        }

        Console.WriteLine(new string('-', 82));
        Console.WriteLine($"  {pass} passed, {fail} failed\n");
        return fail == 0 ? 0 : 1;
    }
}
