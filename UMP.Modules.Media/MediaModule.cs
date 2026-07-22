using LibVLCSharp.Shared;
using VlcMedia = LibVLCSharp.Shared.Media;

namespace UMP.Modules.Media;

public class MediaModule : IDisposable
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;
    private VlcMedia? _currentMedia;
    private volatile bool _disposed;
    private volatile bool _isLooping;
    private volatile bool _stopRequested;
    private volatile string? _currentFilePath;

    /// <summary>
    /// Serialise l'acces natif au MediaPlayer (Stop/Dispose/lecture d'etat) pour
    /// eviter une AccessViolation si un thread lit l'etat pendant que Dispose libere
    /// le handle natif (ex. fermeture de fenetre pendant le polling de demarrage).
    /// </summary>
    private readonly object _playerLock = new();

    public MediaPlayer Player => _mediaPlayer;

    /// <summary>
    /// Lecture d'etat thread-safe : retourne null si le module est dispose,
    /// au lieu de toucher un MediaPlayer natif potentiellement libere.
    /// </summary>
    public LibVLCSharp.Shared.VLCState? SafeState
    {
        get
        {
            lock (_playerLock)
            {
                if (_disposed) return null;
                try { return _mediaPlayer.State; } catch { return null; }
            }
        }
    }
    public string? CurrentFilePath => _currentFilePath;
    public LibVLC GetLibVLC() => _libVLC;
    public MediaPlayer CreateSecondaryPlayer() => new MediaPlayer(_libVLC);


    public bool IsLooping
    {
        get => _isLooping;
        set => _isLooping = value;
    }

    public event EventHandler? OnMediaEnded;

    private static LibVLC? _sharedLibVLC;
    private static readonly object _libVLCLock = new();
    private readonly bool _ownsLibVLC;

    /// <summary>LibVLC partage entre toutes les instances — initialise une seule fois (thread-safe)</summary>
    public static LibVLC SharedLibVLC
    {
        get
        {
            if (_sharedLibVLC is null)
            {
                lock (_libVLCLock)
                    _sharedLibVLC ??= new LibVLC(
                        "--no-volume-save", "--aout=directsound", "--no-video-title-show",
                        "--no-osd", "--no-snapshot-preview", "--no-stats");
            }
            return _sharedLibVLC;
        }
    }

    public MediaModule() : this(null) { }

    public MediaModule(LibVLC? sharedVlc)
    {
        if (sharedVlc is not null)
        {
            _libVLC = sharedVlc;
            _ownsLibVLC = false;
        }
        else
        {
            _libVLC = SharedLibVLC;
            _ownsLibVLC = false;
        }
        _mediaPlayer = new MediaPlayer(_libVLC);
        _mediaPlayer.Volume = 100;

        _mediaPlayer.EndReached += (_, _) =>
        {
            if (_stopRequested) { _stopRequested = false; return; }
            if (_isLooping && _currentFilePath is not null)
            {
                var path = _currentFilePath;
                Task.Run(() => Play(path));
            }
            else
            {
                OnMediaEnded?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>
    /// Lecture — thread-safe, peut etre appele depuis n'importe quel thread.
    /// Les appelants UI doivent wrapper dans Task.Run.
    /// </summary>
    public void Play(string filePath)
    {
        if (_disposed) return;
        _stopRequested = false;

        var ext = Path.GetExtension(filePath).ToLower();
        var isImage = ext is ".png" or ".jpg"
            or ".jpeg" or ".bmp" or ".gif";
        if (isImage) return;

        // Ne pas relancer si c'est deja le meme fichier en cours de lecture
        if (_currentFilePath == filePath && _mediaPlayer.IsPlaying) return;

        _currentFilePath = filePath;

        try
        {
            var oldMedia = _currentMedia;
            var media = new VlcMedia(_libVLC, filePath, FromType.FromPath);

            _currentMedia = media;
            _mediaPlayer.Play(media);

            // Delai avant de disposer l'ancien media
            // pour laisser LibVLC finir la transition
            if (oldMedia is not null)
                Task.Delay(100).ContinueWith(_ =>
                {
                    try { oldMedia.Dispose(); }
                    catch { }
                });
        }
        catch (Exception ex)
        {
            // Dispose pendant un switch = benin ; tout autre echec doit laisser une trace
            if (!_disposed) UMP.Core.Log.Warn($"MediaModule.Play echec sur '{filePath}' : {ex.Message}");
        }
    }

    public void Pause() => _mediaPlayer.Pause();

    public void Stop()
    {
        _stopRequested = true;
        Task.Run(() =>
        {
            lock (_playerLock)
            {
                if (_disposed) return;
                try { _mediaPlayer.Stop(); }
                catch { }
            }
        });
    }

    public void SetVolume(int volume)
        => _mediaPlayer.Volume = Math.Clamp(volume, 0, 100);

    /// <summary>Positionne la lecture de facon thread-safe (no-op si dispose).</summary>
    public bool TrySetPosition(float position)
    {
        lock (_playerLock)
        {
            if (_disposed) return false;
            try { _mediaPlayer.Position = position; return true; } catch { return false; }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _stopRequested = true;
        // Serialise avec Stop()/SafeState : garantit qu'aucun thread ne touche le
        // player natif pendant qu'on le libere (evite AccessViolation a la fermeture).
        lock (_playerLock)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _mediaPlayer.Stop();
                _currentMedia?.Dispose();
                _mediaPlayer.Dispose();
                if (_ownsLibVLC) _libVLC.Dispose();
            }
            catch (Exception ex) { UMP.Core.Log.Warn($"MediaModule.Dispose : {ex.Message}"); }
        }
        GC.SuppressFinalize(this);
    }
}
