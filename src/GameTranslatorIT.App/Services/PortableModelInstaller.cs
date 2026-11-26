using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameTranslatorIT.App.Services;

/// <summary>
/// Le edizioni portable possono contenere i modelli Argos già estratti oppure
/// riceverli come archivi separati (pubblicati in GitHub Releases perché da soli
/// superano il limite di 2 GB per asset). Questa classe ricompone gli archivi
/// nella cartella Models dell'app, verificando SHA256 prima dell'estrazione.
/// </summary>
public static class PortableModelInstaller
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private sealed record ManifestPart(string File, string Sha256, IReadOnlyList<string> Packages);

    private sealed record Manifest(
        string Version,
        int PackageCount,
        IReadOnlyList<ManifestPart> Parts);

    public static async Task EnsureReadyAsync(Action<string> log, CancellationToken cancellationToken = default)
    {
        var baseDirectory = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var modelsDirectory = Path.Combine(baseDirectory, "Models");
        var manifestPath = Path.Combine(modelsDirectory, "models-manifest.json");
        if (!File.Exists(manifestPath)) return;

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = ReadManifest(manifestPath);
            if (manifest is null) return;

            var currentVersion = UpdateService.CurrentVersion;
            if (!string.Equals(manifest.Version, currentVersion, StringComparison.OrdinalIgnoreCase))
            {
                log($"Archivi modelli per versione {manifest.Version}: attendere l'aggiornamento dell'app.");
                return;
            }

            var packagesDirectory = Path.Combine(modelsDirectory, "argos-translate", "packages");
            var markerPath = Path.Combine(modelsDirectory, ".models-ready");
            if (IsReady(markerPath, packagesDirectory, manifest, currentVersion)) return;

            var alreadyPresent = Directory.Exists(packagesDirectory)
                && Directory.GetDirectories(packagesDirectory).Length >= manifest.PackageCount;
            if (alreadyPresent)
            {
                await WriteMarkerAsync(markerPath, currentVersion,
                    Directory.GetDirectories(packagesDirectory).Length, cancellationToken).ConfigureAwait(false);
                log("Modelli già inclusi nella portable: nessun download richiesto.");
                return;
            }

            if (manifest.Parts.Count == 0)
            {
                log("Nessun archivio modello dichiarato nella portable.");
                return;
            }

            var searchDirectories = new List<string> { baseDirectory };
            var parent = Directory.GetParent(baseDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent) && !searchDirectories.Contains(parent))
                searchDirectories.Add(parent);

            var resolved = new List<(ManifestPart Part, string Path)>();
            var missing = 0;
            foreach (var part in manifest.Parts)
            {
                var path = searchDirectories
                    .Select(directory => Path.Combine(directory, part.File))
                    .FirstOrDefault(File.Exists);
                if (path is null)
                {
                    missing++;
                    continue;
                }
                resolved.Add((part, path));
            }

            if (missing > 0)
            {
                log($"Modelli portable incompleti: mancano {missing} di {manifest.Parts.Count} archivi. " +
                    "Scarica tutti i file GameTranslatorIT-Models dalla release e riavvia.");
                return;
            }

            foreach (var (part, path) in resolved)
            {
                if (!string.IsNullOrWhiteSpace(part.Sha256))
                {
                    var actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(actual, part.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        log($"Archivio {part.File} non valido (SHA256 diverso): estrazione annullata.");
                        return;
                    }
                }
            }

            Directory.CreateDirectory(packagesDirectory);
            foreach (var (part, path) in resolved)
            {
                log($"Estrazione modelli da {part.File}...");
                await Task.Run(
                    () => ZipFile.ExtractToDirectory(path, packagesDirectory, overwriteFiles: true),
                    cancellationToken).ConfigureAwait(false);
            }

            var installed = Directory.Exists(packagesDirectory)
                ? Directory.GetDirectories(packagesDirectory).Length
                : 0;
            if (installed < manifest.PackageCount)
            {
                log($"Estrazione incompleta: {installed} pacchetti su {manifest.PackageCount} attesi.");
                return;
            }

            await WriteMarkerAsync(markerPath, currentVersion, installed, cancellationToken).ConfigureAwait(false);
            log($"Modelli portable pronti: {installed} pacchetti, nessun download richiesto.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Arresto dell'app: lo stato verrà verificato al prossimo avvio.
        }
        catch (Exception ex)
        {
            log($"Preparazione modelli portable non riuscita: {ex.Message}");
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task WriteMarkerAsync(
        string markerPath,
        string version,
        int packages,
        CancellationToken cancellationToken)
    {
        var marker = new
        {
            version,
            packages,
            installedAt = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(
            markerPath,
            JsonSerializer.Serialize(marker),
            cancellationToken).ConfigureAwait(false);
    }

    private static Manifest? ReadManifest(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var version = root.GetProperty("version").GetString() ?? string.Empty;
            var packageCount = root.TryGetProperty("packageCount", out var countElement)
                ? countElement.GetInt32()
                : 0;
            var parts = new List<ManifestPart>();
            if (root.TryGetProperty("parts", out var partsElement) &&
                partsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in partsElement.EnumerateArray())
                {
                    var file = item.GetProperty("file").GetString();
                    if (string.IsNullOrWhiteSpace(file)) continue;
                    var sha = item.TryGetProperty("sha256", out var shaElement)
                        ? shaElement.GetString() ?? string.Empty
                        : string.Empty;
                    var packages = new List<string>();
                    if (item.TryGetProperty("packages", out var packagesElement) &&
                        packagesElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var package in packagesElement.EnumerateArray())
                        {
                            var name = package.GetString();
                            if (!string.IsNullOrWhiteSpace(name)) packages.Add(name);
                        }
                    }
                    parts.Add(new ManifestPart(file, sha, packages));
                }
            }
            return new Manifest(version, packageCount, parts);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsReady(string markerPath, string packagesDirectory, Manifest manifest, string version)
    {
        if (!File.Exists(markerPath) || !Directory.Exists(packagesDirectory)) return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(markerPath));
            var root = document.RootElement;
            var markedVersion = root.GetProperty("version").GetString() ?? string.Empty;
            var markedCount = root.GetProperty("packages").GetInt32();
            var actualCount = Directory.GetDirectories(packagesDirectory).Length;
            return string.Equals(markedVersion, version, StringComparison.OrdinalIgnoreCase)
                && markedCount >= manifest.PackageCount
                && actualCount >= manifest.PackageCount;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        using var algorithm = SHA256.Create();
        await using var stream = File.OpenRead(path);
        var hash = await algorithm.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
