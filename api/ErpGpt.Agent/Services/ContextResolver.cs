using System.Diagnostics;
using System.Text.Json;

namespace ErpGpt.Agent.Services;

public class ContextResolver
{
    private readonly PgVectorMemoryStore _vectorStore;

    public ContextResolver(PgVectorMemoryStore vectorStore)
    {
        _vectorStore = vectorStore;
    }

    /// <summary>
    /// Computes prompt vector embedding using Python sentence-transformers (all-MiniLM-L6-v2)
    /// </summary>
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "embeddings", "embed_single.py");
        scriptPath = Path.GetFullPath(scriptPath);

        if (!File.Exists(scriptPath))
        {
            // Fallback script path check
            scriptPath = Path.GetFullPath("embeddings/embed_single.py");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{scriptPath}\" \"{text.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) throw new Exception("Failed to start python embedding process.");

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Embedding process failed: {error}");
        }

        var vector = JsonSerializer.Deserialize<float[]>(output.Trim());
        return vector ?? throw new Exception("Failed to parse vector embedding output.");
    }

    public async Task<List<EndpointSearchResult>> ResolveContextAsync(string userPrompt, int topK = 3)
    {
        var embedding = await GetEmbeddingAsync(userPrompt);
        return await _vectorStore.SearchSimilarEndpointsAsync(embedding, topK);
    }
}
