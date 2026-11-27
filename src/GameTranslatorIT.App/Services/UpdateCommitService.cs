using System.IO;
using System.Text.Json;

namespace GameTranslatorIT.App.Services;

/// <summary>
/// Conferma all'updater esterno che la nuova versione è partita. Senza questa
/// conferma lo script ripristina i file dal backup in TEMP.
/// </summary>
public static class UpdateCommitService
{
    public static void CommitPendingUpdate()
    {
        var root = Path.Combine(Path.GetTempPath(), "GameTranslatorIT", "Updates");
        if (!Directory.Exists(root)) return;

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            try
            {
                var journalPath = Path.Combine(directory, "journal.json");
                if (!File.Exists(journalPath)) continue;
                using var document = JsonDocument.Parse(File.ReadAllText(journalPath));
                var status = document.RootElement.TryGetProperty("status", out var statusElement)
                    ? statusElement.GetString() : null;
                var version = document.RootElement.TryGetProperty("version", out var versionElement)
                    ? versionElement.GetString() : null;
                if (!string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(version)) continue;

                var commitPath = Path.Combine(directory, $"commit-{version}.json");
                File.WriteAllText(commitPath, JsonSerializer.Serialize(new
                {
                    version,
                    confirmedAtUtc = DateTimeOffset.UtcNow,
                    processId = Environment.ProcessId
                }));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Un journal danneggiato non deve impedire l'avvio dell'app.
            }
        }
    }
}
