param(
    [string]$Version = '',
    [switch]$SkipMsix,
    [switch]$SkipFullPortable,
    [switch]$SkipModelAssets,
    [switch]$ReuseModelParts,
    [string]$PfxPath = '',
    [string]$PfxPassword = '',
    [string]$RuntimeLibreTranslatePath = '',
    [string]$ModelsSourcePath = '',
    [string]$FullPortableOutputPath = '',
    [string]$ReleaseOutputPath = ''
)
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $props = Get-Content -Raw -LiteralPath (Join-Path $root 'Directory.Build.props')
    $match = [regex]::Match($props, '<Version>(\d+\.\d+\.\d+)</Version>')
    if (-not $match.Success) { throw "Versione non trovata in Directory.Build.props" }
    $Version = $match.Groups[1].Value
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Formato versione non valido: $Version"
}

$distDir = Join-Path $root 'dist\installer'
$publishDir = Join-Path $root 'dist\GameTranslatorIT'
if ([string]::IsNullOrWhiteSpace($ReleaseOutputPath)) {
    $releaseDir = Join-Path $root "dist\Releases\v$Version"
}
else {
    $releaseDir = Join-Path $ReleaseOutputPath "v$Version"
}
New-Item -ItemType Directory -Force -Path $distDir, $releaseDir | Out-Null

function Find-7Zip {
    $candidates = @(
        'C:\Program Files\7-Zip\7z.exe',
        'C:\Program Files (x86)\7-Zip\7z.exe'
    )
    $fromPath = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($fromPath) { $candidates += $fromPath.Source }
    return ($candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)
}

$sevenZip = Find-7Zip

function New-ZipFromPaths {
    param(
        [Parameter(Mandatory = $true)][string[]]$Paths,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
    if ($sevenZip) {
        & $sevenZip a -tzip -mx=1 -y $Destination @Paths | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Creazione archivio non riuscita: $Destination" }
    }
    else {
        Compress-Archive -Path $Paths -DestinationPath $Destination -CompressionLevel Fastest -Force
    }
}

function Get-PairCode {
    param([Parameter(Mandatory = $true)][string]$Name)
    $match = [regex]::Match(
        $Name,
        '(?:translate-)?(?:en_([a-z-]+)|([a-z-]+)_en)(?:-\d.*)?$',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) { return '' }
    if ($match.Groups[1].Success) { return $match.Groups[1].Value.ToLowerInvariant() }
    return $match.Groups[2].Value.ToLowerInvariant()
}

function New-PortableStage {
    param(
        [Parameter(Mandatory = $true)][string]$StagePath,
        [Parameter(Mandatory = $true)][string]$RuntimeSource,
        [string]$ManifestSource = '',
        [string[]]$IncludePackages = @()
    )
    if (Test-Path -LiteralPath $StagePath) { Remove-Item -LiteralPath $StagePath -Recurse -Force }
    $app = Join-Path $StagePath 'GameTranslatorIT'
    New-Item -ItemType Directory -Force -Path $app | Out-Null

    Get-ChildItem -LiteralPath $publishDir -Force |
        Where-Object { $_.Name -notin @('AppxManifest.xml', 'images') } |
        Copy-Item -Destination $app -Recurse -Force

    $launcher = Join-Path $app 'Avvia GameTranslator IT.cmd'
    $launcherText = @"
@echo off
cd /d "%~dp0"
start "" "%~dp0GameTranslatorIT.App.exe"
"@
    Set-Content -LiteralPath $launcher -Value $launcherText -Encoding ASCII

    Copy-Item -LiteralPath $RuntimeSource -Destination (Join-Path $app 'Runtime\LibreTranslate') -Recurse -Force

    if (-not [string]::IsNullOrWhiteSpace($ManifestSource)) {
        $modelsMeta = Join-Path $app 'Models'
        New-Item -ItemType Directory -Force -Path $modelsMeta | Out-Null
        Copy-Item -LiteralPath $ManifestSource -Destination (Join-Path $modelsMeta 'models-manifest.json') -Force
    }

    if ($IncludePackages.Count -gt 0) {
        $targetPackages = Join-Path $app 'Models\argos-translate\packages'
        New-Item -ItemType Directory -Force -Path $targetPackages | Out-Null
        foreach ($packagePath in $IncludePackages) {
            Copy-Item -LiteralPath $packagePath -Destination $targetPackages -Recurse -Force
        }
    }
    return $app
}

Write-Host "=== Pubblicazione $Version ==="
# I file generati dal passaggio MSIX non devono finire nell'installer EXE.
foreach ($stale in @((Join-Path $publishDir 'AppxManifest.xml'), (Join-Path $publishDir 'images'))) {
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Recurse -Force }
}
& (Join-Path $root 'installer\publish.ps1') -Version $Version
if ($LASTEXITCODE -ne 0) { throw "Pubblicazione non riuscita." }

Write-Host "=== Installer EXE ==="
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($iscc)) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        'C:\Users\corci\AppData\Local\Programs\Inno Setup 6\ISCC.exe'
    )
    $iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($iscc) -or -not (Test-Path -LiteralPath $iscc)) {
    throw "ISCC.exe non trovato. Installa Inno Setup 6 oppure aggiungi ISCC.exe al PATH."
}
& $iscc (Join-Path $root 'installer\InnoSetup\GameTranslatorIT.iss') "/DMyAppVersion=$Version"
if ($LASTEXITCODE -ne 0) { throw "Compilazione installer non riuscita." }

$setupSource = Join-Path $distDir "GameTranslatorIT-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $setupSource)) { throw "Installer non generato: $setupSource" }
Copy-Item -LiteralPath $setupSource -Destination (Join-Path $releaseDir "GameTranslatorIT-Setup-$Version.exe") -Force

Write-Host "=== MSIX ==="
if (-not $SkipMsix) {
    dotnet restore (Join-Path $root 'installer\MSIX\MsixTools.csproj')
    if ($LASTEXITCODE -ne 0) { throw "Restore strumenti MSIX non riuscito." }
    $msixParams = @{ Version = $Version }
    if (-not [string]::IsNullOrWhiteSpace($PfxPath)) { $msixParams.PfxPath = $PfxPath }
    if (-not [string]::IsNullOrWhiteSpace($PfxPassword)) { $msixParams.PfxPassword = $PfxPassword }
    & (Join-Path $root 'installer\MSIX\build-msix.ps1') @msixParams
    if ($LASTEXITCODE -ne 0) { throw "Build MSIX non riuscito." }
    Copy-Item -LiteralPath (Join-Path $distDir 'GameTranslatorIT.msix') `
        -Destination (Join-Path $releaseDir "GameTranslatorIT-$Version.msix") -Force
}

Write-Host "=== Runtime LibreTranslate per le portable ==="
$runtimeSource = $RuntimeLibreTranslatePath
if ([string]::IsNullOrWhiteSpace($runtimeSource)) {
    $installedRuntime = Join-Path $env:LOCALAPPDATA 'GameTranslatorIT\LibreTranslate'
    if (Test-Path -LiteralPath (Join-Path $installedRuntime 'Scripts\libretranslate.exe')) {
        $runtimeSource = $installedRuntime
    }
}
if ([string]::IsNullOrWhiteSpace($runtimeSource) -or
    -not (Test-Path -LiteralPath (Join-Path $runtimeSource 'Scripts\libretranslate.exe'))) {
    $runtimeSource = Join-Path $root 'dist\runtime\LibreTranslate'
    & (Join-Path $root 'installer\LibreTranslate\BuildRuntime.ps1') -OutputDir $runtimeSource
    if ($LASTEXITCODE -ne 0) { throw "Preparazione runtime LibreTranslate non riuscita." }
}
Write-Host "Runtime: $runtimeSource"

$manifestPath = ''
$partFiles = @()
$essentialZip = $null
$fullZip = $null
$essentialStage = $null

if ($SkipModelAssets) {
    Write-Host 'Modelli non preparati in questa build (-SkipModelAssets): verranno caricati separatamente.'
}
else {
    Write-Host "=== Modelli Argos ==="
    $modelsSource = $ModelsSourcePath
    if ([string]::IsNullOrWhiteSpace($modelsSource)) {
        $defaultModels = Join-Path $env:USERPROFILE '.local\share\argos-translate\packages'
        if (Test-Path -LiteralPath $defaultModels) { $modelsSource = $defaultModels }
    }
    if ([string]::IsNullOrWhiteSpace($modelsSource) -or -not (Test-Path -LiteralPath $modelsSource)) {
        throw 'Cartella modelli Argos non trovata. Specifica -ModelsSourcePath.'
    }
    $modelDirs = @(Get-ChildItem -LiteralPath $modelsSource -Directory | Sort-Object Name)
    if ($modelDirs.Count -eq 0) { throw "Nessun modello Argos trovato in $modelsSource" }
    Write-Host "Modelli disponibili: $($modelDirs.Count)"

    Write-Host "=== Archivi modello per GitHub (max 1,6 GB ciascuno) ==="
    $partLimit = 1600MB
    $partRecords = @()
    $reusedParts = $false
    $manifestPath = Join-Path $releaseDir 'models-manifest.json'

    # Dopo un'interruzione della build è possibile riutilizzare le parti già
    # create, ma solo se integrità e elenco pacchetti coincidono col manifest.
    if ($ReuseModelParts -and (Test-Path -LiteralPath $manifestPath)) {
        try {
            $previous = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            if ($previous.version -ne $Version -or $previous.packageCount -ne $modelDirs.Count) {
                throw 'Versione o numero di pacchetti diverso.'
            }
            $candidateFiles = @()
            $candidateRecords = @()
            foreach ($part in @($previous.parts)) {
                $partPath = Join-Path $releaseDir $part.file
                if (-not (Test-Path -LiteralPath $partPath)) { throw "Parte mancante: $($part.file)" }
                $hash = (Get-FileHash -LiteralPath $partPath -Algorithm SHA256).Hash
                if ($hash -ne $part.sha256) { throw "Hash diverso per $($part.file)" }
                $candidateFiles += $partPath
                $candidateRecords += [ordered]@{
                    file     = $part.file
                    sha256   = $hash
                    packages = @($part.packages)
                }
            }
            $expectedNames = @($modelDirs | ForEach-Object { $_.Name } | Sort-Object)
            $actualNames = @($candidateRecords | ForEach-Object { $_.packages } | Sort-Object)
            if (@(Compare-Object -ReferenceObject $expectedNames -DifferenceObject $actualNames).Count -gt 0) {
                throw 'Elenco pacchetti del manifest diverso dalla cartella modelli.'
            }
            $partFiles = $candidateFiles
            $partRecords = $candidateRecords
            $reusedParts = $true
            Write-Host "Riutilizzate $($partFiles.Count) parti modello verificate."
        }
        catch {
            $partFiles = @()
            $partRecords = @()
            Write-Host "Riutilizzo parti non possibile: $($_.Exception.Message). Rigenerazione."
        }
    }

    if (-not $reusedParts) {
        $index = 0
        while ($index -lt $modelDirs.Count) {
            $currentPaths = @()
            $currentNames = @()
            $currentBytes = 0
            while ($index -lt $modelDirs.Count) {
                $dir = $modelDirs[$index]
                $bytes = (Get-ChildItem -LiteralPath $dir.FullName -Recurse -File |
                    Measure-Object -Property Length -Sum).Sum
                if ($currentPaths.Count -gt 0 -and ($currentBytes + $bytes) -gt $partLimit) { break }
                $currentPaths += $dir.FullName
                $currentNames += $dir.Name
                $currentBytes += $bytes
                $index++
            }
            $partNumber = $partFiles.Count + 1
            $partName = 'GameTranslatorIT-Models-{0}.part{1:00}.zip' -f $Version, $partNumber
            $partPath = Join-Path $releaseDir $partName
            Write-Host ("{0}: {1} pacchetti, {2:N0} MB" -f $partName, $currentPaths.Count, ($currentBytes / 1MB))
            New-ZipFromPaths -Paths $currentPaths -Destination $partPath
            $partFiles += $partPath
            $partRecords += [ordered]@{
                file     = $partName
                sha256   = (Get-FileHash -LiteralPath $partPath -Algorithm SHA256).Hash
                packages = @($currentNames)
            }
        }
    }

    $manifest = [ordered]@{
        version           = $Version
        packageCount      = $modelDirs.Count
        packagesDirectory = 'argos-translate/packages'
        parts             = @($partRecords)
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    Write-Host "=== Portable con modelli essenziali ==="
    $essentialCodes = @('it', 'es', 'fr', 'pt', 'zh')
    $essentialPaths = @($modelDirs | Where-Object { $essentialCodes -contains (Get-PairCode -Name $_.Name) } |
        ForEach-Object { $_.FullName })
    if ($essentialPaths.Count -eq 0) { throw 'Nessun modello essenziale selezionato.' }
    $essentialManifestPath = Join-Path $releaseDir 'models-manifest-essenziali.json'
    @{
        version           = $Version
        packageCount      = $essentialPaths.Count
        packagesDirectory = 'argos-translate/packages'
        parts             = @()
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $essentialManifestPath -Encoding UTF8
    $essentialStage = Join-Path $releaseDir 'portable-essential-stage'
    $essentialApp = New-PortableStage -StagePath $essentialStage -RuntimeSource $runtimeSource `
        -ManifestSource $essentialManifestPath -IncludePackages $essentialPaths
    $essentialZip = Join-Path $releaseDir "GameTranslatorIT-Portable-Essenziali-$Version.zip"
    New-ZipFromPaths -Paths @($essentialApp) -Destination $essentialZip

    Write-Host "=== Portable completa (tutti i modelli, solo locale) ==="
    if (-not $SkipFullPortable) {
        if ([string]::IsNullOrWhiteSpace($FullPortableOutputPath)) {
            $FullPortableOutputPath = Join-Path $env:USERPROFILE 'Downloads'
        }
        New-Item -ItemType Directory -Force -Path $FullPortableOutputPath | Out-Null
        $fullManifestPath = Join-Path $releaseDir 'models-manifest-completo.json'
        @{
            version           = $Version
            packageCount      = $modelDirs.Count
            packagesDirectory = 'argos-translate/packages'
            parts             = @()
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fullManifestPath -Encoding UTF8
        $fullStage = Join-Path $releaseDir 'portable-full-stage'
        $allPaths = @($modelDirs | ForEach-Object { $_.FullName })
        $fullApp = New-PortableStage -StagePath $fullStage -RuntimeSource $runtimeSource `
            -ManifestSource $fullManifestPath -IncludePackages $allPaths
        $fullZip = Join-Path $FullPortableOutputPath "GameTranslatorIT-Portable-Completo-$Version.zip"
        New-ZipFromPaths -Paths @($fullApp) -Destination $fullZip
        Remove-Item -LiteralPath $fullStage -Recurse -Force
    }
}

Write-Host "=== Portable standard (con runtime e manifest) ==="
$standardStage = Join-Path $releaseDir 'portable-stage'
$standardApp = New-PortableStage -StagePath $standardStage -RuntimeSource $runtimeSource -ManifestSource $manifestPath
$zipPath = Join-Path $releaseDir "GameTranslatorIT-Portable-$Version.zip"
New-ZipFromPaths -Paths @($standardApp) -Destination $zipPath

if ($essentialStage -and (Test-Path -LiteralPath $essentialStage)) {
    Remove-Item -LiteralPath $essentialStage -Recurse -Force
}
Remove-Item -LiteralPath $standardStage -Recurse -Force

Write-Host "=== Checksum SHA256 ==="
$assets = @(
    (Join-Path $releaseDir "GameTranslatorIT-Setup-$Version.exe"),
    $zipPath
)
if ($essentialZip) { $assets += $essentialZip }
if (-not $SkipMsix) { $assets += (Join-Path $releaseDir "GameTranslatorIT-$Version.msix") }
$assets += $partFiles
if ($fullZip) { $assets += $fullZip }

foreach ($asset in $assets) {
    if (-not (Test-Path -LiteralPath $asset)) { throw "Asset mancante: $asset" }
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash
    $checksumPath = "$asset.sha256"
    [System.IO.File]::WriteAllText($checksumPath, "$hash  $([System.IO.Path]::GetFileName($asset))`n")
    Write-Host "$([System.IO.Path]::GetFileName($asset)) -> $hash"
}

$notes = Join-Path $releaseDir 'release-notes.md'
if (-not (Test-Path -LiteralPath $notes)) {
    [System.IO.File]::WriteAllText($notes, @"
# GameTranslator IT $Version

Tutte le 58 lingue restano selezionabili, con rilevamento automatico della sorgente, cambio lingua senza riavvio e fallback online esplicito per le 8 lingue senza modello Argos.

## Asset pubblicati
- GameTranslatorIT-Setup-$Version.exe (installer EXE, scarica i modelli al primo avvio)
- GameTranslatorIT-Portable-$Version.zip (portable con runtime locale; usa gli archivi modello qui sotto oppure i modelli già installati nel profilo)
- GameTranslatorIT-Portable-Essenziali-$Version.zip (portable offline con le coppie it/es/fr/pt/zh ↔ inglese)
- GameTranslatorIT-Models-$Version.part*.zip + relativi .sha256 (100 modelli Argos, ricomposti automaticamente dall'app)
- GameTranslatorIT-$Version.msix (pacchetto MSIX firmato, quando disponibile)
- File `.sha256` corrispondenti per la verifica integrità

La copia completa con tutti i modelli (~10 GB) viene creata solo in locale, perché supera il limite GitHub di 2 GB per asset.

Le impostazioni utente in `%APPDATA%\GameTranslatorIT` vengono conservate.
"@)
}

$pfxInRelease = Join-Path $releaseDir 'GameTranslatorIT-dev.pfx'
if (Test-Path -LiteralPath $pfxInRelease) { Remove-Item -LiteralPath $pfxInRelease -Force }

Write-Host "Rilascio pronto: $releaseDir"
Get-ChildItem -LiteralPath $releaseDir | Select-Object Name, Length | Format-Table -AutoSize | Out-Host
