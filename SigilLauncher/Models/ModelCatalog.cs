using System.Text.Json.Serialization;

namespace SigilLauncher.Models;

/// <summary>
/// Metadata for a downloadable GGUF model.
/// </summary>
public sealed class ModelInfo
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("sizeGB")]
    public required double SizeGB { get; init; }

    [JsonPropertyName("minRAMGB")]
    public required double MinRAMGB { get; init; }

    [JsonPropertyName("quantization")]
    public required string Quantization { get; init; }

    [JsonPropertyName("parameters")]
    public required string Parameters { get; init; }

    [JsonPropertyName("downloadURL")]
    public required string DownloadURL { get; init; }

    [JsonPropertyName("filename")]
    public required string Filename { get; init; }
}

/// <summary>
/// Static catalog of supported local inference models.
/// </summary>
public static class ModelCatalog
{
    public static readonly IReadOnlyList<ModelInfo> Models = new[]
    {
        new ModelInfo
        {
            Id = "qwen2.5-1.5b-q4",
            Name = "Qwen 2.5 1.5B",
            Description = "Fast, basic suggestions. Best for constrained hardware.",
            SizeGB = 1.0, MinRAMGB = 3.0, Quantization = "Q4_K_M",
            Parameters = "1.5B",
            DownloadURL = "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
            Filename = "qwen2.5-1.5b-q4_k_m.gguf",
        },
        new ModelInfo
        {
            Id = "phi3-mini-3.8b-q4",
            Name = "Phi-3 Mini 3.8B",
            Description = "Good balance of speed and quality.",
            SizeGB = 2.5, MinRAMGB = 5.0, Quantization = "Q4_K_M",
            Parameters = "3.8B",
            DownloadURL = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-gguf/resolve/main/Phi-3-mini-4k-instruct-q4.gguf",
            Filename = "phi3-mini-3.8b-q4_k_m.gguf",
        },
        new ModelInfo
        {
            Id = "llama3.1-8b-q4",
            Name = "LLaMA 3.1 8B",
            Description = "Best quality. Needs 8GB+ VM RAM.",
            SizeGB = 4.5, MinRAMGB = 8.0, Quantization = "Q4_K_M",
            Parameters = "8B",
            DownloadURL = "https://huggingface.co/lmstudio-community/Meta-Llama-3.1-8B-Instruct-GGUF/resolve/main/Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf",
            Filename = "llama3.1-8b-q4_k_m.gguf",
        },
    };

    /// <summary>
    /// Returns models that can run within the given VM RAM allocation.
    /// Reserves 2GB for OS + daemon overhead.
    /// </summary>
    public static IReadOnlyList<ModelInfo> AvailableModels(int vmRAMGB)
    {
        var availableForModel = (double)vmRAMGB - 2.0;
        return Models.Where(m => m.MinRAMGB <= vmRAMGB && m.SizeGB <= availableForModel).ToList();
    }
}
