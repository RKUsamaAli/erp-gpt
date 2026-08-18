using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ErpGpt.Agent.Contracts;

namespace ErpGpt.Agent.Services;

public enum PlanDecision
{
    /// <summary>The plan is executable exactly as written.</summary>
    Execute,
    /// <summary>Supportable, but under-specified. Ask the user; do not guess.</summary>
    Clarification,
    /// <summary>Nothing in the API can answer this. Say so plainly rather than failing later.</summary>
    Unsupported,
}

/// <summary>
/// Whether a problem is something the user or model could put right, or something the API
/// simply cannot do. This is what separates CLARIFICATION from UNSUPPORTED — telling a user
/// "nothing in the API can answer this" because they passed id=0 would be a false statement.
/// </summary>
public enum IssueKind
{
    /// <summary>The user or the model can supply or correct this. -> CLARIFICATION.</summary>
    Fixable,
    /// <summary>No input makes this possible; the capability does not exist. -> UNSUPPORTED.</summary>
    Impossible,
}

public sealed record ValidationIssue(string Code, string Message, string? Field = null, IssueKind Kind = IssueKind.Impossible)
{
    public override string ToString() => Field is null ? $"{Code}: {Message}" : $"{Code} [{Field}]: {Message}";
}



/// <summary>Outcome of Gate A. Separate from ValidationResult on purpose: Gate A approves
/// *proceeding to the model*, never execution, so it must not be mistakable for an executable
/// plan. Returning PlanDecision.Execute with a null plan is exactly the trap this avoids.</summary>
public sealed record RetrievalGateResult(bool Proceed, PlanDecision Decision, IReadOnlyList<ValidationIssue> Issues)
{
    public string ToFeedback() => string.Join("\n", Issues.Select(i => "- " + i.Message));
}

public sealed record ValidationResult(
    PlanDecision Decision,
    IReadOnlyList<ValidationIssue> Issues,
    IReadOnlyList<string> MissingParameters,
    QueryPlanValidator.ValidatedPlan? Approved)
{
    public bool CanExecute => Decision == PlanDecision.Execute && Approved is not null;

    /// <summary>
    /// True when the plan itself was unreadable or used field names that do not exist in the
    /// contract — i.e. the MODEL erred, not the user. This is what the one-retry loop should key
    /// off; the user should never be asked to clarify a JSON shape they never saw.
    /// </summary>
    public bool ShouldRetryModel => Issues.Any(i => i.Code.StartsWith("PLAN_", StringComparison.Ordinal));

    /// <summary>Phrased for the model's one retry — readable enough to self-correct on.
    /// A .NET type name is not; "'take' is not a field of a query plan. Use 'limit'." is.</summary>
    public string ToFeedback() => string.Join("\n", Issues.Select(i => "- " + i.Message));
}

/// <summary>
/// The Validation Engine (roadmap step 7; asked for at 12:54 and 13:51 on 14 Aug, and again at
/// 03:05/03:23 on 18 Aug). Deterministic application code, never an LLM (decision D3).
///
///   Gate A  ValidateRetrieval  BEFORE the model is called — is there enough context to bother?
///   Gate B  Validate           AFTER the model answers, BEFORE anything executes — does the
///                              endpoint exist, are required arguments present, are fields,
///                              operators and option values real?
///
/// Gate B is not deferrable: skipping it is what lets a plan with no date range reach an endpoint
/// whose `from` and `to` are non-nullable.
///
/// NOTHING here throws. A validator that can throw defeats its own purpose, because the caller's
/// catch block turns a precise clarification into a generic failure.
///
/// Legality is read from the CATALOG (generated from live introspection), not from hardcoded
/// knowledge of the schema. Adding an endpoint means regenerating the catalog, not editing this.
/// </summary>
public sealed class QueryPlanValidator
{
    public const string SupportedPlanVersion = "1.0";
    private const int MaxLimit = 100;               // mirrors Cap() and MaxPageSize in the API
    private static readonly DateOnly MinDate = new(1900, 1, 1);
    private static readonly DateOnly MaxDate = new(2100, 12, 31);

    private static readonly HashSet<string> KnownFamilies = new(StringComparer.Ordinal) { "browse", "detail", "aggregation" };
    private static readonly HashSet<string> AllowedDirections = new(StringComparer.Ordinal) { "ASC", "DESC" };

    /// <summary>Names from earlier plan drafts and from the current model prompt, mapped to what
    /// v1 calls them. Turns silent drift into an actionable message.</summary>
    private static readonly Dictionary<string, string> LegacyFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["take"] = "limit",
        ["entity"] = "endpoint",
        ["orderBy"] = "sort",
        ["period"] = "dateRange",
        ["dateRangePreset"] = "dateRange.preset",
        ["interval"] = "options.interval",
        ["aggregation"] = "(not a plan field — pick an aggregation endpoint instead)",
        ["params"] = "(not a plan field — put parameters at the top level)",
        ["parameters"] = "(not a plan field — put parameters at the top level)",
        ["metric"] = "(not a plan field — pick the endpoint that computes it)",
        ["filter"] = "filters",
    };

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Unknown properties are signal, not noise: they mean the model used a name we do not
        // have. Dropping them silently lets a plan execute with the user's intent discarded.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly EndpointCatalog _catalog;
    private readonly DateOnly _today;

    /// <param name="today">
    /// The reference date presets resolve against. Deliberately injected: the demo dataset ends
    /// mid-2025, so a real clock makes "this year" correctly return nothing, which reads as a bug
    /// on stage. Configure Agent:DataAsOf for demos; pass the real date in production.
    /// </param>
    public QueryPlanValidator(EndpointCatalog catalog, DateOnly? today = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _today = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
    }

    /// <summary>
    /// A plan that HAS passed validation. The builder must accept this type, never a raw QueryPlan —
    /// that is what makes "no unvalidated plan is executed" (D3) checkable at compile time rather
    /// than a rule everyone has to remember.
    ///
    /// Honest limit on the guarantee: the constructor is `internal`, so it is unreachable from
    /// outside this assembly but NOT from other types inside ErpGpt.Agent. C# offers nothing
    /// stronger without moving this type to its own assembly. It stops the accident (a caller
    /// passing an unchecked plan to the builder); it does not stop someone determined to forge one.
    /// </summary>
    public sealed class ValidatedPlan
    {
        internal ValidatedPlan(QueryPlan plan, EndpointSpec endpoint, DateOnly? from, DateOnly? to, int? limit)
        { Plan = plan; Endpoint = endpoint; From = from; To = to; EffectiveLimit = limit; }

        public QueryPlan Plan { get; }
        public EndpointSpec Endpoint { get; }
        /// <summary>Presets already resolved to literal dates; the builder never re-interprets them.</summary>
        public DateOnly? From { get; }
        public DateOnly? To { get; }
        /// <summary>The limit after clamping to the server ceiling. Use THIS, not Plan.Limit.</summary>
        public int? EffectiveLimit { get; }
    }

    // ---------------------------------------------------------------- Gate A

    /// <summary>
    /// Runs BEFORE inference. Retrieval confidence ONLY: it says whether we found the right
    /// endpoint docs, and nothing about whether a query built from them will be valid. Those are
    /// independent failure modes and need independent gates.
    /// </summary>
    public RetrievalGateResult ValidateRetrieval(IReadOnlyList<EndpointSearchResult>? docs, double floor = 0.5)
    {
        try
        {
            var live = docs?.Where(d => d is not null).ToList() ?? [];
            if (live.Count == 0)
                return new RetrievalGateResult(false, PlanDecision.Unsupported,
                    [new ValidationIssue("NO_CONTEXT", "Nothing in the knowledge base matched that question.")]);

            // pgvector <=> stores cosine DISTANCE, so smaller is closer. Similarity = 1 - distance.
            var best = live.Min(d => d.Distance);
            if (double.IsNaN(best) || double.IsInfinity(best))
                return new RetrievalGateResult(false, PlanDecision.Unsupported,
                    [new ValidationIssue("SCORE_INVALID",
                        "Retrieval returned a score that is not a number, so confidence cannot be judged. " +
                        "This usually means an embedding failed.")]);

            var similarity = 1.0 - best;
            if (similarity < floor)
                return new RetrievalGateResult(false, PlanDecision.Clarification,
                    [new ValidationIssue("LOW_CONFIDENCE",
                        $"Closest match scored {similarity:F2}, below the {floor:F2} floor. " +
                        "Ask the user what they mean rather than calling the model.", null, IssueKind.Fixable)]);

            return new RetrievalGateResult(true, PlanDecision.Execute, []);
        }
        catch (Exception ex)
        {
            return new RetrievalGateResult(false, PlanDecision.Unsupported,
                [new ValidationIssue("VALIDATOR_ERROR", $"Could not assess retrieval confidence: {ex.Message}")]);
        }
    }

    // ---------------------------------------------------------------- Gate B

    /// <summary>Entry point callers should use: raw model output in, decision out. Parse failures
    /// are translated into messages the model can act on rather than .NET type names.</summary>
    public ValidationResult Validate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Fail(new ValidationIssue("NO_PLAN", "The model returned no query plan."));

        QueryPlan? plan;
        try { plan = JsonSerializer.Deserialize<QueryPlan>(json, ParseOptions); }
        catch (JsonException ex) { return Fail(TranslateJsonError(ex)); }
        catch (Exception ex)
        { return Fail(new ValidationIssue("PLAN_UNREADABLE", $"The query plan could not be read: {ex.Message}")); }

        return Validate(plan);
    }

    /// <summary>Runs AFTER inference, BEFORE execution. Nothing reaches the API without this.</summary>
    public ValidationResult Validate(QueryPlan? plan)
    {
        try { return ValidateCore(plan); }
        catch (Exception ex)
        {
            // Belt and braces. A bug in here must still produce a decision, never an exception:
            // the caller's catch would otherwise turn it into a generic pipeline failure.
            return Fail(new ValidationIssue("VALIDATOR_ERROR",
                $"The plan could not be validated ({ex.GetType().Name}: {ex.Message}). Treating as unsupported."));
        }
    }

    private ValidationResult ValidateCore(QueryPlan? plan)
    {
        var issues = new List<ValidationIssue>();
        var missing = new List<string>();

        if (plan is null)
            return Fail(new ValidationIssue("NO_PLAN", "The model did not return a query plan."));

        if (!string.IsNullOrWhiteSpace(plan.PlanVersion) && plan.PlanVersion != SupportedPlanVersion)
            issues.Add(new ValidationIssue("PLAN_VERSION",
                $"Plan version '{plan.PlanVersion}' is not supported; this validator implements {SupportedPlanVersion}.", "planVersion"));

        // --- endpoint. Unknown endpoint is UNSUPPORTED: no extra user detail brings an API
        //     operation into existence.
        if (string.IsNullOrWhiteSpace(plan.Endpoint))
            return Fail(new ValidationIssue("ENDPOINT_MISSING", "The plan names no endpoint.", "endpoint"));

        var endpointName = plan.Endpoint.Trim();
        if (!_catalog.TryGet(endpointName, out var spec))
        {
            var exact = _catalog.EndpointNames.FirstOrDefault(n => string.Equals(n, endpointName, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return Fail(new ValidationIssue("ENDPOINT_CASE",
                    $"'{endpointName}' is not an endpoint; names are case-sensitive. Use '{exact}'.", "endpoint"));

            var near = _catalog.Similar(endpointName).ToList();
            var hint = near.Count > 0 ? $" Did you mean {string.Join(" or ", near.Select(n => $"'{n}'"))}?" : "";
            return Fail(new ValidationIssue("ENDPOINT_UNKNOWN", $"'{endpointName}' is not an endpoint on this API.{hint}", "endpoint"));
        }

        // --- the catalog itself must be sane. A typo'd family would otherwise turn every gate
        //     below into a silent no-op — fail closed, never open.
        if (!KnownFamilies.Contains(spec.Family))
            return Fail(new ValidationIssue("CATALOG_INVALID",
                $"The endpoint catalog gives '{spec.Endpoint}' an unrecognised family '{spec.Family}'. " +
                "Regenerate it with tools/gen-endpoint-catalog.py."));

        // --- required arguments, driven by the catalog rather than by hardcoded rules.
        DateOnly? from = null, to = null;
        var required = spec.RequiredArgs.ToHashSet(StringComparer.Ordinal);

        var needsDates = required.Contains("from") || required.Contains("to");
        if (needsDates)
        {
            if (plan.DateRange is null)
            {
                foreach (var a in new[] { "from", "to" }) if (required.Contains(a)) missing.Add(a);
                issues.Add(new ValidationIssue("DATE_RANGE_REQUIRED",
                    $"'{spec.Endpoint}' needs a date range. Ask which period the user means.", "dateRange", IssueKind.Fixable));
            }
            else ResolveDateRange(plan.DateRange, issues, missing, out from, out to);
        }
        else if (plan.DateRange is not null)
        {
            issues.Add(new ValidationIssue("DATE_RANGE_NOT_ACCEPTED",
                $"'{spec.Endpoint}' takes no date range. Filter on a date field instead, or use an aggregation endpoint.", "dateRange"));
        }

        if (required.Contains("id"))
        {
            if (plan.Id is null)
            {
                missing.Add("id");
                issues.Add(new ValidationIssue("ID_REQUIRED",
                    $"'{spec.Endpoint}' needs the id of a single record. Ask which one, or use a browse endpoint to find it.", "id", IssueKind.Fixable));
            }
            else if (plan.Id <= 0)
                issues.Add(new ValidationIssue("ID_INVALID",
                    $"id must be a positive number, got {plan.Id}.", "id", IssueKind.Fixable));
        }
        else if (plan.Id is not null)
            issues.Add(new ValidationIssue("ID_NOT_ACCEPTED", $"'{spec.Endpoint}' does not take an id.", "id"));

        // --- limit. Clamp rather than reject: an over-large limit is a harmless misunderstanding
        //     and the API caps it anyway. ValidatedPlan.EffectiveLimit carries the clamped value.
        int? effectiveLimit = null;
        if (plan.Limit is not null)
        {
            var takesLimit = spec.Accepts("limit") || spec.Accepts("take");
            if (!takesLimit)
                issues.Add(new ValidationIssue("LIMIT_NOT_ACCEPTED",
                    $"'{spec.Endpoint}' returns a fixed set of rows and takes no limit.", "limit"));
            else if (plan.Limit < 1)
                issues.Add(new ValidationIssue("LIMIT_INVALID",
                    $"limit must be at least 1, got {plan.Limit}.", "limit", IssueKind.Fixable));
            else
                effectiveLimit = Math.Min(plan.Limit.Value, MaxLimit);
        }

        if (plan.Skip is not null)
        {
            if (!spec.Accepts("skip"))
                issues.Add(new ValidationIssue("SKIP_NOT_ACCEPTED", $"'{spec.Endpoint}' does not support paging.", "skip"));
            else if (plan.Skip < 0)
                issues.Add(new ValidationIssue("SKIP_INVALID", $"skip cannot be negative, got {plan.Skip}.", "skip", IssueKind.Fixable));
        }

        ValidateFilters(plan, spec, issues);
        ValidateSort(plan, spec, issues);
        ValidateOptions(plan, spec, issues);

        if (issues.Count == 0)
            return new ValidationResult(PlanDecision.Execute, [], [],
                new ValidatedPlan(plan, spec, from, to, effectiveLimit));

        // Anything structurally impossible dominates: telling the user "just tell me the period"
        // when the endpoint does not exist would send them round a loop that cannot terminate.
        var decision = issues.Any(i => i.Kind == IssueKind.Impossible)
            ? PlanDecision.Unsupported
            : PlanDecision.Clarification;

        return new ValidationResult(decision, issues, missing, null);
    }

    // ---------------------------------------------------------------- parts

    private static void ValidateFilters(QueryPlan plan, EndpointSpec spec, List<ValidationIssue> issues)
    {
        if (plan.Filters is null || plan.Filters.Count == 0) return;

        var filterable = spec.FilterableFields ?? [];
        if (filterable.Count == 0)
        {
            issues.Add(new ValidationIssue("FILTER_NOT_ACCEPTED",
                $"'{spec.Endpoint}' does not accept filters — it is computed server-side. " +
                "Use a browse endpoint if the user needs to filter rows.", "filters"));
            return;
        }

        for (var i = 0; i < plan.Filters.Count; i++)
        {
            var f = plan.Filters[i];
            if (f is null)
            { issues.Add(new ValidationIssue("FILTER_NULL", $"Filter {i + 1} is empty.", "filters", IssueKind.Fixable)); continue; }

            if (string.IsNullOrWhiteSpace(f.Field))
            { issues.Add(new ValidationIssue("FILTER_FIELD_MISSING", $"Filter {i + 1} has no field name.", "filters", IssueKind.Fixable)); continue; }

            var field = f.Field.Trim();
            var fieldSpec = spec.Filter(field);
            if (fieldSpec is null)
            {
                var near = filterable
                    .Where(x => x.Path.EndsWith("." + field, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(x.Path, field, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Path).Take(3).ToList();
                var hint = near.Count > 0
                    ? $" Did you mean {string.Join(" or ", near.Select(n => $"'{n}'"))}?"
                    : $" Filterable fields include: {string.Join(", ", filterable.Take(8).Select(x => x.Path))}.";
                issues.Add(new ValidationIssue("FILTER_FIELD_UNKNOWN",
                    $"'{field}' cannot be filtered on '{spec.Endpoint}'.{hint}", "filters"));
                continue;
            }

            // GraphQL input field names are case-sensitive: 'CONTAINS' is not 'contains'.
            var op = string.IsNullOrWhiteSpace(f.Operator) ? "eq" : f.Operator.Trim();
            if (!fieldSpec.Operators.Contains(op, StringComparer.Ordinal))
            {
                var ci = fieldSpec.Operators.FirstOrDefault(o => string.Equals(o, op, StringComparison.OrdinalIgnoreCase));
                var hint = ci is not null
                    ? $" Operator names are case-sensitive; use '{ci}'."
                    : $" Supported on this field: {string.Join(", ", fieldSpec.Operators)}.";
                issues.Add(new ValidationIssue("OPERATOR_UNSUPPORTED",
                    $"'{op}' cannot be used on '{field}'.{hint}", "filters"));
            }

            if (f.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                issues.Add(new ValidationIssue("FILTER_VALUE_MISSING",
                    $"Filter on '{field}' has no value.", "filters", IssueKind.Fixable));
            }
            else if (!ValueMatches(fieldSpec.ValueKind, f.Value, op))
            {
                issues.Add(new ValidationIssue("FILTER_VALUE_TYPE",
                    $"'{field}' expects a {Describe(fieldSpec.ValueKind)} value, but got {Describe(f.Value)}. " +
                    "GraphQL will reject a value of the wrong type before the query runs.", "filters", IssueKind.Fixable));
            }
        }
    }

    /// <summary>
    /// Does this JSON value fit the field's GraphQL type? Catching this here matters because a
    /// type mismatch is rejected by GraphQL request validation before execution, which surfaces
    /// as an opaque failure rather than something the user can act on.
    /// </summary>
    private static bool ValueMatches(string expectedKind, JsonElement value, string op)
    {
        // `in`/`nin` take a list of the field's type; check the elements.
        if (op is "in" or "nin")
            return value.ValueKind == JsonValueKind.Array
                   && value.EnumerateArray().All(e => ValueMatches(expectedKind, e, "eq"));

        return expectedKind switch
        {
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number"  => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            // Dates arrive as ISO strings; the API parses them, so shape is what matters here.
            "date"    => value.ValueKind == JsonValueKind.String,
            "string"  => value.ValueKind == JsonValueKind.String,
            _         => true,   // unknown kind: do not invent a rule we cannot justify
        };
    }

    private static string Describe(string kind) => kind switch
    {
        "integer" => "whole number", "number" => "numeric", "boolean" => "true/false",
        "date" => "date (YYYY-MM-DD)", "string" => "text", _ => kind,
    };

    private static string Describe(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => "text", JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "true/false",
        JsonValueKind.Array => "a list", JsonValueKind.Object => "an object",
        _ => v.ValueKind.ToString().ToLowerInvariant(),
    };

    private static void ValidateSort(QueryPlan plan, EndpointSpec spec, List<ValidationIssue> issues)
    {
        if (plan.Sort is null) return;

        var sortable = spec.SortableFields ?? [];
        if (sortable.Count == 0)
        {
            issues.Add(new ValidationIssue("SORT_NOT_ACCEPTED",
                $"'{spec.Endpoint}' returns rows in a fixed order defined by the API.", "sort"));
            return;
        }

        if (string.IsNullOrWhiteSpace(plan.Sort.Field))
            issues.Add(new ValidationIssue("SORT_FIELD_MISSING", "The sort has no field name.", "sort", IssueKind.Fixable));
        else
        {
            var field = plan.Sort.Field.Trim();
            if (!sortable.Contains(field, StringComparer.Ordinal))
            {
                var near = sortable
                    .Where(x => x.EndsWith("." + field, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(x, field, StringComparison.OrdinalIgnoreCase))
                    .Take(3).ToList();
                var hint = near.Count > 0
                    ? $" Did you mean {string.Join(" or ", near.Select(n => $"'{n}'"))}?"
                    : $" Sortable fields include: {string.Join(", ", sortable.Take(8))}.";
                issues.Add(new ValidationIssue("SORT_FIELD_UNKNOWN",
                    $"Cannot sort '{spec.Endpoint}' by '{field}'.{hint}", "sort"));
            }
        }

        var dir = plan.Sort.Direction;
        if (!string.IsNullOrWhiteSpace(dir) && !AllowedDirections.Contains(dir.Trim()))
            issues.Add(new ValidationIssue("SORT_DIRECTION_INVALID",
                $"Sort direction must be ASC or DESC (upper case), got '{dir}'.", "sort", IssueKind.Fixable));
    }

    private static void ValidateOptions(QueryPlan plan, EndpointSpec spec, List<ValidationIssue> issues)
    {
        if (plan.Options is null || plan.Options.Count == 0) return;
        var enums = spec.EnumArgs ?? new Dictionary<string, List<string>>();

        foreach (var (key, value) in plan.Options)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!enums.TryGetValue(key, out var allowed) || allowed is null)
            {
                var hint = enums.Count > 0
                    ? $" '{spec.Endpoint}' accepts: {string.Join(", ", enums.Keys)}."
                    : $" '{spec.Endpoint}' takes no options.";
                issues.Add(new ValidationIssue("OPTION_UNKNOWN", $"'{key}' is not an option of '{spec.Endpoint}'.{hint}", "options"));
                continue;
            }
            // GraphQL enum values are case-sensitive.
            if (value is null || !allowed.Contains(value.Trim(), StringComparer.Ordinal))
                issues.Add(new ValidationIssue("OPTION_VALUE_INVALID",
                    $"'{value}' is not a valid {key}. Use one of: {string.Join(", ", allowed)}.", "options", IssueKind.Fixable));
        }
    }

    // ---------------------------------------------------------------- dates

    private void ResolveDateRange(DateRangeSpec range, List<ValidationIssue> issues, List<string> missing,
                                  out DateOnly? from, out DateOnly? to)
    {
        from = to = null;
        var hasPreset = !string.IsNullOrWhiteSpace(range.Preset);
        var hasLiteral = !string.IsNullOrWhiteSpace(range.From) || !string.IsNullOrWhiteSpace(range.To);

        if (hasPreset && hasLiteral)
        {
            issues.Add(new ValidationIssue("DATE_RANGE_AMBIGUOUS",
                "Give either a preset or explicit from/to dates, not both.", "dateRange", IssueKind.Fixable));
            return;
        }

        if (hasPreset)
        {
            var resolved = ResolvePreset(range.Preset!);
            if (resolved is null)
            {
                issues.Add(new ValidationIssue("DATE_PRESET_UNKNOWN",
                    $"'{range.Preset}' is not a known period. Supported: {string.Join(", ", Presets)}.", "dateRange", IssueKind.Fixable));
                return;
            }
            (from, to) = resolved.Value;
            return;
        }

        if (string.IsNullOrWhiteSpace(range.From))
        { missing.Add("from"); issues.Add(new ValidationIssue("DATE_FROM_MISSING", "The date range has no start date.", "dateRange", IssueKind.Fixable)); }
        if (string.IsNullOrWhiteSpace(range.To))
        { missing.Add("to"); issues.Add(new ValidationIssue("DATE_TO_MISSING", "The date range has no end date.", "dateRange", IssueKind.Fixable)); }
        if (missing.Count > 0) return;

        // Exact ISO only. DateOnly.TryParse without a format is culture-dependent: "03/04/2024"
        // means March in one locale and April in another, and both would validate silently.
        if (!TryParseIso(range.From!, out var f))
        { issues.Add(new ValidationIssue("DATE_FROM_INVALID", $"'{range.From}' is not a date. Use YYYY-MM-DD.", "dateRange", IssueKind.Fixable)); return; }
        if (!TryParseIso(range.To!, out var t))
        { issues.Add(new ValidationIssue("DATE_TO_INVALID", $"'{range.To}' is not a date. Use YYYY-MM-DD.", "dateRange", IssueKind.Fixable)); return; }

        if (f > t)
        { issues.Add(new ValidationIssue("DATE_RANGE_REVERSED", $"Start date {f:yyyy-MM-dd} is after end date {t:yyyy-MM-dd}.", "dateRange", IssueKind.Fixable)); return; }

        // The API does `to.AddDays(1)` to build a half-open window, so DateOnly.MaxValue overflows it.
        if (f < MinDate || t > MaxDate)
        { issues.Add(new ValidationIssue("DATE_OUT_OF_RANGE",
            $"Dates must fall between {MinDate:yyyy-MM-dd} and {MaxDate:yyyy-MM-dd}.", "dateRange", IssueKind.Fixable)); return; }

        from = f; to = t;
    }

    private static bool TryParseIso(string s, out DateOnly value) =>
        DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out value);

    private static readonly string[] Presets =
        ["TODAY", "YESTERDAY", "THIS_WEEK", "THIS_MONTH", "LAST_MONTH", "THIS_QUARTER", "LAST_QUARTER",
         "THIS_YEAR", "LAST_YEAR", "LAST_7_DAYS", "LAST_30_DAYS", "LAST_90_DAYS", "LAST_12_MONTHS"];

    private (DateOnly From, DateOnly To)? ResolvePreset(string preset)
    {
        var d = _today;
        switch (preset.Trim().ToUpperInvariant())
        {
            case "TODAY":          return (d, d);
            case "YESTERDAY":      return (d.AddDays(-1), d.AddDays(-1));
            // ISO week, Monday start. (int)Sunday is 0, so shift by 6 then mod 7.
            case "THIS_WEEK":      return (d.AddDays(-(((int)d.DayOfWeek + 6) % 7)), d);
            case "THIS_MONTH":     return (new DateOnly(d.Year, d.Month, 1), d);
            case "LAST_MONTH":     { var s = new DateOnly(d.Year, d.Month, 1).AddMonths(-1); return (s, s.AddMonths(1).AddDays(-1)); }
            case "THIS_QUARTER":   { var q = (d.Month - 1) / 3; return (new DateOnly(d.Year, q * 3 + 1, 1), d); }
            case "LAST_QUARTER":   { var q = (d.Month - 1) / 3; var s = new DateOnly(d.Year, q * 3 + 1, 1).AddMonths(-3); return (s, s.AddMonths(3).AddDays(-1)); }
            case "THIS_YEAR":      return (new DateOnly(d.Year, 1, 1), d);
            case "LAST_YEAR":      return (new DateOnly(d.Year - 1, 1, 1), new DateOnly(d.Year - 1, 12, 31));
            // Inclusive windows: `to` is inclusive of the whole day, so N days means N-1 back.
            case "LAST_7_DAYS":    return (d.AddDays(-6), d);
            case "LAST_30_DAYS":   return (d.AddDays(-29), d);
            case "LAST_90_DAYS":   return (d.AddDays(-89), d);
            case "LAST_12_MONTHS": return (d.AddMonths(-12).AddDays(1), d);
            default: return null;
        }
    }

    // ---------------------------------------------------------------- json errors

    private static readonly Regex UnmappedRx = new(@"JSON property '([^']+)'", RegexOptions.Compiled);
    private static readonly Regex PathRx = new(@"Path: \$\.?([A-Za-z0-9_\[\]\.]*)", RegexOptions.Compiled);

    /// <summary>
    /// Turns System.Text.Json's internal wording into something the model can act on. Without this
    /// the retry loop is fed "could not be converted to System.Collections.Generic.List`1", which
    /// says nothing about what to change.
    /// </summary>
    private static ValidationIssue TranslateJsonError(JsonException ex)
    {
        var msg = ex.Message ?? "";

        var unmapped = UnmappedRx.Match(msg);
        if (unmapped.Success)
        {
            var prop = unmapped.Groups[1].Value;
            if (LegacyFieldNames.TryGetValue(prop, out var replacement))
                return new ValidationIssue("PLAN_FIELD_UNKNOWN",
                    $"'{prop}' is not a field of a query plan. Use {replacement}.", prop);
            return new ValidationIssue("PLAN_FIELD_UNKNOWN",
                $"'{prop}' is not a field of a query plan. Allowed fields: endpoint, dateRange, id, filters, sort, limit, skip, options.",
                prop);
        }

        var path = PathRx.Match(msg).Groups[1].Value;
        var friendly = path switch
        {
            "filters"   => "'filters' must be an ARRAY of objects, each {\"field\": ..., \"operator\": ..., \"value\": ...}.",
            "sort"      => "'sort' must be an object {\"field\": ..., \"direction\": \"ASC\"|\"DESC\"}.",
            "dateRange" => "'dateRange' must be an object — either {\"preset\": ...} or {\"from\": ..., \"to\": ...}.",
            "limit"     => "'limit' must be a whole number, not text.",
            "skip"      => "'skip' must be a whole number, not text.",
            "id"        => "'id' must be a whole number, not text.",
            "options"   => "'options' must be an object of name/value pairs, e.g. {\"interval\": \"MONTH\"}.",
            "endpoint"  => "'endpoint' must be a string naming one GraphQL field.",
            _           => null,
        };

        if (friendly is not null)
            return new ValidationIssue("PLAN_FIELD_MALFORMED", friendly, path);

        return new ValidationIssue("PLAN_MALFORMED",
            "The query plan is not valid JSON. Return a single JSON object with these fields: " +
            "endpoint, dateRange, id, filters, sort, limit, skip, options.");
    }

    private static ValidationResult Fail(ValidationIssue issue) =>
        new(issue.Kind == IssueKind.Fixable ? PlanDecision.Clarification : PlanDecision.Unsupported,
            [issue], [], null);
}
