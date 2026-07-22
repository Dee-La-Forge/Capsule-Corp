using System.IO;
using System.Text.Json;
using UMP.Core.Models;

namespace UMP.Core.Services;

public class ProjectService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public Project CurrentProject { get; set; } = new();

    /// <summary>
    /// Sauvegarde le projet. Les chemins situes SOUS le dossier du fichier
    /// projet sont ecrits en relatif : le dossier devient portable (autre
    /// disque, autre machine). Les chemins exterieurs restent absolus.
    /// Le projet en memoire n'est pas modifie (l'editeur continue de
    /// travailler avec des chemins absolus).
    /// </summary>
    public void Save(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;

        // Clone (round-trip JSON) pour ne pas relativiser le projet en memoire
        var liveJson = JsonSerializer.Serialize(CurrentProject, Options);
        var clone = JsonSerializer.Deserialize<Project>(liveJson, Options)!;
        ProjectPaths.Transform(clone, p => MakeRelative(p, dir));

        File.WriteAllText(full, JsonSerializer.Serialize(clone, Options));
    }

    /// <summary>
    /// Charge un projet. Les chemins relatifs sont resolus contre le dossier
    /// du fichier projet (meme logique que le Player avec project.json).
    /// Les anciens projets (chemins absolus) se chargent inchanges.
    /// </summary>
    public void Load(string path)
    {
        if (!File.Exists(path)) { CurrentProject = new(); return; }
        var json = File.ReadAllText(path);
        CurrentProject = JsonSerializer.Deserialize<Project>(json, Options) ?? new();

        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        ProjectPaths.Transform(CurrentProject, p => ResolveAgainst(p, dir));
    }

    public void NewProject() => CurrentProject = new();

    /// <summary>Convertit en relatif si le chemin est sous baseDir, sinon le laisse absolu.</summary>
    private static string? MakeRelative(string? p, string baseDir)
    {
        if (string.IsNullOrEmpty(p)) return p;
        try
        {
            if (!Path.IsPathRooted(p)) return p; // deja relatif
            var rel = Path.GetRelativePath(baseDir, p);
            // Ne relativiser que ce qui est SOUS le dossier projet :
            // pas de "..\" (fragile) ni d'autre disque (GetRelativePath rend l'absolu).
            if (rel.StartsWith("..") || Path.IsPathRooted(rel)) return p;
            return rel;
        }
        catch { return p; }
    }

    /// <summary>Resout un chemin relatif contre baseDir ; laisse les absolus inchanges.</summary>
    private static string? ResolveAgainst(string? p, string baseDir)
    {
        if (string.IsNullOrEmpty(p) || Path.IsPathRooted(p)) return p;
        try { return Path.GetFullPath(Path.Combine(baseDir, p)); }
        catch { return p; }
    }
}
