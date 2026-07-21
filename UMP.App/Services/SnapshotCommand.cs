using System.Text.Json;

namespace UMP.App.Services;

/// <summary>
/// Commande Undo basee sur un snapshot JSON d'un objet.
/// Deep-copy via serialisation JSON complete.
/// </summary>
public class SnapshotCommand<T> : IUndoCommand where T : class
{
    private readonly T _target;
    private readonly string _beforeJson;
    private readonly string _afterJson;
    private readonly Action? _onApply;
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = false };
    public string Description { get; }

    public SnapshotCommand(string description, T target, string beforeJson, Action? onApply = null)
    {
        Description = description;
        _target = target;
        _beforeJson = beforeJson;
        _afterJson = JsonSerializer.Serialize(target, _opts);
        _onApply = onApply;
    }

    /// <summary>Verifie si l'objet a change depuis le snapshot</summary>
    public bool HasChanged => _beforeJson != _afterJson;

    public static string CaptureSnapshot(T obj) => JsonSerializer.Serialize(obj, _opts);

    public void Execute() { Restore(_afterJson); }
    public void Undo() { Restore(_beforeJson); }

    private void Restore(string json)
    {
        var restored = JsonSerializer.Deserialize<T>(json, _opts);
        if (restored is null) return;

        foreach (var prop in typeof(T).GetProperties())
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            // Ignorer les proprietes JsonIgnore et calculees
            if (prop.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true).Length > 0)
                continue;
            try
            {
                var val = prop.GetValue(restored);
                // Deep-copy les listes en re-serialisant
                if (val is System.Collections.IList && prop.PropertyType.IsGenericType)
                {
                    var listJson = JsonSerializer.Serialize(val, _opts);
                    var deepCopy = JsonSerializer.Deserialize(listJson, prop.PropertyType, _opts);
                    prop.SetValue(_target, deepCopy);
                }
                else
                {
                    prop.SetValue(_target, val);
                }
            }
            catch { }
        }
        _onApply?.Invoke();
    }
}
