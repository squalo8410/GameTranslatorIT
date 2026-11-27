using System.Text.Json.Serialization;

namespace GameTranslatorIT.App.Services;

/// <summary>File gestito dall'aggiornamento differenziale.</summary>
public sealed record DeltaFileEntry
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "app";

    [JsonPropertyName("action")]
    public string Action { get; init; } = "replace";

    [JsonPropertyName("optional")]
    public bool Optional { get; init; }
}

/// <summary>Archivio contenente i soli file modificati (o il pacchetto completo di fallback).</summary>
public sealed record DeltaAsset
{
    [JsonPropertyName("asset")]
    public string? Asset { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }
}

/// <summary>Manifest versionato, firmato implicitamente dal suo file .sha256.</summary>
public sealed record DeltaDefinition
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("tag")]
    public string? Tag { get; init; }

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    [JsonPropertyName("releaseUrl")]
    public string? ReleaseUrl { get; init; }

    [JsonPropertyName("delta")]
    public DeltaAsset Delta { get; init; } = new();

    [JsonPropertyName("full")]
    public DeltaAsset Full { get; init; } = new();

    [JsonPropertyName("files")]
    public IReadOnlyList<DeltaFileEntry> Files { get; init; } = Array.Empty<DeltaFileEntry>();
}

/// <summary>Operazione effettiva da applicare, già verificata lato client.</summary>
public sealed record PlannedDeltaFile
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; init; } = "replace";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }
}

/// <summary>Piano scritto su disco e letto dall'updater esterno.</summary>
public sealed record DeltaApplyPlan
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = "preparing";

    [JsonPropertyName("files")]
    public IReadOnlyList<PlannedDeltaFile> Files { get; init; } = Array.Empty<PlannedDeltaFile>();
}

/// <summary>Risultato del tentativo differenziale. Applied=false significa "usa il full".</summary>
public sealed record DeltaApplyResult(bool Applied, string Message, int ChangedFiles, int SkippedFiles);
