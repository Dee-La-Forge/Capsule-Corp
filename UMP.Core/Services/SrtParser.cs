using System.IO;
using System.Text.RegularExpressions;
using UMP.Core.Models;

namespace UMP.Core.Services;

/// <summary>
/// Parseur SRT unique, partage par l'editeur, l'apercu et le Player.
/// (Remplace les deux copies qui vivaient dans SubtitlePropertiesPanel
/// et PlayerWindow.)
/// </summary>
public static class SrtParser
{
    public static List<SubtitleEntry> Parse(string path)
    {
        var result = new List<SubtitleEntry>();
        try
        {
            var lines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
            int i = 0;
            while (i < lines.Length)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) { i++; continue; }
                if (int.TryParse(lines[i].Trim(), out _)) { i++; continue; }
                var tsLine = lines[i].Trim();
                if (!tsLine.Contains("-->")) { i++; continue; }
                i++;
                var parts = tsLine.Split("-->");
                if (parts.Length < 2) continue;

                var textLines = new List<string>();
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
                { textLines.Add(lines[i].Trim()); i++; }

                if (textLines.Count > 0)
                {
                    var text = string.Join("\n", textLines);
                    text = Regex.Replace(text, "<[^>]+>", "");
                    result.Add(new SubtitleEntry
                    {
                        Text = text,
                        InMs = ParseTs(parts[0].Trim()),
                        OutMs = ParseTs(parts[1].Trim())
                    });
                }
            }
        }
        catch (Exception ex) { Log.Warn($"Parsing SRT echoue '{path}' : {ex.Message}"); }
        return result;
    }

    private static long ParseTs(string ts)
    {
        ts = ts.Replace(',', '.');
        return TimeSpan.TryParse(ts, out var t) ? (long)t.TotalMilliseconds : 0;
    }
}
