namespace GameTranslatorIT.App.Services;

/// <summary>Asset di una release GitHub utilizzabile per l'aggiornamento.</summary>
public sealed record UpdateAsset(string Name, string Url, long? Size);

/// <summary>Informazioni sulla versione disponibile, già filtrate e validate.</summary>
public sealed record UpdateInfo(
    string Version,
    string Tag,
    string Title,
    string? Notes,
    DateTimeOffset? PublishedAt,
    string ReleaseUrl,
    UpdateAsset Asset,
    string ChecksumAssetName,
    IReadOnlyList<UpdateAsset> AllAssets)
{
    /// <summary>Asset manifest per l'aggiornamento differenziale, se pubblicato.</summary>
    public string? ManifestAssetName { get; init; }
}

/// <summary>Esito di un controllo versione. Gli errori non interrompono l'app.</summary>
public sealed record UpdateCheckResult(
    bool Success,
    UpdateInfo? AvailableUpdate,
    string StatusMessage,
    string? ErrorMessage);

public enum UpdateInstallChannel
{
    /// <summary>Installer Inno Setup: aggiornamento silenzato con conservazione cartella.</summary>
    Installer,

    /// <summary>Copia portable: sostituzione cartella con backup e rollback.</summary>
    Portable,

    /// <summary>Pacchetto MSIX: Add-AppxPackage con ForceUpdateFromAnyVersion.</summary>
    Msix
}
