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
            scriptPath = Path.GetFullPath("embeddings/embed_single.py");
        }

        // Check for PYTHON_PATH env variable, or candidate python executables
        string pythonExe = Environment.GetEnvironmentVariable("PYTHON_PATH") ?? "py";
        
        string output = "";
        string error = "";
        int exitCode = -1;

        string[] candidates = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PYTHON_PATH")) 
            ? new[] { "py", "python", "python3" } 
            : new[] { pythonExe };

        foreach (var candidate in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = $"\"{scriptPath}\" \"{text.Replace("\"", "\\\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) continue;

                output = await process.StandardOutput.ReadToEndAsync();
                error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                exitCode = process.ExitCode;

                if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    break;
                }
            }
            catch
            {
                // Try next candidate
            }
        }

        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new Exception(
                $"Embedding process failed. Python could not be executed.\n" +
                $"Details: {error}\n" +
                $"Tip: Ensure Python is installed and in your PATH, or set the environment variable PYTHON_PATH to your python.exe location.");
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
