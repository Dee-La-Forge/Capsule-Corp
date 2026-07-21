using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using UMP.Core.Models;
using UMP.Modules.Media;
using UMP.Shared;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Image = System.Windows.Controls.Image;
using Stretch = System.Windows.Media.Stretch;

namespace UMP.Player;

public partial class PlayerWindow : Window
{

    private readonly Zone _zone;
    private readonly MediaModule _mediaModule;
    private System.Windows.Threading.DispatcherTimer? _timer;
    private System.Windows.Threading.DispatcherTimer? _escTimer;
    private System.Diagnostics.Stopwatch? _slideStopwatch;
    private bool _closed;
    private readonly Dictionary<string, List<SubtitleEntry>> _subCache = new();
    private readonly Dictionary<string, BitmapImage> _imageCache = new();
    private double _mediaW, _mediaH;
    private int _jumpEndIndex = -1;
    private readonly HashSet<string> _pipToggledVisible = new();
    private readonly Dictionary<string, Border> _pipBorders = new();
    private bool _refreshingOverlays;

    public static event Action? EscapePressed;
    public static event Action? StopAllScreensRequested;
    public static event Action<int, int>? JumpToItemAllScreensRequested;

    public static void InvokeStopAllScreens() => StopAllScreensRequested?.Invoke();
    public static void InvokeJumpToItemAllScreens(int start, int end) => JumpToItemAllScreensRequested?.Invoke(start, end);

    public PlayerWindow(Zone zone)
    {
        InitializeComponent();
        _zone = zone;
        _mediaModule = new MediaModule();
        _mediaModule.SetVolume(zone.IsMuted ? 0 : zone.Volume);
        _mediaModule.IsLooping = zone.IsLooping;
        _mediaModule.OnMediaEnded += OnMediaEnded;

        StopAllScreensRequested += OnStopAllScreens;
        JumpToItemAllScreensRequested += OnJumpToItemAllScreens;

        // Position on assigned screen — chercher par DeviceName d'abord, fallback sur index
        var screens = System.Windows.Forms.Screen.AllScreens;
        var idx = Math.Clamp(zone.ScreenIndex, 0, screens.Length - 1);
        if (!string.IsNullOrEmpty(zone.ScreenDeviceName))
        {
            var match = Array.FindIndex(screens, s => s.DeviceName == zone.ScreenDeviceName);
            if (match >= 0) idx = match;
        }
        var bounds = screens[idx].Bounds;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        WindowState = WindowState.Normal;

        _timer = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += Timer_Tick;

        OverlayCanvas.SizeChanged += (s, e) =>
        {
            if (_mediaW > 0) RefreshOverlays();
        };

        // Poll Escape because the native HWND captures keyboard focus
        _escTimer = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(100) };
        _escTimer.Tick += (s, e) =>
        {
            if ((System.Windows.Input.Keyboard.GetKeyStates(Key.Escape) & System.Windows.Input.KeyStates.Down) != 0)
                EscapePressed?.Invoke();
        };
        _escTimer.Start();

        Loaded += (s, e) =>
        {
            VideoView.MediaPlayer = _mediaModule.Player;
            try
            {
                var host = FindChild<System.Windows.Forms.Integration.WindowsFormsHost>(VideoView);
                if (host?.Child is not null)
                {
                    host.Child.BackColor = System.Drawing.Color.Black;
                }
            }
            catch { }
            StartPlayback();
        };
    }

    private void StartPlayback()
    {
        var seq = _zone.Sequence;
        if (seq is not null && seq.Items.Count > 0)
        {
            seq.CurrentIndex = 0;
            var item = seq.Items[0];
            var path = item.EffectivePath;
            if (!string.IsNullOrEmpty(path))
            {
                PlayMedia(ResolvePath(path), item.IsImageSlide);
            }
        }
        else if (!string.IsNullOrEmpty(_zone.MediaFilePath))
        {
            PlayMedia(ResolvePath(_zone.MediaFilePath), false);
        }
    }

    /// <summary>
    /// Resolve relative paths against the exe directory.
    /// </summary>
    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        var baseDir = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(baseDir, path));
    }

    private void ResetPipToggleState()
    {
        _pipToggledVisible.Clear();
        // Stopper, liberer et retirer les anciens PiP du canvas
        foreach (var pb in _pipBorders.Values)
        {
            if (pb.Child is AlphaVideoView av)
                try { av.Dispose(); } catch { }
            OverlayCanvas.Children.Remove(pb);
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
                        SlideImage.Source = bmp;
                        ImageOverlay.Visibility = Visibility.Visible;
                        if (imgW > 0 && imgH > 0) { _mediaW = imgW; _mediaH = imgH; }
                        _slideStopwatch = System.Diagnostics.Stopwatch.StartNew();
                        _timer?.Start();
                        RefreshOverlays();
                    }));
                }
                catch (Exception ex)
                {
                    UMP.Core.Log.Warn($"Chargement image echoue '{path}' : {ex.Message}");
                }
            });
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ImageOverlay.Visibility = Visibility.Collapsed;
                SlideImage.Source = null;
            }));
            var mm = _mediaModule;
            Task.Run(() => { try { mm.Play(path); } catch { } });
            Task.Run(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    Thread.Sleep(100);
                    if (_closed) return;
                    try
                    {
                        var state = mm.SafeState;   // null si dispose -> ne pas toucher le player natif
                        if (state is null) return;
                        if (state == LibVLCSharp.Shared.VLCState.Playing)
                        {
                            Dispatcher.BeginInvoke(new Action(() => { if (!_closed) { _timer?.Start(); RefreshOverlays(); } }));
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
        if (_closed) return;
        try { Timer_TickCore(); }
        catch (Exception ex) { UMP.Core.Log.Warn($"PlayerWindow.Timer_Tick : {ex.Message}"); }
    }

    private void Timer_TickCore()
    {
        if (ImageOverlay.Visibility == Visibility.Visible && _slideStopwatch is not null)
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

        // Video - read position
        try
        {
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
        catch { }
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_closed) return;
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
                        PlayMedia(ResolvePath(path), curItem.IsImageSlide);
                        return;
                    }
                }
                OnItemFinished();
            }
            catch { }
        }));
    }

    private void OnItemFinished()
    {
        var seq = _zone.Sequence;
        if (seq is null || seq.Items.Count == 0) return;

        // Si on a atteint la fin de la plage JumpToItemAllScreens → retour item 0
        if (_jumpEndIndex >= 0 && seq.CurrentIndex >= _jumpEndIndex)
        {
            _jumpEndIndex = -1;
            seq.CurrentIndex = 0;
            var first = seq.Items[0];
            var firstPath = first.EffectivePath;
            if (!string.IsNullOrEmpty(firstPath))
            {
                _slideStopwatch?.Stop(); _slideStopwatch = null;
                PlayMedia(ResolvePath(firstPath), first.IsImageSlide);
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
            PlayMedia(ResolvePath(path), item.IsImageSlide);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            EscapePressed?.Invoke();
            e.Handled = true;
        }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var r = FindChild<T>(child); if (r is not null) return r;
        }
        return null;
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
            UMP.Core.Log.Warn($"Chargement image echoue '{path}' : {ex.Message}");
            return null;
        }
    }

    private static SolidColorBrush ParseBrush(string hex, Color fallback)
    {
        try { return new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush(fallback); }
    }

    /// <summary>
    /// Garantit une taille d'overlay finie et non nulle. Un Border de largeur 0 (ou NaN)
    /// contenant un TextBlock TextWrapping=Wrap renvoie une DesiredSize NaN, ce qui fait
    /// planter la passe de layout WPF (InvalidOperationException relancee a chaque rendu).
    /// </summary>
    private static double SafeSize(double v) => double.IsFinite(v) && v >= 1 ? v : 1;

    private void RefreshOverlays()
    {
        if (_refreshingOverlays) return;
        _refreshingOverlays = true;
        try { RefreshOverlaysCore(); } finally { _refreshingOverlays = false; }
    }

    private void RefreshOverlaysCore()
    {
        // Retirer tout sauf les PiP persistants
        for (int i = OverlayCanvas.Children.Count - 1; i >= 0; i--)
        {
            var child = OverlayCanvas.Children[i];
            if (child is Border bd && bd.Tag is PipConfig) continue;
            OverlayCanvas.Children.RemoveAt(i);
        }
        var seq = _zone.Sequence;
        if (seq is null || seq.CurrentIndex < 0 || seq.CurrentIndex >= seq.Items.Count) return;

        var cw = OverlayCanvas.ActualWidth;
        var ch = OverlayCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        if (_mediaW <= 0 || _mediaH <= 0) return;
        var scale = Math.Min(cw / _mediaW, ch / _mediaH);
        var rw = _mediaW * scale; var rh = _mediaH * scale;
        var ox = (cw - rw) / 2.0; var oy = (ch - rh) / 2.0;

        var item = seq.Items[seq.CurrentIndex];

        // Image overlays
        foreach (var img in item.ImageOverlays)
        {
            if (string.IsNullOrEmpty(img.ImagePath)) continue;
            try
            {
                var imgPath = ResolvePath(img.ImagePath);
                var bmp = LoadCachedImage(imgPath);
                if (bmp is null) continue;
                var iw2 = SafeSize(img.Width * rw); var ih2 = SafeSize(img.Height * rh);
                var b = new Border
                {
                    Width = iw2, Height = ih2,
                    CornerRadius = new CornerRadius(img.CornerRadius * Math.Min(iw2, ih2)),
                    ClipToBounds = true, Background = Brushes.Transparent,
                    BorderThickness = new Thickness(Math.Max(0, img.BorderWidth)),
                    BorderBrush = ParseBrush(img.BorderColor, Color.FromRgb(255, 255, 255)),
                    Opacity = Math.Clamp(img.Opacity, 0, 1),
                    Child = new Image { Source = bmp, Stretch = Stretch.Uniform },
                    Tag = img, IsHitTestVisible = false
                };
                if (img.Rotation != 0) b.RenderTransform = new RotateTransform(img.Rotation, b.Width / 2, b.Height / 2);
                Canvas.SetLeft(b, ox + img.X * rw); Canvas.SetTop(b, oy + img.Y * rh);
                OverlayCanvas.Children.Add(b);
            }
            catch { }
        }

        // PiP — reutiliser les MediaElement existants
        foreach (var pip in item.PictureInPictures)
        {
            if (string.IsNullOrEmpty(pip.VideoPath)) continue;
            Border b;
            if (_pipBorders.TryGetValue(pip.Id, out var existing))
            {
                b = existing;
                b.Width = SafeSize(pip.Width * rw); b.Height = SafeSize(pip.Height * rh);
                b.CornerRadius = new CornerRadius(pip.CornerRadius * Math.Min(b.Width, b.Height));
            }
            else
            {
                var pipPath = ResolvePath(pip.VideoPath);
                // Rendu via FFmpeg (FFMediaToolkit) pour supporter l'alpha (ProRes 4444, etc.)
                var av = new AlphaVideoView { Stretch = Stretch.Uniform, IsLooping = pip.IsLooping };
                av.Open(pipPath);
                av.Play();
                var pw2 = SafeSize(pip.Width * rw); var ph2 = SafeSize(pip.Height * rh);
                b = new Border
                {
                    Width = pw2, Height = ph2,
                    CornerRadius = new CornerRadius(pip.CornerRadius * Math.Min(pw2, ph2)),
                    ClipToBounds = true, Background = Brushes.Transparent,
                    BorderThickness = new Thickness(Math.Max(0, pip.BorderWidth)),
                    BorderBrush = ParseBrush(pip.BorderColor, Color.FromRgb(255, 255, 255)),
                    Opacity = Math.Clamp(pip.Opacity, 0.1, 1), Child = av, Tag = pip, IsHitTestVisible = false
                };
                // Etat initial : visible, sauf si "Demarre cache"
                if (pip.StartHidden) _pipToggledVisible.Remove(pip.Id);
                else _pipToggledVisible.Add(pip.Id);
                b.Visibility = _pipToggledVisible.Contains(pip.Id)
                    ? Visibility.Visible : Visibility.Collapsed;
                _pipBorders[pip.Id] = b;
            }
            if (pip.Rotation != 0) b.RenderTransform = new RotateTransform(pip.Rotation, b.Width / 2, b.Height / 2);
            Canvas.SetLeft(b, ox + pip.X * rw); Canvas.SetTop(b, oy + pip.Y * rh);
            if (!OverlayCanvas.Children.Contains(b))
                OverlayCanvas.Children.Add(b);
        }

        // Buttons
        foreach (var btn in item.Buttons)
        {
            var bw2 = SafeSize(btn.Width * rw); var bh2 = SafeSize(btn.Height * rh);
            var fontScale = rh / 1080.0;
            var fs = Math.Clamp(btn.FontSize * (4.0 / 3.0) * fontScale, 6, 200);
            Color bgColor;
            try { bgColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(btn.BackgroundColor); if (bgColor.A == 255) bgColor.A = 200; }
            catch { bgColor = Color.FromArgb(200, 42, 42, 64); }
            var bgTop = Color.FromArgb(bgColor.A, (byte)Math.Min(255, bgColor.R + 25), (byte)Math.Min(255, bgColor.G + 25), (byte)Math.Min(255, bgColor.B + 25));
            var btnFw = ParseSubFontWeight(btn.FontWeight);
            System.Windows.Media.FontFamily btnFf;
            if (!string.IsNullOrEmpty(btn.CustomFontPath))
            {
                var resolvedFont = ResolvePath(btn.CustomFontPath);
                if (File.Exists(resolvedFont))
                    try { btnFf = ResolveFontFamily(new SubtitleConfig { CustomFontPath = resolvedFont, FontFamily = btn.FontFamily }); }
                    catch { btnFf = new System.Windows.Media.FontFamily(btn.FontFamily); }
                else btnFf = new System.Windows.Media.FontFamily(btn.FontFamily);
            }
            else btnFf = new System.Windows.Media.FontFamily(btn.FontFamily);
            UIElement btnContent;
            var imgPath = btn.IsToggleActive && !string.IsNullOrEmpty(btn.ImagePathOn)
                ? btn.ImagePathOn : btn.ImagePath;
            if (!string.IsNullOrEmpty(imgPath))
            {
                try
                {
                    var resolvedImg = ResolvePath(imgPath);
                    var bmp = LoadCachedImage(resolvedImg);
                    if (bmp is not null)
                        btnContent = new Image
                        {
                            Source = bmp, Stretch = Stretch.Uniform,
                            Margin = new Thickness(Math.Max(0, btn.Padding))
                        };
                    else throw new Exception();
                }
                catch
                {
                    btnContent = new TextBlock
                    {
                        Text = btn.Label, FontSize = fs, FontWeight = btnFw, FontFamily = btnFf,
                        Foreground = ParseBrush(btn.TextColor, Color.FromRgb(255, 255, 255)),
                        TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(Math.Max(0, btn.Padding))
                    };
                }
            }
            else
            {
                btnContent = new TextBlock
                {
                    Text = btn.Label, FontSize = fs, FontWeight = btnFw, FontFamily = btnFf,
                    Foreground = ParseBrush(btn.TextColor, Color.FromRgb(255, 255, 255)),
                    TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(Math.Max(0, btn.Padding))
                };
            }
            var capturedBtn = btn;
            var bWidth = Math.Max(0, btn.BorderWidth);
            var isOutside = btn.BorderPos == UMP.Core.Models.BorderPosition.Outside;
            var borderW = isOutside ? bw2 + bWidth * 2 : bw2;
            var borderH = isOutside ? bh2 + bWidth * 2 : bh2;
            var b = new Border
            {
                Width = borderW, Height = borderH,
                CornerRadius = new CornerRadius(btn.CornerRadius * Math.Min(bw2, bh2)),
                Background = new LinearGradientBrush(bgTop, bgColor, 90),
                BorderThickness = new Thickness(bWidth),
                BorderBrush = ParseBrush(btn.BorderColor, Color.FromRgb(255, 255, 255)),
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
                    try { ht.Foreground = ParseBrush(capturedBtn.TextColorHover, Color.FromRgb(255, 255, 255)); } catch { }
                    ht.FontSize = Math.Clamp(capturedBtn.FontSizeHover * (4.0 / 3.0) * (rh / 1080.0), 6, 200);
                    ht.FontWeight = ParseSubFontWeight(capturedBtn.FontWeightHover);
                }
                b.BorderBrush = ParseBrush(capturedBtn.BorderColorHover, Color.FromArgb(60, 255, 255, 255));
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
            if (btn.Rotation != 0) b.RenderTransform = new RotateTransform(btn.Rotation, borderW / 2, borderH / 2);
            var posX = ox + btn.X * rw - (isOutside ? bWidth : 0);
            var posY = oy + btn.Y * rh - (isOutside ? bWidth : 0);
            Canvas.SetLeft(b, posX); Canvas.SetTop(b, posY);
            OverlayCanvas.Children.Add(b);
        }

        // Subtitles
        foreach (var sub in item.Subtitles)
        {
            if (string.IsNullOrEmpty(sub.FilePath)) continue;
            var sw2 = SafeSize(sub.Width * rw);
            var fs2 = Math.Clamp(sub.FontSize * (4.0 / 3.0) * (rh / 1080.0), 6, 200);
            var stb = new TextBlock
            {
                Text = "", FontSize = fs2,
                FontFamily = ResolveFontFamily(sub),
                FontWeight = ParseSubFontWeight(sub.FontWeight),
                Foreground = ParseBrush(sub.TextColor, Color.FromRgb(255, 255, 255)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = ParseSubTextAlign(sub.TextAlign),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
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
                var outlineGrid = new System.Windows.Controls.Grid();
                var strokeTb = new TextBlock
                {
                    Text = "", FontSize = fs2,
                    FontFamily = stb.FontFamily, FontWeight = stb.FontWeight,
                    Foreground = ParseBrush(sub.OutlineColor, Color.FromRgb(0, 0, 0)),
                    TextWrapping = TextWrapping.Wrap, TextAlignment = stb.TextAlignment,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = stb.Margin
                };
                strokeTb.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = sub.OutlineWidth };
                outlineGrid.Children.Add(strokeTb);
                outlineGrid.Children.Add(stb);
                outlineGrid.Tag = strokeTb; // pour mettre a jour le texte
                subContent = outlineGrid;
            }
            else subContent = stb;

            var subCr = sub.CornerRadius * Math.Min(sw2, sw2 * 0.5);
            var b = new Border
            {
                Width = sw2,
                CornerRadius = new CornerRadius(subCr),
                Background = ParseBrush(sub.BackgroundColor, Color.FromArgb(150, 0, 0, 0)),
                BorderThickness = new Thickness(Math.Max(0, sub.BorderWidth)),
                BorderBrush = ParseBrush(sub.BorderColor, Color.FromRgb(255, 255, 255)),
                Opacity = Math.Clamp(sub.Opacity, 0.1, 1), Child = subContent, Tag = sub,
                IsHitTestVisible = false, Visibility = Visibility.Collapsed
            };
            Canvas.SetLeft(b, ox + sub.X * rw); Canvas.SetTop(b, oy + sub.Y * rh);
            OverlayCanvas.Children.Add(b);
        }
    }

    private void UpdateOverlayVisibility(long currentMs)
    {
        foreach (UIElement el in OverlayCanvas.Children)
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
                    var subPath = ResolvePath(sub.FilePath);
                    if (!_subCache.TryGetValue(subPath, out var entries))
                    {
                        entries = ParseSrtFile(subPath);
                        _subCache[subPath] = entries;
                    }
                    var entry = entries.FirstOrDefault(en => currentMs >= en.InMs && currentMs <= en.OutMs);
                    if (entry is not null)
                    {
                        b.Visibility = Visibility.Visible;
                        if (b.Child is TextBlock stb && stb.Text != entry.Text) stb.Text = entry.Text;
                        else if (b.Child is System.Windows.Controls.Grid g)
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

    private void ExecuteButtonActions(ButtonConfig btn)
    {
        if (btn.IsToggle)
        {
            btn.IsToggleActive = !btn.IsToggleActive;
            // Mettre a jour le label du bouton toggle sans recréer les overlays
            foreach (UIElement el in OverlayCanvas.Children)
                if (el is Border tb && tb.Tag == btn && tb.Child is TextBlock ttb)
                    ttb.Text = btn.IsToggleActive ? btn.LabelOn : btn.Label;
        }
        var actions = btn.IsToggle && btn.IsToggleActive ? btn.ActionsOn : btn.Actions;
        foreach (var a in actions) ExecuteAction(a);
    }

    public void ExecuteActionPublic(ButtonAction action) => ExecuteAction(action);

    private void ExecuteAction(ButtonAction action)
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
                            PlayMedia(ResolvePath(path), item.IsImageSlide);
                        }
                    }
                }
                break;
            case ButtonActionType.PlayMedia:
                if (!string.IsNullOrEmpty(action.MediaPath))
                    PlayMedia(ResolvePath(action.MediaPath), false);
                break;
            case ButtonActionType.SwitchMedia:
                if (!string.IsNullOrEmpty(action.MediaPath))
                {
                    var pos = 0f;
                    try { pos = _mediaModule.Player.Position; } catch { }
                    PlayMedia(ResolvePath(action.MediaPath), false);
                    if (pos > 0)
                    {
                        var p = pos;
                        var mm = _mediaModule;
                        Task.Run(async () =>
                        {
                            for (int i = 0; i < 30; i++)
                            {
                                await Task.Delay(100);
                                if (_closed) return;
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
                StopAllScreensRequested?.Invoke();
                break;
            case ButtonActionType.JumpToItemAllScreens:
                if (action.TargetItemIndex.HasValue)
                    JumpToItemAllScreensRequested?.Invoke(action.TargetItemIndex.Value, action.EndItemIndex ?? action.TargetItemIndex.Value);
                break;
            case ButtonActionType.TogglePip:
                foreach (UIElement el in OverlayCanvas.Children)
                    if (el is Border pb && pb.Tag is PipConfig tp)
                    {
                        if (_pipToggledVisible.Contains(tp.Id))
                        { _pipToggledVisible.Remove(tp.Id); pb.Visibility = Visibility.Collapsed; }
                        else
                        { _pipToggledVisible.Add(tp.Id); pb.Visibility = Visibility.Visible; }
                    }
                break;
            case ButtonActionType.ShowPip:
                foreach (UIElement el in OverlayCanvas.Children)
                    if (el is Border spb && spb.Tag is PipConfig sp)
                    { _pipToggledVisible.Add(sp.Id); spb.Visibility = Visibility.Visible; }
                break;
            case ButtonActionType.HidePip:
                foreach (UIElement el in OverlayCanvas.Children)
                    if (el is Border hpb && hpb.Tag is PipConfig hp)
                    { _pipToggledVisible.Remove(hp.Id); hpb.Visibility = Visibility.Collapsed; }
                break;
        }
    }

    private void PipControlAll(bool play)
    {
        foreach (UIElement el in OverlayCanvas.Children)
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

    private void OnStopAllScreens()
    {
        if (_closed) return;
        Dispatcher.BeginInvoke(() =>
        {
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
                    PlayMedia(ResolvePath(path), item.IsImageSlide);
                    return;
                }
            }
            Task.Run(() => { try { _mediaModule.Stop(); } catch { } });
        });
    }

    private void OnJumpToItemAllScreens(int startIndex, int endIndex)
    {
        if (_closed) return;
        Dispatcher.BeginInvoke(() =>
        {
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
                PlayMedia(ResolvePath(path), item.IsImageSlide);
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_closed) { base.OnClosed(e); return; }
        _closed = true;
        StopAllScreensRequested -= OnStopAllScreens;
        JumpToItemAllScreensRequested -= OnJumpToItemAllScreens;
        _timer?.Stop(); _timer = null;
        _escTimer?.Stop(); _escTimer = null;
        _slideStopwatch?.Stop(); _slideStopwatch = null;
        _mediaModule.OnMediaEnded -= OnMediaEnded;
        ResetPipToggleState();
        // Detacher le player du VideoView (sur le thread UI) avant de le disposer :
        // sinon le VideoView WPF garde une reference vers un player libere -> crash natif.
        try { VideoView.MediaPlayer = null; } catch { }
        var mm = _mediaModule;
        Task.Run(() =>
        {
            // Dispose serialise Stop en interne (verrou _playerLock) : pas de double Stop concurrent.
            try { mm.Dispose(); } catch { }
        });
        base.OnClosed(e);
    }

    // ---- Inline SRT parsing (avoids dependency on UMP.App controls) ----

    public static List<SubtitleEntry> ParseSrtFile(string path)
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
                        InMs = ParseSrtTs(parts[0].Trim()),
                        OutMs = ParseSrtTs(parts[1].Trim())
                    });
                }
            }
        }
        catch (Exception ex) { UMP.Core.Log.Warn($"Parsing SRT echoue '{path}' : {ex.Message}"); }
        return result;
    }

    private static long ParseSrtTs(string ts)
    {
        ts = ts.Replace(',', '.');
        return TimeSpan.TryParse(ts, out var t) ? (long)t.TotalMilliseconds : 0;
    }

    private static System.Windows.Media.FontFamily ResolveFontFamily(SubtitleConfig sub)
    {
        if (!string.IsNullOrEmpty(sub.CustomFontPath))
        {
            var resolved = ResolvePath(sub.CustomFontPath);
            if (File.Exists(resolved))
            {
                try
                {
                    var dir = Path.GetDirectoryName(resolved)!.Replace('\\', '/');
                    var glue = new GlyphTypeface(new Uri(resolved));
                    var familyName = glue.FamilyNames.Values.FirstOrDefault() ?? "Unknown";
                    return new System.Windows.Media.FontFamily(new Uri("file:///" + dir + "/"), "./#" + familyName);
                }
                catch { }
            }
        }
        return new System.Windows.Media.FontFamily(sub.FontFamily);
    }

    private static FontWeight ParseSubFontWeight(string weight) => weight switch
    {
        "Thin" => FontWeights.Thin,
        "Light" => FontWeights.Light,
        "Normal" => FontWeights.Normal,
        "Medium" => FontWeights.Medium,
        "SemiBold" => FontWeights.SemiBold,
        "ExtraBold" => FontWeights.ExtraBold,
        "Black" => FontWeights.Black,
        _ => FontWeights.Bold
    };

    private static TextAlignment ParseSubTextAlign(string align) => align switch
    {
        "Left" => TextAlignment.Left,
        "Right" => TextAlignment.Right,
        _ => TextAlignment.Center
    };
}
