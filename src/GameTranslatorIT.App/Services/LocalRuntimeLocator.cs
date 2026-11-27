using System.Diagnostics;
using System.IO;

namespace GameTranslatorIT.App.Services;

/// <summary>Runtime locale individuato senza percorsi assoluti del PC di sviluppo.</summary>
public sealed record LocalRuntime(
    bool IsPortable,
    string PythonExecutable,
    string RuntimeDirectory,
    string? PythonHome,
    string? ArgosPackagesDirectory);

/// <summary>
/// Cerca sempre prima il runtime portable accanto all'app, poi il venv installato
/// dall'EXE, infine un Python eventualmente presente nel PATH. Non scarica nulla.
/// </summary>
public static class LocalRuntimeLocator
{
    public static LocalRuntime? Find(string? configuredPythonPath, string? configuredPackagesDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredPythonPath))
        {
            var configured = Environment.ExpandEnvironmentVariables(configuredPythonPath.Trim());
            if (File.Exists(configured))
                return new LocalRuntime(false, configured,
                    Path.GetDirectoryName(configured) ?? AppContext.BaseDirectory, null,
                    ResolvePackagesDirectory(configuredPackagesDirectory, false));
        }

        var appDirectory = AppContext.BaseDirectory;
        var portableRuntime = Path.Combine(appDirectory, "Runtime", "LibreTranslate");
        var portablePython = Path.Combine(portableRuntime, "Scripts", "python.exe");
        var portableHome = Path.Combine(portableRuntime, "Python");
        if (File.Exists(portablePython))
        {
            return new LocalRuntime(true, portablePython, portableRuntime,
                Directory.Exists(portableHome) ? portableHome : null,
                ResolvePackagesDirectory(configuredPackagesDirectory, true));
        }

        var installedRuntime = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameTranslatorIT", "LibreTranslate");
        var installedPython = Path.Combine(installedRuntime, "Scripts", "python.exe");
        if (File.Exists(installedPython))
        {
            return new LocalRuntime(false, installedPython, installedRuntime, null,
                ResolvePackagesDirectory(configuredPackagesDirectory, false));
        }

        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(entry.Trim('"'), "python.exe");
                if (File.Exists(candidate))
                {
                    return new LocalRuntime(false, candidate,
                        Path.GetDirectoryName(candidate) ?? appDirectory, null,
                        ResolvePackagesDirectory(configuredPackagesDirectory, false));
                }
            }
            catch (ArgumentException)
            {
                // Voce PATH non valida: si ignora.
            }
        }

        return null;
    }

    private static string? ResolvePackagesDirectory(string? configured, bool portable)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
            return expanded;
        }

        return portable
            ? Path.Combine(AppContext.BaseDirectory, "Models", "argos-translate", "packages")
            : null; //null = cartella predefinita di Argos Translate nel profilo utente
    }
}
