using System.IO;

namespace GameTranslatorIT.App.Services;

/// <summary>
/// Glossario locale: una voce per riga nel formato "inglese=italiano".
/// Le sostituzioni vengono applicate al testo tradotto per correggere nomi e termini gergali.
/// </summary>
public sealed class GlossaryService
{
    private readonly List<(string From, string To)> _entries = new();

    public string ContextText => string.Join(Environment.NewLine, _entries.Select(e => $"{e.From} = {e.To}"));

    public int Count => _entries.Count;

    public void Load(string? path)
    {
        _entries.Clear();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1) continue;

            var from = line[..separator].Trim();
            var to = line[(separator + 1)..].Trim();
            if (from.Length > 0 && to.Length > 0)
                _entries.Add((from, to));
        }
    }

    public string Apply(string text)
    {
        var result = text;
        foreach (var (from, to) in _entries)
            result = ReplaceIgnoreCase(result, from, to);
        return result;
    }

    private static string ReplaceIgnoreCase(string source, string from, string to)
    {
        var startIndex = 0;
        while (true)
        {
            var index = source.IndexOf(from, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return source;

            source = string.Concat(source.AsSpan(0, index), to, source.AsSpan(index + from.Length));
            startIndex = index + to.Length;
        }
    }
}
