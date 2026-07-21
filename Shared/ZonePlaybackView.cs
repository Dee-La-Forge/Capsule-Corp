using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UMP.Core;
using UMP.Core.Models;
using UMP.Core.Services;
using UMP.Modules.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Stretch = System.Windows.Media.Stretch;

namespace UMP.Shared;

/// <summary>
/// Surface de lecture d'une zone : video LibVLC, plans fixes, overlays
/// (boutons, images, sous-titres, PiP alpha) et enchainement de sequence.
/// MOTEUR UNIQUE partage par l'Apercu (editeur) et le Player : toute
/// divergence entre l'apercu et le rendu final est un bug par construction.
/// Structure visuelle identique a l'ancien XAML des deux fenetres :
/// VideoView > Grid > (Border ImageOverlay > Image) + Canvas OverlayCanvas.
/// </summary>
public sealed class ZonePlaybackView : Grid
{
    private readonly Zone _zone;
    /// <summary>Resolution des chemins (Player : relatifs a l'exe ; Apercu : identite).</summary>
    private readonly Func<string, string> _resolvePath;
    private readonly MediaModule _mediaModule;
    private readonly LibVLCSharp.WPF.VideoView _videoView;
    private readonly Border _imageOverlay;
    private readonly Image _slideImage;
    private readonly Canvas _overlayCanvas;

    private DispatcherTimer? _timer;
    private System.Diagnostics.Stopwatch? _slideStopwatch;
    private bool _disposed;
    private bool _started;
    private readonly Dictionary<string, List<SubtitleEntry>> _subCache = new();
    private readonly Dictionary<string, BitmapImage> _imageCache = new();
    private double _mediaW, _mediaH;
    private int _jumpEndIndex = -1;
    private readonly HashSet<string> _pipToggledVisible = new();
    private readonly Dictionary<string, Border> _pipBorders = new();
    private bool _refreshingOverlays;

    // ===== Registre statique : routage cross-zone + dispatch boutons physiques =====

    /// <summary>Surfaces vivantes (UI thread uniquement).</summary>
    private static readonly List<ZonePlaybackView> _instances = new();

    /// <summary>Id de la zone affichee par cette surface.</summary>
    public string ZoneId => _zone.Id;

    /// <summary>Stop + retour a l'item 0 sur toutes les surfaces vivantes.</summary>
    public static void StopAllScreens()
    {
        foreach (var v in _instances.ToList()) v.HandleStopAllScreens();
    }

    /// <summary>Saut vers une plage d'items sur toutes les surfaces vivantes.</summary>
    public static void JumpToItemAllScreens(int start, int end)
    {
        foreach (var v in _instances.ToList()) v.HandleJumpToItemAllScreens(start, end);
    }

    /// <summary>
    /// Route une action de bouton physique :
    /// - actions globales -> toutes les surfaces ;
    /// - PiP sans zone cible -> toutes (le PiP peut etre sur n'importe quel ecran) ;
    /// - actions per-zone -> surface de la zone cible, ou premiere surface (compat).
    /// </summary>
    public static void DispatchGlobalAction(ButtonAction action)
    {
        switch (action.Type)
        {
            case ButtonActionType.StopAllScreens:
                StopAllScreens();
                break;
            case ButtonActionType.JumpToItemAllScreens:
                if (action.TargetItemIndex.HasValue)
                    JumpToItemAllScreens(action.TargetItemIndex.Value,
                        action.EndItemIndex ?? action.TargetItemIndex.Value);
                break;
            case ButtonActionType.TogglePip:
            case ButtonActionType.ShowPip:
            case ButtonActionType.HidePip:
                foreach (var v in _instances.ToList())
                    if (string.IsNullOrEmpty(action.ZoneId) || v.ZoneId == action.ZoneId)
                        v.ExecuteActionLocal(action);
                break;
            default:
                var target = !string.IsNullOrEmpty(action.ZoneId)
                    ? _instances.FirstOrDefault(v => v.ZoneId == action.ZoneId)
                    : _instances.FirstOrDefault();
                if (target is null)
                    Log.Warn($"Bouton physique {action.Type} : zone cible '{action.ZoneId}' introuvable");
                else
                    target.ExecuteActionLocal(action);
                break;
        }
    }

    // ===== Construction =====

    public ZonePlaybackView(Zone zone, Func<string, string>? resolvePath = null)
    {
        _zone = zone;
        _resolvePath = resolvePath ?? (p => p);

        _mediaModule = new MediaModule();
        _mediaModule.SetVolume(zone.IsMuted ? 0 : zone.Volume);
        _mediaModule.IsLooping = zone.IsLooping;
        _mediaModule.OnMediaEnded += OnMediaEnded;

        // Arborescence identique a l'ancien XAML des fenetres Apercu/Player.
        _slideImage = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false };
        _imageOverlay = new Border
        {
            Background = Brushes.Black,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = _slideImage
        };
        _overlayCanvas = new Canvas { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
        var inner = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
        inner.Children.Add(_imageOverlay);
        inner.Children.Add(_overlayCanvas);
        _videoView = new LibVLCSharp.WPF.VideoView { Background = Brushes.Black, Content = inner };
        Background = Brushes.Black;
        Children.Add(_videoView);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += Timer_Tick;

        _overlayCanvas.SizeChanged += (s, e) => { if (_mediaW > 0) RefreshOverlays(); };

        Loaded += (s, e) =>
        {
            if (_started || _disposed) return;
            _started = true;
            _videoView.MediaPlayer = _mediaModule.Player;
            try
            {
                var host = RenderHelpers.FindChild<System.Windows.Forms.Integration.WindowsFormsHost>(_videoView);
                if (host?.Child is not null)
                    host.Child.BackColor = System.Drawing.Color.Black;
            }
            catch { }
            StartPlayback();
        };

        _instances.Add(this);
    }

    /// <summary>Arret complet et liberation (a appeler a la fermeture de la fenetre hote).</summary>
    public void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;
        _instances.Remove(this);
        _timer?.Stop(); _timer = null;
        _slideStopwatch?.Stop(); _slideStopwatch = null;
        _mediaModule.OnMediaEnded -= OnMediaEnded;
        ResetPipToggleState();
        // Detacher le player du VideoView (thread UI) avant Dispose : evite un
        // crash natif si le VideoView WPF touche un player deja libere.
        try { _videoView.MediaPlayer = null; } catch { }
        var mm = _mediaModule;
        Task.Run(() =>
        {
            // Dispose serialise Stop en interne (verrou _playerLock).
            try { mm.Dispose(); } catch { }
        });
    }

    // ===== Lecture =====

    private void StartPlayback()
    {
        var seq = _zone.Sequence;
        if (seq is not null && seq.Items.Count > 0)
        {
            seq.CurrentIndex = 0;
            var item = seq.Items[0];
            var path = item.EffectivePath;
            if (!string.IsNullOrEmpty(path))
                PlayMedia(_resolvePath(path), item.IsImageSlide);
        }
        else if (!string.IsNullOrEmpty(_zone.MediaFilePath))
        {
            PlayMedia(_resolvePath(_zone.MediaFilePath), false);
        }
    }

    private void ResetPipToggleState()
    {
        _pipToggledVisible.Clear();
        // Stopper, liberer et retirer les anciens PiP du canvas
        foreach (var pb in _pipBorders.Values)
        {
            if (pb.Child is AlphaVideoView av)
                try { av.Dispose(); } catch { }
            _overlayCanvas.Children.Remove(pb);
        }
        _pipBorders.Clear();
        // Limiter le cache sous-titres
        if (_subCache.Count > 20) _subCache.Clear();
    }

    private void PlayMedia(string path, bool isImage)
    {
        ResetPipToggleState();
        if (isImage)
        {
            // Arreter la video precedente : sinon son audio continue derriere l'image.
            var mmStop = _mediaModule;
            Task.Run(() => { try { mmStop.Stop(); } catch { } });
            Task.Run(() =>
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit(); bmp.Freeze();

                    var imgW = bmp.PixelWidth;
                    var imgH = bmp.PixelHeight;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_disposed) return;
                        _slideImage.Source = bmp;
                        _imageOverlay.Visibility = Visibility.Visible;
                        if (imgW > 0 && imgH > 0) { _mediaW = imgW; _mediaH = imgH; }
                        _slideStopwatch = System.Diagnostics.Stopwatch.StartNew();
                        _timer?.Start();
                        RefreshOverlays();
                    }));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Chargement image echoue '{path}' : {ex.Message}");
                }
            });
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _imageOverlay.Visibility = Visibility.Collapsed;
                _slideImage.Source = null;
            }));
            var mm = _mediaModule;
            Task.Run(() => { try { mm.Play(path); } catch { } });
            Task.Run(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    Thread.Sleep(100);
                    if (_disposed) return;
                    try
                    {
                        var state = mm.SafeState;   // null si dispose -> ne pas toucher le player natif
                        if (state is null) return;
                        if (state == LibVLCSharp.Shared.VLCState.Playing)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            { if (!_disposed) { _timer?.Start(); RefreshOverlays(); } }));
                            return;
                        }
                    }
                    catch { return; }
                }
            });
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        try { Timer_TickCore(); }
        catch (Exception ex) { Log.Warn($"ZonePlaybackView.Timer_Tick : {ex.Message}"); }
    }

    private void Timer_TickCore()
    {
        if (_imageOverlay.Visibility == Visibility.Visible && _slideStopwatch is not null)
        {
            var elapsed = _slideStopwatch.ElapsedMilliseconds;
            var seq = _zone.Sequence;
            var curItem = seq is not null && seq.CurrentIndex >= 0 && seq.CurrentIndex < seq.Items.Count
                ? seq.Items[seq.CurrentIndex] : null;

            if (curItem?.SlideDuration == ImageSlideDuration.UntilClick)
            {
                UpdateOverlayVisibility(0);
                return;
            }

            long total = curItem?.DurationMs ?? 5000;
            UpdateOverlayVisibility(elapsed);
            if (elapsed >= total)
            {
                if (curItem?.IsLooping == true)
                    _slideStopwatch = System.Diagnostics.Stopwatch.StartNew();
                else
                    OnItemFinished();
            }
            return;
        }

        // Video — lire la position
        var player = _mediaModule.Player;
        if (player is null || player.State != LibVLCSharp.Shared.VLCState.Playing) return;

        if (_mediaW <= 0 || _mediaH <= 0)
        {
            uint vw = 0, vh = 0;
            try { player.Size(0, ref vw, ref vh); } catch { }
            if (vw > 0 && vh > 0)
            { _mediaW = vw; _mediaH = vh; RefreshOverlays(); }
        }

        var length = player.Length;
        if (length <= 0) return;
        var currentMs = (long)(player.Position * length);
        UpdateOverlayVisibility(currentMs);
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_disposed) return;
                if (_mediaModule.IsLooping && _zone.Sequence is null) return;
                var seq = _zone.Sequence;
                if (seq is null) return;
                var idx = seq.CurrentIndex;
                if (idx < 0 || idx >= seq.Items.Count) return;
                var curItem = seq.Items[idx];
                if (curItem.IsLooping)
                {
                    var path = curItem.EffectivePath;
                    if (!string.IsNullOrEmpty(path))
                    {
                        _slideStopwatch?.Stop(); _slideStopwatch = null;
                        PlayMedia(_resolvePath(path), curItem.IsImageSlide);
                        return;
                    }
                }
                OnItemFinished();
            }
            catch (Exception ex) { Log.Warn($"ZonePlaybackView.OnMediaEnded : {ex.Message}"); }
        }));
    }

    private void OnItemFinished()
    {
        var seq = _zone.Sequence;
        if (seq is null || seq.Items.Count == 0) return;

        // Fin de la plage JumpToItemAllScreens -> retour item 0
        if (_jumpEndIndex >= 0 && seq.CurrentIndex >= _jumpEndIndex)
        {
            _jumpEndIndex = -1;
            seq.CurrentIndex = 0;
            var first = seq.Items[0];
            var firstPath = first.EffectivePath;
            if (!string.IsNullOrEmpty(firstPath))
            {
                _slideStopwatch?.Stop(); _slideStopwatch = null;
                PlayMedia(_resolvePath(firstPath), first.IsImageSlide);
            }
            return;
        }

        // Avancement normal
        seq.CurrentIndex++;
        if (seq.CurrentIndex >= seq.Items.Count)
        {
            if (seq.IsLooping)
                seq.CurrentIndex = 0;
            else
            { _timer?.Stop(); return; }
        }

        var item = seq.Items[seq.CurrentIndex];
        var path = item.EffectivePath;
        if (!string.IsNullOrEmpty(path))
        {
            _slideStopwatch?.Stop(); _slideStopwatch = null;
            PlayMedia(_resolvePath(path), item.IsImageSlide);
        }
    }

    private BitmapImage? LoadCachedImage(string path)
    {
        if (_imageCache.TryGetValue(path, out var cached)) return cached;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit(); bmp.Freeze();
            _imageCache[path] = bmp;
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Warn($"Chargement image echoue '{path}' : {ex.Message}");
            return null;
        }
    }

    // ===== Rendu des overlays =====

    private void RefreshOverlays()
    {
        if (_refreshingOverlays) return;
        _refreshingOverlays = true;
        try { RefreshOverlaysCore(); } finally { _refreshingOverlays = false; }
    }

    private void RefreshOverlaysCore()
    {
        // Retirer tout sauf les PiP persistants
        for (int i = _overlayCanvas.Children.Count - 1; i >= 0; i--)
        {
            var child = _overlayCanvas.Children[i];
            if (child is Border bd && bd.Tag is PipConfig) continue;
            _overlayCanvas.Children.RemoveAt(i);
        }
        var seq = _zone.Sequence;
        if (seq is null || seq.CurrentIndex < 0 || seq.CurrentIndex >= seq.Items.Count) return;

        var cw = _overlayCanvas.ActualWidth;
        var ch = _overlayCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        if (_mediaW <= 0 || _mediaH <= 0) return; // dimensions pas encore connues
        var scale = Math.Min(cw / _mediaW, ch / _mediaH);
        var rw = _mediaW * scale; var rh = _mediaH * scale;
        var ox = (cw - rw) / 2.0; var oy = (ch - rh) / 2.0;

        var item = seq.Items[seq.CurrentIndex];

        // Images overlay
        foreach (var img in item.ImageOverlays)
        {
            if (string.IsNullOrEmpty(img.ImagePath)) continue;
            try
            {
                var bmp = LoadCachedImage(_resolvePath(img.ImagePath));
                if (bmp is null) continue;
                var iw2 = RenderHelpers.SafeSize(img.Width * rw);
                var ih2 = RenderHelpers.SafeSize(img.Height * rh);
                var b = new Border
                {
                    Width = iw2, Height = ih2,
                    CornerRadius = new CornerRadius(img.CornerRadius * Math.Min(iw2, ih2)),
                    ClipToBounds = true, Background = Brushes.Transparent,
                    BorderThickness = new Thickness(Math.Max(0, img.BorderWidth)),
                    BorderBrush = RenderHelpers.ParseBrush(img.BorderColor, Color.FromRgb(255, 255, 255)),
                    Opacity = Math.Clamp(img.Opacity, 0, 1),
                    Child = new Image { Source = bmp, Stretch = Stretch.Uniform },
                    Tag = img, IsHitTestVisible = false
                };
                if (img.Rotation != 0)
                    b.RenderTransform = new RotateTransform(img.Rotation, b.Width / 2, b.Height / 2);
                Canvas.SetLeft(b, ox + img.X * rw); Canvas.SetTop(b, oy + img.Y * rh);
                _overlayCanvas.Children.Add(b);
            }
            catch { }
        }

        // PiP — reutiliser les vues existantes (pas de redemarrage du decodage FFmpeg)
        foreach (var pip in item.PictureInPictures)
        {
            if (string.IsNullOrEmpty(pip.VideoPath)) continue;
            Border b;
            if (_pipBorders.TryGetValue(pip.Id, out var existing))
            {
                b = existing;
                b.Width = RenderHelpers.SafeSize(pip.Width * rw);
                b.Height = RenderHelpers.SafeSize(pip.Height * rh);
                b.CornerRadius = new CornerRadius(pip.CornerRadius * Math.Min(b.Width, b.Height));
            }
            else
            {
                // Rendu via FFmpeg (FFMediaToolkit) pour supporter l'alpha (ProRes 4444, etc.)
                var av = new AlphaVideoView { Stretch = Stretch.Uniform, IsLooping = pip.IsLooping };
                av.Open(_resolvePath(pip.VideoPath));
                av.Play();
                var pw2 = RenderHelpers.SafeSize(pip.Width * rw);
                var ph2 = RenderHelpers.SafeSize(pip.Height * rh);
                b = new Border
                {
                    Width = pw2, Height = ph2,
                    CornerRadius = new CornerRadius(pip.CornerRadius * Math.Min(pw2, ph2)),
                    ClipToBounds = true, Background = Brushes.Transparent,
                    BorderThickness = new Thickness(Math.Max(0, pip.BorderWidth)),
                    BorderBrush = RenderHelpers.ParseBrush(pip.BorderColor, Color.FromRgb(255, 255, 255)),
                    Opacity = Math.Clamp(pip.Opacity, 0.1, 1), Child = av, Tag = pip, IsHitTestVisible = false
                };
                // Etat initial : visible, sauf si "Demarre cache"
                if (pip.StartHidden) _pipToggledVisible.Remove(pip.Id);
                else _pipToggledVisible.Add(pip.Id);
                b.Visibility = _pipToggledVisible.Contains(pip.Id)
                    ? Visibility.Visible : Visibility.Collapsed;
                _pipBorders[pip.Id] = b;
            }
            if (pip.Rotation != 0)
                b.RenderTransform = new RotateTransform(pip.Rotation, b.Width / 2, b.Height / 2);
            Canvas.SetLeft(b, ox + pip.X * rw); Canvas.SetTop(b, oy + pip.Y * rh);
            if (!_overlayCanvas.Children.Contains(b))
                _overlayCanvas.Children.Add(b);
        }

        // Boutons
        foreach (var btn in item.Buttons)
        {
            var bw2 = RenderHelpers.SafeSize(btn.Width * rw);
            var bh2 = RenderHelpers.SafeSize(btn.Height * rh);
            var fontScale = rh / 1080.0;
            var fs = Math.Clamp(btn.FontSize * (4.0 / 3.0) * fontScale, 6, 200);
            Color bgColor;
            try
            {
                bgColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(btn.BackgroundColor);
                if (bgColor.A == 255) bgColor.A = 200;
            }
            catch { bgColor = Color.FromArgb(200, 42, 42, 64); }
            var bgTop = Color.FromArgb(bgColor.A,
                (byte)Math.Min(255, bgColor.R + 25),
                (byte)Math.Min(255, bgColor.G + 25),
                (byte)Math.Min(255, bgColor.B + 25));
            var btnFw = RenderHelpers.ParseFontWeight(btn.FontWeight);
            var btnFf = RenderHelpers.ResolveFontFamily(
                string.IsNullOrEmpty(btn.CustomFontPath) ? null : _resolvePath(btn.CustomFontPath),
                btn.FontFamily);

            UIElement btnContent;
            var imgPath = btn.IsToggleActive && !string.IsNullOrEmpty(btn.ImagePathOn)
                ? btn.ImagePathOn : btn.ImagePath;
            BitmapImage? btnBmp = null;
            if (!string.IsNullOrEmpty(imgPath))
                btnBmp = LoadCachedImage(_resolvePath(imgPath));
            if (btnBmp is not null)
            {
                btnContent = new Image
                {
                    Source = btnBmp, Stretch = Stretch.Uniform,
                    Margin = new Thickness(Math.Max(0, btn.Padding))
                };
            }
            else
            {
                btnContent = new TextBlock
                {
                    Text = btn.Label, FontSize = fs, FontWeight = btnFw, FontFamily = btnFf,
                    Foreground = RenderHelpers.ParseBrush(btn.TextColor, Color.FromRgb(255, 255, 255)),
                    TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(Math.Max(0, btn.Padding))
                };
            }
            var capturedBtn = btn;
            var bWidth = Math.Max(0, btn.BorderWidth);
            var isOutside = btn.BorderPos == BorderPosition.Outside;
            var borderW = isOutside ? bw2 + bWidth * 2 : bw2;
            var borderH = isOutside ? bh2 + bWidth * 2 : bh2;
            var b = new Border
            {
                Width = borderW, Height = borderH,
                CornerRadius = new CornerRadius(btn.CornerRadius * Math.Min(bw2, bh2)),
                Background = new LinearGradientBrush(bgTop, bgColor, 90),
                BorderThickness = new Thickness(bWidth),
                BorderBrush = RenderHelpers.ParseBrush(btn.BorderColor, Color.FromRgb(255, 255, 255)),
                Opacity = Math.Clamp(btn.Opacity, 0.1, 1), Child = btnContent, Tag = btn,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            b.PreviewMouseLeftButtonUp += (s2, e2) =>
            {
                ExecuteButtonActions(capturedBtn);
                e2.Handled = true;
            };
            // Hover
            var normalBg = b.Background;
            var normalFg = (btnContent is TextBlock t0) ? t0.Foreground : null;
            var normalBorder = b.BorderBrush;
            var normalBorderW = b.BorderThickness;
            var normalFs = fs;
            var normalFw = btnFw;
            b.MouseEnter += (s2, e2) =>
            {
                try
                {
                    var hBg = (Color)System.Windows.Media.ColorConverter.ConvertFromString(capturedBtn.BackgroundColorHover);
                    if (hBg.A == 255) hBg.A = 220;
                    b.Background = new SolidColorBrush(hBg);
                }
                catch { }
                if (b.Child is TextBlock ht)
                {
                    try { ht.Foreground = RenderHelpers.ParseBrush(capturedBtn.TextColorHover, Color.FromRgb(255, 255, 255)); } catch { }
                    ht.FontSize = Math.Clamp(capturedBtn.FontSizeHover * (4.0 / 3.0) * (rh / 1080.0), 6, 200);
                    ht.FontWeight = RenderHelpers.ParseFontWeight(capturedBtn.FontWeightHover);
                }
                b.BorderBrush = RenderHelpers.ParseBrush(capturedBtn.BorderColorHover, Color.FromArgb(60, 255, 255, 255));
                b.BorderThickness = new Thickness(Math.Max(0, capturedBtn.BorderWidthHover));
            };
            b.MouseLeave += (s2, e2) =>
            {
                b.Background = normalBg;
                if (b.Child is TextBlock lt)
                {
                    if (normalFg is not null) lt.Foreground = normalFg;
                    lt.FontSize = normalFs;
                    lt.FontWeight = normalFw;
                }
                b.BorderBrush = normalBorder;
                b.BorderThickness = normalBorderW;
            };
            if (btn.Rotation != 0)
                b.RenderTransform = new RotateTransform(btn.Rotation, borderW / 2, borderH / 2);
            var posX = ox + btn.X * rw - (isOutside ? bWidth : 0);
            var posY = oy + btn.Y * rh - (isOutside ? bWidth : 0);
            Canvas.SetLeft(b, posX); Canvas.SetTop(b, posY);
            _overlayCanvas.Children.Add(b);
        }

        // Sous-titres
        foreach (var sub in item.Subtitles)
        {
            if (string.IsNullOrEmpty(sub.FilePath)) continue;
            var sw2 = RenderHelpers.SafeSize(sub.Width * rw);
            var fs2 = Math.Clamp(sub.FontSize * (4.0 / 3.0) * (rh / 1080.0), 6, 200);
            var subFf = RenderHelpers.ResolveFontFamily(
                string.IsNullOrEmpty(sub.CustomFontPath) ? null : _resolvePath(sub.CustomFontPath),
                sub.FontFamily);
            var stb = new TextBlock
            {
                Text = "", FontSize = fs2,
                FontFamily = subFf,
                FontWeight = RenderHelpers.ParseFontWeight(sub.FontWeight),
                Foreground = RenderHelpers.ParseBrush(sub.TextColor, Color.FromRgb(255, 255, 255)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = RenderHelpers.ParseTextAlignment(sub.TextAlign),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Math.Max(0, sub.Padding))
            };
            TextOptions.SetTextRenderingMode(stb, TextRenderingMode.ClearType);
            // Ombre
            if (sub.ShadowBlur > 0)
            {
                try
                {
                    var angle = Math.Atan2(sub.ShadowOffsetY, sub.ShadowOffsetX) * 180.0 / Math.PI;
                    var depth = Math.Sqrt(sub.ShadowOffsetX * sub.ShadowOffsetX + sub.ShadowOffsetY * sub.ShadowOffsetY);
                    stb.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(sub.ShadowColor),
                        BlurRadius = sub.ShadowBlur, Direction = angle, ShadowDepth = depth,
                        RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality
                    };
                }
                catch { }
            }
            // Contour — Grid avec TextBlock duplique derriere
            UIElement subContent;
            if (sub.OutlineWidth > 0)
            {
                var outlineGrid = new Grid();
                var strokeTb = new TextBlock
                {
                    Text = "", FontSize = fs2,
                    FontFamily = stb.FontFamily, FontWeight = stb.FontWeight,
                    Foreground = RenderHelpers.ParseBrush(sub.OutlineColor, Color.FromRgb(0, 0, 0)),
                    TextWrapping = TextWrapping.Wrap, TextAlignment = stb.TextAlignment,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = stb.Margin
                };
                strokeTb.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = sub.OutlineWidth };
                outlineGrid.Children.Add(strokeTb);
                outlineGrid.Children.Add(stb);
                subContent = outlineGrid;
            }
            else subContent = stb;

            var subCr = sub.CornerRadius * Math.Min(sw2, sw2 * 0.5);
            var b = new Border
            {
                Width = sw2,
                CornerRadius = new CornerRadius(subCr),
                Background = RenderHelpers.ParseBrush(sub.BackgroundColor, Color.FromArgb(150, 0, 0, 0)),
                BorderThickness = new Thickness(Math.Max(0, sub.BorderWidth)),
                BorderBrush = RenderHelpers.ParseBrush(sub.BorderColor, Color.FromRgb(255, 255, 255)),
                Opacity = Math.Clamp(sub.Opacity, 0.1, 1), Child = subContent, Tag = sub,
                IsHitTestVisible = false, Visibility = Visibility.Collapsed
            };
            Canvas.SetLeft(b, ox + sub.X * rw); Canvas.SetTop(b, oy + sub.Y * rh);
            _overlayCanvas.Children.Add(b);
        }
    }

    private void UpdateOverlayVisibility(long currentMs)
    {
        foreach (UIElement el in _overlayCanvas.Children)
        {
            if (el is Border b)
            {
                if (b.Tag is ButtonConfig btn)
                {
                    b.Visibility = (currentMs >= btn.InMs && (btn.OutMs <= 0 || currentMs <= btn.OutMs))
                        ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (b.Tag is ImageOverlayConfig img)
                {
                    b.Visibility = (currentMs >= img.InMs && (img.OutMs <= 0 || currentMs <= img.OutMs))
                        ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (b.Tag is PipConfig)
                {
                    // Visibilite PiP geree uniquement par ShowPip/HidePip/TogglePip
                }
                else if (b.Tag is SubtitleConfig sub)
                {
                    var subPath = _resolvePath(sub.FilePath);
                    if (!_subCache.TryGetValue(subPath, out var entries))
                    {
                        entries = SrtParser.Parse(subPath);
                        _subCache[subPath] = entries;
                    }
                    var entry = entries.FirstOrDefault(en => currentMs >= en.InMs && currentMs <= en.OutMs);
                    if (entry is not null)
                    {
                        b.Visibility = Visibility.Visible;
                        if (b.Child is TextBlock stb && stb.Text != entry.Text) stb.Text = entry.Text;
                        else if (b.Child is Grid g)
                        {
                            foreach (var c in g.Children)
                                if (c is TextBlock tb && tb.Text != entry.Text) tb.Text = entry.Text;
                        }
                    }
                    else b.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    // ===== Actions =====

    private void ExecuteButtonActions(ButtonConfig btn)
    {
        if (btn.IsToggle)
        {
            btn.IsToggleActive = !btn.IsToggleActive;
            // Mettre a jour le label du bouton toggle sans recreer les overlays
            foreach (UIElement el in _overlayCanvas.Children)
                if (el is Border tb && tb.Tag == btn && tb.Child is TextBlock ttb)
                    ttb.Text = btn.IsToggleActive ? btn.LabelOn : btn.Label;
        }
        var actions = btn.IsToggle && btn.IsToggleActive ? btn.ActionsOn : btn.Actions;
        foreach (var a in actions) ExecuteAction(a);
    }

    /// <summary>
    /// Execute une action, avec routage cross-zone : une action qui cible une
    /// autre zone est executee par la surface de cette zone.
    /// </summary>
    public void ExecuteAction(ButtonAction action)
    {
        if (!string.IsNullOrEmpty(action.ZoneId) && action.ZoneId != _zone.Id)
        {
            var target = _instances.FirstOrDefault(v => !v._disposed && v.ZoneId == action.ZoneId);
            if (target is null)
                Log.Warn($"Action {action.Type} : zone cible '{action.ZoneId}' introuvable");
            else
                target.ExecuteActionLocal(action);
            return;
        }
        ExecuteActionLocal(action);
    }

    private void ExecuteActionLocal(ButtonAction action)
    {
        switch (action.Type)
        {
            case ButtonActionType.Play:
            case ButtonActionType.PlaySequence:
                StartPlayback(); PipControlAll(true); break;
            case ButtonActionType.Pause:
            case ButtonActionType.PauseSequence:
                Task.Run(() => { try { _mediaModule.Pause(); } catch { } }); PipControlAll(false); break;
            case ButtonActionType.Stop:
            case ButtonActionType.StopSequence:
                _timer?.Stop();
                Task.Run(() => { try { _mediaModule.Stop(); } catch { } }); PipControlAll(false); break;
            case ButtonActionType.ToggleSequence:
                if (_mediaModule.Player.IsPlaying)
                { Task.Run(() => { try { _mediaModule.Pause(); } catch { } }); PipControlAll(false); }
                else { StartPlayback(); PipControlAll(true); }
                break;
            case ButtonActionType.JumpToItem:
                if (action.TargetItemIndex.HasValue && _zone.Sequence is not null)
                {
                    var idx = action.TargetItemIndex.Value;
                    if (idx >= 0 && idx < _zone.Sequence.Items.Count)
                    {
                        _zone.Sequence.CurrentIndex = idx;
                        var item = _zone.Sequence.Items[idx];
                        var path = item.EffectivePath;
                        if (!string.IsNullOrEmpty(path))
                        {
                            _slideStopwatch?.Stop(); _slideStopwatch = null;
                            PlayMedia(_resolvePath(path), item.IsImageSlide);
                        }
                    }
                }
                break;
            case ButtonActionType.PlayMedia:
                if (!string.IsNullOrEmpty(action.MediaPath))
                    PlayMedia(_resolvePath(action.MediaPath), false);
                break;
            case ButtonActionType.SwitchMedia:
                if (!string.IsNullOrEmpty(action.MediaPath))
                {
                    var pos = 0f;
                    try { pos = _mediaModule.Player.Position; } catch { }
                    PlayMedia(_resolvePath(action.MediaPath), false);
                    if (pos > 0)
                    {
                        var p = pos;
                        var mm = _mediaModule;
                        Task.Run(async () =>
                        {
                            for (int i = 0; i < 30; i++)
                            {
                                await Task.Delay(100);
                                if (_disposed) return;
                                var state = mm.SafeState;
                                if (state is null) return;
                                if (state == LibVLCSharp.Shared.VLCState.Playing)
                                { mm.TrySetPosition(p); break; }
                            }
                        });
                    }
                }
                break;
            case ButtonActionType.StopAllScreens:
                StopAllScreens();
                break;
            case ButtonActionType.JumpToItemAllScreens:
                if (action.TargetItemIndex.HasValue)
                    JumpToItemAllScreens(action.TargetItemIndex.Value,
                        action.EndItemIndex ?? action.TargetItemIndex.Value);
                break;
            case ButtonActionType.TogglePip:
                foreach (UIElement el in _overlayCanvas.Children)
                    if (el is Border pb && pb.Tag is PipConfig tp)
                    {
                        if (_pipToggledVisible.Contains(tp.Id))
                        { _pipToggledVisible.Remove(tp.Id); pb.Visibility = Visibility.Collapsed; }
                        else
                        { _pipToggledVisible.Add(tp.Id); pb.Visibility = Visibility.Visible; }
                    }
                break;
            case ButtonActionType.ShowPip:
                foreach (UIElement el in _overlayCanvas.Children)
                    if (el is Border spb && spb.Tag is PipConfig sp)
                    { _pipToggledVisible.Add(sp.Id); spb.Visibility = Visibility.Visible; }
                break;
            case ButtonActionType.HidePip:
                foreach (UIElement el in _overlayCanvas.Children)
                    if (el is Border hpb && hpb.Tag is PipConfig hp)
                    { _pipToggledVisible.Remove(hp.Id); hpb.Visibility = Visibility.Collapsed; }
                break;
        }
    }

    private void PipControlAll(bool play)
    {
        foreach (UIElement el in _overlayCanvas.Children)
            if (el is Border b && b.Tag is PipConfig && b.Child is AlphaVideoView av)
            {
                try
                {
                    if (play) av.Resume();
                    else av.Pause();
                }
                catch { }
            }
    }

    private void HandleStopAllScreens()
    {
        if (_disposed) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            _timer?.Stop();
            _slideStopwatch?.Stop(); _slideStopwatch = null;
            _jumpEndIndex = -1;
            // Revenir a l'item 0
            var seq = _zone.Sequence;
            if (seq is not null && seq.Items.Count > 0)
            {
                seq.CurrentIndex = 0;
                var item = seq.Items[0];
                var path = item.EffectivePath;
                if (!string.IsNullOrEmpty(path))
                {
                    PlayMedia(_resolvePath(path), item.IsImageSlide);
                    return;
                }
            }
            Task.Run(() => { try { _mediaModule.Stop(); } catch { } });
        });
    }

    private void HandleJumpToItemAllScreens(int startIndex, int endIndex)
    {
        if (_disposed) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            var seq = _zone.Sequence;
            if (seq is null || seq.Items.Count == 0) return;
            var idx = Math.Clamp(startIndex, 0, seq.Items.Count - 1);
            _jumpEndIndex = Math.Clamp(endIndex, idx, seq.Items.Count - 1);
            seq.CurrentIndex = idx;
            var item = seq.Items[idx];
            var path = item.EffectivePath;
            if (!string.IsNullOrEmpty(path))
            {
                _slideStopwatch?.Stop(); _slideStopwatch = null;
                PlayMedia(_resolvePath(path), item.IsImageSlide);
            }
        });
    }
}
