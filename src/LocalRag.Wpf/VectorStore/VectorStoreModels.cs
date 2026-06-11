namespace LocalRag.Wpf.VectorStore;

public enum VectorStoreProviderType
{
    TurboVecSidecar,
    SqliteVec
}

public enum EmbeddingProviderType
{
    TurboVecSidecar
}

public enum IndexMode
{
    Dynamic,
    StaticReadOnly
}

public enum RetrievalIntent
{
    Rules,
    Examples,
    Validation,
    SignatureLookup,
    ToolboxLookup,
    Generation,
    Mixed
}

public enum RaslDocumentFamily
{
    GovernancePrompt,
    TopicRulePrompt,
    ApplicationCorePrompt,
    ApplicationDomainPrompt,
    MethodCatalogPrompt,
    ToolboxCatalog,
    ScenarioPromptVariant,
    LegacyPromptVariant,
    Unknown
}

public enum ChunkKind
{
    GovernanceRule,
    Rule,
    FallbackRule,
    StyleGuidance,
    Validation,
    Example,
    AntiExample,
    Template,
    SignatureCatalog,
    ToolboxSignatureCatalog,
    Notice,
    Reference,
    LegacyReference
}

public sealed record VectorChunkRecord(
    string DocumentId,
    string SourcePath,
    int ChunkIndex,
    string FileName,
    string? FileVersion,
    string? SectionTitle,
    string? ParentSection,
    string? Topic,
    string ChunkKind,
    string? DocumentFamily,
    string? CanonicalStatus,
    string? ParentFile,
    string? Domain,
    string? ApiSurface,
    string? CallStyle,
    string? MethodFamily,
    string? MethodNames,
    int SignatureCount,
    int Priority,
    int PrecedenceLevel,
    bool IsReferenceOnly,
    bool IsCanonical,
    bool IsSignatureCatalog,
    bool IsLegacyVariant,
    bool IsPlaceholder,
    bool RequiresGovernance,
    bool RequiresApplicationCore,
    string OriginalText,
    string EmbeddingText,
    float[] Embedding,
    DateTimeOffset ModifiedUtc,
    string? ContentHash);

public sealed record VectorSearchRequest(
    float[] QueryEmbedding,
    string RawUserQuery,
    string RetrievalQuery,
    int CandidateTopK,
    int FinalTopK,
    RetrievalIntent Intent,
    string? PreferredTopic = null,
    string? PreferredDomain = null,
    string? PreferredApiSurface = null);

public sealed record VectorSearchHit(
    long ChunkId,
    string DocumentId,
    string SourcePath,
    int ChunkIndex,
    string FileName,
    string? FileVersion,
    string? SectionTitle,
    string? ParentSection,
    string? Topic,
    string ChunkKind,
    string? DocumentFamily,
    string? CanonicalStatus,
    string? ParentFile,
    string? Domain,
    string? ApiSurface,
    string? CallStyle,
    string? MethodFamily,
    string? MethodNames,
    int SignatureCount,
    int Priority,
    int PrecedenceLevel,
    bool IsReferenceOnly,
    bool IsCanonical,
    bool IsSignatureCatalog,
    bool IsLegacyVariant,
    bool IsPlaceholder,
    bool RequiresGovernance,
    bool RequiresApplicationCore,
    string OriginalText,
    double Distance,
    double Score,
    double RerankedScore);

public interface IVectorStoreProvider
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task UpsertDocumentChunksAsync(
        IReadOnlyList<VectorChunkRecord> chunks,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);
}
