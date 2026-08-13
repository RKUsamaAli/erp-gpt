using System.Text.Json;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;

namespace ErpGpt.Agent.Services;

public class EndpointSearchResult
{
    public string EndpointName { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }
    public double Distance { get; set; }
}

public class PgVectorMemoryStore
{
    private readonly string _connectionString;

    public PgVectorMemoryStore(string connectionString = "Host=localhost;Port=5432;Database=erpgpt;Username=erpgpt;Password=devonly")
    {
        _connectionString = connectionString;
    }

    public async Task<List<EndpointSearchResult>> SearchSimilarEndpointsAsync(float[] queryVector, int topK = 3)
    {
        var results = new List<EndpointSearchResult>();

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
        dataSourceBuilder.UseVector();
        await using var dataSource = dataSourceBuilder.Build();

        await using var conn = await dataSource.OpenConnectionAsync();

        var vec = new Vector(queryVector);

        // Cosine distance operator (<=>) in pgvector
        var query = @"
            SELECT endpoint_name, question, payload, (embedding <=> $1) AS distance
            FROM endpoint_embeddings
            ORDER BY embedding <=> $1
            LIMIT $2;";

        await using var cmd = new NpgsqlCommand(query, conn);
        cmd.Parameters.AddWithValue(vec);
        cmd.Parameters.AddWithValue(topK);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var payloadJson = reader.GetString(2);
            using var doc = JsonDocument.Parse(payloadJson);

            results.Add(new EndpointSearchResult
            {
                EndpointName = reader.GetString(0),
                Question = reader.GetString(1),
                Payload = doc.RootElement.Clone(),
                Distance = reader.GetDouble(3)
            });
        }

        return results;
    }
}
