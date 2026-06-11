using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalRag.Wpf.Rag;
using LocalRag.Wpf.VectorStore;

namespace LocalRag.Wpf.Configuration;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public VectorStoreSettings VectorStore { get; set; } = new();
    public TurboVecSettings TurboVec { get; set; } = new();

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TurboVecPoc",
        "appsettings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            var defaults = new AppSettings();
            defaults.Save();
            return defaults;
        }

        var json = File.ReadAllText(SettingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}

public sealed class VectorStoreSettings
{
    public VectorStoreProviderType Provider { get; set; } = VectorStoreProviderType.TurboVecSidecar;
    public string DatabasePath { get; set; } = "%LOCALAPPDATA%\\TurboVecPoc\\rag.db";
    public string SqliteVecExtensionPath { get; set; } = "Native\\win-x64\\vec0.dll";
    public string EmbeddingModelId { get; set; } = BgeEmbeddingService.RequiredModelId;
    public int EmbeddingDimensions { get; set; } = BgeEmbeddingService.RequiredDimensions;
    public bool NormalizeEmbeddings { get; set; } = BgeEmbeddingService.RequiredNormalizeEmbeddings;
    public string DistanceMetric { get; set; } = BgeEmbeddingService.RequiredDistanceMetric;
}

public sealed class TurboVecSettings
{
    public string BaseUrl { get; set; } = "http://localhost:8008";
}
