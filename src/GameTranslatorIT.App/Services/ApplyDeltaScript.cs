using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace GameTranslatorIT.App.Services;

/// <summary>
/// Updater esterno: lavora dopo la chiusura dell'app, tocca solo i file del piano,
/// conserva un backup in TEMP e fa rollback se la sostituzione o l'avvio falliscono.
/// </summary>
internal static class ApplyDeltaScript
{
    public static string Write(string workDirectory)
    {
        var path = Path.Combine(workDirectory, "apply-delta.ps1");
        File.WriteAllText(path, Script);
        return path;
    }

    public static void Start(
        string scriptPath,
        string planPath,
        string targetDirectory,
        string restartPath,
        int waitPid,
        string logPath,
        string version,
        string releaseUrl)
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
        start.ArgumentList.Add(planPath);
        start.ArgumentList.Add(targetDirectory);
        start.ArgumentList.Add(restartPath);
        start.ArgumentList.Add(waitPid.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(logPath);
        start.ArgumentList.Add(version);
        start.ArgumentList.Add(releaseUrl);

        _ = Process.Start(start) ?? throw new InvalidOperationException("Impossibile avviare l'updater esterno.");
    }

    private const string Script = """
        param(
            [string]$PlanPath,
            [string]$TargetDir,
            [string]$RestartPath,
            [int]$WaitPid,
            [string]$LogPath,
            [string]$Version,
            [string]$ReleaseUrl
        )
        $ErrorActionPreference = 'Stop'
        $plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
        $workDir = Split-Path -Parent $PlanPath
        $payload = Join-Path $workDir 'payload'
        $backup = Join-Path $workDir 'backup'
        $journal = Join-Path $workDir 'journal.json'
        $commit = Join-Path $workDir ('commit-' + $Version + '.json')
        $applied = [System.Collections.Generic.List[object]]::new()
        $committed = $false
        $TargetDir = $TargetDir -replace '[\\/]+$', ''

        function Write-Log([string]$Message) {
            Add-Content -LiteralPath $LogPath -Value ('[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $Message) -Encoding UTF8
        }
        function Write-Journal([string]$Status) {
            @{ version = $Version; status = $Status; updatedUtc = (Get-Date).ToUniversalTime().ToString('o') } |
                ConvertTo-Json | Set-Content -LiteralPath $journal -Encoding UTF8
        }
        function Resolve-Destination([string]$Relative) {
            if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or
                $Relative -match '(^|[\\/])\.\.([\\/]|$)' -or $Relative -match ':') {
                throw ('Percorso non valido nel piano: ' + $Relative)
            }
            return Join-Path $TargetDir $Relative
        }
        function Assert-Hash([string]$Path, [string]$Expected) {
            $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
            if ($actual -ne $Expected) { throw ('SHA256 non valido per ' + $Path) }
        }
        function Restore-Previous {
            for ($i = $applied.Count - 1; $i -ge 0; $i--) {
                $entry = $applied[$i]
                $dest = Resolve-Destination $entry.path
                $saved = Join-Path $backup $entry.path
                try {
                    if (Test-Path -LiteralPath $saved) {
                        $parent = Split-Path -Parent $dest
                        if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
                        if (Test-Path -LiteralPath $dest) { Set-ItemProperty -LiteralPath $dest -Name IsReadOnly -Value $false }
                        Copy-Item -LiteralPath $saved -Destination $dest -Force
                    } elseif ($entry.action -eq 'replace' -and (Test-Path -LiteralPath $dest)) {
                        Remove-Item -LiteralPath $dest -Force
                    }
                }
                catch {
                    Write-Log ('ROLLBACK parziale per ' + $entry.path + ': ' + $_.Exception.Message)
                }
            }
        }

        try {
            Write-Log ('Aggiornamento differenziale avviato: versione ' + $Version)
            if ($WaitPid -gt 0) {
                for ($i = 0; $i -lt 120; $i++) {
                    if (-not (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue)) { break }
                    Start-Sleep -Seconds 1
                }
                if (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue) {
                    throw "L'applicazione non si e chiusa entro 120 secondi."
                }
            }

            Write-Journal 'in-progress'
            New-Item -ItemType Directory -Force -Path $backup | Out-Null

            foreach ($entry in @($plan.files)) {
                $dest = Resolve-Destination $entry.path
                $saved = Join-Path $backup $entry.path
                $parent = Split-Path -Parent $saved
                if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }

                if ($entry.action -eq 'delete') {
                    if (-not (Test-Path -LiteralPath $dest)) { continue }
                    Assert-Hash $dest $entry.sha256
                    Copy-Item -LiteralPath $dest -Destination $saved -Force
                    Remove-Item -LiteralPath $dest -Force
                    $applied.Add(@{ path = $entry.path; action = 'delete' })
                    Write-Log ('Rimosso: ' + $entry.path)
                    continue
                }

                $source = Join-Path $payload $entry.path
                if (-not (Test-Path -LiteralPath $source)) { throw ('File mancante dal payload: ' + $entry.path) }
                Assert-Hash $source $entry.sha256

                if (Test-Path -LiteralPath $dest) {
                    Set-ItemProperty -LiteralPath $dest -Name IsReadOnly -Value $false
                    Copy-Item -LiteralPath $dest -Destination $saved -Force
                }
                $destParent = Split-Path -Parent $dest
                if (-not (Test-Path -LiteralPath $destParent)) { New-Item -ItemType Directory -Force -Path $destParent | Out-Null }

                $temporary = $dest + '.gtit-tmp'
                Copy-Item -LiteralPath $source -Destination $temporary -Force
                Assert-Hash $temporary $entry.sha256
                Move-Item -LiteralPath $temporary -Destination $dest -Force
                $applied.Add(@{ path = $entry.path; action = 'replace' })
                Write-Log ('Sostituito: ' + $entry.path)
            }

            Write-Journal 'pending'
            if (-not [string]::IsNullOrWhiteSpace($RestartPath) -and (Test-Path -LiteralPath $RestartPath)) {
                Start-Process -FilePath $RestartPath | Out-Null
            }

            for ($i = 0; $i -lt 90; $i++) {
                if (Test-Path -LiteralPath $commit) {
                    $committed = $true
                    Write-Journal 'committed'
                    Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue
                    Write-Log 'Aggiornamento confermato dall''app. Completato.'
                    exit 0
                }
                Start-Sleep -Seconds 1
            }
            throw 'La nuova versione non ha confermato l''avvio entro 90 secondi.'
        }
        catch {
            Write-Log ('ERRORE: ' + $_.Exception.Message)
            if (-not $committed) {
                Restore-Previous
                Write-Journal 'failed'
            }
            if (-not [string]::IsNullOrWhiteSpace($ReleaseUrl)) {
                Start-Process $ReleaseUrl | Out-Null
            }
            exit 1
        }
        """;
}
