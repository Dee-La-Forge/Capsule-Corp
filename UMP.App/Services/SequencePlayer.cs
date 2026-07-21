using UMP.Core.Models;

namespace UMP.App.Services;

public class SequencePlayer : IDisposable
{
    private readonly Zone _zone;
    private readonly Action<string> _playAction;
    private readonly Func<long> _getCurrentMs;
    private readonly Action<ButtonAction> _executeAction;
    private bool _isRunning;
    private bool _disposed;

    public bool IsRunning => _isRunning;
    public event Action<int>? CurrentIndexChanged;
    public event Action? PlaybackStarted;

    public SequencePlayer(
        Zone zone,
        Action<string> playAction,
        Func<long> getCurrentMs,
        Action<ButtonAction> executeAction)
    {
        _zone = zone;
        _playAction = playAction;
        _getCurrentMs = getCurrentMs;
        _executeAction = executeAction;
    }

    public void Start()
    {
        if (_zone.Sequence is null) return;
        _isRunning = true;
        _zone.Sequence.CurrentIndex = 0;
        PlayCurrentItem();
    }

    public void Stop() => _isRunning = false;

    public void JumpTo(int index)
    {
        if (_zone.Sequence is null) return;
        var seq = _zone.Sequence;
        if (index < 0 || index >= seq.Items.Count) return;

        var item = seq.Items[index];
        if (string.IsNullOrEmpty(item.EffectivePath)) return;

        _isRunning = true;
        seq.CurrentIndex = index;
        CurrentIndexChanged?.Invoke(index);
        PlaybackStarted?.Invoke();

        var path = item.EffectivePath!;
        Task.Run(() => _playAction(path));
    }

    public void Tick(long currentMs)
    {
        if (!_isRunning || _zone.Sequence is null) return;
        var seq = _zone.Sequence;
        if (seq.CurrentIndex < 0 || seq.CurrentIndex >= seq.Items.Count) return;

        var item = seq.Items[seq.CurrentIndex];

        // Plan fixe avec duree fixe
        if (item.IsImageSlide &&
            item.SlideDuration == ImageSlideDuration.Fixed &&
            item.DurationMs.HasValue)
        {
            if (currentMs >= item.DurationMs.Value)
                PlayNextItem();
            return;
        }

        // Plan fixe UntilClick — ne rien faire
        if (item.IsImageSlide) return;
    }

    public void OnMediaEnded()
    {
        if (!_isRunning) return;
        PlayNextItem();
    }

    public void OnSlideCompleted()
    {
        if (!_isRunning) return;
        PlayNextItem();
    }

    private void PlayCurrentItem()
    {
        if (_zone.Sequence is null) return;
        var seq = _zone.Sequence;

        // Sauter les items sans path defini
        // PAS de File.Exists ici — peut etre appele depuis UI thread
        while (seq.CurrentIndex < seq.Items.Count
               && string.IsNullOrEmpty(seq.Items[seq.CurrentIndex].EffectivePath))
            seq.CurrentIndex++;

        if (seq.CurrentIndex >= seq.Items.Count)
        {
            HandleEnd();
            return;
        }

        var item = seq.Items[seq.CurrentIndex];
        CurrentIndexChanged?.Invoke(seq.CurrentIndex);
        PlaybackStarted?.Invoke();

        var path = item.EffectivePath!;
        Task.Run(() => _playAction(path));
    }

    private void PlayNextItem()
    {
        if (_zone.Sequence is null) return;
        var seq = _zone.Sequence;
        seq.CurrentIndex++;

        if (seq.CurrentIndex >= seq.Items.Count)
        {
            if (seq.IsLooping)
            {
                seq.CurrentIndex = 0;
                        PlayCurrentItem();
            }
            else
                _isRunning = false;
            return;
        }

        PlayCurrentItem();
    }

    private void HandleEnd() => PlayNextItem();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
