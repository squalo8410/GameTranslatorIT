using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameTranslatorIT.App.Services;

/// <summary>
/// Controlla GitHub Releases, scarica gli asset e verifica l'integrità prima di applicare
/// l'aggiornamento. Non dipende dal provider di traduzione e non blocca la pipeline.
/// </summary>
public sealed class UpdateService
{
    private const string DefaultOwner = "squalo8410";
    private const string DefaultRepo = "GameTranslatorIT";
    private const string DefaultApiUrl = $"https://api.github.com/repos/{DefaultOwner}/{DefaultRepo}/releases/latest";
    private const string UserAgent = "GameTranslatorIT-Updater/1.0";
    private const int ApiTimeoutSeconds = 20;
    private const int DownloadTimeoutMinutes = 15;

    public static string CurrentVersion
    {
        get
        {
            var raw = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            return TryParseVersion(raw, out var version) ? version.ToString(3) : "1.0.0";
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (apiUri, allowLocal) = ResolveApiUri();
            using var client = CreateClient(TimeSpan.FromSeconds(ApiTimeoutSeconds), allowLocal);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Trim());

            using var response = await client.GetAsync(apiUri, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized =>
                        "GitHub ha rifiutato GITHUB_TOKEN. Controlla che il token sia valido e abbia accesso al repository privato.",
                    HttpStatusCode.NotFound =>
                        "Repository o release non trovato. Per un repository privato imposta la variabile GITHUB_TOKEN prima di avviare l'app.",
                    HttpStatusCode.Forbidden =>
                        "GitHub nega l'accesso alla release. Controlla i permessi del token GITHUB_TOKEN.",
                    _ => $"GitHub ha risposto con {(int)response.StatusCode} {response.StatusCode}."
                };
                return new UpdateCheckResult(false, null, "Verifica non riuscita", message);
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var isDraft = root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True;
            var isPrerelease = root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True;
            if (isDraft || isPrerelease)
                return new UpdateCheckResult(true, null, "Nessuna versione stabile pubblicata", null);

            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            if (!TryParseVersion(tag, out var remoteVersion))
                return new UpdateCheckResult(false, null, "Verifica non riuscita", $"Tag GitHub non valido: '{tag}'.");

            // Usa la stessa normalizzazione a 4 componenti di TryParseVersion: new Version("1.0.3")
            // ha Revision = -1 e verrebbe considerato inferiore a 1.0.3.0, mostrando un falso aggiornamento.
            var current = TryParseVersion(CurrentVersion, out var parsedCurrent) ? parsedCurrent : new Version(0, 0, 0, 0);
            if (remoteVersion <= current)
                return new UpdateCheckResult(true, null,
                    $"Nessun aggiornamento disponibile (versione installata {current.ToString(3)}).", null);

            var assets = ParseAssets(root, allowLocal);
            var selected = SelectAsset(assets, DetectInstallChannel());
            if (selected is null)
                return new UpdateCheckResult(false, null, "Verifica non riuscita",
                    "Nella release non è presente alcun asset GameTranslatorIT supportato (EXE, ZIP o MSIX).");

            var checksumName = selected.Name + ".sha256";
            if (!assets.Any(a => string.Equals(a.Name, checksumName, StringComparison.OrdinalIgnoreCase)))
                return new UpdateCheckResult(false, null, "Verifica non riuscita",
                    $"Manca il file di integrità '{checksumName}' nella release.");

            var info = new UpdateInfo(
                remoteVersion.ToString(3),
                tag ?? remoteVersion.ToString(3),
                root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? $"Versione {remoteVersion.ToString(3)}" : $"Versione {remoteVersion.ToString(3)}",
                root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() : null,
                TryPublishedAt(root),
                root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() ?? DefaultApiUrl : DefaultApiUrl,
                selected,
                checksumName,
                assets);

            return new UpdateCheckResult(true, info, $"Nuova versione {info.Version} disponibile.", null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(false, null, "Verifica annullata", null);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, null, "Verifica non riuscita",
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public async Task DownloadAndApplyAsync(UpdateInfo info, IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        var (_, allowLocal) = ResolveApiUri();
        var workDirectory = Path.Combine(Path.GetTempPath(), "GameTranslatorIT", "Updates", info.Version);
        Directory.CreateDirectory(workDirectory);

        progress?.Report("Download dell'aggiornamento in corso...");
        var assetPath = await DownloadAsync(info.Asset, workDirectory, allowLocal, progress, cancellationToken).ConfigureAwait(false);

        var checksumAsset = info.AllAssets.FirstOrDefault(a =>
            string.Equals(a.Name, info.ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Checksum '{info.ChecksumAssetName}' non trovato nella release.");

        progress?.Report("Download del checksum in corso...");
        var checksumPath = await DownloadAsync(checksumAsset, workDirectory, allowLocal, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report("Verifica SHA256 del file scaricato...");
        var expectedHash = await VerifySha256Async(assetPath, checksumPath, info.Asset.Size).ConfigureAwait(false);

        var channel = DetectInstallChannel();
        var configuredThumbprint = Environment.GetEnvironmentVariable("GITUPDATER_CERT_THUMBPRINT");
        var verifySignature = !string.IsNullOrWhiteSpace(configuredThumbprint) && channel != UpdateInstallChannel.Portable;

        if (verifySignature)
        {
            progress?.Report("Verifica della firma digitale...");
            await VerifyAuthenticodeAsync(assetPath, configuredThumbprint!, workDirectory, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report($"Preparazione aggiornamento ({ChannelLabel(channel)})...");
        var scriptPath = WriteApplyScript(workDirectory);
        var logPath = Path.Combine(workDirectory, "apply-update.log");
        if (File.Exists(logPath)) File.Delete(logPath);

        var restartPath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "GameTranslatorIT.App.exe");
        var targetDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        StartApplyScript(
            scriptPath,
            channel.ToString().ToLowerInvariant(),
            assetPath,
            targetDirectory,
            restartPath,
            Environment.ProcessId,
            info.ReleaseUrl,
            logPath,
            expectedHash,
            verifySignature ? configuredThumbprint! : string.Empty);

        progress?.Report("Aggiornamento avviato. L'app si chiuderà e si riavvierà da sola.");
    }

    private static async Task<string> DownloadAsync(
        UpdateAsset asset,
        string workDirectory,
        bool allowLocal,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri) || !IsAllowedAssetUri(uri, allowLocal))
            throw new InvalidOperationException($"URL asset non attendibile: {asset.Url}");

        var path = Path.Combine(workDirectory, Path.GetFileName(asset.Name));
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(DownloadTimeoutMinutes) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var finalUri = response.RequestMessage?.RequestUri ?? uri;
        if (!IsAllowedAssetUri(finalUri, allowLocal))
            throw new InvalidOperationException($"Redirection non attendibile: {finalUri}");

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Download di {asset.Name} non riuscito: {(int)response.StatusCode} {response.StatusCode}.");

        var total = response.Content.Headers.ContentLength ?? asset.Size;
        if (asset.Size.HasValue && total.HasValue && total.Value != asset.Size.Value)
            throw new InvalidDataException($"Dimensione dichiarata per {asset.Name} non coerente con GitHub.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(path);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            if (total is > 0)
                progress?.Report($"Download {asset.Name}: {Math.Min(100, received * 100 / total.Value)}%");
        }
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (asset.Size.HasValue && new FileInfo(path).Length != asset.Size.Value)
            throw new InvalidDataException($"File {asset.Name} incompleto: dimensione diversa da quella pubblicata.");

        return path;
    }

    private static async Task<string> VerifySha256Async(string filePath, string checksumPath, long? expectedSize)
    {
        var checksumText = await File.ReadAllTextAsync(checksumPath).ConfigureAwait(false);
        var hash = checksumText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(hash))
            throw new InvalidDataException("Il file .sha256 pubblicato è vuoto o non valido.");

        var algorithm = SHA256.Create();
        await using (var stream = File.OpenRead(filePath))
        {
            var computed = Convert.ToHexString(await algorithm.ComputeHashAsync(stream).ConfigureAwait(false));
            if (!string.Equals(computed, hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA256 non valido. Atteso {hash}, calcolato {computed}.");
        }

        if (expectedSize.HasValue && new FileInfo(filePath).Length != expectedSize.Value)
            throw new InvalidDataException("Il file scaricato ha dimensione diversa da quella pubblicata.");

        return hash.ToUpperInvariant();
    }

    private static async Task VerifyAuthenticodeAsync(
        string filePath,
        string thumbprint,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(workDirectory, "verify-signature.ps1");
        const string script = """
            param([string]$Path, [string]$Thumbprint)
            $ErrorActionPreference = 'Stop'
            $sig = Get-AuthenticodeSignature -LiteralPath $Path
            $actual = ''
            if ($null -ne $sig.SignerCertificate) { $actual = ($sig.SignerCertificate.Thumbprint -replace '\s', '') }
            if ($sig.Status -ne 'Valid' -or $actual -ne $Thumbprint) {
                Write-Output ("FIRMA_NON_VALIDA|" + $sig.Status + "|" + $actual)
                exit 1
            }
            Write-Output "FIRMA_VALIDA"
            """;

        await File.WriteAllTextAsync(scriptPath, script, cancellationToken).ConfigureAwait(false);
        var normalizedThumbprint = thumbprint.Replace(" ", string.Empty).Replace("\t", string.Empty).Trim();

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(scriptPath);
        start.ArgumentList.Add(filePath);
        start.ArgumentList.Add(normalizedThumbprint);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Impossibile avviare la verifica della firma digitale.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new InvalidDataException($"Firma digitale non valida: {output.Trim()} {error.Trim()}");
    }

    private static void StartApplyScript(
        string scriptPath,
        string mode,
        string assetPath,
        string targetDirectory,
        string restartPath,
        int appProcessId,
        string releaseUrl,
        string logPath,
        string expectedHash,
        string certificateThumbprint)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(scriptPath);
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(assetPath);
        start.ArgumentList.Add(targetDirectory);
        start.ArgumentList.Add(restartPath);
        start.ArgumentList.Add(appProcessId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(releaseUrl);
        start.ArgumentList.Add(logPath);
        start.ArgumentList.Add(expectedHash);
        start.ArgumentList.Add(certificateThumbprint);

        _ = Process.Start(start) ?? throw new InvalidOperationException("Impossibile avviare la procedura di aggiornamento.");
    }

    private static string WriteApplyScript(string workDirectory)
    {
        var scriptPath = Path.Combine(workDirectory, "apply-update.ps1");
        const string script = """
            param(
                [string]$Mode,
                [string]$Asset,
                [string]$TargetDir,
                [string]$RestartPath,
                [int]$WaitPid,
                [string]$ReleaseUrl,
                [string]$LogPath,
                [string]$ExpectedHash,
                [string]$CertificateThumbprint
            )
            $ErrorActionPreference = 'Stop'
            $backupToDelete = $null

            function Write-Log([string]$Message) {
                $line = '[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $Message
                Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
            }

            try {
                $TargetDir = $TargetDir -replace '[\\/]+$', ''
                Write-Log ('Aggiornamento avviato: {0}' -f $Mode)

                if ($WaitPid -gt 0) {
                    for ($i = 0; $i -lt 120; $i++) {
                        if (-not (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue)) { break }
                        Start-Sleep -Seconds 1
                    }
                    if (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue) {
                        throw "L'applicazione non si è chiusa entro 120 secondi."
                    }
                    Write-Log 'Applicazione chiusa, procedo con la sostituzione dei file.'
                }

                $actualHash = (Get-FileHash -LiteralPath $Asset -Algorithm SHA256).Hash
                if (-not [string]::IsNullOrWhiteSpace($ExpectedHash) -and $actualHash -ne $ExpectedHash) {
                    throw 'Hash SHA256 del file da installare non valido.'
                }
                Write-Log ('Integrità confermata: {0}' -f $actualHash)

                if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
                    $signature = Get-AuthenticodeSignature -LiteralPath $Asset
                    $thumb = ''
                    if ($null -ne $signature.SignerCertificate) {
                        $thumb = ($signature.SignerCertificate.Thumbprint -replace '\s', '')
                    }
                    if ($signature.Status -ne 'Valid' -or $thumb -ne $CertificateThumbprint) {
                        throw ('Firma digitale non valido: {0} / {1}' -f $signature.Status, $thumb)
                    }
                    Write-Log 'Firma digitale confermata.'
                }

                switch ($Mode) {
                    'installer' {
                        Write-Log 'Avvio installer EXE in modalità silenzata...'
                        $dirArg = '/DIR="{0}"' -f $TargetDir
                        $logArg = '/LOG="{0}.inno.log"' -f $LogPath
                        $installer = Start-Process -FilePath $Asset `
                            -ArgumentList @('/VERYSILENT', '/NORESTART', '/CLOSEAPPLICATIONS', $dirArg, $logArg) `
                            -Wait -PassThru
                        if ($installer.ExitCode -ne 0) {
                            throw ('Installer terminato con codice {0}' -f $installer.ExitCode)
                        }
                        Write-Log 'Installer completato.'
                    }
                    'portable' {
                        Write-Log 'Preparazione sostituzione cartella portable...'
                        $parent = Split-Path -Parent $TargetDir
                        $staging = Join-Path $parent ('gtit-staging-' + [guid]::NewGuid().ToString('N'))
                        New-Item -ItemType Directory -Force -Path $staging | Out-Null
                        Expand-Archive -LiteralPath $Asset -DestinationPath $staging -Force
                        if (-not (Test-Path (Join-Path $staging 'GameTranslatorIT.App.exe'))) {
                            throw 'Archivio portable incompleto: manca GameTranslatorIT.App.exe.'
                        }
                        $backup = $TargetDir + '.gtit-backup'
                        if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
                        Rename-Item -LiteralPath $TargetDir -NewName ([IO.Path]::GetFileName($backup))
                        try {
                            Rename-Item -LiteralPath $staging -NewName ([IO.Path]::GetFileName($TargetDir))
                        }
                        catch {
                            Rename-Item -LiteralPath $backup -NewName ([IO.Path]::GetFileName($TargetDir))
                            throw
                        }
                        $backupToDelete = $backup
                        Write-Log 'Copia portable aggiornata con backup di ripristino.'
                    }
                    'msix' {
                        Write-Log 'Installazione pacchetto MSIX...'
                        Add-AppxPackage -Path $Asset -ForceUpdateFromAnyVersion -ErrorAction Stop
                        Write-Log 'Pacchetto MSIX installato.'
                    }
                    default { throw ('Modalità di aggiornamento sconosciuta: {0}' -f $Mode) }
                }

                if (-not [string]::IsNullOrWhiteSpace($RestartPath) -and (Test-Path -LiteralPath $RestartPath)) {
                    Write-Log 'Riavvio dell''applicazione...'
                    Start-Process -FilePath $RestartPath | Out-Null
                    Start-Sleep -Seconds 5
                }

                if ($backupToDelete -and (Test-Path -LiteralPath $backupToDelete)) {
                    Remove-Item -LiteralPath $backupToDelete -Recurse -Force -ErrorAction SilentlyContinue
                }
                Write-Log 'Aggiornamento completato.'
            }
            catch {
                Write-Log ('ERRORE: {0}' -f $_.Exception.Message)
                if (-not [string]::IsNullOrWhiteSpace($ReleaseUrl)) {
                    Start-Process $ReleaseUrl | Out-Null
                }
                exit 1
            }
            """;

        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    private static UpdateInstallChannel DetectInstallChannel()
    {
        var baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (baseDirectory.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
            return UpdateInstallChannel.Msix;

        if (File.Exists(Path.Combine(baseDirectory, "unins000.exe")))
            return UpdateInstallChannel.Installer;

        var launcherName = "Avvia GameTranslator IT.cmd";
        if (File.Exists(Path.Combine(baseDirectory, launcherName)))
            return UpdateInstallChannel.Portable;

        var parent = Directory.GetParent(baseDirectory)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent) &&
            File.Exists(Path.Combine(parent, launcherName)))
            return UpdateInstallChannel.Portable;

        return UpdateInstallChannel.Installer;
    }

    private static string ChannelLabel(UpdateInstallChannel channel) => channel switch
    {
        UpdateInstallChannel.Installer => "installer EXE",
        UpdateInstallChannel.Portable => "copia portable",
        _ => "pacchetto MSIX"
    };

    private static List<UpdateAsset> ParseAssets(JsonElement root, bool allowLocal)
    {
        var assets = new List<UpdateAsset>();
        if (!root.TryGetProperty("assets", out var array) || array.ValueKind != JsonValueKind.Array)
            return assets;

        foreach (var item in array.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var url = item.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
            long? size = null;
            if (item.TryGetProperty("size", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number &&
                sizeElement.TryGetInt64(out var sizeValue))
                size = sizeValue;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsAllowedAssetUri(uri, allowLocal)) continue;

            assets.Add(new UpdateAsset(name, url, size));
        }
        return assets;
    }

    private static UpdateAsset? SelectAsset(IReadOnlyList<UpdateAsset> assets, UpdateInstallChannel channel)
    {
        var installer = assets.FirstOrDefault(a =>
            a.Name.StartsWith("GameTranslatorIT-Setup-", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        var portable = assets.FirstOrDefault(a =>
            a.Name.StartsWith("GameTranslatorIT-Portable-", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        var msix = assets.FirstOrDefault(a => a.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase));

        return channel switch
        {
            UpdateInstallChannel.Msix => msix ?? installer ?? portable,
            UpdateInstallChannel.Portable => portable ?? installer ?? msix,
            _ => installer ?? portable ?? msix
        };
    }

    private static DateTimeOffset? TryPublishedAt(JsonElement root)
    {
        if (!root.TryGetProperty("published_at", out var element)) return null;
        var text = element.GetString();
        return DateTimeOffset.TryParse(text, out var value) ? value : null;
    }

    private static (Uri Uri, bool AllowLocal) ResolveApiUri()
    {
        var configured = Environment.GetEnvironmentVariable("GITUPDATER_API_URL");
        var raw = string.IsNullOrWhiteSpace(configured) ? DefaultApiUrl : configured.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"URL API aggiornamenti non valido: {raw}");

        if (uri.Scheme == Uri.UriSchemeHttps)
            return (uri, false);

        if (uri.Scheme == Uri.UriSchemeHttp && IsLoopback(uri.Host))
            return (uri, true);

        throw new InvalidOperationException("L'URL di controllo aggiornamenti deve usare HTTPS (oppure localhost per i test).");
    }

    private static HttpClient CreateClient(TimeSpan timeout, bool allowLocal)
    {
        var client = new HttpClient { Timeout = timeout };
        if (allowLocal)
            client.DefaultRequestHeaders.Add("X-GameTranslatorIT-Test", "local");
        return client;
    }

    private static bool IsAllowedAssetUri(Uri uri, bool allowLocal)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
            return allowLocal && IsLoopback(uri.Host);

        var host = uri.Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopback(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text.Trim().TrimStart('v', 'V');
        var plus = value.IndexOf('+');
        if (plus >= 0) value = value[..plus];
        var dash = value.IndexOf('-');
        if (dash >= 0) value = value[..dash];

        var parts = value.Split('.');
        if (parts.Length is < 1 or > 4) return false;

        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        if (numbers.Length < 3)
        {
            var padded = new int[3];
            Array.Copy(numbers, padded, numbers.Length);
            numbers = padded;
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers.Length > 3 ? numbers[3] : 0);
        return true;
    }
}
