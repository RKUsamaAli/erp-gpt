using System.Text.Json;
using ErpGpt.Agent.Contracts;

namespace ErpGpt.Agent.Services;

public enum PlanDecision
{
    /// <summary>The plan is executable exactly as written.</summary>
    Execute,
    /// <summary>The request is supportable but under-specified. Ask the user; do not guess.</summary>
    Clarification,
    /// <summary>Nothing in the API can answer this. Say so plainly rather than failing later.</summary>
    Unsupported,
}

public sealed record ValidationIssue(string Code, string Message, string? Field = null)
{
    public override string ToString() => Field is null ? $"{Code}: {Message}" : $"{Code} [{Field}]: {Message}";
}

/// <summary>
/// A plan that HAS passed validation. Only <see cref="QueryPlanValidator"/> can construct one,
/// so "never execute an unvalidated plan" (D3, principle 2) is enforced by the type system
/// rather than by everyone remembering to call the validator first. The builder should take
/// this type, never a raw <see cref="QueryPlan"/>.
/// </summary>
public sealed class ValidatedPlan
{
    internal ValidatedPlan(QueryPlan plan, EndpointSpec endpoint, DateOnly? from, DateOnly? to)
    {
        Plan = plan; Endpoint = endpoint; From = from; To = to;
    }

    public QueryPlan Plan { get; }
    public EndpointSpec Endpoint { get; }
    /// <summary>Presets already resolved to literal dates. The builder never re-interprets them.</summary>
    public DateOnly? From { get; }
    public DateOnly? To { get; }
}

public sealed record ValidationResult(
    PlanDecision Decision,
    IReadOnlyList<ValidationIssue> Issues,
    IReadOnlyList<string> MissingParameters,
    ValidatedPlan? Approved)
{
    public bool CanExecute => Decision == PlanDecision.Execute && Approved is not null;

    /// <summary>Errors phrased for the model's one retry — readable enough to self-correct on.
    /// A stack trace is not; "Did you mean 'topCustomers'?" is.</summary>
    public string ToFeedback() => string.Join("\n", Issues.Select(i => "- " + i.Message));
}

/// <summary>
/// The Validation Engine (roadmap step 7; asked for at 12:54 and 13:51 of the 14 Aug call).
///
/// Deterministic application code, never an LLM (decision D3). Two gates:
///
///   Gate A  ValidateRetrieval  — BEFORE the model is called. Is there enough context to
///                                bother? Below the similarity floor we ask instead of guess.
///   Gate B  Validate           — AFTER the model answers, BEFORE anything executes. Does the
///                                endpoint exist, are required parameters present, are fields
///                                and operators real?
///
/// Gate B is the one that is not deferrable: skipping it is what lets a plan with no date
/// range reach an endpoint whose `from` and `to` are non-nullable.
/// </summary>
public sealed class QueryPlanValidator
{
    public const string SupportedPlanVersion = "1.0";
    private const int MaxLimit = 100;   // mirrors Cap() and MaxPageSize in the API

    private static readonly HashSet<string> AllowedOperators =
        new(StringComparer.OrdinalIgnoreCase) { "eq", "neq", "gt", "gte", "lt", "lte", "contains", "startsWith", "endsWith", "in" };

    private static readonly HashSet<string> AllowedDirections =
        new(StringComparer.OrdinalIgnoreCase) { "ASC", "DESC" };

    private readonly EndpointCatalog _catalog;
    private readonly DateOnly _today;

    /// <param name="today">
    /// The reference date presets resolve against. Deliberately injected: the demo dataset ends
    /// mid-2025, so a real clock makes "this year" correctly return nothing, which reads as a
    /// bug on stage. Configure Agent:DataAsOf for demos; pass the real date in production.
    /// </param>
    public QueryPlanValidator(EndpointCatalog catalog, DateOnly? today = null)
    {
        _catalog = catalog;
        _today = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
    }

    // ---------------------------------------------------------------- Gate A

    /// <summary>
    /// Runs BEFORE inference. Retrieval confidence only — it says whether we found the right
    /// endpoint docs, and says nothing about whether a query built from them will be valid.
    /// Those are independent failure modes; conflating them is how "&gt;90% confidence" gets
    /// mistaken for a safety guarantee.
    /// </summary>
    public ValidationResult ValidateRetrieval(IReadOnlyList<EndpointSearchResult> docs, double floor = 0.5)
    {
        if (docs.Count == 0)
            return Fail(PlanDecision.Unsupported,
                new ValidationIssue("NO_CONTEXT", "Nothing in the knowledge base matched that question."));

        // Stored as cosine DISTANCE (pgvector <=>), so smaller is closer. Similarity is 1 - distance.
        var best = docs.Min(d => d.Distance);
        var similarity = 1.0 - best;

        if (similarity < floor)
            return Fail(PlanDecision.Clarification,
                new ValidationIssue("LOW_CONFIDENCE",
                    $"Closest match scored {similarity:F2}, below the {floor:F2} floor. " +
                    "Ask the user what they mean rather than calling the model."));

        return new ValidationResult(PlanDecision.Execute, [], [], null);
    }

    // ---------------------------------------------------------------- Gate B

    /// <summary>Runs AFTER inference, BEFORE execution. Nothing reaches the API without this.</summary>
    public ValidationResult Validate(QueryPlan? plan)
    {
        var issues = new List<ValidationIssue>();
        var missing = new List<string>();

        if (plan is null)
            return Fail(PlanDecision.Unsupported, new ValidationIssue("NO_PLAN", "The model did not return a query plan."));

        if (!string.IsNullOrWhiteSpace(plan.PlanVersion) && plan.PlanVersion != SupportedPlanVersion)
            issues.Add(new ValidationIssue("PLAN_VERSION",
                $"Plan version '{plan.PlanVersion}' is not supported; this validator implements {SupportedPlanVersion}.", "planVersion"));

        // --- endpoint must exist. Unknown endpoint is UNSUPPORTED, not clarification:
        //     no amount of extra user detail creates an API operation.
        if (string.IsNullOrWhiteSpace(plan.Endpoint))
            return Fail(PlanDecision.Unsupported, new ValidationIssue("ENDPOINT_MISSING", "The plan names no endpoint.", "endpoint"));

        if (!_catalog.TryGet(plan.Endpoint, out var spec))
        {
            var near = _catalog.Similar(plan.Endpoint).ToList();
            var hint = near.Count > 0 ? $" Did you mean {string.Join(" or ", near.Select(n => $"'{n}'"))}?" : "";
            return Fail(PlanDecision.Unsupported,
                new ValidationIssue("ENDPOINT_UNKNOWN", $"'{plan.Endpoint}' is not an endpoint on this API.{hint}", "endpoint"));
        }

        // --- required parameters. Absent required input is CLARIFICATION: the user can supply it.
        DateOnly? from = null, to = null;

        if (spec.Family == "aggregation")
        {
            if (plan.DateRange is null)
            {
                missing.AddRange(["from", "to"]);
                issues.Add(new ValidationIssue("DATE_RANGE_REQUIRED",
                    $"'{spec.Endpoint}' needs a date range. Ask which period the user means.", "dateRange"));
            }
            else
            {
                ResolveDateRange(plan.DateRange, issues, missing, out from, out to);
            }
        }
        else if (plan.DateRange is not null && spec.Family == "detail")
        {
            issues.Add(new ValidationIssue("DATE_RANGE_NOT_ACCEPTED",
                $"'{spec.Endpoint}' looks up a single record by id and takes no date range.", "dateRange"));
        }

        if (spec.Family == "detail")
        {
            if (plan.Id is null)
            {
                missing.Add("id");
                issues.Add(new ValidationIssue("ID_REQUIRED",
                    $"'{spec.Endpoint}' needs the id of a single record. Ask which one, or use a browse endpoint to find it.", "id"));
            }
            else if (plan.Id <= 0)
            {
                issues.Add(new ValidationIssue("ID_INVALID", $"id must be a positive number, got {plan.Id}.", "id"));
            }
        }
        else if (plan.Id is not null)
        {
            issues.Add(new ValidationIssue("ID_NOT_ACCEPTED", $"'{spec.Endpoint}' does not take an id.", "id"));
        }

        // --- limit. Clamp rather than reject: an over-large limit is a harmless misunderstanding,
        //     and the API caps it anyway. Under 1 is nonsense and worth surfacing.
        if (plan.Limit is not null)
        {
            if (spec.Family == "detail")
                issues.Add(new ValidationIssue("LIMIT_NOT_ACCEPTED", $"'{spec.Endpoint}' returns a single record; limit does not apply.", "limit"));
            else if (plan.Limit < 1)
                issues.Add(new ValidationIssue("LIMIT_INVALID", $"limit must be at least 1, got {plan.Limit}.", "limit"));
            else if (plan.Limit > MaxLimit)
                issues.Add(new ValidationIssue("LIMIT_TOO_LARGE", $"limit is capped at {MaxLimit}; {plan.Limit} was requested.", "limit"));
        }

        // --- filters. Only the browse family accepts `where`; aggregations are pre-shaped in C#.
        foreach (var f in plan.Filters)
        {
            if (spec.Family != "browse")
            {
                issues.Add(new ValidationIssue("FILTER_NOT_ACCEPTED",
                    $"'{spec.Endpoint}' does not accept filters — it is computed server-side. " +
                    "Use a browse endpoint if the user needs to filter rows.", "filters"));
                break;
            }
            if (string.IsNullOrWhiteSpace(f.Field))
                issues.Add(new ValidationIssue("FILTER_FIELD_MISSING", "A filter has no field name.", "filters"));
            else if (!spec.SelectableFields.Contains(f.Field, StringComparer.Ordinal))
                issues.Add(new ValidationIssue("FILTER_FIELD_UNKNOWN",
                    $"'{f.Field}' is not a field on {spec.ResultType}. Available: {string.Join(", ", spec.SelectableFields)}.", "filters"));

            if (!AllowedOperators.Contains(f.Operator))
                issues.Add(new ValidationIssue("OPERATOR_UNKNOWN",
                    $"'{f.Operator}' is not a supported operator. Use one of: {string.Join(", ", AllowedOperators)}.", "filters"));

            if (f.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                issues.Add(new ValidationIssue("FILTER_VALUE_MISSING", $"Filter on '{f.Field}' has no value.", "filters"));
        }

        // --- sort
        if (plan.Sort is not null)
        {
            if (spec.Family != "browse")
                issues.Add(new ValidationIssue("SORT_NOT_ACCEPTED",
                    $"'{spec.Endpoint}' returns rows in a fixed order defined by the API.", "sort"));
            else
            {
                if (!spec.SelectableFields.Contains(plan.Sort.Field, StringComparer.Ordinal))
                    issues.Add(new ValidationIssue("SORT_FIELD_UNKNOWN",
                        $"Cannot sort by '{plan.Sort.Field}' — not a field on {spec.ResultType}.", "sort"));
                if (!AllowedDirections.Contains(plan.Sort.Direction))
                    issues.Add(new ValidationIssue("SORT_DIRECTION_INVALID",
                        $"Sort direction must be ASC or DESC, got '{plan.Sort.Direction}'.", "sort"));
            }
        }

        // --- decide. Missing input the user could give -> ask. Anything else wrong -> unsupported.
        if (issues.Count == 0)
            return new ValidationResult(PlanDecision.Execute, [], [], new ValidatedPlan(plan, spec, from, to));

        var decision = missing.Count > 0 ? PlanDecision.Clarification : PlanDecision.Unsupported;
        return new ValidationResult(decision, issues, missing, null);
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
                "Give either a preset or explicit from/to dates, not both.", "dateRange"));
            return;
        }

        if (hasPreset)
        {
            var resolved = ResolvePreset(range.Preset!);
            if (resolved is null)
            {
                issues.Add(new ValidationIssue("DATE_PRESET_UNKNOWN",
                    $"'{range.Preset}' is not a known period. Supported: {string.Join(", ", Presets)}.", "dateRange"));
                return;
            }
            (from, to) = resolved.Value;
            return;
        }

        if (string.IsNullOrWhiteSpace(range.From)) { missing.Add("from"); issues.Add(new ValidationIssue("DATE_FROM_MISSING", "The date range has no start date.", "dateRange")); }
        if (string.IsNullOrWhiteSpace(range.To))   { missing.Add("to");   issues.Add(new ValidationIssue("DATE_TO_MISSING", "The date range has no end date.", "dateRange")); }
        if (missing.Count > 0) return;

        if (!DateOnly.TryParse(range.From, out var f))
        { issues.Add(new ValidationIssue("DATE_FROM_INVALID", $"'{range.From}' is not a date. Use YYYY-MM-DD.", "dateRange")); return; }
        if (!DateOnly.TryParse(range.To, out var t))
        { issues.Add(new ValidationIssue("DATE_TO_INVALID", $"'{range.To}' is not a date. Use YYYY-MM-DD.", "dateRange")); return; }

        if (f > t)
        { issues.Add(new ValidationIssue("DATE_RANGE_REVERSED", $"Start date {f:yyyy-MM-dd} is after end date {t:yyyy-MM-dd}.", "dateRange")); return; }

        from = f; to = t;
    }

    private static readonly string[] Presets =
        ["TODAY", "THIS_WEEK", "THIS_MONTH", "LAST_MONTH", "THIS_QUARTER", "LAST_QUARTER", "THIS_YEAR", "LAST_YEAR", "LAST_30_DAYS", "LAST_90_DAYS"];

    private (DateOnly From, DateOnly To)? ResolvePreset(string preset)
    {
        var d = _today;
        switch (preset.Trim().ToUpperInvariant())
        {
            case "TODAY":        return (d, d);
            case "THIS_WEEK":    { var s = d.AddDays(-(((int)d.DayOfWeek + 6) % 7)); return (s, d); }
            case "THIS_MONTH":   return (new DateOnly(d.Year, d.Month, 1), d);
            case "LAST_MONTH":   { var s = new DateOnly(d.Year, d.Month, 1).AddMonths(-1); return (s, s.AddMonths(1).AddDays(-1)); }
            case "THIS_QUARTER": { var q = (d.Month - 1) / 3; return (new DateOnly(d.Year, q * 3 + 1, 1), d); }
            case "LAST_QUARTER": { var q = (d.Month - 1) / 3; var s = new DateOnly(d.Year, q * 3 + 1, 1).AddMonths(-3); return (s, s.AddMonths(3).AddDays(-1)); }
            case "THIS_YEAR":    return (new DateOnly(d.Year, 1, 1), d);
            case "LAST_YEAR":    return (new DateOnly(d.Year - 1, 1, 1), new DateOnly(d.Year - 1, 12, 31));
            case "LAST_30_DAYS": return (d.AddDays(-30), d);
            case "LAST_90_DAYS": return (d.AddDays(-90), d);
            default: return null;
        }
    }

    private static ValidationResult Fail(PlanDecision decision, ValidationIssue issue) =>
        new(decision, [issue], [], null);
}
