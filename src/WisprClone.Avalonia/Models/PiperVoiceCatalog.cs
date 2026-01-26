using System.Text.Json;
using System.Text.Json.Serialization;

namespace WisprClone.Models;

/// <summary>
/// Represents the Piper voice catalog from HuggingFace.
/// </summary>
public class PiperVoiceCatalog
{
    public Dictionary<string, PiperVoiceEntry> Voices { get; set; } = new();

    public static async Task<PiperVoiceCatalog> LoadFromUrlAsync(HttpClient httpClient, CancellationToken ct = default)
    {
        const string catalogUrl = "https://huggingface.co/rhasspy/piper-voices/raw/main/voices.json";

        var response = await httpClient.GetStringAsync(catalogUrl, ct);
        var voices = JsonSerializer.Deserialize<Dictionary<string, PiperVoiceEntry>>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new();

        return new PiperVoiceCatalog { Voices = voices };
    }

    public IEnumerable<PiperVoiceEntry> GetVoicesByLanguage(string languageFamily)
    {
        return Voices.Values.Where(v =>
            v.Language?.Family?.Equals(languageFamily, StringComparison.OrdinalIgnoreCase) == true);
    }

    public IEnumerable<string> GetAvailableLanguages()
    {
        return Voices.Values
            .Where(v => v.Language != null)
            .Select(v => v.Language!.Family ?? "unknown")
            .Distinct()
            .OrderBy(l => l);
    }
}

public class PiperVoiceEntry
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public PiperLanguageInfo? Language { get; set; }

    [JsonPropertyName("quality")]
    public string Quality { get; set; } = "medium";

    [JsonPropertyName("num_speakers")]
    public int NumSpeakers { get; set; } = 1;

    [JsonPropertyName("speaker_id_map")]
    public Dictionary<string, int>? SpeakerIdMap { get; set; }

    [JsonPropertyName("files")]
    public Dictionary<string, PiperFileInfo>? Files { get; set; }

    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; set; }

    /// <summary>
    /// Gets the display name for the voice.
    /// </summary>
    public string DisplayName => $"{char.ToUpper(Name[0])}{Name[1..]} ({Quality})";

    /// <summary>
    /// Gets the full display name with language.
    /// </summary>
    public string FullDisplayName => $"{Language?.NameEnglish ?? "Unknown"} - {DisplayName}";

    /// <summary>
    /// Gets the .onnx file path from the files dictionary.
    /// </summary>
    public string? GetOnnxFilePath()
    {
        return Files?.Keys.FirstOrDefault(k => k.EndsWith(".onnx") && !k.EndsWith(".onnx.json"));
    }

    /// <summary>
    /// Gets the .onnx.json config file path.
    /// </summary>
    public string? GetOnnxJsonFilePath()
    {
        return Files?.Keys.FirstOrDefault(k => k.EndsWith(".onnx.json"));
    }

    /// <summary>
    /// Gets the total size in bytes for downloading.
    /// </summary>
    public long GetTotalSizeBytes()
    {
        if (Files == null) return 0;
        return Files.Values.Sum(f => f.SizeBytes);
    }

    /// <summary>
    /// Gets the download URL for the .onnx file.
    /// </summary>
    public string? GetOnnxDownloadUrl()
    {
        var path = GetOnnxFilePath();
        if (path == null) return null;
        return $"https://huggingface.co/rhasspy/piper-voices/resolve/main/{path}";
    }

    /// <summary>
    /// Gets the download URL for the .onnx.json file.
    /// </summary>
    public string? GetOnnxJsonDownloadUrl()
    {
        var path = GetOnnxJsonFilePath();
        if (path == null) return null;
        return $"https://huggingface.co/rhasspy/piper-voices/resolve/main/{path}";
    }
}

public class PiperLanguageInfo
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("family")]
    public string? Family { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("name_native")]
    public string? NameNative { get; set; }

    [JsonPropertyName("name_english")]
    public string? NameEnglish { get; set; }

    [JsonPropertyName("country_english")]
    public string? CountryEnglish { get; set; }
}

public class PiperFileInfo
{
    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("md5_digest")]
    public string? Md5Digest { get; set; }
}
