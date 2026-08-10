// ErpGpt.MetadataGen — roadmap step 2: Database/GraphQL Metadata Generator.
//
// Introspects the RUNNING system rather than documenting it by hand:
//   1. EF Core IModel  → tables, fields, types, PKs, FKs, relationships, indexes
//   2. HotChocolate schema → operations, parameters, response structures (+ SDL)
//   3. [Semantic] attributes → business meanings for entities, fields, statuses
//
// Output (kb/metadata/, git-committed so diffs are reviewable):
//   entities.json    — schema + relationships + meanings
//   operations.json  — GraphQL ops with args and return types + meanings
//   schema.graphql   — full SDL, for the validator and for humans
//
// This produces the TECHNICAL half of the knowledge base. The human half —
// kb/*.json example_questions — cannot be generated (see kb/README.md).
//
// Usage:  dotnet run --project api/ErpGpt.MetadataGen [output-dir]
//         (default output: <repo-root>/kb/metadata)
// No database connection is made: the EF model and GraphQL schema are both
// built in-process from the code alone.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErpGpt.Api.Data;
using ErpGpt.Api.Domain;
using ErpGpt.Api.GraphQL;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var outDir = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "kb", "metadata"));
Directory.CreateDirectory(outDir);

var json = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

// ---------------------------------------------------------------------------
// 1. EF Core model → entities.json
// ---------------------------------------------------------------------------

var options = new DbContextOptionsBuilder<ErpDbContext>()
    .UseNpgsql("Host=metadata-only") // model building never connects
    .Options;
using var db = new ErpDbContext(options);

static string? MeaningOf(MemberInfo? m) =>
    m?.GetCustomAttribute<SemanticAttribute>()?.Meaning;

static object? EnumValuesOf(Type t)
{
    if (!t.IsEnum) return null;
    return Enum.GetNames(t).Select(name => new
    {
        name,
        value = (int)Enum.Parse(t, name),
        meaning = MeaningOf(t.GetField(name)),
    }).ToList();
}

var entities = db.Model.GetEntityTypes().Select(et =>
{
    var clr = et.ClrType;
    var pk = et.FindPrimaryKey()?.Properties.Select(p => p.Name).ToList();

    var fields = et.GetProperties().Select(p =>
    {
        var underlying = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
        return new
        {
            name = p.Name,
            type = underlying.Name,
            nullable = p.IsNullable,
            isPrimaryKey = pk?.Contains(p.Name) == true,
            isForeignKey = p.IsForeignKey(),
            meaning = MeaningOf(clr.GetProperty(p.Name)),
            enumValues = EnumValuesOf(underlying),
        };
    }).ToList();

    var relationships = et.GetForeignKeys().Select(fk => new
    {
        foreignKey = fk.Properties.Select(p => p.Name).ToList(),
        references = fk.PrincipalEntityType.ClrType.Name,
        referencedKey = fk.PrincipalKey.Properties.Select(p => p.Name).ToList(),
        navigation = fk.DependentToPrincipal?.Name,
        inverseNavigation = fk.PrincipalToDependent?.Name,
        onDelete = fk.DeleteBehavior.ToString(),
    }).ToList();

    var indexes = et.GetIndexes().Select(ix => new
    {
        columns = ix.Properties.Select(p => p.Name).ToList(),
        unique = ix.IsUnique,
    }).ToList();

    return new
    {
        entity = clr.Name,
        table = et.GetTableName(),
        meaning = MeaningOf(clr),
        primaryKey = pk,
        fields,
        relationships,
        indexes,
    };
}).OrderBy(e => e.entity).ToList();

File.WriteAllText(Path.Combine(outDir, "entities.json"),
    JsonSerializer.Serialize(new { generatedAt = DateTime.UtcNow, entities }, json));
Console.WriteLine($"entities.json    — {entities.Count} entities");

// ---------------------------------------------------------------------------
// 2. HotChocolate schema → operations.json + schema.graphql
// ---------------------------------------------------------------------------

var schema = await new ServiceCollection()
    .AddGraphQL()
    .AddQueryType<Query>()
    .AddProjections()
    .AddFiltering()
    .AddSorting()
    .BuildSchemaAsync();

File.WriteAllText(Path.Combine(outDir, "schema.graphql"), schema.ToString());

// Map GraphQL field name back to the C# resolver method to read [Semantic].
var resolverMeanings = typeof(Query).GetMethods(BindingFlags.Public | BindingFlags.Instance)
    .Where(m => m.DeclaringType == typeof(Query))
    .ToDictionary(
        m => char.ToLowerInvariant(m.Name.StartsWith("Get") ? m.Name[3] : m.Name[0])
             + (m.Name.StartsWith("Get") ? m.Name[4..] : m.Name[1..]),
        m => MeaningOf(m),
        StringComparer.OrdinalIgnoreCase);

var operations = schema.QueryType.Fields
    .Where(f => !f.IsIntrospectionField)
    .Select(f => new
    {
        operation = f.Name,
        meaning = resolverMeanings.GetValueOrDefault(f.Name),
        parameters = f.Arguments.Select(a => new
        {
            name = a.Name,
            type = a.Type.Print(),
            hasDefault = a.DefaultValue is not null,
        }).ToList(),
        returns = f.Type.Print(),
    })
    .OrderBy(o => o.operation)
    .ToList();

File.WriteAllText(Path.Combine(outDir, "operations.json"),
    JsonSerializer.Serialize(new { generatedAt = DateTime.UtcNow, operations }, json));
Console.WriteLine($"operations.json  — {operations.Count} operations");
Console.WriteLine($"schema.graphql   — SDL export");
Console.WriteLine($"\nOutput: {outDir}");
Console.WriteLine("Commit the diff. Re-run whenever entities or endpoints change.");
