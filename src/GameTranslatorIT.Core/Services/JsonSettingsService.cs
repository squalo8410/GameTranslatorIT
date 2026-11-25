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
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            settings.EnsureCaptureRegions();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // In caso di file corrotto si riparte dai valori predefiniti senza cancellare il file:
            // l'utente potrà recuperarlo manualmente.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.EnsureCaptureRegions();
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(SettingsPath, json);
    }
}
