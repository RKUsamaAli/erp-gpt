using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErpGpt.Agent.Contracts;

/// <summary>
/// The structured interpretation the model produces. It is DATA, not a command —
/// nothing here reaches the API until QueryPlanValidator has approved it (D3, principle 2).
///
/// One shape covers all three endpoint families. The model never writes GraphQL and never
/// picks field names: it names an endpoint and fills that endpoint's parameters, which is
/// exactly the job docs/architecture.md assigns it.
/// </summary>
public sealed record QueryPlan
{
    [JsonPropertyName("planVersion")] public string? PlanVersion { get; init; }

    /// <summary>Exact GraphQL field name. Checked against the catalog — never trusted.</summary>
    [JsonPropertyName("endpoint")] public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// Required by the aggregation family. Either a symbolic preset or a literal window,
    /// never both. The model does NO date arithmetic; deterministic code resolves presets,
    /// because date maths is what a small model is worst at.
    /// </summary>
    [JsonPropertyName("dateRange")] public DateRangeSpec? DateRange { get; init; }

    /// <summary>Detail family only.</summary>
    [JsonPropertyName("id")] public int? Id { get; init; }

    [JsonPropertyName("filters")] public List<PlanFilter>? Filters { get; init; }

    [JsonPropertyName("sort")] public PlanSort? Sort { get; init; }

    /// <summary>One plan field for "how many". The builder emits `take` or `limit` per family —
    /// mirroring both API names into the plan hands the model a distinction it gets wrong
    /// about half the time.</summary>
    [JsonPropertyName("limit")] public int? Limit { get; init; }

    /// <summary>Browse paging offset.</summary>
    [JsonPropertyName("skip")] public int? Skip { get; init; }

    /// <summary>Enum-valued arguments, e.g. revenueByPeriod's interval = MONTH|QUARTER|YEAR.
    /// Validated per endpoint against the catalog's enumArgs.</summary>
    [JsonPropertyName("options")] public Dictionary<string, string>? Options { get; init; }
}

public sealed record DateRangeSpec
{
    [JsonPropertyName("preset")] public string? Preset { get; init; }
    [JsonPropertyName("from")]   public string? From { get; init; }
    [JsonPropertyName("to")]     public string? To { get; init; }
}

public sealed record PlanFilter
{
    [JsonPropertyName("field")]    public string? Field { get; init; }
    [JsonPropertyName("operator")] public string? Operator { get; init; }
    [JsonPropertyName("value")]    public JsonElement Value { get; init; }
}

public sealed record PlanSort
{
    [JsonPropertyName("field")]     public string? Field { get; init; }
    [JsonPropertyName("direction")] public string? Direction { get; init; }
}
