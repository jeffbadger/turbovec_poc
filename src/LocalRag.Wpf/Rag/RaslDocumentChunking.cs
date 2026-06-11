using System.IO;
using System.Text.RegularExpressions;
using LocalRag.Wpf.VectorStore;

namespace LocalRag.Wpf.Rag;

public sealed record RaslDocumentMetadata(
    RaslDocumentFamily Family,
    string? FileName,
    string? Version,
    string? Section,
    string? Parent,
    string? Subfiles,
    string? Status,
    string CanonicalStatus,
    int PrecedenceLevel,
    bool IsPlaceholder,
    string? Domain,
    string? ApiSurface,
    string? CallStyle);

public static class RaslDocumentClassifier
{
    public static RaslDocumentMetadata Classify(string sourcePath, string text)
    {
        var header = ParseHeaders(text);
        var fileName = header.GetValueOrDefault("FILE") ?? Path.GetFileNameWithoutExtension(sourcePath);
        var section = header.GetValueOrDefault("SECTION");
        var parent = header.GetValueOrDefault("PARENT");
        var status = header.GetValueOrDefault("STATUS");
        var family = DetectFamily(sourcePath, text, fileName, section, parent);
        return new RaslDocumentMetadata(
            family,
            fileName,
            header.GetValueOrDefault("VERSION"),
            section,
            parent,
            header.GetValueOrDefault("SUBFILES"),
            status,
            CanonicalStatusFor(family),
            PrecedenceFor(family),
            status?.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) == true,
            DetectDomain(fileName, text),
            DetectApiSurface(family, fileName, text),
            DetectCallStyle(family));
    }

    private static Dictionary<string, string> ParseHeaders(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(text, @"^////\s*(FILE|VERSION|SECTION|PARENT|SUBFILES|STATUS):\s*(.+?)\s*$", RegexOptions.Multiline))
        {
            values[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        }

        return values;
    }

    private static RaslDocumentFamily DetectFamily(string sourcePath, string text, string fileName, string? section, string? parent)
    {
        if (text.Contains("//// FILE: RASLPrompt_1_Governance", StringComparison.OrdinalIgnoreCase)) return RaslDocumentFamily.GovernancePrompt;
        if (text.Contains("//// FILE: RASLPrompt_4_Applications", StringComparison.OrdinalIgnoreCase) && section?.Contains("Application-general rules", StringComparison.OrdinalIgnoreCase) == true) return RaslDocumentFamily.ApplicationCorePrompt;
        if (text.Contains("//// PARENT: RASLPrompt_4_Applications", StringComparison.OrdinalIgnoreCase)) return RaslDocumentFamily.ApplicationDomainPrompt;
        if (text.Contains("GENERATED FILE", StringComparison.OrdinalIgnoreCase) && text.Contains("declare namespace", StringComparison.OrdinalIgnoreCase)) return RaslDocumentFamily.ToolboxCatalog;
        if (text.Contains("//// FILE: RASLPrompt_", StringComparison.OrdinalIgnoreCase) && (text.Contains("supported signatures", StringComparison.OrdinalIgnoreCase) || text.Contains("method signatures", StringComparison.OrdinalIgnoreCase))) return RaslDocumentFamily.MethodCatalogPrompt;
        if (text.Contains("//// FILE: RASLPrompt_", StringComparison.OrdinalIgnoreCase) && text.Contains("# Hard Rules", StringComparison.OrdinalIgnoreCase)) return RaslDocumentFamily.TopicRulePrompt;
        if (!text.Contains("//// FILE:", StringComparison.OrdinalIgnoreCase) && fileName.Contains("Applications", StringComparison.OrdinalIgnoreCase))
        {
            return fileName.Contains("Goal", StringComparison.OrdinalIgnoreCase) || fileName.Contains("Modify", StringComparison.OrdinalIgnoreCase)
                ? RaslDocumentFamily.ScenarioPromptVariant
                : RaslDocumentFamily.LegacyPromptVariant;
        }

        return RaslDocumentFamily.Unknown;
    }

    public static string CanonicalStatusFor(RaslDocumentFamily family) => family switch
    {
        RaslDocumentFamily.GovernancePrompt => "canonical",
        RaslDocumentFamily.ApplicationCorePrompt => "canonical_topic_core",
        RaslDocumentFamily.ApplicationDomainPrompt => "supplemental_domain",
        RaslDocumentFamily.TopicRulePrompt => "supplemental",
        RaslDocumentFamily.MethodCatalogPrompt => "canonical_signature_catalog",
        RaslDocumentFamily.ToolboxCatalog => "canonical_toolbox_catalog",
        RaslDocumentFamily.LegacyPromptVariant => "legacy",
        RaslDocumentFamily.ScenarioPromptVariant => "scenario_variant",
        _ => "unknown"
    };

    public static int PrecedenceFor(RaslDocumentFamily family) => family switch
    {
        RaslDocumentFamily.GovernancePrompt => 100,
        RaslDocumentFamily.ApplicationDomainPrompt => 90,
        RaslDocumentFamily.ApplicationCorePrompt => 85,
        RaslDocumentFamily.TopicRulePrompt => 80,
        RaslDocumentFamily.MethodCatalogPrompt => 80,
        RaslDocumentFamily.ToolboxCatalog => 80,
        RaslDocumentFamily.ScenarioPromptVariant => 50,
        RaslDocumentFamily.LegacyPromptVariant => 30,
        _ => 10
    };

    private static string? DetectDomain(string fileName, string text)
    {
        foreach (var domain in new[] { "ExcelConnector", "Windows", "Web", "Text" })
        {
            if (fileName.Contains(domain, StringComparison.OrdinalIgnoreCase) || text.Contains(domain, StringComparison.OrdinalIgnoreCase)) return domain;
        }

        if (fileName.Contains("Excel", StringComparison.OrdinalIgnoreCase)) return "ExcelConnector";
        return null;
    }

    private static string? DetectApiSurface(RaslDocumentFamily family, string fileName, string text)
    {
        if (family == RaslDocumentFamily.ToolboxCatalog)
        {
            var ns = Regex.Match(text, @"declare\s+namespace\s+(\w+)", RegexOptions.IgnoreCase).Groups[1].Value;
            return string.IsNullOrWhiteSpace(ns) ? "Toolbox.String" : $"Toolbox.{ns}";
        }

        if (fileName.Contains("StringVariable", StringComparison.OrdinalIgnoreCase) || text.Contains("StringVariable", StringComparison.OrdinalIgnoreCase)) return "StringVariable";
        return null;
    }

    private static string? DetectCallStyle(RaslDocumentFamily family) => family switch
    {
        RaslDocumentFamily.MethodCatalogPrompt => "instance_method",
        RaslDocumentFamily.ToolboxCatalog => "static_toolbox_function",
        _ => null
    };
}

public sealed class RaslDocumentChunker
{
    public IReadOnlyList<VectorChunkRecord> CreateUnembeddedChunks(string sourcePath, string text, DateTimeOffset modifiedUtc, string? contentHash)
    {
        var metadata = RaslDocumentClassifier.Classify(sourcePath, text);
        var sections = SplitMarkdownSections(text);
        var records = new List<VectorChunkRecord>();
        var index = 0;
        foreach (var section in sections)
        {
            var original = section.Text.Trim();
            if (string.IsNullOrWhiteSpace(original)) continue;
            var kind = InferChunkKind(metadata.Family, section.Title, original);
            var methods = ExtractMethodNames(metadata, original);
            var priority = InferPriority(metadata, kind, section.Title, original);
            var isSignature = kind is ChunkKind.SignatureCatalog or ChunkKind.ToolboxSignatureCatalog;
            var record = new VectorChunkRecord(
                sourcePath,
                sourcePath,
                index++,
                metadata.FileName ?? Path.GetFileName(sourcePath),
                metadata.Version,
                section.Title ?? metadata.Section,
                metadata.Section,
                InferTopic(metadata, section.Title, original),
                kind.ToString(),
                metadata.Family.ToString(),
                metadata.CanonicalStatus,
                metadata.Parent,
                metadata.Domain,
                metadata.ApiSurface,
                metadata.CallStyle,
                InferMethodFamily(methods, original),
                string.Join(",", methods),
                CountSignatures(metadata, original),
                priority,
                metadata.PrecedenceLevel,
                IsReferenceOnly(section.Title, original),
                metadata.Family == RaslDocumentFamily.GovernancePrompt || kind is ChunkKind.Rule or ChunkKind.Validation or ChunkKind.SignatureCatalog or ChunkKind.ToolboxSignatureCatalog,
                isSignature,
                metadata.Family is RaslDocumentFamily.LegacyPromptVariant or RaslDocumentFamily.ScenarioPromptVariant,
                metadata.IsPlaceholder,
                metadata.Family is RaslDocumentFamily.ApplicationCorePrompt or RaslDocumentFamily.ApplicationDomainPrompt,
                metadata.Family is RaslDocumentFamily.ApplicationDomainPrompt,
                original,
                BuildEmbeddingText(metadata, section.Title, kind, original, methods, priority),
                Array.Empty<float>(),
                modifiedUtc,
                contentHash);
            records.Add(record);
        }

        return records;
    }

    private static List<(string? Title, string Text)> SplitMarkdownSections(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Trim();
        var matches = Regex.Matches(normalized, @"(?m)^#{1,4}\s+.+$").Cast<Match>().ToList();
        if (matches.Count == 0) return new() { (null, normalized) };
        var result = new List<(string?, string)>();
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : normalized.Length;
            var block = normalized[start..end].Trim();
            result.Add((matches[i].Value.Trim('#', ' ', '\t'), block));
        }

        return result;
    }

    private static ChunkKind InferChunkKind(RaslDocumentFamily family, string? title, string text)
    {
        if (family == RaslDocumentFamily.GovernancePrompt) return ChunkKind.GovernanceRule;
        if (family == RaslDocumentFamily.ToolboxCatalog) return ChunkKind.ToolboxSignatureCatalog;
        if (family == RaslDocumentFamily.MethodCatalogPrompt && Regex.IsMatch(text, @"\w+\s*\([^)]*\)\s*;|\]\.\w+\s*\(", RegexOptions.IgnoreCase)) return ChunkKind.SignatureCatalog;
        var t = title ?? string.Empty;
        if (t.Contains("Validation", StringComparison.OrdinalIgnoreCase)) return ChunkKind.Validation;
        if (t.Contains("Fallback", StringComparison.OrdinalIgnoreCase)) return ChunkKind.FallbackRule;
        if (t.Contains("Style", StringComparison.OrdinalIgnoreCase)) return ChunkKind.StyleGuidance;
        if (t.Contains("Example", StringComparison.OrdinalIgnoreCase) && text.Contains("incorrect", StringComparison.OrdinalIgnoreCase)) return ChunkKind.AntiExample;
        if (t.Contains("Template", StringComparison.OrdinalIgnoreCase)) return ChunkKind.Template;
        if (t.Contains("Example", StringComparison.OrdinalIgnoreCase)) return ChunkKind.Example;
        if (t.Contains("Hard Rules", StringComparison.OrdinalIgnoreCase) || text.Contains("MUST", StringComparison.OrdinalIgnoreCase)) return ChunkKind.Rule;
        return family is RaslDocumentFamily.LegacyPromptVariant or RaslDocumentFamily.ScenarioPromptVariant ? ChunkKind.LegacyReference : ChunkKind.Reference;
    }

    private static bool IsReferenceOnly(string? title, string text) =>
        (title?.Contains("REFERENCE-ONLY", StringComparison.OrdinalIgnoreCase) == true) || text.Contains("REFERENCE-ONLY", StringComparison.OrdinalIgnoreCase);

    private static int InferPriority(RaslDocumentMetadata metadata, ChunkKind kind, string? title, string text)
    {
        if (metadata.Family == RaslDocumentFamily.GovernancePrompt) return 10;
        if (kind is ChunkKind.Validation or ChunkKind.SignatureCatalog or ChunkKind.ToolboxSignatureCatalog) return 10;
        if (kind == ChunkKind.Rule) return 8;
        var haystack = (title ?? string.Empty) + "\n" + text;
        foreach (var term in new[] { "WaitForCreate", "waitOn", "PerformClick2", "Globals", "Open", "RadioButton", "Checkbox", "Split", "GeneratePassword" })
        {
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase)) return 10;
        }
        return 0;
    }

    private static string? InferTopic(RaslDocumentMetadata metadata, string? title, string text)
    {
        if (!string.IsNullOrWhiteSpace(metadata.Domain)) return metadata.Domain;
        var source = (title ?? metadata.FileName ?? text).ToLowerInvariant();
        if (source.Contains("entrypoint")) return "EntryPoint";
        if (source.Contains("label")) return "Label";
        if (source.Contains("loop")) return "Loop";
        if (source.Contains("comparison")) return "Comparison";
        if (source.Contains("stringvariable") || source.Contains("split")) return "StringVariable";
        return metadata.FileName;
    }

    private static IReadOnlyList<string> ExtractMethodNames(RaslDocumentMetadata metadata, string text)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(text, metadata.Family == RaslDocumentFamily.ToolboxCatalog ? @"(?:\bString\.|\bfunction\s+)?([A-Z]\w*)\s*\(" : @"\.([A-Z]\w*)\s*\(|\b([A-Z]\w*)\s*\([^)]*\)\s*;"))
        {
            var name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
        }
        return names.OrderBy(x => x).ToList();
    }

    private static int CountSignatures(RaslDocumentMetadata metadata, string text) => metadata.Family switch
    {
        RaslDocumentFamily.ToolboxCatalog => Regex.Matches(text, @"(?:\bString\.|\bfunction\s+)?[A-Z]\w*\s*\(").Count,
        RaslDocumentFamily.MethodCatalogPrompt => Regex.Matches(text, @"\.\w+\s*\(|\w+\s*\([^)]*\)\s*;").Count,
        _ => 0
    };

    private static string? InferMethodFamily(IReadOnlyList<string> methods, string text)
    {
        var joined = string.Join(' ', methods) + " " + text;
        if (joined.Contains("Split", StringComparison.OrdinalIgnoreCase) || joined.Contains("Join", StringComparison.OrdinalIgnoreCase)) return "splitJoin";
        if (joined.Contains("GeneratePassword", StringComparison.OrdinalIgnoreCase)) return "password";
        if (joined.Contains("Contains", StringComparison.OrdinalIgnoreCase)) return "containment";
        if (joined.Contains("Trim", StringComparison.OrdinalIgnoreCase) || joined.Contains("Pad", StringComparison.OrdinalIgnoreCase)) return "paddingCasingTrim";
        return methods.Count > 0 ? "signature" : null;
    }

    private static string BuildEmbeddingText(RaslDocumentMetadata metadata, string? section, ChunkKind kind, string original, IReadOnlyList<string> methods, int priority) =>
        $"Source: {metadata.FileName}\nVersion: {metadata.Version}\nDocumentFamily: {metadata.Family}\nCanonicalStatus: {metadata.CanonicalStatus}\nParentFile: {metadata.Parent}\nDomain: {metadata.Domain}\nApiSurface: {metadata.ApiSurface}\nCallStyle: {metadata.CallStyle}\nKind: {kind}\nTopic: {metadata.Domain ?? metadata.FileName}\nSection: {section}\nParentSection: {metadata.Section}\nMethodFamily: {InferMethodFamily(methods, original)}\nMethods: {string.Join(',', methods)}\nSignatureCount: {CountSignatures(metadata, original)}\nPriority: {priority}\nPrecedenceLevel: {metadata.PrecedenceLevel}\n\n{original}";
}

public static class RaslDocumentChunkerFactory
{
    public static RaslDocumentChunker Create() => new();
}
