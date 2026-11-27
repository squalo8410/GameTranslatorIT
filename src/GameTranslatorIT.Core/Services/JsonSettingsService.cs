using System.Text.Json;
using System.Text.Json.Serialization;
using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.Core.Services;

public interface ISettingsService
{
    string SettingsPath { get; }
    AppSettings Load();
    void Save(AppSettings settings);
}

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string SettingsPath { get; }

    public JsonSettingsService()
    {
        // Un file settings.json messo accanto all'eseguibile portable viene usato
        // quando esiste: così le selezioni viaggiano con la copia. Senza quel file
        // resta il profilo Windows, che è la posizione storica e preserva i dati
        // dell'utente tra aggiornamenti e reinstallazioni.
        var portable = Path.Combine(AppContext.BaseDirectory, "settings.json");
        if (File.Exists(portable))
        {
            SettingsPath = portable;
            return;
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(root, "GameTranslatorIT");
        Directory.CreateDirectory(folder);
        SettingsPath = Path.Combine(folder, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Options)
                           ?? new AppSettings();
            settings.EnsureCaptureRegions();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Non sostituiamo mai un file dell'utente: segnaliamo soltanto il
            // percorso reale, così il problema è diagnosticabile anche su un PC nuovo.
            return new AppSettings
            {
                LoadWarning = $"Impossibile leggere {SettingsPath}: {ex.Message}. " +
                              "Le impostazioni correnti non sono state modificate."
            };
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.EnsureCaptureRegions();

        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporary = SettingsPath + ".gtit-tmp";
        try
        {
            if (File.Exists(SettingsPath) &&
                (File.GetAttributes(SettingsPath) & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(SettingsPath, FileAttributes.Normal);

            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, SettingsPath, overwrite: true);
            settings.LoadWarning = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch { /* il file temporaneo può restare: non è mai letto */ }
            }

            throw new IOException(
                $"Impossibile salvare le impostazioni in {SettingsPath}: {ex.Message}. " +
                "Controlla i permessi della cartella e che il file non sia in sola lettura.", ex);
        }
    }
}
