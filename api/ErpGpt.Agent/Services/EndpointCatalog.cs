using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErpGpt.Agent.Services;

/// <summary>
/// What the validator checks against: the real shape of every endpoint the API exposes.
///
/// Generated from GraphQL introspection at BUILD time and committed, deliberately not
/// read from the API at run time — introspection is disabled outside Development
/// (HotChocolate returns HC0046), so a run-time lookup would work on a developer's
/// machine and fail in production. Regenerate whenever endpoints change; CI compares
/// the committed file against the live schema.
/// </summary>
public sealed class EndpointCatalog
{
    private readonly Dictionary<string, EndpointSpec> _byName;

    private EndpointCatalog(IEnumerable<EndpointSpec> endpoints) =>
        _byName = endpoints.ToDictionary(e => e.Endpoint, StringComparer.Ordinal);

    public IReadOnlyCollection<string> EndpointNames => _byName.Keys;
    public int Count => _byName.Count;

    public bool TryGet(string endpoint, out EndpointSpec spec) =>
        _byName.TryGetValue(endpoint, out spec!);

    /// <summary>Endpoint names within a small edit distance — turns "you can't do that"
    /// into "did you mean...", which is what lets the model self-correct on retry.</summary>
    public IEnumerable<string> Similar(string name, int max = 3) =>
        _byName.Keys
            .Select(k => (k, d: Distance(k.ToLowerInvariant(), name.ToLowerInvariant())))
            .Where(x => x.d <= Math.Max(3, name.Length / 3))
            .OrderBy(x => x.d)
            .Take(max)
            .Select(x => x.k);

    public static EndpointCatalog Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "Contracts", "endpoint-catalog.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Endpoint catalog not found at {path}. Regenerate it from the live schema " +
                "before the agent can validate anything.", path);

        var doc = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), Opts)
                  ?? throw new InvalidDataException($"Endpoint catalog at {path} is empty.");
        return new EndpointCatalog(doc.Endpoints);
    }

    public static EndpointCatalog FromJson(string json) =>
        new(JsonSerializer.Deserialize<CatalogFile>(json, Opts)!.Endpoints);

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    private sealed class CatalogFile
    {
        [JsonPropertyName("endpoints")] public List<EndpointSpec> Endpoints { get; set; } = [];
    }

    // Levenshtein, iterative, two rows. Only ever runs on a failed lookup.
    private static int Distance(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0 || b.Length == 0) return Math.Max(a.Length, b.Length);
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1),
                                  prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}

public sealed record EndpointSpec
{
    [JsonPropertyName("endpoint")]         public string Endpoint { get; init; } = string.Empty;
    /// <summary>browse | detail | aggregation — decides which ARGUMENTS are legal.</summary>
    [JsonPropertyName("family")]           public string Family { get; init; } = string.Empty;
    /// <summary>connection | object | list — decides how the RESULT is read and rendered.
    /// Not derivable from family: salesSummary is an aggregation returning one object.</summary>
    [JsonPropertyName("result_shape")]     public string ResultShape { get; init; } = string.Empty;
    [JsonPropertyName("returns")]          public string Returns { get; init; } = string.Empty;
    [JsonPropertyName("resultType")]       public string ResultType { get; init; } = string.Empty;
    [JsonPropertyName("args")]             public List<ArgSpec> Args { get; init; } = [];
    [JsonPropertyName("selectableFields")] public List<string> SelectableFields { get; init; } = [];

    public IEnumerable<string> RequiredArgs => Args.Where(a => a.Required).Select(a => a.Name);
    public bool Accepts(string arg) => Args.Any(a => a.Name == arg);
}

public sealed record ArgSpec
{
    [JsonPropertyName("name")]     public string Name { get; init; } = string.Empty;
    [JsonPropertyName("type")]     public string Type { get; init; } = string.Empty;
    [JsonPropertyName("required")] public bool Required { get; init; }
}
