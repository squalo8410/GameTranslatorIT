using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GameTranslatorIT.App.Services;

/// <summary>Avanzamento di una lingua: viene emesso per ogni coppia en↔lingua.</summary>
public sealed record LanguageInstallProgress(
    int CompletedLanguages,
    int TotalLanguages,
    string CurrentLanguage,
    string Message,
    int SucceededLanguages,
    int FailedLanguages);

/// <summary>Risultato riassuntivo; i fallimenti sono rinviabili senza ripetere gli successi.</summary>
public sealed record LanguageInstallResult(
    int RequestedLanguages,
    int SucceededLanguages,
    IReadOnlyList<string> FailedLanguages,
    IReadOnlyList<string> FailedMessages,
    string Message);

/// <summary>
/// Installa i modelli Argos mancanti usando esattamente la fonte già presente
/// nel progetto: l'indice e i download di argostranslate, interrogati dal runtime
/// LibreTranslate/Python locale. Nessun URL o pacchetto viene inventato.
/// </summary>
public sealed class LanguageInstallService
{
    private const int RetryAttempts = 2;

    private readonly string? _configuredPythonPath;
    private readonly string? _configuredPackagesDirectory;

    public LanguageInstallService(string? configuredPythonPath, string? configuredPackagesDirectory)
    {
        _configuredPythonPath = configuredPythonPath;
        _configuredPackagesDirectory = configuredPackagesDirectory;
    }

    public async Task<LanguageInstallResult> InstallAsync(
        IReadOnlyList<(string Code, string DisplayName)> languages,
        IProgress<LanguageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (languages.Count == 0)
            return new LanguageInstallResult(0, 0, Array.Empty<string>(), Array.Empty<string>(),
                "Nessuna lingua mancante: non è stato installato nulla.");

        var runtime = LocalRuntimeLocator.Find(_configuredPythonPath, _configuredPackagesDirectory)
            ?? throw new InvalidOperationException(
                "Python non trovato. Installa il motore LibreTranslate locale dall'installer " +
                "oppure imposta il percorso di python.exe in Impostazioni > Lingue.");

        var workDirectory = Path.Combine(Path.GetTempPath(), "GameTranslatorIT", "Languages");
        Directory.CreateDirectory(workDirectory);

        var scriptPath = Path.Combine(workDirectory, "install-argos-packages.py");
        var inputPath = Path.Combine(workDirectory, "language-pairs.json");
        await File.WriteAllTextAsync(scriptPath, PythonScript, cancellationToken).ConfigureAwait(false);

        var pairs = languages.Select(language => new
        {
            name = language.DisplayName,
            pairs = new[]
            {
                new { fromCode = "en", toCode = language.Code },
                new { fromCode = language.Code, toCode = "en" }
            }
        }).ToList();
        await File.WriteAllTextAsync(inputPath,
            JsonSerializer.Serialize(pairs), cancellationToken).ConfigureAwait(false);

        var start = new ProcessStartInfo(runtime.PythonExecutable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = runtime.RuntimeDirectory
        };
        start.ArgumentList.Add(scriptPath);
        start.ArgumentList.Add(inputPath);
        start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        start.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
        if (!string.IsNullOrWhiteSpace(runtime.PythonHome))
            start.EnvironmentVariables["PYTHONHOME"] = runtime.PythonHome;
        if (!string.IsNullOrWhiteSpace(runtime.ArgosPackagesDirectory))
            start.EnvironmentVariables["ARGOS_PACKAGES_DIR"] = runtime.ArgosPackagesDirectory;

        var succeeded = new List<string>();
        var failed = new List<string>();
        var failedMessages = new List<string>();
        var completed = 0;
        var outcomes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Impossibile avviare l'installazione dei modelli.");

        var errorsTask = process.StandardError.ReadToEndAsync(cancellationToken);
        while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('|');
            var kind = parts[0];
            var name = parts.Length > 3 ? parts[3] : languages.FirstOrDefault(l => l.Code == parts[1]).DisplayName;

            if (kind == "AVVIO")
            {
                progress?.Report(new LanguageInstallProgress(completed, languages.Count,
                    name, $"Installazione di {name} ({parts[1]} ↔ en): download {parts[2]}/2…",
                    succeeded.Count, failed.Count));
                continue;
            }

            if (kind is not ("OK" or "SKIP" or "ERRORE")) continue;

            if (!outcomes.TryGetValue(name, out var list))
                outcomes[name] = list = new List<string>();
            list.Add(kind);
            if (kind == "ERRORE")
                failedMessages.Add($"{name} ({parts[1]}→{parts[2]}): {string.Join('|', parts.Skip(4))}");

            if (list.Count < 2) continue; //una lingua ha sempre due direzioni en↔lingua
            completed++;

            if (list.Any(result => result == "ERRORE"))
            {
                failed.Add(name);
                progress?.Report(new LanguageInstallProgress(completed, languages.Count,
                    name, $"{name}: installazione non riuscita.", succeeded.Count, failed.Count));
            }
            else
            {
                succeeded.Add(name);
                progress?.Report(new LanguageInstallProgress(completed, languages.Count,
                    name, $"{name}: pronta.", succeeded.Count, failed.Count));
            }
        }

        var errors = await errorsTask.ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0 && failed.Count == 0)
            failedMessages.Add(string.IsNullOrWhiteSpace(errors)
                ? $"Processo Python terminato con codice {process.ExitCode}."
                : errors.Trim());

        if (failed.Count == 0 && failedMessages.Count > 0)
            failed.AddRange(languages.Select(l => l.DisplayName));

        var message = failed.Count == 0
            ? $"Installazione completata: {succeeded.Count} lingue pronte."
            : $"Installazione terminata con {failed.Count} fallimenti. Usa 'Riprova fallimenti' per ripetere solo quelli.";

        return new LanguageInstallResult(languages.Count, succeeded.Count,
            failed, failedMessages, message);
    }

    private const string PythonScript = """
        import json
        import sys
        import time
        from argostranslate import package

        with open(sys.argv[1], 'r', encoding='utf-8') as handle:
            languages = json.load(handle)

        package.update_package_index()
        available = {(p.from_code, p.to_code): p for p in package.get_available_packages()}
        installed = {(p.from_code, p.to_code) for p in package.get_installed_packages()}

        for language in languages:
            name = language['name']
            for pair in language['pairs']:
                source = pair['fromCode']
                target = pair['toCode']
                key = (source, target)
                if key in installed:
                    print('SKIP|%s|%s|%s' % (source, target, name), flush=True)
                    continue
                model = available.get(key)
                if model is None:
                    print('ERRORE|%s|%s|%s|Pacchetto non presente nell indice Argos' % (source, target, name), flush=True)
                    continue
                print('AVVIO|%s|%s|%s|1' % (source, target, name), flush=True)
                last_error = ''
                for attempt in range(3):
                    try:
                        model.install_from_path(model.download())
                        installed.add(key)
                        print('OK|%s|%s|%s' % (source, target, name), flush=True)
                        break
                    except Exception as exc:
                        last_error = str(exc)
                        if attempt < 2:
                            time.sleep(2)
                else:
                    print('ERRORE|%s|%s|%s|%s' % (source, target, name, last_error), flush=True)
        """;
}
