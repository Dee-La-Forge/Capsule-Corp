using System;
using System.IO;
using FFMediaToolkit;

namespace UMP.Shared;

/// <summary>
/// Initialise FFMediaToolkit en pointant vers les DLLs FFmpeg embarquees
/// dans le sous-dossier "ffmpeg" a cote de l'executable.
/// </summary>
public static class FFmpegInit
{
    private static bool _done;
    private static readonly object _lock = new();

    /// <summary>True si les DLLs FFmpeg ont ete localisees.</summary>
    public static bool Available { get; private set; }

    public static void Ensure()
    {
        if (_done) return;
        lock (_lock)
        {
            if (_done) return;
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
                if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "avcodec-61.dll")))
                {
                    FFmpegLoader.FFmpegPath = dir;
                    Available = true;
                }
                else
                    UMP.Core.Log.Warn($"FFmpeg introuvable dans '{dir}' : les PiP avec alpha ne seront pas rendus");
            }
            catch (Exception ex)
            {
                Available = false;
                UMP.Core.Log.Error("FFmpegInit : echec d'initialisation", ex);
            }
            _done = true;
        }
    }
}
