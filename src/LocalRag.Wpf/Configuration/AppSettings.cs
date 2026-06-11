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

    public IndexSettings Index { get; set; } = new();
    public EmbeddingSettings Embedding { get; set; } = new();
    public VectorStoreSettings VectorStore { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
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
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        settings.NormalizeLegacySettings();
        settings.VectorStore ??= new VectorStoreSettings();
        settings.Search ??= new SearchSettings();
        settings.TurboVec ??= new TurboVecSettings();
        return settings;
    }

    public void Save()
    {
        NormalizeLegacySettings();
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private void NormalizeLegacySettings()
    {
        if (!string.IsNullOrWhiteSpace(TurboVec.BaseUrl) &&
            (string.IsNullOrWhiteSpace(Embedding.BaseUrl) || string.Equals(Embedding.BaseUrl, "http://localhost:8008", StringComparison.OrdinalIgnoreCase)))
        {
            Embedding.BaseUrl = TurboVec.BaseUrl;
        }

        if (string.IsNullOrWhiteSpace(TurboVec.BaseUrl))
        {
            TurboVec.BaseUrl = Embedding.BaseUrl;
        }

        VectorStore.EmbeddingDimensions = Embedding.Dimensions;
        VectorStore.DistanceMetric = BgeEmbeddingService.RequiredDistanceMetric;
        VectorStore.ReadOnly = Index.Mode == IndexMode.StaticReadOnly;
        Index.AllowRuntimeIngestion = Index.Mode == IndexMode.Dynamic && Index.AllowRuntimeIngestion;
    }
}

public sealed class IndexSettings
{
    public IndexMode Mode { get; set; } = IndexMode.Dynamic;
    public bool AllowRuntimeIngestion { get; set; } = true;
}

public sealed class EmbeddingSettings
{
    public EmbeddingProviderType Provider { get; set; } = EmbeddingProviderType.TurboVecSidecar;
    public string BaseUrl { get; set; } = "http://localhost:8008";
    public string ModelId { get; set; } = BgeEmbeddingService.RequiredModelId;
    public int Dimensions { get; set; } = BgeEmbeddingService.RequiredDimensions;
    public int BatchSize { get; set; } = 32;
    public bool Normalize { get; set; } = BgeEmbeddingService.RequiredNormalizeEmbeddings;
    public string BgeQueryPrefix { get; set; } = "Represent this sentence for searching relevant passages: ";
}

public sealed class VectorStoreSettings
{
    public VectorStoreProviderType Provider { get; set; } = VectorStoreProviderType.TurboVecSidecar;
    public string DatabasePath { get; set; } = "%LOCALAPPDATA%\\TurboVecPoc\\rag.db";
    public string SqliteVecExtensionPath { get; set; } = "Native\\win-x64\\vec0.dll";
    public int EmbeddingDimensions { get; set; } = BgeEmbeddingService.RequiredDimensions;
    public string DistanceMetric { get; set; } = BgeEmbeddingService.RequiredDistanceMetric;
    public bool ReadOnly { get; set; }
}

public sealed class SearchSettings
{
    public int CandidateTopK { get; set; } = 8;
    public int FinalTopK { get; set; } = 2;
}

public sealed class SearchSettings
{
    public const int DefaultTopKValue = 2;

    public int DefaultTopK { get; set; } = DefaultTopKValue;
}

public sealed class TurboVecSettings
{
    public string BaseUrl { get; set; } = "http://localhost:8008";
}
