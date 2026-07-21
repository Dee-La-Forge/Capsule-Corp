using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;

namespace UMP.Shared;

/// <summary>
/// Lecteur video WPF qui decode via FFMediaToolkit (FFmpeg) et rend les frames
/// avec leur canal alpha (BGRA premultiplie) dans un WriteableBitmap.
/// Remplace MediaElement pour les PiP : permet d'afficher des videos avec
/// transparence (ProRes 4444, etc.) que MediaElement ne sait pas rendre.
/// Drop-in : herite de Image, donc s'utilise comme enfant d'un Border.
/// </summary>
public sealed class AlphaVideoView : System.Windows.Controls.Image, IDisposable
{
    private MediaFile? _file;
    private WriteableBitmap? _wb;
    private byte[]? _buf;
    private int _w, _h;
    private double _frameMs = 40;
    private string? _path;

    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _paused;     // pause explicite (video principale en pause)
    private volatile bool _visible = true; // gate de rendu (PiP cache => on decode mais on ne rend pas)
    private volatile bool _disposed;
    private readonly object _gate = new();

    public bool IsLooping { get; set; } = true;

    public AlphaVideoView()
    {
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
        IsVisibleChanged += (_, e) => _visible = (bool)e.NewValue;
    }

    /// <summary>Definit le fichier a lire (sans demarrer).</summary>
    public void Open(string path) => _path = path;

    /// <summary>Demarre (ou reprend) la lecture.</summary>
    public void Play()
    {
        if (_disposed) return;
        _paused = false;
        if (_running) return;
        if (string.IsNullOrEmpty(_path)) return;
        _running = true;
        _thread = new Thread(DecodeLoop) { IsBackground = true, Name = "AlphaVideoView" };
        _thread.Start();
    }

    /// <summary>Met en pause la lecture (gel de la frame courante).</summary>
    public void Pause() => _paused = true;

    /// <summary>Reprend apres une pause.</summary>
    public void Resume() => _paused = false;

    private void DecodeLoop()
    {
        try
        {
            if (!OpenFile()) return;

            Dispatcher.Invoke(() =>
            {
                if (_disposed) return;
                _wb = new WriteableBitmap(_w, _h, 96, 96, PixelFormats.Pbgra32, null);
                Source = _wb;
            });
            _buf = new byte[_w * 4 * _h];

            var sw = System.Diagnostics.Stopwatch.StartNew();
            long nextDue = 0;

            while (_running && !_disposed)
            {
                if (_paused) { Thread.Sleep(30); sw.Restart(); nextDue = 0; continue; }

                ImageData img = default;
                bool ok;
                lock (_gate) { ok = _file != null && _file.Video.TryGetNextFrame(out img); }
                if (!ok)
                {
                    if (IsLooping && ReopenFile()) { sw.Restart(); nextDue = 0; continue; }
                    break;
                }

                // Ne convertir/afficher que si le PiP est visible (le decodage continue
                // pour garder la lecture synchronisee dans le temps reel).
                if (_visible && _buf != null)
                {
                    Premultiply(img);
                    if (!_running || _disposed) break;
                    try
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_disposed || _wb == null) return;
                            _wb.WritePixels(new Int32Rect(0, 0, _w, _h), _buf, _w * 4, 0);
                        }, DispatcherPriority.Render);
                    }
                    catch (OperationCanceledException) { break; }
                }

                nextDue += (long)_frameMs;
                long wait = nextDue - sw.ElapsedMilliseconds;
                if (wait > 1) Thread.Sleep((int)Math.Min(wait, 200));
            }
        }
        catch (Exception ex)
        {
            // erreurs IO/decodage -> arret du PiP, mais avec une trace pour diagnostic
            if (!_disposed) UMP.Core.Log.Warn($"AlphaVideoView : arret decodage '{_path}' : {ex.Message}");
        }
        finally { CloseFile(); }
    }

    private bool OpenFile()
    {
        try
        {
            FFmpegInit.Ensure();
            if (!FFmpegInit.Available)
            {
                UMP.Core.Log.Warn($"AlphaVideoView : FFmpeg indisponible, PiP '{_path}' non lu");
                return false;
            }
            var opts = new MediaOptions { VideoPixelFormat = ImagePixelFormat.Bgra32, StreamsToLoad = MediaMode.Video };
            lock (_gate) { _file = MediaFile.Open(_path!, opts); }
            var info = _file.Video.Info;
            _w = info.FrameSize.Width;
            _h = info.FrameSize.Height;
            var fps = info.AvgFrameRate;
            _frameMs = (fps >= 1 && fps <= 240) ? 1000.0 / fps : 40;
            return _w > 0 && _h > 0;
        }
        catch (Exception ex)
        {
            UMP.Core.Log.Warn($"AlphaVideoView : ouverture echouee '{_path}' : {ex.Message}");
            return false;
        }
    }

    private bool ReopenFile()
    {
        try
        {
            var opts = new MediaOptions { VideoPixelFormat = ImagePixelFormat.Bgra32, StreamsToLoad = MediaMode.Video };
            lock (_gate)
            {
                try { _file?.Dispose(); } catch { }
                _file = MediaFile.Open(_path!, opts);
            }
            return true;
        }
        catch { return false; }
    }

    private void CloseFile()
    {
        lock (_gate) { try { _file?.Dispose(); } catch { } _file = null; }
    }

    private unsafe void Premultiply(ImageData img)
    {
        var data = img.Data; // BGRA droit (alpha non premultiplie)
        int stride = img.Stride;
        int w = _w, h = _h;
        var buf = _buf!;
        fixed (byte* srcBase = data)
        fixed (byte* dstBase = buf)
        {
            for (int y = 0; y < h; y++)
            {
                byte* s = srcBase + y * stride;
                byte* d = dstBase + y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    byte a = s[3];
                    if (a == 255)
                    {
                        d[0] = s[0]; d[1] = s[1]; d[2] = s[2]; d[3] = 255;
                    }
                    else if (a == 0)
                    {
                        *((int*)d) = 0;
                    }
                    else
                    {
                        d[0] = (byte)(s[0] * a / 255);
                        d[1] = (byte)(s[1] * a / 255);
                        d[2] = (byte)(s[2] * a / 255);
                        d[3] = a;
                    }
                    s += 4; d += 4;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        CloseFile();
    }
}
