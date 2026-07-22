using System.IO;

namespace UMP.Core;

/// <summary>
/// Journalisation fichier minimaliste et thread-safe.
/// Ecrit dans %LOCALAPPDATA%\CapsuleMedia\logs\&lt;app&gt;-yyyyMMdd.log.
/// Ne leve JAMAIS d'exception : le logging ne doit pas faire tomber l'application.
/// </summary>
public static class Log
{
    private static readonly object _lock = new();
    private static string? _logFile;
    private static string _lastMessage = "";
    private static DateTime _lastMessageAt;

    /// <summary>Prefixe du fichier de log (ex. "editor", "player"). A definir au demarrage.</summary>
    public static string AppName { get; set; } = "app";

    /// <summary>Chemin du fichier de log courant (null tant que rien n'a ete ecrit).</summary>
    public static string? CurrentFile => _logFile;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                // Anti-spam : un message identique repete en moins de 2 s est ignore
                // (ex. erreur dans un timer a 500 ms -> 2 lignes/s max, pas 120/min).
                var now = DateTime.Now;
                if (message == _lastMessage && (now - _lastMessageAt).TotalSeconds < 2) return;
                _lastMessage = message;
                _lastMessageAt = now;

                if (_logFile is null)
                {
                    var dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CapsuleMedia", "logs");
                    Directory.CreateDirectory(dir);
                    _logFile = Path.Combine(dir, $"{AppName}-{now:yyyyMMdd}.log");
                    CleanupOldLogs(dir);
                }
                File.AppendAllText(_logFile, $"{now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { /* jamais d'exception depuis le logger */ }
    }

    /// <summary>Supprime les logs de plus de 14 jours.</summary>
    private static void CleanupOldLogs(string dir)
    {
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*.log"))
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-14))
                    File.Delete(f);
        }
        catch { }
    }
}
