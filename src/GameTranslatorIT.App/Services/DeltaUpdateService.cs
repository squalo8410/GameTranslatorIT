using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameTranslatorIT.App.Services;

/// <summary>
/// Aggiornamento file-per-file: confronta gli hash locali con il manifest pubblicato,
/// scarica solo l'archivio dei file cambiati e lo applica in place tramite updater esterno.
/// Se il manifest non c'è, segnala che si può usare il pacchetto completo.
/// </summary>
public sealed class DeltaUpdateService
{
    public async Task<DeltaApplyResult> ApplyAsync(
        UpdateInfo info,
        string? token,
        string? configuredManifestUrl,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var channel = UpdateService.DetectInstallChannel();
        if (channel == UpdateInstallChannel.Msix)
            return new DeltaApplyResult(false, "MSIX: aggiornamento differenziale non applicabile.", 0, 0);

        var (_, allowLocal) = UpdateService.ResolveApiUri();
        var manifestUrlRaw = Environment.GetEnvironmentVariable("GITUPDATER_MANIFEST_URL");
        var useConfigured = !string.IsNullOrWhiteSpace(manifestUrlRaw) ||
                            !string.IsNullOrWhiteSpace(configuredManifestUrl);
        if (string.IsNullOrWhiteSpace(manifestUrlRaw))
            manifestUrlRaw = configuredManifestUrl;

        Uri? manifestUri = null;
        UpdateAsset? manifestAsset = null;
        string? allowedHost = null;
        string? resolvedToken = null;

        if (useConfigured)
        {
            if (!Uri.TryCreate(manifestUrlRaw?.Trim(), UriKind.Absolute, out manifestUri) ||
                !IsAllowedConfiguredUri(manifestUri, allowLocal))
                throw new InvalidOperationException($"URL manifest non attendibile: {manifestUrlRaw}");

            // Il token viene inviato solo a host GitHub da DownloadAssetAsync.
            resolvedToken = UpdateService.ResolveToken(token);
            if (!IsGitHubHost(manifestUri.Host))
                allowedHost = manifestUri.Host; //host configurato esplicitamente

            manifestAsset = new UpdateAsset(
                Path.GetFileName(manifestUri.LocalPath),
                manifestUri.ToString(), null);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(info.ManifestAssetName))
                return new DeltaApplyResult(false,
                    "Manifest differenziale non pubblicato per questa versione/canale.", 0, 0);

            manifestAsset = info.AllAssets.FirstOrDefault(a =>
                string.Equals(a.Name, info.ManifestAssetName, StringComparison.OrdinalIgnoreCase));
            if (manifestAsset is null || !Uri.TryCreate(manifestAsset.Url, UriKind.Absolute, out manifestUri))
                return new DeltaApplyResult(false, "Manifest differenziale non scaricabile.", 0, 0);
            if (!UpdateService.IsAllowedAssetUri(manifestUri, allowLocal))
                throw new InvalidOperationException($"URL manifest non attendibile: {manifestAsset.Url}");

            resolvedToken = UpdateService.ResolveToken(token);
        }

        var channelName = channel.ToString().ToLowerInvariant();
        var workDirectory = Path.Combine(Path.GetTempPath(), "GameTranslatorIT", "Updates", info.Version);
        Directory.CreateDirectory(workDirectory);

        progress?.Report("Download del manifest aggiornamenti…");
        var manifestPath = await UpdateService.DownloadAssetAsync(
            manifestAsset, workDirectory, allowLocal, resolvedToken, progress, cancellationToken, allowedHost)
            .ConfigureAwait(false);

        progress?.Report("Verifica del manifest…");
        await VerifyWithAdjacentChecksumAsync(
            manifestAsset, manifestPath, workDirectory, allowLocal, resolvedToken, progress, cancellationToken, allowedHost)
            .ConfigureAwait(false);

        var definition = JsonSerializer.Deserialize<DeltaDefinition>(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidDataException("Manifest aggiornamenti vuoto.");

        if (definition.SchemaVersion != 1)
            throw new InvalidDataException($"Schema manifest non supportato: {definition.SchemaVersion}.");
        if (!string.Equals(definition.Version, info.Version, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Manifest della versione {definition.Version}, release {info.Version}: mancata corrispondenza.");
        if (!string.Equals(definition.Channel, channelName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Manifest per canale {definition.Channel}, installazione {channelName}.");

        var targetDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var planned = new List<PlannedDeltaFile>();
        var skipped = 0;

        progress?.Report("Confronto dei file locali con il manifest…");
        foreach (var entry in definition.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeRelativePath(entry.Path);
            var localPath = Path.Combine(targetDirectory, relative);

            if (string.Equals(entry.Action, "delete", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(localPath))
                {
                    skipped++;
                    continue;
                }
                var localHash = await ComputeSha256Async(localPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(localHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue; //non cancelliamo mai un file modificato dall'utente
                }
                planned.Add(new PlannedDeltaFile { Path = relative, Action = "delete", Sha256 = entry.Sha256, Size = entry.Size });
                continue;
            }

            if (File.Exists(localPath))
            {
                var localHash = await ComputeSha256Async(localPath, cancellationToken).ConfigureAwait(false);
                var localSize = new FileInfo(localPath).Length;
                if (string.Equals(localHash, entry.Sha256, StringComparison.OrdinalIgnoreCase) &&
                    (entry.Size <= 0 || localSize == entry.Size))
                {
                    skipped++;
                    continue;
                }
            }
            else if (entry.Optional)
            {
                skipped++;
                continue;
            }

            planned.Add(new PlannedDeltaFile { Path = relative, Action = "replace", Sha256 = entry.Sha256, Size = entry.Size });
        }

        progress?.Report($"{planned.Count} file da sostituire, {skipped} già aggiornati.");

        var payloadDirectory = Path.Combine(workDirectory, "payload");
        if (Directory.Exists(payloadDirectory)) Directory.Delete(payloadDirectory, recursive: true);
        Directory.CreateDirectory(payloadDirectory);

        var replacements = planned.Where(p => p.Action == "replace").ToList();
        if (replacements.Count > 0)
        {
            var deltaAsset = ResolveDeltaAsset(definition, info, allowedHost);
            if (deltaAsset is null)
                throw new InvalidDataException("L'archivio differenziale dichiarato nel manifest non è presente nella release.");

            progress?.Report("Download dei soli file modificati…");
            var zipPath = await UpdateService.DownloadAssetAsync(
                deltaAsset, workDirectory, allowLocal,
                resolvedToken, progress, cancellationToken, allowedHost)
                .ConfigureAwait(false);

            var actual = await ComputeSha256Async(zipPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, definition.Delta.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA256 dell'archivio differenziale non valido.");
            if (definition.Delta.Size > 0 && new FileInfo(zipPath).Length != definition.Delta.Size)
                throw new InvalidDataException("Dimensione dell'archivio differenziale non valida.");

            progress?.Report("Verifica di ogni file scaricato…");
            ZipFile.ExtractToDirectory(zipPath, payloadDirectory, overwriteFiles: true);
            foreach (var file in replacements)
            {
                var extracted = Path.Combine(payloadDirectory, file.Path);
                if (!File.Exists(extracted))
                    throw new InvalidDataException($"File mancante dall'archivio differenziale: {file.Path}");
                var hash = await ComputeSha256Async(extracted, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    (file.Size > 0 && new FileInfo(extracted).Length != file.Size))
                    throw new InvalidDataException($"File corrotto nell'archivio differenziale: {file.Path}");
            }
        }

        var plan = new DeltaApplyPlan
        {
            Version = info.Version,
            Channel = channelName,
            Status = "preparing",
            Files = planned
        };
        var planPath = Path.Combine(workDirectory, "apply-plan.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan), cancellationToken).ConfigureAwait(false);

        progress?.Report($"Applicazione di {planned.Count} file nella cartella esistente…");
        var scriptPath = ApplyDeltaScript.Write(workDirectory);
        var logPath = Path.Combine(workDirectory, "apply-delta.log");
        if (File.Exists(logPath)) File.Delete(logPath);

        var restartPath = Environment.ProcessPath
            ?? Path.Combine(targetDirectory, "GameTranslatorIT.App.exe");
        ApplyDeltaScript.Start(scriptPath, planPath, targetDirectory, restartPath,
            Environment.ProcessId, logPath, info.Version, info.ReleaseUrl);

        return new DeltaApplyResult(true,
            $"Aggiornamento differenziale avviato: {planned.Count} file, {skipped} invariati.",
            planned.Count, skipped);
    }

    private static UpdateAsset? ResolveDeltaAsset(
        DeltaDefinition definition,
        UpdateInfo info,
        string? allowedHost)
    {
        UpdateAsset? asset = null;
        if (!string.IsNullOrWhiteSpace(definition.Delta.Asset))
        {
            asset = info.AllAssets.FirstOrDefault(a =>
                string.Equals(a.Name, definition.Delta.Asset, StringComparison.OrdinalIgnoreCase));
        }

        if (asset is null && !string.IsNullOrWhiteSpace(definition.Delta.Url) &&
            Uri.TryCreate(definition.Delta.Url, UriKind.Absolute, out var uri))
        {
            if (!UpdateService.IsAllowedAssetUri(uri, false, allowedHost))
                throw new InvalidOperationException($"URL archivio differenziale non attendibile: {uri}");
            asset = new UpdateAsset(Path.GetFileName(uri.LocalPath), uri.ToString(), definition.Delta.Size);
        }

        return asset;
    }

    private static async Task VerifyWithAdjacentChecksumAsync(
        UpdateAsset asset,
        string assetPath,
        string workDirectory,
        bool allowLocal,
        string? token,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        string? allowedHost)
    {
        var checksumUri = new Uri(asset.Url + ".sha256");
        if (!UpdateService.IsAllowedAssetUri(checksumUri, allowLocal, allowedHost))
            throw new InvalidOperationException($"URL checksum manifest non attendibile: {checksumUri}");

        var checksumAsset = new UpdateAsset(asset.Name + ".sha256", checksumUri.ToString(), null);
        var checksumPath = await UpdateService.DownloadAssetAsync(
            checksumAsset, workDirectory, allowLocal,
            token, progress, cancellationToken, allowedHost).ConfigureAwait(false);
        await UpdateService.VerifySha256Async(assetPath, checksumPath, null).ConfigureAwait(false);
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Percorso file manifest vuoto.");
        if (Path.IsPathRooted(path) || path.Contains(':'))
            throw new InvalidDataException($"Percorso non relativo nel manifest: {path}");

        var normalized = path.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s == "." || s == ".."))
            throw new InvalidDataException($"Percorso non valido nel manifest: {path}");

        return Path.Combine(segments);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        using var algorithm = SHA256.Create();
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await algorithm.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static bool IsAllowedConfiguredUri(Uri uri, bool allowLocal)
    {
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        return allowLocal && uri.Scheme == Uri.UriSchemeHttp && IsLoopback(uri.Host);
    }

    private static bool IsGitHubHost(string host) =>
        host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        (System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address));
}
