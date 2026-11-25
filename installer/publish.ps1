param([string]$Version = '')
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\GameTranslatorIT.App\GameTranslatorIT.App.csproj'
$output = Join-Path $root 'dist\GameTranslatorIT'

Write-Host "Pubblicazione self-contained win-x64 in: $output"
$publishArgs = @($project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-o', $output)
if (-not [string]::IsNullOrWhiteSpace($Version)) { $publishArgs += "-p:Version=$Version" }
& dotnet publish @publishArgs

if ($LASTEXITCODE -ne 0) { throw "dotnet publish non riuscito (codice $LASTEXITCODE)." }
Write-Host "Pubblicazione completata."
