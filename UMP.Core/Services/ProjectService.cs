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

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(CurrentProject, Options);
        File.WriteAllText(path, json);
    }

    public void Load(string path)
    {
        if (!File.Exists(path)) { CurrentProject = new(); return; }
        var json = File.ReadAllText(path);
        CurrentProject = JsonSerializer.Deserialize<Project>(json, Options) ?? new();
    }

    public void NewProject() => CurrentProject = new();
}
