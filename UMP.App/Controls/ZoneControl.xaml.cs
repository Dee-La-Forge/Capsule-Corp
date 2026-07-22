using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UMP.Core.Models;
using UMP.Modules.Media;
using UMP.Shared;
using UMP.App.Services;
using UMP.App.Windows;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using BrushConverter = System.Windows.Media.BrushConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Stretch = System.Windows.Media.Stretch;

namespace UMP.App.Controls;

public partial class ZoneControl : System.Windows.Controls.UserControl
{
    private const string MediaFilter = "Media|*.mp4;*.mkv;*.avi;*.mov;*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tous|*.*";
    private const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tous|*.*";
    private const string SelTag = "sel";

    private Zone? _zone;
    private MediaModule? _mediaModule;
    private DispatcherTimer? _timer;
    private string? _lastFilePath;
    private Dictionary<string, ZoneRuntime>? _zonesRuntime;
    private bool _isInitializing;
    private bool _isMuted;
    private int _lastVolume = 100;
    private SequencePlayer? _sequencePlayer;
    private System.Diagnostics.Stopwatch? _slideStopwatch;
    private bool _seqPanelInitialized;
    private int _jumpEndIndex = -1;
    private readonly HashSet<string> _pipToggledVisible = new();
    private ButtonConfig? _draggingButton;
    private ButtonConfig? _resizingButton;
    private System.Windows.Point _dragOffset;
    private bool _refreshingCanvas;
    private double _mediaW, _mediaH;
    private bool _autoPlay = true;
    private string? _editingButtonId;
    private ImageOverlayConfig? _draggingImage;
    private ImageOverlayConfig? _resizingImage;
    private ImageOverlayConfig? _rotatingImage;
    private SubtitleConfig? _draggingSubtitle;
    private PipConfig? _draggingPip;
    private PipConfig? _resizingPip;
    private PipConfig? _rotatingPip;
    private string? _editingPipId;
    private SubtitleConfig? _rotatingSubtitle;
    private string? _editingSubtitleId;
    private ButtonConfig? _rotatingButton;
    private string? _editingImageId;
    private bool _draggingBracketIn, _draggingBracketOut;

    public event Action? VideoLoaded;
    public event Action? ScreenChanged;
    public event Action? BeforeModification;
    public event Action<Services.IUndoCommand>? UndoCommandPushed;
    public static event Action? StopAllScreensRequested;
    public static event Action<int, int>? JumpToItemAllScreensRequested;

    public ZoneControl()
    {
        InitializeComponent();
        BtnPropsPanel.Applied += () => { PushPropsSnapshot(); RefreshOverlayCanvas(); SeqPanel.RefreshItems(); };
        BtnPropsPanel.CloseRequested += CloseBtnPropsPanel;
        SubPropsPanel.Applied += () => { PushPropsSnapshot(); _subtitleCache.Clear(); RefreshOverlayCanvas(); SeqPanel.RefreshItems(); };
        SubPropsPanel.CloseRequested += CloseSubPropsPanel;
        PipPropsPanel.Applied += () => { PushPropsSnapshot(); RefreshOverlayCanvas(); SeqPanel.RefreshItems(); };
        PipPropsPanel.CloseRequested += ClosePipPropsPanel;
        ImgPropsPanel.Applied += () => { PushPropsSnapshot(); RefreshOverlayCanvas(); SeqPanel.RefreshItems(); };
        ImgPropsPanel.CloseRequested += CloseImgPropsPanel;

        StopAllScreensRequested += OnStopAllFromEditor;
        JumpToItemAllScreensRequested += OnJumpToItemAllFromEditor;
    }

    private void OpenBtnPropsPanel(ButtonConfig btn)
    {
        if (_zone is null || _zonesRuntime is null) return;
        if (BtnPropsPanel.Visibility == Visibility.Visible && _editingButtonId == btn.Id)
        { CloseBtnPropsPanel(); return; }
        FinalizePropsSnapshot();
        _propsSnapshotJson = Services.SnapshotCommand<ButtonConfig>.CaptureSnapshot(btn);
        _propsSnapshotTarget = btn;
        SubPropsPanel.Visibility = Visibility.Collapsed;
        PipPropsPanel.Visibility = Visibility.Collapsed;
        ImgPropsPanel.Visibility = Visibility.Collapsed;
        _editingButtonId = btn.Id;
        _editingSubtitleId = null;
        _editingPipId = null;
        BtnPropsPanel.LoadButton(btn, _zone, _zonesRuntime);
        BtnPropsPanel.Visibility = Visibility.Visible;
        ColBtnProps.Width = new System.Windows.GridLength(380);
        Task.Delay(100).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(ForceBlackBackground)));
        SeqPanel.SetEditingButton(btn.Id);
        HighlightEditingButton();
    }

    private void CloseBtnPropsPanel()
    {
        FinalizePropsSnapshot();
        _editingButtonId = null;
        BtnPropsPanel.Visibility = Visibility.Collapsed;
        ColBtnProps.Width = new System.Windows.GridLength(0);
        SeqPanel.SetEditingButton(null);
        HighlightEditingButton();
        UpdateBrackets();
    }

    private void OpenSubPropsPanel(SubtitleConfig sub)
    {
        if (SubPropsPanel.Visibility == Visibility.Visible && _editingSubtitleId == sub.Id)
        { CloseSubPropsPanel(); return; }
        FinalizePropsSnapshot();
        _propsSnapshotJson = Services.SnapshotCommand<SubtitleConfig>.CaptureSnapshot(sub);
        _propsSnapshotTarget = sub;
        BtnPropsPanel.Visibility = Visibility.Collapsed;
        PipPropsPanel.Visibility = Visibility.Collapsed;
        ImgPropsPanel.Visibility = Visibility.Collapsed;
        SubPropsPanel.LoadSubtitle(sub);
        SubPropsPanel.Visibility = Visibility.Visible;
        ColBtnProps.Width = new System.Windows.GridLength(380);
    }

    private void OpenPipPropsPanel(PipConfig pip)
    {
        if (PipPropsPanel.Visibility == Visibility.Visible && _editingPipId == pip.Id)
        { ClosePipPropsPanel(); return; }
        FinalizePropsSnapshot();
        _propsSnapshotJson = Services.SnapshotCommand<PipConfig>.CaptureSnapshot(pip);
        _propsSnapshotTarget = pip;
        BtnPropsPanel.Visibility = Visibility.Collapsed;
        SubPropsPanel.Visibility = Visibility.Collapsed;
        ImgPropsPanel.Visibility = Visibility.Collapsed;
        PipPropsPanel.LoadPip(pip);
        PipPropsPanel.Visibility = Visibility.Visible;
        ColBtnProps.Width = new System.Windows.GridLength(380);
    }

    private void ClosePipPropsPanel()
    {
        FinalizePropsSnapshot();
        _editingPipId = null;
        PipPropsPanel.Visibility = Visibility.Collapsed;
        ColBtnProps.Width = new System.Windows.GridLength(0);
        SeqPanel.SetEditingPip(null);
        HighlightEditingButton();
        UpdateBrackets();
    }

    private void OpenImgPropsPanel(ImageOverlayConfig img)
    {
        if (ImgPropsPanel.Visibility == Visibility.Visible && _editingImageId == img.Id)
        { CloseImgPropsPanel(); return; }
        FinalizePropsSnapshot();
        _propsSnapshotJson = Services.SnapshotCommand<ImageOverlayConfig>.CaptureSnapshot(img);
        _propsSnapshotTarget = img;
        BtnPropsPanel.Visibility = Visibility.Collapsed;
        SubPropsPanel.Visibility = Visibility.Collapsed;
        PipPropsPanel.Visibility = Visibility.Collapsed;
        ImgPropsPanel.LoadImage(img);
        ImgPropsPanel.Visibility = Visibility.Visible;
        ColBtnProps.Width = new System.Windows.GridLength(380);
    }

    private void CloseImgPropsPanel()
    {
        FinalizePropsSnapshot();
        _editingImageId = null;
        ImgPropsPanel.Visibility = Visibility.Collapsed;
        ColBtnProps.Width = new System.Windows.GridLength(0);
        SeqPanel.SetEditingImage(null);
        HighlightEditingButton();
        UpdateBrackets();
    }

    private void CloseSubPropsPanel()
    {
        FinalizePropsSnapshot();
        _editingSubtitleId = null;
        SubPropsPanel.Visibility = Visibility.Collapsed;
        ColBtnProps.Width = new System.Windows.GridLength(0);
        SeqPanel.SetEditingSubtitle(null);
        HighlightEditingButton();
        UpdateBrackets();
    }

    private void UpdateSelectionVisuals(double left, double top, double w, double h)
    {
        foreach (UIElement el in OverlayCanvas.Children)
        {
            if (el is FrameworkElement fe && fe.Tag is string s && s == SelTag)
            {
                if (fe is System.Windows.Shapes.Rectangle rect)
                {
                    rect.Width = w + 6; rect.Height = h + 6;
                    Canvas.SetLeft(rect, left - 3); Canvas.SetTop(rect, top - 3);
                }
                else if (fe is Border handle)
                {
                    Canvas.SetLeft(handle, left + w - 5); Canvas.SetTop(handle, top + h - 5);
                }
            }
        }
    }

    private void SelectOverlayImage(ImageOverlayConfig img)
    {
        _editingImageId = img.Id;
        _editingButtonId = null;
        _editingSubtitleId = null;
        _editingPipId = null;
        SeqPanel.SetEditingImage(img.Id);
        HighlightEditingButton();
        UpdateBrackets();
        System.Windows.Input.Keyboard.Focus(Window.GetWindow(this));
        BtnPropsPanel.Visibility = Visibility.Collapsed;
        SubPropsPanel.Visibility = Visibility.Collapsed;
        PipPropsPanel.Visibility = Visibility.Collapsed;
        OpenImgPropsPanel(img);
    }

    private void SelectOverlayButton(ButtonConfig btn)
    {
        _moveTarget = null;
        _editingButtonId = btn.Id;
        _editingImageId = null;
        _editingSubtitleId = null;
        _editingPipId = null;
        SeqPanel.SetEditingButton(btn.Id);
        HighlightEditingButton();
        UpdateBrackets();
        // Forcer le focus WPF pour que les raccourcis clavier fonctionnent
        System.Windows.Input.Keyboard.Focus(Window.GetWindow(this));
        // Mettre a jour le panneau proprietes s'il est ouvert
        SubPropsPanel.Visibility = Visibility.Collapsed;
        if (BtnPropsPanel.Visibility == Visibility.Visible && _zone is not null && _zonesRuntime is not null)
            BtnPropsPanel.LoadButton(btn, _zone, _zonesRuntime);
    }

    private void HighlightEditingButton()
    {
        // Supprimer les anciens elements de selection (tag = "sel")
        var toRemove = OverlayCanvas.Children.OfType<UIElement>()
            .Where(e => e is FrameworkElement fe && fe.Tag is string s && s == SelTag).ToList();
        foreach (var r in toRemove) OverlayCanvas.Children.Remove(r);

        // Restaurer le style normal de tous les boutons
        foreach (UIElement el in OverlayCanvas.Children)
        {
            if (el is Border b && b.Tag is ButtonConfig)
                b.Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = Color.FromRgb(0, 0, 0), BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5, Direction = 270 };
        }

        // Trouver l'element selectionne (bouton, image OU sous-titre)
        Border? selBorder = null;
        string? selId = _editingButtonId ?? _editingImageId ?? _editingSubtitleId ?? _editingPipId;
        bool isImage = _editingImageId is not null && _editingButtonId is null && _editingSubtitleId is null;
        if (selId is null) return;

        foreach (UIElement el in OverlayCanvas.Children)
        {
            if (el is Border b)
            {
                if (b.Tag is ButtonConfig bc && bc.Id == selId) { selBorder = b; break; }
                if (b.Tag is ImageOverlayConfig ic && ic.Id == selId) { selBorder = b; break; }
                if (b.Tag is SubtitleConfig sc && sc.Id == selId) { selBorder = b; break; }
                if (b.Tag is PipConfig pc && pc.Id == selId) { selBorder = b; break; }
            }
        }
        if (selBorder is null) return;

        var left = Canvas.GetLeft(selBorder);
        var top = Canvas.GetTop(selBorder);
        // Width/Height peuvent etre NaN (ex. sous-titre : hauteur auto) ->
        // retomber sur les dimensions mesurees pour ne pas propager NaN
        // dans le cadre de selection (crash de la passe de layout WPF).
        var w = double.IsFinite(selBorder.Width) ? selBorder.Width : selBorder.ActualWidth;
        var h = double.IsFinite(selBorder.Height) ? selBorder.Height : selBorder.ActualHeight;
        if (!double.IsFinite(left) || !double.IsFinite(top)
            || !double.IsFinite(w) || !double.IsFinite(h)) return;

        // Cadre de selection — rectangle en pointilles
        var selRect = new System.Windows.Shapes.Rectangle
        {
            Width = w + 6, Height = h + 6,
            Stroke = new SolidColorBrush(Color.FromRgb(120, 110, 220)),
            StrokeThickness = 1.5,
            StrokeDashArray = new System.Windows.Media.DoubleCollection(new[] { 4.0, 3.0 }),
            Fill = Brushes.Transparent,
            IsHitTestVisible = false, Tag = SelTag
        };
        Canvas.SetLeft(selRect, left - 3);
        Canvas.SetTop(selRect, top - 3);
        OverlayCanvas.Children.Add(selRect);

        // Poignee de resize en bas a droite
        var resizeHandle = new Border
        {
            Width = 10, Height = 10,
            Background = new SolidColorBrush(Color.FromRgb(92, 79, 191)),
            BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.SizeNWSE, Tag = SelTag
        };
        Canvas.SetLeft(resizeHandle, left + w - 5);
        Canvas.SetTop(resizeHandle, top + h - 5);
        resizeHandle.PreviewMouseRightButtonDown += (s, e) =>
        {
            if (selBorder.Tag is ImageOverlayConfig ic) _resizingImage = ic;
            else if (selBorder.Tag is ButtonConfig bc) _resizingButton = bc;
            else if (selBorder.Tag is PipConfig pc) _resizingPip = pc;
            e.Handled = true;
        };
        resizeHandle.PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (!isImage && selBorder.Tag is ButtonConfig bc)
            { SelectOverlayButton(bc); ExecuteButtonActions(bc); }
            e.Handled = true;
        };
        OverlayCanvas.Children.Add(resizeHandle);

        // Poignee de rotation en haut centre
        var rotHandle = new System.Windows.Shapes.Ellipse
        {
            Width = 12, Height = 12,
            Fill = new SolidColorBrush(Color.FromRgb(220, 120, 50)),
            Stroke = Brushes.White, StrokeThickness = 1.5,
            Cursor = System.Windows.Input.Cursors.Hand, Tag = SelTag
        };
        Canvas.SetLeft(rotHandle, left + w / 2 - 6);
        Canvas.SetTop(rotHandle, top - 20);
        var rotLine = new System.Windows.Shapes.Line
        {
            X1 = left + w / 2, Y1 = top - 8,
            X2 = left + w / 2, Y2 = top,
            Stroke = new SolidColorBrush(Color.FromRgb(120, 110, 220)),
            StrokeThickness = 1, IsHitTestVisible = false, Tag = SelTag
        };
        OverlayCanvas.Children.Add(rotLine);
        rotHandle.PreviewMouseRightButtonDown += (s, e) =>
        {
            if (selBorder.Tag is ImageOverlayConfig ic)
                _rotatingImage = ic;
            else if (selBorder.Tag is ButtonConfig bc)
                _rotatingButton = bc;
            else if (selBorder.Tag is SubtitleConfig sc)
                _rotatingSubtitle = sc;
            else if (selBorder.Tag is PipConfig pc)
                _rotatingPip = pc;
            e.Handled = true;
        };
        OverlayCanvas.Children.Add(rotHandle);
    }

    public void Initialize(Zone zone, MediaModule mediaModule)
    {
        if (_zone is not null) return;
        _isInitializing = true;
        _zone = zone;
        _mediaModule = mediaModule;
        TxtZoneName.Text = zone.Name;

        var screens = System.Windows.Forms.Screen.AllScreens;
        var screenIdx = ResolveScreenIndex(zone, screens);
        UpdateScreenLabel(screenIdx, screens);
        _isMuted = zone.IsMuted;
        _lastVolume = zone.Volume;
        VolumeSlider.Value = _isMuted ? 0 : zone.Volume;
        BtnMute.Content = _isMuted ? "\uD83D\uDD07" : "\uD83D\uDD0A";
        ChkLoop.IsChecked = zone.IsLooping;
        _mediaModule.SetVolume(_isMuted ? 0 : zone.Volume);
        _mediaModule.IsLooping = zone.IsLooping;
        _mediaModule.OnMediaEnded += OnMediaEnded;

        // PAS d'event Player.Playing — cause des race conditions
        // Le timer est gere manuellement via StartTimerSafe()

        VideoView.Loaded += (s, e) =>
        {
            VideoView.MediaPlayer = _mediaModule.Player;
            ForceBlackBackground();

            if (!_autoPlay)
            {
                // Chargement de projet : pas de lecture auto, juste afficher le canvas
                Task.Delay(300).ContinueWith(_ =>
                    Dispatcher.BeginInvoke(DispatcherPriority.Background,
                        new Action(RefreshOverlayCanvas)));
                return;
            }

            // En mode Scenario, jouer le premier item
            if (zone.Sequence is not null &&
                zone.Sequence.Mode == SequenceMode.Scenario &&
                zone.Sequence.Items.Count > 0)
            {
                var firstItem = zone.Sequence.Items[0];
                if (!string.IsNullOrEmpty(firstItem.EffectivePath))
                {
                    EnsureSequencePlayer();
                    _sequencePlayer!.JumpTo(0);
                }
            }
            else if (_mediaModule.Player.State == LibVLCSharp.Shared.VLCState.NothingSpecial
                || _mediaModule.Player.State == LibVLCSharp.Shared.VLCState.Stopped)
            {
                if (!string.IsNullOrEmpty(zone.MediaFilePath))
                {
                    _lastFilePath = zone.MediaFilePath;
                    ActivePlay(_lastFilePath);
                    StartTimerSafe();
                    SetPlaying(true);
                }
            }
            else
                _timer?.Start();

            Task.Delay(300).ContinueWith(_ =>
                Dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(RefreshOverlayCanvas)));
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += Timer_Tick;

        // Recalculer les positions des boutons quand le canvas change de taille
        OverlayCanvas.SizeChanged += (s, e) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RefreshOverlayCanvas));

        // Forcer le fond noir a chaque resize
        VideoView.SizeChanged += (s, e) => ForceBlackBackground();

        // Redessiner les graduations quand la timeline change de taille
        TimelineRuler.SizeChanged += (s, e) => DrawTimelineRuler();
        BracketsCanvas.SizeChanged += (s, e) => UpdateBrackets();
        InitBrackets();

        if (zone.Sequence is not null)
            EnsureSequencePlayer();

        _isInitializing = false;
    }

    // ===== ACTIVE* — Task.Run obligatoire =====

    private void ResetPipToggleState()
    {
        // L'etat de visibilite est reinitialise par item ; chaque PiP recree
        // recoit son etat par defaut (visible sauf "Demarre cache") dans AddPipElement.
        _pipToggledVisible.Clear();
    }

    private void ActivePlay(string filePath)
    {
        ResetPipToggleState();
        var ext = Path.GetExtension(filePath).ToLower();
        var isImage = ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

        if (isImage)
        {
            // Pauser SEULEMENT si la video joue
            if (_mediaModule?.Player?.IsPlaying == true)
                Task.Run(() => { try { _mediaModule?.Pause(); } catch { } });

            Task.Run(() =>
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(filePath, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();

                    // Dimensions connues ICI (bmp est Frozen, thread-safe)
                    var imgW = bmp.PixelWidth;
                    var imgH = bmp.PixelHeight;

                    Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                    {
                        SlideImage.Source = bmp;
                        ImageOverlay.Visibility = Visibility.Visible;
                        if (imgW > 0 && imgH > 0)
                        { _mediaW = imgW; _mediaH = imgH; }
                        _timer?.Start();
                        RefreshOverlayCanvas();
                    }));
                }
                catch { }
            });
            return;
        }

        // Video — cacher image
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            ImageOverlay.Visibility = Visibility.Collapsed;
            SlideImage.Source = null;
        }));
        var mm = _mediaModule;
        Task.Run(() => { try { mm?.Play(filePath); } catch { } });
    }

    private void ActivePause()
    {
        Task.Run(() => { try { _mediaModule?.Pause(); } catch { } });
    }

    private void ActiveStop()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ImageOverlay.Visibility = Visibility.Collapsed;
            SlideImage.Source = null;
        }));
        _mediaModule?.Stop();
    }

    /// <summary>
    /// Demarre le timer apres un delai, en verifiant
    /// que LibVLC est pret (pas en transition).
    /// Pour les images, demarre immediatement.
    /// </summary>
    private void StartTimerSafe()
    {
        // Image : demarrer immediatement
        // Verifier le modele, pas l'UI (ImageOverlay peut ne pas etre visible encore)
        var seq = _zone?.Sequence;
        if (seq is not null && seq.CurrentIndex >= 0 &&
            seq.CurrentIndex < seq.Items.Count &&
            seq.Items[seq.CurrentIndex].IsImageSlide)
        {
            _timer?.Start();
            return;
        }

        if (ImageOverlay.Visibility == Visibility.Visible)
        {
            _timer?.Start();
            return;
        }

        // Video — attendre que le player soit en etat Playing puis lire les dimensions
        Task.Run(() =>
        {
            // Phase 1 : attendre Playing
            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(100);
                try
                {
                    if (_mediaModule?.Player?.State == LibVLCSharp.Shared.VLCState.Playing)
                        break;
                }
                catch { return; }
            }
            // Phase 2 : essayer de lire les dimensions (retry car souvent 0 au debut)
            uint vw = 0, vh = 0;
            for (int j = 0; j < 20; j++)
            {
                try { _mediaModule?.Player?.Size(0, ref vw, ref vh); } catch { }
                if (vw > 0 && vh > 0) break;
                Thread.Sleep(100);
            }
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (vw > 0 && vh > 0)
                { _mediaW = vw; _mediaH = vh; }
                _timer?.Start();
                RefreshOverlayCanvas();
            }));
        });
    }

    private bool _seekDragging = false;

    private void UpdateTimeline(long currentMs, long totalMs)
    {
        TxtPosition.Text = FormatTimecode(currentMs);
        if (totalMs <= 0)
        {
            TxtDuration.Text = "--:--:--";
            return;
        }
        if (!_seekDragging)
            TimelineSlider.Value = (double)currentMs / totalMs * 1000;
        TxtDuration.Text = FormatTimecode(totalMs);
        if (_lastTotalMs != totalMs)
        {
            _lastTotalMs = totalMs;
            DrawTimelineRuler();
        }
        UpdateBrackets();
    }

    private void TimelineSlider_MouseUp(object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_mediaModule is null) return;
        if (ImageOverlay.Visibility == Visibility.Visible) return;

        var ratio = (float)(TimelineSlider.Value / 1000.0);
        var player = _mediaModule.Player;
        // Seek via Task.Run pour ne pas bloquer le UI
        Task.Run(() => { try { player.Position = ratio; } catch { } });
    }

    private static string FormatTimecode(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
    }

    private long _lastTotalMs;

    private void TimelineSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        DrawTimelineRuler();
    }

    private void DrawTimelineRuler()
    {
        if (TimelineRuler is null || _lastTotalMs <= 0) return;
        TimelineRuler.Children.Clear();
        var w = TimelineRuler.ActualWidth;
        if (w <= 0) return;

        var totalSec = _lastTotalMs / 1000.0;
        // Determiner l'intervalle entre les graduations (1s, 5s, 10s, 30s, 60s, 300s)
        double interval = 1;
        var intervals = new double[] { 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 };
        foreach (var iv in intervals)
        {
            if (totalSec / iv <= w / 50) { interval = iv; break; }
            interval = iv;
        }

        var margin = 7.0; // marge du slider
        var usableW = w - margin * 2;

        for (double t = 0; t <= totalSec; t += interval)
        {
            var x = margin + (t / totalSec) * usableW;
            var isMajor = t % (interval * 5) < 0.01 || interval >= 30;
            var h = isMajor ? 10 : 5;

            var line = new System.Windows.Shapes.Line
            {
                X1 = x, X2 = x,
                Y1 = 14 - h, Y2 = 14,
                Stroke = new SolidColorBrush(isMajor
                    ? Color.FromRgb(80, 75, 120)
                    : Color.FromRgb(45, 42, 70)),
                StrokeThickness = 1
            };
            TimelineRuler.Children.Add(line);

            if (isMajor && usableW > 100)
            {
                var ts = TimeSpan.FromSeconds(t);
                var label = ts.TotalHours >= 1
                    ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                    : $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
                var tb = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush(Color.FromRgb(70, 65, 110)),
                    FontSize = 8, FontFamily = new System.Windows.Media.FontFamily("Consolas")
                };
                Canvas.SetLeft(tb, x + 2);
                Canvas.SetTop(tb, 1);
                TimelineRuler.Children.Add(tb);
            }
        }
    }

    // ===== BRACKETS IN/OUT =====

    private (long inMs, long outMs, Action<long>? setIn, Action<long>? setOut) GetSelectedInOut()
    {
        if (_editingButtonId is not null)
        {
            var seq = _zone?.Sequence;
            if (seq?.Items != null && seq.CurrentIndex >= 0 && seq.CurrentIndex < seq.Items.Count)
            {
                var btn = seq.Items[seq.CurrentIndex].Buttons
                    .FirstOrDefault(b => b.Id == _editingButtonId);
                if (btn is not null)
                    return (btn.InMs, btn.OutMs, v => btn.InMs = v, v => btn.OutMs = v);
            }
        }
        if (_editingImageId is not null)
        {
            var seq = _zone?.Sequence;
            if (seq?.Items != null && seq.CurrentIndex >= 0 && seq.CurrentIndex < seq.Items.Count)
            {
                var img = seq.Items[seq.CurrentIndex].ImageOverlays
                    .FirstOrDefault(i => i.Id == _editingImageId);
                if (img is not null)
                    return (img.InMs, img.OutMs, v => img.InMs = v, v => img.OutMs = v);
            }
        }
        if (_editingPipId is not null)
        {
            var seq = _zone?.Sequence;
            if (seq?.Items != null && seq.CurrentIndex >= 0 && seq.CurrentIndex < seq.Items.Count)
            {
                var pip = seq.Items[seq.CurrentIndex].PictureInPictures
                    .FirstOrDefault(p => p.Id == _editingPipId);
                if (pip is not null)
                    return (pip.InMs, pip.OutMs, v => pip.InMs = v, v => pip.OutMs = v);
            }
        }
        return (0, 0, null, null);
    }

    private Border _bracketIn = null!, _bracketOut = null!;
    private System.Windows.Shapes.Rectangle _bracketZone = null!;

    private void InitBrackets()
    {
        _bracketZone = new System.Windows.Shapes.Rectangle
        {
            Height = 4,
            Fill = new SolidColorBrush(Color.FromArgb(50, 92, 79, 191)),
            IsHitTestVisible = false
        };
        Canvas.SetTop(_bracketZone, 12);
        BracketsCanvas.Children.Add(_bracketZone);

        _bracketIn = new Border
        {
            Width = 8, Height = 22,
            Background = new SolidColorBrush(Color.FromRgb(80, 200, 120)),
            CornerRadius = new CornerRadius(3, 0, 0, 3),
            Cursor = System.Windows.Input.Cursors.SizeWE
        };
        Canvas.SetTop(_bracketIn, 3);
        _bracketIn.MouseLeftButtonDown += (s, e) =>
        { _draggingBracketIn = true; _bracketIn.CaptureMouse(); e.Handled = true; };
        _bracketIn.MouseMove += BracketDrag;
        _bracketIn.MouseLeftButtonUp += BracketRelease;
        BracketsCanvas.Children.Add(_bracketIn);

        _bracketOut = new Border
        {
            Width = 8, Height = 22,
            Background = new SolidColorBrush(Color.FromRgb(220, 80, 80)),
            CornerRadius = new CornerRadius(0, 3, 3, 0),
            Cursor = System.Windows.Input.Cursors.SizeWE
        };
        Canvas.SetTop(_bracketOut, 3);
        _bracketOut.MouseLeftButtonDown += (s, e) =>
        { _draggingBracketOut = true; _bracketOut.CaptureMouse(); e.Handled = true; };
        _bracketOut.MouseMove += BracketDrag;
        _bracketOut.MouseLeftButtonUp += BracketRelease;
        BracketsCanvas.Children.Add(_bracketOut);
    }

    private void UpdateBrackets()
    {
        var (inMs, outMs, setIn, setOut) = GetSelectedInOut();

        // Cacher si : rien selectionne, pas de duree, ou timeline cachee (plan fixe)
        if (setIn is null || _lastTotalMs <= 0 || TimelineBorder.Visibility != Visibility.Visible)
        {
            _bracketIn.Visibility = Visibility.Collapsed;
            _bracketOut.Visibility = Visibility.Collapsed;
            _bracketZone.Visibility = Visibility.Collapsed;
            return;
        }

        var w = BracketsCanvas.ActualWidth;
        if (w <= 0) return;
        var margin = 9.0;
        var usable = w - margin * 2;
        if (usable <= 0) return;

        var inX = margin + (double)inMs / _lastTotalMs * usable;
        var outX = outMs > 0
            ? margin + (double)outMs / _lastTotalMs * usable
            : margin + usable;

        _bracketZone.Visibility = Visibility.Visible;
        _bracketIn.Visibility = Visibility.Visible;
        _bracketOut.Visibility = Visibility.Visible;

        Canvas.SetLeft(_bracketZone, inX);
        _bracketZone.Width = Math.Max(0, outX - inX);
        Canvas.SetLeft(_bracketIn, inX - 8);
        Canvas.SetLeft(_bracketOut, outX);
        _bracketIn.ToolTip = $"IN {FormatTimecode(inMs)}";
        _bracketOut.ToolTip = $"OUT {(outMs > 0 ? FormatTimecode(outMs) : "\u221E")}";
    }

    private void BracketDrag(object s, System.Windows.Input.MouseEventArgs e)
    {
        if (!_draggingBracketIn && !_draggingBracketOut) return;
        if (_lastTotalMs <= 0) return;
        var w = BracketsCanvas.ActualWidth;
        var margin = 9.0;
        var usable = w - margin * 2;
        if (usable <= 0) return;

        var pos = e.GetPosition(BracketsCanvas);
        var ms = (long)(Math.Clamp((pos.X - margin) / usable, 0, 1) * _lastTotalMs);
        var (inMs, outMs, setIn, setOut) = GetSelectedInOut();

        if (_draggingBracketIn && setIn is not null)
        {
            // Borner la limite haute pour eviter Math.Clamp(min > max) -> ArgumentException
            var hi = Math.Max(0, outMs > 0 ? outMs - 100 : _lastTotalMs - 100);
            setIn(Math.Clamp(ms, 0, hi));
        }
        else if (_draggingBracketOut && setOut is not null)
        {
            // Borner la limite basse pour eviter Math.Clamp(min > max) -> ArgumentException
            var lo = Math.Min(inMs + 100, _lastTotalMs);
            setOut(Math.Clamp(ms, lo, _lastTotalMs));
        }

        UpdateBrackets();
    }

    private void BracketRelease(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_draggingBracketIn) { _draggingBracketIn = false; _bracketIn.ReleaseMouseCapture(); }
        if (_draggingBracketOut) { _draggingBracketOut = false; _bracketOut.ReleaseMouseCapture(); }
        SeqPanel.RefreshItems();
    }

    private static readonly string[] _mediaExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    private void VideoArea_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            DropOverlay.Visibility = Visibility.Visible;
    }

    private void VideoArea_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void VideoArea_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files?.Length > 0 && _mediaExtensions.Contains(Path.GetExtension(files[0]).ToLower()))
            { e.Effects = System.Windows.DragDropEffects.Copy; e.Handled = true; return; }
        }
        e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void VideoArea_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
        if (files is null || files.Length == 0 || _zone is null) return;
        BeforeModification?.Invoke();

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLower();
            if (!_mediaExtensions.Contains(ext)) continue;
            var isImage = ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

            if (_zone.Sequence is null) _zone.Sequence = new UMP.Core.Models.Sequence();

            if (isImage)
                _zone.Sequence.Items.Add(new UMP.Core.Models.SequenceItem { ImageSlidePath = file });
            else
                _zone.Sequence.Items.Add(new UMP.Core.Models.SequenceItem { MediaPath = file });
        }

        // Ouvrir le panneau sequence si pas deja ouvert (cablage complet)
        if (SeqPanel.Visibility == Visibility.Collapsed)
            OpenSequencePanel();
        else
            SeqPanel.RefreshItems();

        // Jouer le premier fichier droppe si rien ne joue
        if (!_isPlaying && _zone.Sequence?.Items.Count > 0)
        {
            var first = _zone.Sequence.Items[^1];
            var path = first.EffectivePath;
            if (!string.IsNullOrEmpty(path))
            { _lastFilePath = path; ActivePlay(path); StartTimerSafe(); SetPlaying(true); }
        }
        VideoLoaded?.Invoke();
    }

    private bool _blackBgApplied;
    private void ForceBlackBackground()
    {
        if (_blackBgApplied) return;
        try
        {
            var host = FindChild<System.Windows.Forms.Integration.WindowsFormsHost>(VideoView);
            if (host is not null)
            {
                host.Background = System.Windows.Media.Brushes.Black;
                if (host.Child is not null)
                    host.Child.BackColor = System.Drawing.Color.Black;
            }
            // Forcer noir sur le ForegroundWindow de LibVLCSharp (via reflection)
            var fwField = VideoView.GetType().GetField("_foregroundWindow",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fwField?.GetValue(VideoView) is Window fw)
                fw.Background = System.Windows.Media.Brushes.Black;
            _blackBgApplied = true;
        }
        catch { }
    }

    private static T? FindChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    private void ActiveSetVolume(int vol)
        => _mediaModule?.SetVolume(vol);

    // ===== TIMER =====

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_mediaModule is null) return;
        try
        {
            // Image affichee → PAS de lecture VLC
            if (ImageOverlay.Visibility == Visibility.Visible)
            {
                if (_slideStopwatch is not null)
                {
                    var elapsed = _slideStopwatch.ElapsedMilliseconds;
                    var seq2 = _zone?.Sequence;
                    var curItem = (seq2 is not null && seq2.CurrentIndex >= 0 &&
                        seq2.CurrentIndex < seq2.Items.Count) ? seq2.Items[seq2.CurrentIndex] : null;

                    long imgTotal = curItem?.DurationMs ?? 5000;
                    if (curItem?.SlideDuration == ImageSlideDuration.UntilClick)
                    {
                        // Plan fixe : timeline avec duree mais en pause (pas d'avance auto)
                        UpdateTimeline(0, imgTotal);
                    }
                    else
                    {
                        var imgCurrent = Math.Min(elapsed, imgTotal);
                        UpdateTimeline(imgCurrent, imgTotal);
                    }
                    if (curItem?.IsLooping == true && curItem.SlideDuration == ImageSlideDuration.Fixed && elapsed >= imgTotal)
                        _slideStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    else if (curItem?.IsLooping != true)
                        _sequencePlayer?.Tick(elapsed);
                    UpdateOverlayVisibility(curItem?.IsLooping == true ? Math.Min(elapsed, imgTotal) : elapsed);
                }
                return;
            }

            var player = _mediaModule.Player;
            if (player is null) return;
            var state = player.State;
            if (state == LibVLCSharp.Shared.VLCState.NothingSpecial ||
                state == LibVLCSharp.Shared.VLCState.Stopped ||
                state == LibVLCSharp.Shared.VLCState.Opening ||
                state == LibVLCSharp.Shared.VLCState.Buffering ||
                state == LibVLCSharp.Shared.VLCState.Error)
                return;

            // Rattraper les dimensions video si pas encore connues
            if (_mediaW <= 0 || _mediaH <= 0)
            {
                uint vw = 0, vh = 0;
                try { player.Size(0, ref vw, ref vh); } catch { }
                if (vw > 0 && vh > 0)
                { _mediaW = vw; _mediaH = vh; RefreshOverlayCanvas(); }
            }

            var position = player.Position;
            var length = player.Length;
            if (length <= 0) return;

            var totalMs = length;
            var currentMs = (long)(position * length);

            UpdateTimeline(currentMs, totalMs);
            UpdateOverlayVisibility(currentMs);
            _sequencePlayer?.Tick(currentMs);
        }
        catch (Exception ex) { UMP.Core.Log.Warn($"ZoneControl.Timer_Tick : {ex.Message}"); }
    }

    private void UpdateTimelineVisibility()
    {
        var seq = _zone?.Sequence;
        if (seq is not null && seq.CurrentIndex >= 0 && seq.CurrentIndex < seq.Items.Count)
        {
            var item = seq.Items[seq.CurrentIndex];
            var isPlanFixe = item.IsImageSlide && item.SlideDuration == ImageSlideDuration.UntilClick;
            TimelineBorder.Visibility = isPlanFixe ? Visibility.Collapsed : Visibility.Visible;
        }
        else
            TimelineBorder.Visibility = Visibility.Visible;
    }

    private void UpdateOverlayVisibility(long currentMs)
    {
        foreach (UIElement el in OverlayCanvas.Children)
        {
            if (el is Border b)
            {
                if (b.Tag is ButtonConfig btn)
                {
                    var visible = currentMs >= btn.InMs
                        && (btn.OutMs <= 0 || currentMs <= btn.OutMs);
                    b.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (b.Tag is ImageOverlayConfig img)
                {
                    var visible = currentMs >= img.InMs
                        && (img.OutMs <= 0 || currentMs <= img.OutMs);
                    b.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (b.Tag is PipConfig)
                {
                    // Visibilite PiP geree uniquement par ShowPip/HidePip/TogglePip
                }
                else if (b.Tag is SubtitleConfig sub)
                {
                    var entries = GetSubtitleEntries(sub.FilePath);
                    var entry = entries.FirstOrDefault(e => currentMs >= e.InMs && currentMs <= e.OutMs);
                    if (entry is not null)
                    {
                        b.Visibility = Visibility.Visible;
                        if (b.Child is TextBlock stb && stb.Text != entry.Text)
                            stb.Text = entry.Text;
                        else if (b.Child is System.Windows.Controls.Grid g)
                            foreach (var c in g.Children)
                                if (c is TextBlock tb && tb.Text != entry.Text) tb.Text = entry.Text;
                    }
                    else
                        b.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (_mediaModule is null) return;
            // Ignorer si une image est affichee ou en cours de chargement
            if (ImageOverlay.Visibility == Visibility.Visible) return;
            var seqCheck = _zone?.Sequence;
            if (seqCheck is not null && seqCheck.CurrentIndex >= 0 &&
                seqCheck.CurrentIndex < seqCheck.Items.Count &&
                seqCheck.Items[seqCheck.CurrentIndex].IsImageSlide) return;
            // Loop sur l'item courant si IsLooping
            var seq2 = _zone?.Sequence;
            var curItem2 = seq2 is not null && seq2.CurrentIndex >= 0 && seq2.CurrentIndex < seq2.Items.Count
                ? seq2.Items[seq2.CurrentIndex] : null;
            if (curItem2?.IsLooping == true && !string.IsNullOrEmpty(_lastFilePath))
            {
                var path = _lastFilePath;
                Task.Delay(200).ContinueWith(_ =>
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ActivePlay(path); StartTimerSafe();
                    })));
                return;
            }
            // Fin de plage JumpToItemAllScreens → retour item 0
            if (_jumpEndIndex >= 0 && seq2 is not null && seq2.CurrentIndex >= _jumpEndIndex)
            {
                _jumpEndIndex = -1;
                if (seq2.Items.Count == 0) return;
                seq2.CurrentIndex = 0;
                var first = seq2.Items[0];
                var firstPath = first.EffectivePath;
                if (!string.IsNullOrEmpty(firstPath))
                {
                    _lastFilePath = firstPath;
                    ActivePlay(firstPath);
                    StartTimerSafe();
                    SeqPanel.SetActiveIndex(0);
                }
                return;
            }
            if (_zone?.Sequence is not null &&
                _zone.Sequence.Items.Count > 0)
            {
                EnsureSequencePlayer();
                _sequencePlayer!.OnMediaEnded();
                return;
            }
            if (_mediaModule.IsLooping) return;
            _timer?.Stop();
        }));
    }

    // ===== HANDLERS =====

    private void TxtZoneName_TextChanged(object s, TextChangedEventArgs e)
    {
        if (_zone is null || _zone.Name == TxtZoneName.Text) return;
        _zone.Name = TxtZoneName.Text;
        if (!_isInitializing) BeforeModification?.Invoke();
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaModule is null) return;
        var d = new Microsoft.Win32.OpenFileDialog
            { Filter = MediaFilter };
        if (d.ShowDialog() != true) return;

        _lastFilePath = d.FileName;
        if (_zone is not null) _zone.MediaFilePath = _lastFilePath;
        BeforeModification?.Invoke();

        if (_zone?.Sequence is not null && _zone.Sequence.Items.Count > 0)
        {
            if (_zone.Sequence.Items.Count == 0)
                _zone.Sequence.Items.Add(new SequenceItem { MediaPath = _lastFilePath });
            else
                _zone.Sequence.Items[0].MediaPath = _lastFilePath;
            SeqPanel.RefreshItems();
            _timer?.Stop();
            EnsureSequencePlayer();
            _sequencePlayer!.JumpTo(0);
            VideoLoaded?.Invoke();
            return;
        }

        _timer?.Stop();
        ActivePlay(_lastFilePath!);
        StartTimerSafe();
        VideoLoaded?.Invoke();
    }

    private bool _isPlaying = false;

    private void SetPlaying(bool playing)
    {
        _isPlaying = playing;
        BtnPlayPause.Content = playing ? "\u23F8" : "\u25B6";
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            // Pause — gerer images ET videos
            if (ImageOverlay.Visibility == Visibility.Visible)
            {
                // Image : stopper le stopwatch (pause la duree)
                _slideStopwatch?.Stop();
                _timer?.Stop();
            }
            else
            {
                ActivePause();
            }
            SetPlaying(false);
            return;
        }

        // Play
        SetPlaying(true);

        // Reprendre une image en pause
        if (ImageOverlay.Visibility == Visibility.Visible && _slideStopwatch is not null)
        {
            _slideStopwatch.Start(); // Reprend le chrono
            _timer?.Start();
            return;
        }

        if (_zone?.Sequence is not null && _zone.Sequence.Items.Count > 0)
        {
            if (_zone.Sequence.Items.Count == 0) return;
            _mediaModule!.IsLooping = false;
            _timer?.Stop();
            EnsureSequencePlayer();
            _sequencePlayer!.Start();
            return;
        }
        if (string.IsNullOrEmpty(_lastFilePath)) return;
        _timer?.Stop();
        ActivePlay(_lastFilePath!);
        StartTimerSafe();
    }

    private void BtnStop_Click(object s, RoutedEventArgs e)
    {
        _jumpEndIndex = -1;
        _sequencePlayer?.Stop();
        ActiveStop();
        _timer?.Stop();
        // ClickOverlay supprime — gere par OverlayCanvas_MouseUp
        _slideStopwatch?.Stop(); _slideStopwatch = null;
        SetPlaying(false);
        TimelineSlider.Value = 0;
        TxtPosition.Text = "00:00.00";
    }

    private void EnsureSequencePlayer()
    {
        if (_sequencePlayer is not null || _zone is null) return;
        _zone.Sequence ??= new Sequence();
        _sequencePlayer = new SequencePlayer(_zone, ActivePlay, GetCurrentMs, ExecuteSequenceAction);

        _sequencePlayer.CurrentIndexChanged += (idx) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                var item = _zone?.Sequence?.Items.ElementAtOrDefault(idx);
                if (item is not null)
                { _zone!.MediaFilePath = item.EffectivePath; _lastFilePath = item.EffectivePath; }

                if (item?.IsImageSlide == true)
                    _slideStopwatch = System.Diagnostics.Stopwatch.StartNew();
                else { _slideStopwatch?.Stop(); _slideStopwatch = null; }

                // UntilClick gere par OverlayCanvas_MouseUp

                SeqPanel.SetActiveIndex(idx);
                SetPlaying(true);
                VideoLoaded?.Invoke();
                RefreshOverlayCanvas();
                UpdateTimelineVisibility();
            }));
        };
        _sequencePlayer.PlaybackStarted += () =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => StartTimerSafe()));
    }

    private void VolumeSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_mediaModule is null || _zone is null) return;
        var vol = (int)e.NewValue;
        if (vol == _zone.Volume && !_isMuted) return;
        if (_isMuted && vol > 0) { _isMuted = false; _zone.IsMuted = false; BtnMute.Content = "\uD83D\uDD0A"; }
        ActiveSetVolume(vol);
        if (!_isMuted) _zone.Volume = vol;
        if (TxtVolume is not null) TxtVolume.Text = vol.ToString();
        if (!_isInitializing) BeforeModification?.Invoke();
    }

    private void BtnMute_Click(object s, RoutedEventArgs e)
    {
        if (_isInitializing || _mediaModule is null) return;
        _isMuted = !_isMuted;
        if (_zone is not null) _zone.IsMuted = _isMuted;
        BeforeModification?.Invoke();
        if (_isMuted)
        {
            _lastVolume = (int)VolumeSlider.Value;
            ActiveSetVolume(0);
            VolumeSlider.Value = 0;
            BtnMute.Content = "\uD83D\uDD07";
        }
        else
        {
            ActiveSetVolume(_lastVolume);
            VolumeSlider.Value = _lastVolume;
            BtnMute.Content = "\uD83D\uDD0A";
        }
    }

    private void ChkLoop_Checked(object s, RoutedEventArgs e)
    { if (!_isInitializing && _mediaModule is not null) { _mediaModule.IsLooping = true; if (_zone is not null) _zone.IsLooping = true; BeforeModification?.Invoke(); } }
    private void ChkLoop_Unchecked(object s, RoutedEventArgs e)
    { if (!_isInitializing && _mediaModule is not null) { _mediaModule.IsLooping = false; if (_zone is not null) _zone.IsLooping = false; BeforeModification?.Invoke(); } }
    private void BtnScreen_Click(object sender, RoutedEventArgs e)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var menu = new System.Windows.Controls.ContextMenu();
        for (int i = 0; i < screens.Length; i++)
        {
            var idx = i;
            var label = $"Ecran {i + 1}  {screens[i].DeviceName}";
            if (screens[i].Primary) label += " (principal)";
            label += $"  {screens[i].Bounds.Width}x{screens[i].Bounds.Height}";
            var mi = new System.Windows.Controls.MenuItem { Header = label };
            if (_zone?.ScreenIndex == i)
                mi.FontWeight = FontWeights.Bold;
            mi.Click += (s2, e2) =>
            {
                if (_zone is not null)
                {
                    _zone.ScreenIndex = idx;
                    _zone.ScreenDeviceName = screens[idx].DeviceName;
                    BeforeModification?.Invoke();
                }
                UpdateScreenLabel(idx, screens);
                ScreenChanged?.Invoke();
            };
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = BtnScreen;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    private static int ResolveScreenIndex(Zone zone, System.Windows.Forms.Screen[] screens)
    {
        var idx = Math.Clamp(zone.ScreenIndex, 0, screens.Length - 1);
        if (!string.IsNullOrEmpty(zone.ScreenDeviceName))
        {
            var match = Array.FindIndex(screens, s => s.DeviceName == zone.ScreenDeviceName);
            if (match >= 0) idx = match;
        }
        return idx;
    }

    private void UpdateScreenLabel(int idx, System.Windows.Forms.Screen[] screens)
    {
        var label = $"Ecran {idx + 1}";
        if (idx < screens.Length && screens[idx].Primary) label += " *";
        TxtScreen.Text = label;
    }

    // ===== PUBLIC =====

    public (string? type, string? json) CopySelectedOverlay()
    {
        if (_zone?.Sequence is null) return (null, null);
        var seq = _zone.Sequence;
        var idx = seq.CurrentIndex;
        if (idx < 0 || idx >= seq.Items.Count) return (null, null);
        var item = seq.Items[idx];
        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = false };

        if (_editingButtonId is not null)
        {
            var btn = item.Buttons.FirstOrDefault(b => b.Id == _editingButtonId);
            if (btn is not null) return ("ButtonConfig", System.Text.Json.JsonSerializer.Serialize(btn, opts));
        }
        else if (_editingSubtitleId is not null)
        {
            var sub = item.Subtitles.FirstOrDefault(s => s.Id == _editingSubtitleId);
            if (sub is not null) return ("SubtitleConfig", System.Text.Json.JsonSerializer.Serialize(sub, opts));
        }
        else if (_editingPipId is not null)
        {
            var pip = item.PictureInPictures.FirstOrDefault(p => p.Id == _editingPipId);
            if (pip is not null) return ("PipConfig", System.Text.Json.JsonSerializer.Serialize(pip, opts));
        }
        else if (_editingImageId is not null)
        {
            var img = item.ImageOverlays.FirstOrDefault(i => i.Id == _editingImageId);
            if (img is not null) return ("ImageOverlayConfig", System.Text.Json.JsonSerializer.Serialize(img, opts));
        }
        else
        {
            // Copier l'item entier
            return ("SequenceItem", System.Text.Json.JsonSerializer.Serialize(item, opts));
        }
        return (null, null);
    }

    public void PasteOverlay(string type, string json)
    {
        if (_zone?.Sequence is null) return;
        var seq = _zone.Sequence;
        var idx = seq.CurrentIndex;
        if (idx < 0 || idx >= seq.Items.Count) return;
        var item = seq.Items[idx];
        var opts = new System.Text.Json.JsonSerializerOptions();

        try
        {
            switch (type)
            {
                case "ButtonConfig":
                    var btn = System.Text.Json.JsonSerializer.Deserialize<UMP.Core.Models.ButtonConfig>(json, opts);
                    if (btn is not null) { btn.Id = Guid.NewGuid().ToString(); item.Buttons.Add(btn); }
                    break;
                case "SubtitleConfig":
                    var sub = System.Text.Json.JsonSerializer.Deserialize<UMP.Core.Models.SubtitleConfig>(json, opts);
                    if (sub is not null) { sub.Id = Guid.NewGuid().ToString(); item.Subtitles.Add(sub); }
                    break;
                case "PipConfig":
                    var pip = System.Text.Json.JsonSerializer.Deserialize<UMP.Core.Models.PipConfig>(json, opts);
                    if (pip is not null) { pip.Id = Guid.NewGuid().ToString(); item.PictureInPictures.Add(pip); }
                    break;
                case "ImageOverlayConfig":
                    var img = System.Text.Json.JsonSerializer.Deserialize<UMP.Core.Models.ImageOverlayConfig>(json, opts);
                    if (img is not null) { img.Id = Guid.NewGuid().ToString(); item.ImageOverlays.Add(img); }
                    break;
                case "SequenceItem":
                    var newItem = System.Text.Json.JsonSerializer.Deserialize<UMP.Core.Models.SequenceItem>(json, opts);
                    if (newItem is not null)
                    {
                        newItem.Id = Guid.NewGuid().ToString();
                        foreach (var b in newItem.Buttons) b.Id = Guid.NewGuid().ToString();
                        foreach (var s in newItem.Subtitles) s.Id = Guid.NewGuid().ToString();
                        foreach (var p in newItem.PictureInPictures) p.Id = Guid.NewGuid().ToString();
                        foreach (var i in newItem.ImageOverlays) i.Id = Guid.NewGuid().ToString();
                        seq.Items.Insert(idx + 1, newItem);
                    }
                    break;
            }
            RefreshOverlayCanvas();
            SeqPanel.RefreshItems();
        }
        catch { }
    }

    private void PushUndo(Services.IUndoCommand cmd) => UndoCommandPushed?.Invoke(cmd);

    private string? _propsSnapshotJson;
    private object? _propsSnapshotTarget;

    private bool _propsDirty;

    private void PushPropsSnapshot()
    {
        // Marquer comme dirty — le snapshot sera pousse a la fermeture du panneau.
        // BeforeModification marque le projet non sauvegarde IMMEDIATEMENT :
        // sans ca, editer des proprietes panneau ouvert puis fermer l'appli
        // ne declenchait aucun prompt de sauvegarde.
        _propsDirty = true;
        BeforeModification?.Invoke();
    }

    private void FinalizePropsSnapshot()
    {
        if (!_propsDirty || _propsSnapshotJson is null || _propsSnapshotTarget is null) return;
        _propsDirty = false;
        var refresh = new Action(() => { RefreshOverlayCanvas(); SeqPanel.RefreshItems(); });
        Services.IUndoCommand? cmd = null;
        if (_propsSnapshotTarget is ButtonConfig b)
            cmd = new Services.SnapshotCommand<ButtonConfig>("Modifier bouton", b, _propsSnapshotJson, refresh);
        else if (_propsSnapshotTarget is SubtitleConfig s)
            cmd = new Services.SnapshotCommand<SubtitleConfig>("Modifier sous-titre", s, _propsSnapshotJson, refresh);
        else if (_propsSnapshotTarget is PipConfig p)
            cmd = new Services.SnapshotCommand<PipConfig>("Modifier PiP", p, _propsSnapshotJson, refresh);
        else if (_propsSnapshotTarget is ImageOverlayConfig i)
            cmd = new Services.SnapshotCommand<ImageOverlayConfig>("Modifier image", i, _propsSnapshotJson, refresh);
        if (cmd is not null && cmd.HasChanged) PushUndo(cmd);
        _propsSnapshotJson = null;
        _propsSnapshotTarget = null;
    }

    private double _moveStartX, _moveStartY;
    private object? _moveTarget;

    public void RefreshAfterUndo()
    {
        RefreshOverlayCanvas();
        SeqPanel.RefreshItems();
        // Recharger le panneau de proprietes ouvert avec les donnees restaurees
        if (_zone?.Sequence is null) return;
        var seq = _zone.Sequence;
        if (seq.CurrentIndex < 0 || seq.CurrentIndex >= seq.Items.Count) return;
        var item = seq.Items[seq.CurrentIndex];

        if (_editingButtonId is not null && BtnPropsPanel.Visibility == Visibility.Visible)
        {
            var btn = item.Buttons.FirstOrDefault(b => b.Id == _editingButtonId);
            if (btn is not null && _zonesRuntime is not null)
            {
                BtnPropsPanel.LoadButton(btn, _zone, _zonesRuntime);
                _propsSnapshotJson = Services.SnapshotCommand<ButtonConfig>.CaptureSnapshot(btn);
                _propsSnapshotTarget = btn;
                _propsDirty = false;
            }
        }
        else if (_editingSubtitleId is not null && SubPropsPanel.Visibility == Visibility.Visible)
        {
            var sub = item.Subtitles.FirstOrDefault(s => s.Id == _editingSubtitleId);
            if (sub is not null)
            {
                SubPropsPanel.LoadSubtitle(sub);
                _propsSnapshotJson = Services.SnapshotCommand<SubtitleConfig>.CaptureSnapshot(sub);
                _propsSnapshotTarget = sub;
                _propsDirty = false;
            }
        }
        else if (_editingPipId is not null && PipPropsPanel.Visibility == Visibility.Visible)
        {
            var pip = item.PictureInPictures.FirstOrDefault(p => p.Id == _editingPipId);
            if (pip is not null)
            {
                PipPropsPanel.LoadPip(pip);
                _propsSnapshotJson = Services.SnapshotCommand<PipConfig>.CaptureSnapshot(pip);
                _propsSnapshotTarget = pip;
                _propsDirty = false;
            }
        }
        else if (_editingImageId is not null && ImgPropsPanel.Visibility == Visibility.Visible)
        {
            var img = item.ImageOverlays.FirstOrDefault(i => i.Id == _editingImageId);
            if (img is not null)
            {
                ImgPropsPanel.LoadImage(img);
                _propsSnapshotJson = Services.SnapshotCommand<ImageOverlayConfig>.CaptureSnapshot(img);
                _propsSnapshotTarget = img;
                _propsDirty = false;
            }
        }
    }

    public void FinalizeAllPending()
    {
        FinalizePropsSnapshot();
        var moveCmd = FinalizeMoveCommand();
        if (moveCmd is not null) PushUndo(moveCmd);
    }

    public void MoveSelectedOverlay(System.Windows.Input.Key key)
    {
        if (_zone?.Sequence is null) return;
        var seq = _zone.Sequence;
        if (seq.CurrentIndex < 0 || seq.CurrentIndex >= seq.Items.Count) return;
        var item = seq.Items[seq.CurrentIndex];
        var step = 0.002;

        // Trouver la cible
        object? target = null;
        double curX = 0, curY = 0;
        if (_editingButtonId is not null)
        { var b = item.Buttons.FirstOrDefault(b => b.Id == _editingButtonId); if (b is not null) { target = b; curX = b.X; curY = b.Y; } }
        else if (_editingImageId is not null)
        { var i = item.ImageOverlays.FirstOrDefault(i => i.Id == _editingImageId); if (i is not null) { target = i; curX = i.X; curY = i.Y; } }
        else if (_editingSubtitleId is not null)
        { var s = item.Subtitles.FirstOrDefault(s => s.Id == _editingSubtitleId); if (s is not null) { target = s; curX = s.X; curY = s.Y; } }
        else if (_editingPipId is not null)
        { var p = item.PictureInPictures.FirstOrDefault(p => p.Id == _editingPipId); if (p is not null) { target = p; curX = p.X; curY = p.Y; } }
        if (target is null) return;

        // Sauvegarder la position initiale au premier deplacement
        if (_moveTarget != target) { _moveStartX = curX; _moveStartY = curY; _moveTarget = target; }

        // Appliquer le deplacement
        double dx = 0, dy = 0;
        if (key == System.Windows.Input.Key.Left) dx = -step;
        else if (key == System.Windows.Input.Key.Right) dx = step;
        else if (key == System.Windows.Input.Key.Up) dy = -step;
        else if (key == System.Windows.Input.Key.Down) dy = step;

        if (target is ButtonConfig btn) { btn.X += dx; btn.Y += dy; }
        else if (target is ImageOverlayConfig img) { img.X += dx; img.Y += dy; }
        else if (target is SubtitleConfig sub) { sub.X += dx; sub.Y += dy; }
        else if (target is PipConfig pip) { pip.X += dx; pip.Y += dy; }

        RefreshOverlayCanvas();
        HighlightEditingButton();
    }

    /// <summary>Finalise le deplacement et pousse la commande Undo</summary>
    public Services.IUndoCommand? FinalizeMoveCommand()
    {
        if (_moveTarget is null) return null;
        var target = _moveTarget;
        var startX = _moveStartX;
        var startY = _moveStartY;
        double endX = 0, endY = 0;

        if (target is ButtonConfig btn) { endX = btn.X; endY = btn.Y; }
        else if (target is ImageOverlayConfig img) { endX = img.X; endY = img.Y; }
        else if (target is SubtitleConfig sub) { endX = sub.X; endY = sub.Y; }
        else if (target is PipConfig pip) { endX = pip.X; endY = pip.Y; }

        if (Math.Abs(endX - startX) < 0.0001 && Math.Abs(endY - startY) < 0.0001)
        { _moveTarget = null; return null; }

        var sx = startX; var sy = startY; var ex = endX; var ey = endY;
        var refresh = new Action(RefreshOverlayCanvas);

        Services.IUndoCommand cmd;
        if (target is ButtonConfig b2)
            cmd = new Services.PropertyCommand<(double, double)>("Deplacer bouton",
                v => { b2.X = v.Item1; b2.Y = v.Item2; }, (sx, sy), (ex, ey), refresh);
        else if (target is ImageOverlayConfig i2)
            cmd = new Services.PropertyCommand<(double, double)>("Deplacer image",
                v => { i2.X = v.Item1; i2.Y = v.Item2; }, (sx, sy), (ex, ey), refresh);
        else if (target is SubtitleConfig s2)
            cmd = new Services.PropertyCommand<(double, double)>("Deplacer sous-titre",
                v => { s2.X = v.Item1; s2.Y = v.Item2; }, (sx, sy), (ex, ey), refresh);
        else if (target is PipConfig p2)
            cmd = new Services.PropertyCommand<(double, double)>("Deplacer PiP",
                v => { p2.X = v.Item1; p2.Y = v.Item2; }, (sx, sy), (ex, ey), refresh);
        else { _moveTarget = null; return null; }

        _moveTarget = null;
        return cmd;
    }

    public void DeleteSelectedOverlay()
    {
        if (_zone?.Sequence is null) return;
        var seq = _zone.Sequence;
        var idx = seq.CurrentIndex;
        if (idx < 0 || idx >= seq.Items.Count) return;
        var item = seq.Items[idx];

        if (_editingButtonId is not null)
        {
            var btn = item.Buttons.FirstOrDefault(b => b.Id == _editingButtonId);
            if (btn is not null)
            {
                if (System.Windows.MessageBox.Show("Supprimer ce bouton ?", "Confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                BeforeModification?.Invoke();
                item.Buttons.Remove(btn);
                CloseBtnPropsPanel();
                RefreshOverlayCanvas();
                SeqPanel.RefreshItems();
            }
        }
        else if (_editingSubtitleId is not null)
        {
            var sub = item.Subtitles.FirstOrDefault(s => s.Id == _editingSubtitleId);
            if (sub is not null)
            {
                if (System.Windows.MessageBox.Show("Supprimer ce sous-titre ?", "Confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                BeforeModification?.Invoke();
                item.Subtitles.Remove(sub);
                CloseSubPropsPanel();
                RefreshOverlayCanvas();
                SeqPanel.RefreshItems();
            }
        }
        else if (_editingPipId is not null)
        {
            var pip = item.PictureInPictures.FirstOrDefault(p => p.Id == _editingPipId);
            if (pip is not null)
            {
                if (System.Windows.MessageBox.Show("Supprimer ce PiP ?", "Confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                BeforeModification?.Invoke();
                item.PictureInPictures.Remove(pip);
                ClosePipPropsPanel();
                RefreshOverlayCanvas();
                SeqPanel.RefreshItems();
            }
        }
        else if (_editingImageId is not null)
        {
            var img = item.ImageOverlays.FirstOrDefault(i => i.Id == _editingImageId);
            if (img is not null)
            {
                if (System.Windows.MessageBox.Show("Supprimer cette image ?", "Confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                BeforeModification?.Invoke();
                item.ImageOverlays.Remove(img);
                CloseImgPropsPanel();
                RefreshOverlayCanvas();
                SeqPanel.RefreshItems();
            }
        }
    }

    public void SetAutoPlay(bool auto) => _autoPlay = auto;

    public void StopPlayback()
    {
        _jumpEndIndex = -1;
        _sequencePlayer?.Stop();
        _timer?.Stop();
        _slideStopwatch?.Stop(); _slideStopwatch = null;
        var mm = _mediaModule;
        if (mm is not null) Task.Run(() => { try { mm.Stop(); } catch { } });
        DisposePipViews();
        Dispatcher.BeginInvoke(new Action(() => SetPlaying(false)));
    }

    public void PlayFirstItem()
    {
        if (_zone?.Sequence is null || _zone.Sequence.Items.Count == 0) return;
        var first = _zone.Sequence.Items[0];
        if (string.IsNullOrEmpty(first.EffectivePath)) return;
        _lastFilePath = first.EffectivePath;
        _zone.MediaFilePath = first.EffectivePath;
        _timer?.Stop();
        EnsureSequencePlayer();
        _sequencePlayer!.JumpTo(0);
    }

    public void SetZonesRuntime(Dictionary<string, ZoneRuntime> r)
    {
        _zonesRuntime = r;
    }
    public string GetZoneId() => _zone?.Id ?? "";
    public void UpdateRuntime(ZoneRuntime r)
    { r.PlayAction = ActivePlay; r.PauseAction = ActivePause; r.StopAction = ActiveStop; r.GetCurrentMsAction = GetCurrentMs; }

    private long GetCurrentMs()
    {
        var p = _mediaModule?.Player;
        if (p is null) return -1;
        var l = p.Length;
        return l <= 0 ? -1 : (long)(p.Position * l);
    }

    private void BtnSequence_Click(object sender, RoutedEventArgs e)
    {
        if (SeqPanel.Visibility == Visibility.Collapsed)
            OpenSequencePanel();
        else
        { SeqPanel.Visibility = Visibility.Collapsed; ColSequence.Width = new System.Windows.GridLength(0); }
    }

    /// <summary>
    /// Ouvre le panneau sequence avec TOUT le cablage (callbacks + events).
    /// Utilise par le bouton Sequence ET le drag&drop : un panneau ouvert
    /// par drop n'avait auparavant ni undo, ni selection, ni SequenceChanged.
    /// </summary>
    private void OpenSequencePanel()
    {
            SeqPanel.Visibility = Visibility.Visible;
            ColSequence.Width = new System.Windows.GridLength(420);
            SeqPanel.Initialize(_zone!, GetCurrentMs);
            SeqPanel.SetZoneCallbacks(
                (item, btn) => OpenBtnPropsPanel(btn),
                (btn) => SelectOverlayButton(btn),
                (img) => SelectOverlayImage(img),
                () => RefreshOverlayCanvas());
            SeqPanel.SetSubtitleCallback((sub) => SelectOverlaySubtitle(sub));
            SeqPanel.SetPipCallback((pip) => SelectOverlayPip(pip));

            if (!_seqPanelInitialized)
            {
                _seqPanelInitialized = true;
                SeqPanel.CloseRequested += () =>
                { SeqPanel.Visibility = Visibility.Collapsed; ColSequence.Width = new System.Windows.GridLength(0); };

                // Toute modification de sequence (ajout/suppr/deplacement d'item,
                // duree de slide, boutons/sous-titres/PiP...) doit marquer le projet
                // comme non sauvegarde. BeforeModification est relaye jusqu'a
                // MainWindow.MarkUnsaved via l'abonnement existant.
                SeqPanel.SequenceChanged += () => BeforeModification?.Invoke();
                SeqPanel.UndoCommandPushed += c => PushUndo(c);

                SeqPanel.ItemSelected += (item) =>
                {
                    var idx = _zone?.Sequence?.Items.IndexOf(item) ?? -1;
                    if (idx < 0) return;
                    if (_zone?.Sequence is not null) _zone.Sequence.CurrentIndex = idx;
                    _timer?.Stop();
                    _mediaModule!.IsLooping = false;
                    // REGLE : pas de File.Exists sur UI thread
                    // Verifier seulement si le path est defini
                    var path = item.EffectivePath;
                    if (!string.IsNullOrEmpty(path))
                    { _lastFilePath = path; _zone!.MediaFilePath = path;
                      EnsureSequencePlayer(); _sequencePlayer!.JumpTo(idx); }
                    else
                        SeqPanel.SetActiveIndex(idx);
                };

                SeqPanel.ItemFileChangeRequested += (item) =>
                {
                    var d = new Microsoft.Win32.OpenFileDialog
                        { Filter = MediaFilter };
                    if (d.ShowDialog() != true) return;

                    var ext = Path.GetExtension(d.FileName).ToLower();
                    var isImg = ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";
                    if (isImg) { item.ImageSlidePath = d.FileName; item.MediaPath = null; }
                    else { item.MediaPath = d.FileName; item.ImageSlidePath = null; }
                    BeforeModification?.Invoke();

                    // Rafraichir la liste d'abord
                    SeqPanel.RefreshItems();

                    // Auto-preview uniquement pour les videos
                    // Les images sont previsualisees quand on clique sur l'item
                    var idx = _zone?.Sequence?.Items.IndexOf(item) ?? -1;
                    if (idx == _zone?.Sequence?.CurrentIndex && !isImg)
                    {
                        _lastFilePath = d.FileName;
                        _zone!.MediaFilePath = d.FileName;
                        _timer?.Stop();
                        ActivePlay(d.FileName);
                    }

                    VideoLoaded?.Invoke();
                };
            }
    }


    private void ExecuteSequenceAction(ButtonAction action)
    { Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => ExecuteButtonAction(action))); }

    // ===== OVERLAY CANVAS =====

    /// <summary>
    /// Calcule le rectangle de rendu du media (video/image) dans le canvas.
    /// Le media est affiche en Uniform (aspect ratio preserve, centre).
    /// Retourne (offsetX, offsetY, renderedWidth, renderedHeight).
    /// </summary>
    private (double ox, double oy, double rw, double rh) GetContentRect()
    {
        var cw = OverlayCanvas.ActualWidth;
        var ch = OverlayCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0 || _mediaW <= 0 || _mediaH <= 0)
            return (0, 0, 0, 0); // dimensions inconnues → pas de rendu

        var scale = Math.Min(cw / _mediaW, ch / _mediaH);
        var rw = _mediaW * scale;
        var rh = _mediaH * scale;
        var ox = (cw - rw) / 2.0;
        var oy = (ch - rh) / 2.0;
        return (ox, oy, rw, rh);
    }

    private void UpdateButtonPositions()
    {
        var (ox, oy, rw, rh) = GetContentRect();
        if (rw <= 0 || rh <= 0) return;

        foreach (UIElement el in OverlayCanvas.Children)
        {
            if (el is Border b && b.Tag is ButtonConfig btn)
            {
                MigrateButtonIfNeeded(btn);
                // SafeSize obligatoire : lors d'un redimensionnement de fenetre,
                // rw/rh peuvent devenir minuscules -> largeur < 1 px -> DesiredSize
                // NaN sur un Border a TextBlock wrappant -> crash de layout WPF.
                var bw = SafeSize(btn.Width * rw);
                var bh = SafeSize(btn.Height * rh);
                b.Width = bw;
                b.Height = bh;
                b.CornerRadius = new CornerRadius(btn.CornerRadius * Math.Min(bw, bh));
                Canvas.SetLeft(b, ox + btn.X * rw);
                Canvas.SetTop(b, oy + btn.Y * rh);
                // FontSize proportionnel : pt * scale relatif a 1080p
                var fontScale = rh / 1080.0;
                if (b.Child is TextBlock tb2)
                    tb2.FontSize = Math.Clamp(btn.FontSize * (4.0 / 3.0) * fontScale, 6, 200);
            }
            else if (el is Border ib && ib.Tag is ImageOverlayConfig img)
            {
                var iw = SafeSize(img.Width * rw);
                var ih = SafeSize(img.Height * rh);
                ib.Width = iw; ib.Height = ih;
                ib.CornerRadius = new CornerRadius(img.CornerRadius * Math.Min(iw, ih));
                Canvas.SetLeft(ib, ox + img.X * rw);
                Canvas.SetTop(ib, oy + img.Y * rh);
            }
            else if (el is Border sb && sb.Tag is SubtitleConfig sub)
            {
                sb.Width = SafeSize(sub.Width * rw);
                Canvas.SetLeft(sb, ox + sub.X * rw);
                Canvas.SetTop(sb, oy + sub.Y * rh);
                var fontScale2 = rh / 1080.0;
                if (sb.Child is TextBlock stb)
                    stb.FontSize = Math.Clamp(sub.FontSize * (4.0 / 3.0) * fontScale2, 6, 200);
            }
            else if (el is Border pb && pb.Tag is PipConfig pip)
            {
                pb.Width = SafeSize(pip.Width * rw);
                pb.Height = SafeSize(pip.Height * rh);
                pb.CornerRadius = new CornerRadius(pip.CornerRadius * Math.Min(pb.Width, pb.Height));
                Canvas.SetLeft(pb, ox + pip.X * rw);
                Canvas.SetTop(pb, oy + pip.Y * rh);
            }
        }
    }

    private int _refreshRetries = 0;

    public void RefreshOverlayCanvas()
    {
        if (_refreshingCanvas) return;
        OverlayCanvas.Children.Clear();
        if (_zone?.Sequence is null) { DisposePipViews(); return; }

        var cw = OverlayCanvas.ActualWidth;
        var ch = OverlayCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0)
        {
            if (_refreshRetries >= 3) { _refreshRetries = 0; return; }
            _refreshRetries++;
            _refreshingCanvas = true;
            Dispatcher.InvokeAsync(() => { _refreshingCanvas = false; RefreshOverlayCanvas(); }, DispatcherPriority.Render);
            return;
        }
        _refreshRetries = 0;

        var seq = _zone.Sequence;
        if (seq.Items.Count == 0) { DisposePipViews(); return; }
        if (seq.CurrentIndex < 0 || seq.CurrentIndex >= seq.Items.Count) seq.CurrentIndex = 0;

        var (ox, oy, rw, rh) = GetContentRect();
        if (rw <= 0 || rh <= 0)
        {
            // Dimensions media pas encore connues → reessayer dans 500ms
            if (_refreshRetries < 10)
            {
                _refreshRetries++;
                _refreshingCanvas = true;
                Task.Delay(500).ContinueWith(_ =>
                    Dispatcher.BeginInvoke(DispatcherPriority.Background,
                        new Action(() => { _refreshingCanvas = false; RefreshOverlayCanvas(); })));
            }
            return;
        }

        var item = seq.Items[seq.CurrentIndex];
        // Liberer les PiP qui ne sont plus dans l'item courant (les autres persistent)
        PrunePipViews(item.PictureInPictures.Select(p => p.Id));
        // Images overlay d'abord (en dessous des boutons)
        foreach (var img in item.ImageOverlays)
            AddImageOverlayElement(img, ox, oy, rw, rh);
        foreach (var btn in item.Buttons)
            AddButtonElement(btn, ox, oy, rw, rh);
        foreach (var pip in item.PictureInPictures)
            AddPipElement(pip, ox, oy, rw, rh);
        foreach (var sub in item.Subtitles)
            AddSubtitleElement(sub, ox, oy, rw, rh);

        UpdateBrackets();
    }

    private void AddImageOverlayElement(ImageOverlayConfig img,
        double ox, double oy, double rw, double rh)
    {
        if (string.IsNullOrEmpty(img.ImagePath)) return;

        var iw = SafeSize(img.Width * rw);
        var ih = SafeSize(img.Height * rh);
        var cr = img.CornerRadius * Math.Min(iw, ih);

        BitmapImage? bmp = null;
        try
        {
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(img.ImagePath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit(); bmp.Freeze();
        }
        catch { return; }

        var image = new Image
        {
            Source = bmp, Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };

        var border = new Border
        {
            Width = iw, Height = ih,
            CornerRadius = new CornerRadius(cr),
            ClipToBounds = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(Math.Max(0, img.BorderWidth)),
            BorderBrush = ParseBrush(img.BorderColor, Color.FromRgb(255, 255, 255)),
            Opacity = Math.Clamp(img.Opacity, 0.0, 1.0),
            Child = image,
            Tag = img,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        if (img.Rotation != 0)
            border.RenderTransform = new System.Windows.Media.RotateTransform(img.Rotation, iw / 2, ih / 2);

        Canvas.SetLeft(border, ox + img.X * rw);
        Canvas.SetTop(border, oy + img.Y * rh);

        // Clic gauche = selectionner
        border.PreviewMouseLeftButtonUp += (s, e) =>
        {
            SelectOverlayImage(img);
            e.Handled = true;
        };

        // Clic droit = drag
        border.PreviewMouseRightButtonDown += (s, e) =>
        {
            SelectOverlayImage(img);
            _draggingImage = img;
            _moveStartX = img.X; _moveStartY = img.Y; _moveTarget = img;
            _dragOffset = e.GetPosition(border);
            border.Cursor = System.Windows.Input.Cursors.SizeAll;
            e.Handled = true;
        };

        OverlayCanvas.Children.Add(border);
    }

    // Cache des sous-titres parses par fichier
    private readonly Dictionary<string, List<SubtitleEntry>> _subtitleCache = new();

    private List<SubtitleEntry> GetSubtitleEntries(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return new();
        if (_subtitleCache.TryGetValue(filePath, out var cached)) return cached;
        var entries = SubtitlePropertiesPanel.ParseSrtFile(filePath);
        _subtitleCache[filePath] = entries;
        return entries;
    }

    private void AddSubtitleElement(SubtitleConfig sub,
        double ox, double oy, double rw, double rh)
    {
        if (string.IsNullOrEmpty(sub.FilePath)) return;

        var sw = SafeSize(sub.Width * rw);
        var fontScale = rh / 1080.0;
        var fs = Math.Clamp(sub.FontSize * (4.0 / 3.0) * fontScale, 6, 200);

        var fw = FontWeights.Bold;
        try { var c = new System.Windows.FontWeightConverter().ConvertFromString(sub.FontWeight);
            if (c is System.Windows.FontWeight p) fw = p; } catch { }
        System.Windows.Media.FontFamily ff;
        try { ff = SubtitlePropertiesPanel.ResolveFontFamily(sub); }
        catch { ff = new System.Windows.Media.FontFamily("Segoe UI"); }

        var align = sub.TextAlign switch
        {
            "Left" => TextAlignment.Left,
            "Right" => TextAlignment.Right,
            _ => TextAlignment.Center
        };

        var tb = new TextBlock
        {
            Text = "", FontSize = fs, FontWeight = fw, FontFamily = ff,
            Foreground = ParseBrush(sub.TextColor, Color.FromRgb(255, 255, 255)),
            TextWrapping = TextWrapping.Wrap, TextAlignment = align,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Math.Max(0, sub.Padding))
        };
        System.Windows.Media.TextOptions.SetTextRenderingMode(tb, System.Windows.Media.TextRenderingMode.ClearType);
        // Ombre
        if (sub.ShadowBlur > 0)
        {
            try
            {
                var angle = Math.Atan2(sub.ShadowOffsetY, sub.ShadowOffsetX) * 180.0 / Math.PI;
                var depth = Math.Sqrt(sub.ShadowOffsetX * sub.ShadowOffsetX + sub.ShadowOffsetY * sub.ShadowOffsetY);
                // Echelle des effets alignee sur celle du texte : sans ca, un flou
                // de 5 px sur un texte reduit a ~8 px dans le viewport dilue
                // l'ombre/le contour jusqu'a l'invisible.
                tb.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(sub.ShadowColor),
                    BlurRadius = sub.ShadowBlur * fontScale, Direction = angle,
                    ShadowDepth = depth * fontScale,
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
                Text = "", FontSize = fs, FontFamily = ff, FontWeight = fw,
                Foreground = ParseBrush(sub.OutlineColor, Color.FromRgb(0, 0, 0)),
                TextWrapping = TextWrapping.Wrap, TextAlignment = align,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = tb.Margin
            };
            // Rayon a l'echelle du texte (borne basse pour rester visible)
            strokeTb.Effect = new System.Windows.Media.Effects.BlurEffect
            { Radius = Math.Max(0.75, sub.OutlineWidth * fontScale) };
            outlineGrid.Children.Add(strokeTb);
            outlineGrid.Children.Add(tb);
            outlineGrid.Tag = strokeTb;
            subContent = outlineGrid;
        }
        else subContent = tb;

        var cr2 = sub.CornerRadius * Math.Min(sw, sw * 0.5);
        var border = new Border
        {
            Width = sw,
            CornerRadius = new CornerRadius(cr2),
            Background = ParseBrush(sub.BackgroundColor, Color.FromArgb(150, 0, 0, 0)),
            BorderThickness = new Thickness(Math.Max(0, sub.BorderWidth)),
            BorderBrush = ParseBrush(sub.BorderColor, Color.FromRgb(255, 255, 255)),
            Opacity = Math.Clamp(sub.Opacity, 0.1, 1.0),
            Child = subContent, Tag = sub,
            Cursor = System.Windows.Input.Cursors.Hand,
            Visibility = Visibility.Collapsed // cache par defaut, UpdateOverlayVisibility l'affiche
        };
        Canvas.SetLeft(border, ox + sub.X * rw);
        Canvas.SetTop(border, oy + sub.Y * rh);

        border.PreviewMouseLeftButtonUp += (s, e) =>
        { SelectOverlaySubtitle(sub); e.Handled = true; };
        border.PreviewMouseRightButtonDown += (s, e) =>
        {
            SelectOverlaySubtitle(sub);
            _draggingSubtitle = sub;
            _moveStartX = sub.X; _moveStartY = sub.Y; _moveTarget = sub;
            _dragOffset = e.GetPosition(border);
            border.Cursor = System.Windows.Input.Cursors.SizeAll;
            e.Handled = true;
        };

        OverlayCanvas.Children.Add(border);
    }

    private void AddPipElement(PipConfig pip,
        double ox, double oy, double rw, double rh)
    {
        if (string.IsNullOrEmpty(pip.VideoPath)) return;

        var pw = SafeSize(pip.Width * rw);
        var ph = SafeSize(pip.Height * rh);
        var cr = pip.CornerRadius * Math.Min(pw, ph);

        Border border;
        if (_pipViews.TryGetValue(pip.Id, out var existing) && existing.Child is AlphaVideoView)
        {
            // Reutiliser le PiP existant : evite de relancer le decodage FFmpeg
            // (et donc le clignotement) a chaque rafraichissement du canvas.
            border = existing;
            border.Width = pw; border.Height = ph;
            border.CornerRadius = new CornerRadius(cr);
            border.BorderThickness = new Thickness(Math.Max(0, pip.BorderWidth));
            border.BorderBrush = ParseBrush(pip.BorderColor, Color.FromRgb(255, 255, 255));
            border.Opacity = Math.Clamp(pip.Opacity, 0.1, 1.0);
        }
        else
        {
            // Rendu via FFmpeg (FFMediaToolkit) pour supporter l'alpha (ProRes 4444, etc.)
            var media = new AlphaVideoView { Stretch = Stretch.Uniform, IsLooping = pip.IsLooping };
            media.Open(pip.VideoPath);
            media.Play();

            border = new Border
            {
                Width = pw, Height = ph,
                CornerRadius = new CornerRadius(cr),
                ClipToBounds = true,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(Math.Max(0, pip.BorderWidth)),
                BorderBrush = ParseBrush(pip.BorderColor, Color.FromRgb(255, 255, 255)),
                Opacity = Math.Clamp(pip.Opacity, 0.1, 1.0),
                Child = media, Tag = pip,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            // Clic gauche = selectionner
            border.PreviewMouseLeftButtonUp += (s, e) =>
            { SelectOverlayPip(pip); e.Handled = true; };
            // Clic droit = drag
            border.PreviewMouseRightButtonDown += (s, e) =>
            {
                SelectOverlayPip(pip);
                _draggingPip = pip;
                _moveStartX = pip.X; _moveStartY = pip.Y; _moveTarget = pip;
                _dragOffset = e.GetPosition(border);
                border.Cursor = System.Windows.Input.Cursors.SizeAll;
                e.Handled = true;
            };
            // Etat initial : visible, sauf si "Demarre cache"
            if (pip.StartHidden) _pipToggledVisible.Remove(pip.Id);
            else _pipToggledVisible.Add(pip.Id);
            _pipViews[pip.Id] = border;
        }

        Canvas.SetLeft(border, ox + pip.X * rw);
        Canvas.SetTop(border, oy + pip.Y * rh);
        border.RenderTransform = pip.Rotation != 0
            ? new System.Windows.Media.RotateTransform(pip.Rotation, pw / 2, ph / 2) : null;
        border.Visibility = _pipToggledVisible.Contains(pip.Id)
            ? Visibility.Visible : Visibility.Collapsed;

        if (!OverlayCanvas.Children.Contains(border))
            OverlayCanvas.Children.Add(border);
    }

    private void SelectOverlayPip(PipConfig pip)
    {
        _editingPipId = pip.Id;
        _editingButtonId = null;
        _editingImageId = null;
        _editingSubtitleId = null;
        SeqPanel.SetEditingPip(pip.Id);
        HighlightEditingButton();
        UpdateBrackets();
        System.Windows.Input.Keyboard.Focus(Window.GetWindow(this));
        BtnPropsPanel.Visibility = Visibility.Collapsed;
        SubPropsPanel.Visibility = Visibility.Collapsed;
        OpenPipPropsPanel(pip);
    }

    private void SelectOverlaySubtitle(SubtitleConfig sub)
    {
        _editingSubtitleId = sub.Id;
        _editingButtonId = null;
        _editingImageId = null;
        _editingPipId = null;
        SeqPanel.SetEditingButton(null);
        SeqPanel.SetEditingImage(null);
        SeqPanel.SetEditingSubtitle(sub.Id);
        HighlightEditingButton();
        UpdateBrackets();
        System.Windows.Input.Keyboard.Focus(Window.GetWindow(this));
        // Ouvrir le panneau proprietes sous-titre
        BtnPropsPanel.Visibility = Visibility.Collapsed;
        OpenSubPropsPanel(sub);
    }

    /// <summary>
    /// Migre un bouton de l'ancien format (pixels absolus) vers le nouveau (fractions 0-1).
    /// Detecte automatiquement : si Width > 1.0, c'est l'ancien format.
    /// </summary>
    private static void MigrateButtonIfNeeded(ButtonConfig btn)
    {
        if (btn.Width > 1.0)
        {
            btn.X = btn.X / 1920.0;
            btn.Y = btn.Y / 1080.0;
            btn.Width = btn.Width / 1920.0;
            btn.Height = btn.Height / 1080.0;
        }
        // FontSize est maintenant en px — les anciennes fractions (< 1.0) → convertir en px
        if (btn.FontSize < 1.0) btn.FontSize = 14;
        if (btn.CornerRadius > 1.0) btn.CornerRadius = 0.25;
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

    private static TextBlock MakeButtonLabel(ButtonConfig btn, double fs,
        System.Windows.FontWeight fw, System.Windows.Media.FontFamily ff, Brush fg)
    {
        var pad = Math.Max(0, btn.Padding);
        return new TextBlock
        {
            Text = btn.IsToggleActive ? btn.LabelOn : btn.Label,
            FontSize = fs, FontWeight = fw, FontFamily = ff,
            Foreground = fg,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(pad)
        };
    }

    private void AddButtonElement(ButtonConfig btn,
        double ox, double oy, double rw, double rh)
    {
        MigrateButtonIfNeeded(btn);

        var bw = SafeSize(btn.Width * rw);
        var bh = SafeSize(btn.Height * rh);
        var cr = btn.CornerRadius * Math.Min(bw, bh);
        var fontScale = rh / 1080.0;
        var rawFs = btn.IsToggleActive ? btn.FontSizeOn : btn.FontSize;
        var fs = Math.Clamp(rawFs * (4.0 / 3.0) * fontScale, 6, 200);

        // Couleur de fond — forcer semi-transparence si couleur opaque
        var bgHex = btn.IsToggleActive ? btn.BackgroundColorOn : btn.BackgroundColor;
        Color bgColor;
        try
        {
            var parsed = (Color)System.Windows.Media.ColorConverter.ConvertFromString(bgHex);
            // Si la couleur est totalement opaque, rendre semi-transparente
            if (parsed.A == 255) parsed.A = 200;
            bgColor = parsed;
        }
        catch { bgColor = Color.FromArgb(200, 42, 42, 64); }

        // Couleur texte
        Brush fgBrush;
        try { fgBrush = new BrushConverter().ConvertFromString(
            btn.IsToggleActive ? btn.TextColorOn : btn.TextColor) as Brush ?? Brushes.White; }
        catch { fgBrush = Brushes.White; }

        // Fond avec gradient subtil (plus clair en haut, plus fonce en bas)
        var bgTop = Color.FromArgb(bgColor.A,
            (byte)Math.Min(255, bgColor.R + 25),
            (byte)Math.Min(255, bgColor.G + 25),
            (byte)Math.Min(255, bgColor.B + 25));
        var bgGradient = new System.Windows.Media.LinearGradientBrush(bgTop, bgColor, 90);

        // Typo
        var fw = FontWeights.SemiBold;
        var rawFw = btn.IsToggleActive ? btn.FontWeightOn : btn.FontWeight;
        try { var conv = new System.Windows.FontWeightConverter().ConvertFromString(rawFw);
            if (conv is System.Windows.FontWeight parsed) fw = parsed; } catch { }
        System.Windows.Media.FontFamily ff;
        try
        {
            if (!string.IsNullOrEmpty(btn.CustomFontPath) && System.IO.File.Exists(btn.CustomFontPath))
                ff = SubtitlePropertiesPanel.CreateFontFamily(btn.CustomFontPath);
            else
                ff = new System.Windows.Media.FontFamily(btn.FontFamily);
        }
        catch { ff = new System.Windows.Media.FontFamily("Segoe UI"); }

        // Contenu : image (toggle-aware) ou texte
        var imgPath = btn.IsToggleActive && !string.IsNullOrEmpty(btn.ImagePathOn)
            ? btn.ImagePathOn : btn.ImagePath;
        UIElement content;
        // hasImg track si le contenu est une image
        if (!string.IsNullOrEmpty(imgPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imgPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit(); bmp.Freeze();
                var pad = Math.Max(0, btn.Padding);
                content = new Image
                {
                    Source = bmp, Stretch = Stretch.Uniform,
                    Margin = new Thickness(pad)
                };
                // image chargee
            }
            catch { content = MakeButtonLabel(btn, fs, fw, ff, fgBrush); }
        }
        else
        {
            content = MakeButtonLabel(btn, fs, fw, ff, fgBrush);
        }

        var bWidth = Math.Max(0, btn.IsToggleActive ? btn.BorderWidthOn : btn.BorderWidth);
        var isOutside = btn.BorderPos == UMP.Core.Models.BorderPosition.Outside;
        var borderW = isOutside ? bw + bWidth * 2 : bw;
        var borderH = isOutside ? bh + bWidth * 2 : bh;

        var border = new Border
        {
            Width = borderW, Height = borderH,
            CornerRadius = new CornerRadius(cr),
            Background = bgGradient,
            BorderThickness = new Thickness(bWidth),
            BorderBrush = ParseBrush(
                btn.IsToggleActive ? btn.BorderColorOn : btn.BorderColor,
                Color.FromRgb(255, 255, 255)),
            Cursor = System.Windows.Input.Cursors.Hand, Tag = btn,
            Child = content,
            Opacity = Math.Clamp(btn.Opacity, 0.1, 1.0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 0, 0), BlurRadius = 12,
                ShadowDepth = 2, Opacity = 0.5, Direction = 270
            }
        };
        var posX = ox + btn.X * rw - (isOutside ? bWidth : 0);
        var posY = oy + btn.Y * rh - (isOutside ? bWidth : 0);
        Canvas.SetLeft(border, posX);
        Canvas.SetTop(border, posY);
        if (btn.Rotation != 0)
            border.RenderTransform = new System.Windows.Media.RotateTransform(
                btn.Rotation, borderW / 2, borderH / 2);

        // Clic gauche = selectionner + executer les actions du bouton
        border.PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (_draggingButton is not null) return; // on etait en drag
            SelectOverlayButton(btn);
            ExecuteButtonActions(btn);
            e.Handled = true;
        };

        // Clic droit enfonce = selectionner + demarrer le drag
        border.PreviewMouseRightButtonDown += (s, e) =>
        {
            SelectOverlayButton(btn);
            _draggingButton = btn;
            _dragOffset = e.GetPosition(border);
            _moveStartX = btn.X; _moveStartY = btn.Y; _moveTarget = btn;
            border.Cursor = System.Windows.Input.Cursors.SizeAll;
            e.Handled = true;
        };

        // Hover — changer fond, texte et bordure au survol
        var normalBg = border.Background;
        var normalFg = (content is TextBlock t) ? t.Foreground : null;
        var normalBorder = border.BorderBrush;
        var normalBorderWidth = border.BorderThickness;
        border.MouseEnter += (s, e) =>
        {
            if (_draggingButton is not null || _resizingButton is not null) return;
            try
            {
                var hBg = (Color)System.Windows.Media.ColorConverter.ConvertFromString(btn.BackgroundColorHover);
                if (hBg.A == 255) hBg.A = 220;
                border.Background = new SolidColorBrush(hBg);
            }
            catch { }
            if (content is TextBlock ht)
            {
                try { ht.Foreground = new BrushConverter().ConvertFromString(btn.TextColorHover) as Brush ?? Brushes.White; }
                catch { }
                var fontScaleH = rh / 1080.0;
                ht.FontSize = Math.Clamp(btn.FontSizeHover * (4.0 / 3.0) * fontScaleH, 6, 200);
                try { var c = new System.Windows.FontWeightConverter().ConvertFromString(btn.FontWeightHover);
                    if (c is System.Windows.FontWeight pw) ht.FontWeight = pw; } catch { }
            }
            border.BorderBrush = ParseBrush(btn.BorderColorHover, Color.FromArgb(60, 255, 255, 255));
            border.BorderThickness = new Thickness(Math.Max(0, btn.BorderWidthHover));
        };
        var normalFs = fs;
        var normalFw = fw;
        border.MouseLeave += (s, e) =>
        {
            border.Background = normalBg;
            if (content is TextBlock lt)
            {
                if (normalFg is not null) lt.Foreground = normalFg;
                lt.FontSize = normalFs;
                lt.FontWeight = normalFw;
            }
            border.BorderBrush = normalBorder;
            border.BorderThickness = normalBorderWidth;
        };

        OverlayCanvas.Children.Add(border);
    }

    private void OverlayCanvas_MouseMove(object s, System.Windows.Input.MouseEventArgs e)
    {
        // === ROTATION (image ou bouton) ===
        if (_rotatingImage is not null && e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var (rox2, roy2, rrw2, rrh2) = GetContentRect();
            if (rrw2 <= 0) return;
            var mp = e.GetPosition(OverlayCanvas);
            var cx = rox2 + _rotatingImage.X * rrw2 + _rotatingImage.Width * rrw2 / 2;
            var cy = roy2 + _rotatingImage.Y * rrh2 + _rotatingImage.Height * rrh2 / 2;
            var angle = Math.Atan2(mp.Y - cy, mp.X - cx) * 180.0 / Math.PI + 90;
            _rotatingImage.Rotation = ((angle % 360) + 360) % 360;
            // Mettre a jour le transform en place
            foreach (UIElement el in OverlayCanvas.Children)
            {
                if (el is Border rb && rb.Tag is ImageOverlayConfig ric && ric == _rotatingImage)
                {
                    rb.RenderTransform = new System.Windows.Media.RotateTransform(
                        _rotatingImage.Rotation, rb.Width / 2, rb.Height / 2);
                    break;
                }
            }
            return;
        }
        if (_rotatingButton is not null && e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var (rox2b, roy2b, rrw2b, rrh2b) = GetContentRect();
            if (rrw2b <= 0) return;
            var mp = e.GetPosition(OverlayCanvas);
            var cx = rox2b + _rotatingButton.X * rrw2b + _rotatingButton.Width * rrw2b / 2;
            var cy = roy2b + _rotatingButton.Y * rrh2b + _rotatingButton.Height * rrh2b / 2;
            var angle = Math.Atan2(mp.Y - cy, mp.X - cx) * 180.0 / Math.PI + 90;
            _rotatingButton.Rotation = ((angle % 360) + 360) % 360;
            foreach (UIElement el in OverlayCanvas.Children)
            {
                if (el is Border rb && rb.Tag is ButtonConfig rbc && rbc == _rotatingButton)
                {
                    rb.RenderTransform = new System.Windows.Media.RotateTransform(
                        _rotatingButton.Rotation, rb.Width / 2, rb.Height / 2);
                    break;
                }
            }
            return;
        }
        if (_rotatingSubtitle is not null && e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var (rox2s, roy2s, rrw2s, rrh2s) = GetContentRect();
            if (rrw2s <= 0) return;
            var mp = e.GetPosition(OverlayCanvas);
            var cx = rox2s + _rotatingSubtitle.X * rrw2s + _rotatingSubtitle.Width * rrw2s / 2;
            var cy = roy2s + _rotatingSubtitle.Y * rrh2s + rrh2s * 0.04;
            var angle = Math.Atan2(mp.Y - cy, mp.X - cx) * 180.0 / Math.PI + 90;
            _rotatingSubtitle.Rotation = ((angle % 360) + 360) % 360;
            foreach (UIElement el in OverlayCanvas.Children)
                if (el is Border rb && rb.Tag is SubtitleConfig rsc && rsc == _rotatingSubtitle)
                { rb.RenderTransform = new System.Windows.Media.RotateTransform(_rotatingSubtitle.Rotation, rb.ActualWidth / 2, rb.ActualHeight / 2); break; }
            return;
        }
        if (_rotatingPip is not null && e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var (roxP, royP, rrwP, rrhP) = GetContentRect();
            if (rrwP <= 0) return;
            var mp = e.GetPosition(OverlayCanvas);
            var cx = roxP + _rotatingPip.X * rrwP + _rotatingPip.Width * rrwP / 2;
            var cy = royP + _rotatingPip.Y * rrhP + _rotatingPip.Height * rrhP / 2;
            var angle = Math.Atan2(mp.Y - cy, mp.X - cx) * 180.0 / Math.PI + 90;
            _rotatingPip.Rotation = ((angle % 360) + 360) % 360;
            foreach (UIElement el in OverlayCanvas.Children)
                if (el is Border rb && rb.Tag is PipConfig rpc && rpc == _rotatingPip)
                { rb.RenderTransform = new System.Windows.Media.RotateTransform(_rotatingPip.Rotation, rb.Width / 2, rb.Height / 2); break; }
            return;
        }
        if (_rotatingImage is not null || _rotatingButton is not null || _rotatingSubtitle is not null || _rotatingPip is not null) return;

        // === IMAGE OVERLAY : resize ===
        if (_resizingImage is not null && e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var (rox3, roy3, rrw3, rrh3) = GetContentRect();
            if (rrw3 <= 0) return;
            var rp3 = e.GetPosition(OverlayCanvas);
            foreach (UIElement el in OverlayCanvas.Children)
            {
                if (el is Border rb && rb.Tag is ImageOverlayConfig ric && ric == _resizingImage)
                {
                    var rl = Canvas.GetLeft(rb); var rt = Canvas.GetTop(rb);
                    var nw = Math.Max(20, rp3.X - rl);
                    var nh = Math.Max(14, rp3.Y - rt);
                    rb.Width = nw; rb.Height = nh;
                    _resizingImage.Width = nw / rrw3;
                    _resizingImage.Height = nh / rrh3;
                    UpdateSelectionVisuals(rl, rt, nw, nh);
                    break;
                }
            }
            return;
        }

        // === PIP : resize ===
        if (_resizingPip is not null && e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var (roxP2, royP2, rrwP2, rrhP2) = GetContentRect();
            if (rrwP2 <= 0) return;
            var rpP = e.GetPosition(OverlayCanvas);
            foreach (UIElement el in OverlayCanvas.Children)
            {
                if (el is Border rb && rb.Tag is PipConfig rpc && rpc == _resizingPip)
                {
                    var rl = Canvas.GetLeft(rb); var rt = Canvas.GetTop(rb);
                    var nw = Math.Max(30, rpP.X - rl);
                    var nh = Math.Max(20, rpP.Y - rt);
                    rb.Width = nw; rb.Height = nh;
                    _resizingPip.Width = nw / rrwP2;
                    _resizingPip.Height = nh / rrhP2;
                    UpdateSelectionVisuals(rl, rt, nw, nh);
                    break;
                }
            }
            return;
        }

        // === IMAGE OVERLAY : drag ===
        if (_draggingImage is not null && e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var (ox4, oy4, rw4, rh4) = GetContentRect();
            if (rw4 <= 0) return;
            var dp = e.GetPosition(OverlayCanvas);
            var maxX = ox4 + rw4 - _draggingImage.Width * rw4;
            var maxY = oy4 + rh4 - _draggingImage.Height * rh4;
            var nx = Math.Clamp(dp.X - _dragOffset.X, ox4, maxX);
            var ny = Math.Clamp(dp.Y - _dragOffset.Y, oy4, maxY);
            _draggingImage.X = (nx - ox4) / rw4;
            _draggingImage.Y = (ny - oy4) / rh4;
            foreach (UIElement el in OverlayCanvas.Children)
                if (el is Border b && b.Tag is ImageOverlayConfig ic && ic == _draggingImage)
                { Canvas.SetLeft(b, nx); Canvas.SetTop(b, ny); break; }
            return;
        }

        // === PIP : drag ===
        if (_draggingPip is not null && e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var (oxP, oyP, rwP, rhP) = GetContentRect();
            if (rwP <= 0) return;
            var dpP = e.GetPosition(OverlayCanvas);
            var nxP = Math.Clamp(dpP.X - _dragOffset.X, oxP, oxP + rwP - _draggingPip.Width * rwP);
            var nyP = Math.Clamp(dpP.Y - _dragOffset.Y, oyP, oyP + rhP - _draggingPip.Height * rhP);
            _draggingPip.X = (nxP - oxP) / rwP;
            _draggingPip.Y = (nyP - oyP) / rhP;
            foreach (UIElement el in OverlayCanvas.Children)
                if (el is Border b && b.Tag is PipConfig pc && pc == _draggingPip)
                { Canvas.SetLeft(b, nxP); Canvas.SetTop(b, nyP); break; }
            return;
        }

        // === SUBTITLE : drag ===
        if (_draggingSubtitle is not null && e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var (ox5, oy5, rw5, rh5) = GetContentRect();
            if (rw5 <= 0) return;
            var dp5 = e.GetPosition(OverlayCanvas);
            var nx5 = Math.Clamp(dp5.X - _dragOffset.X, ox5, ox5 + rw5 - _draggingSubtitle.Width * rw5);
            var ny5 = Math.Clamp(dp5.Y - _dragOffset.Y, oy5, oy5 + rh5 * 0.95);
            _draggingSubtitle.X = (nx5 - ox5) / rw5;
            _draggingSubtitle.Y = (ny5 - oy5) / rh5;
            foreach (UIElement el in OverlayCanvas.Children)
                if (el is Border b && b.Tag is SubtitleConfig sc && sc == _draggingSubtitle)
                { Canvas.SetLeft(b, nx5); Canvas.SetTop(b, ny5); break; }
            return;
        }

        // Resize en cours (clic droit)
        if (_resizingButton is not null &&
            e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var (rox, roy, rrw, rrh) = GetContentRect();
            if (rrw <= 0 || rrh <= 0) return;
            var rp = e.GetPosition(OverlayCanvas);
            foreach (UIElement el in OverlayCanvas.Children)
            {
                if (el is Border rb && rb.Tag is ButtonConfig rbc && rbc == _resizingButton)
                {
                    var rl = Canvas.GetLeft(rb);
                    var rt = Canvas.GetTop(rb);
                    var nw = Math.Max(20, rp.X - rl);
                    var nh = Math.Max(14, rp.Y - rt);
                    rb.Width = nw; rb.Height = nh;
                    _resizingButton.Width = nw / rrw;
                    _resizingButton.Height = nh / rrh;
                    // Mettre a jour la poignee et le cadre sans tout reconstruire
                    UpdateSelectionVisuals(rl, rt, nw, nh);
                    break;
                }
            }
            return;
        }
        if (_resizingButton is not null) return;

        // Drag par clic droit enfonce
        if (_draggingButton is null &&
            e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(OverlayCanvas);
            foreach (UIElement el in OverlayCanvas.Children)
            {
                if (el is Border b && b.Tag is ButtonConfig btn)
                {
                    var left = Canvas.GetLeft(b);
                    var top = Canvas.GetTop(b);
                    if (pos.X >= left && pos.X <= left + b.ActualWidth &&
                        pos.Y >= top && pos.Y <= top + b.ActualHeight)
                    {
                        _draggingButton = btn;
                        _dragOffset = new System.Windows.Point(pos.X - left, pos.Y - top);
                        b.BorderThickness = new Thickness(2);
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(92, 79, 191));
                        b.Cursor = System.Windows.Input.Cursors.SizeAll;
                        break;
                    }
                }
            }
        }

        if (_draggingButton is null) return;
        var (ox, oy, rw, rh) = GetContentRect();
        if (rw <= 0 || rh <= 0) return;

        var dragPos = e.GetPosition(OverlayCanvas);

        // Contraindre dans la zone de rendu du media
        var maxPx = ox + rw - _draggingButton.Width * rw;
        var maxPy = oy + rh - _draggingButton.Height * rh;
        var newPx = Math.Clamp(dragPos.X - _dragOffset.X, ox, maxPx);
        var newPy = Math.Clamp(dragPos.Y - _dragOffset.Y, oy, maxPy);

        // Convertir en fraction de la zone de rendu
        _draggingButton.X = (newPx - ox) / rw;
        _draggingButton.Y = (newPy - oy) / rh;

        foreach (UIElement el in OverlayCanvas.Children)
            if (el is Border b && b.Tag is ButtonConfig bc && bc == _draggingButton)
            { Canvas.SetLeft(b, newPx); Canvas.SetTop(b, newPy); break; }
    }

    private void OverlayCanvas_MouseUp(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_rotatingImage is not null)
        { _rotatingImage = null; SeqPanel.RefreshItems(); return; }
        if (_rotatingButton is not null)
        { _rotatingButton = null; SeqPanel.RefreshItems(); return; }
        if (_rotatingSubtitle is not null)
        { _rotatingSubtitle = null; SeqPanel.RefreshItems(); return; }
        if (_rotatingPip is not null)
        { _rotatingPip = null; SeqPanel.RefreshItems(); return; }
        if (_draggingPip is not null)
        {
            foreach (UIElement el in OverlayCanvas.Children)
                if (el is Border b && b.Tag is PipConfig pc && pc == _draggingPip)
                { b.Cursor = System.Windows.Input.Cursors.Hand; break; }
            _draggingPip = null;
            var pipCmd = FinalizeMoveCommand(); if (pipCmd is not null) PushUndo(pipCmd);
            SeqPanel.RefreshItems(); HighlightEditingButton(); return;
        }
        if (_resizingPip is not null)
        { _resizingPip = null; SeqPanel.RefreshItems(); HighlightEditingButton(); return; }
        if (_resizingImage is not null)
        { _resizingImage = null; SeqPanel.RefreshItems(); HighlightEditingButton(); return; }
        if (_draggingImage is not null)
        {
            foreach (UIElement el in OverlayCanvas.Children)
                if (el is Border b && b.Tag is ImageOverlayConfig ic && ic == _draggingImage)
                { b.Cursor = System.Windows.Input.Cursors.Hand; break; }
            _draggingImage = null;
            var imgCmd = FinalizeMoveCommand(); if (imgCmd is not null) PushUndo(imgCmd);
            SeqPanel.RefreshItems(); HighlightEditingButton(); return;
        }
        if (_draggingSubtitle is not null)
        {
            foreach (UIElement el in OverlayCanvas.Children)
                if (el is Border b && b.Tag is SubtitleConfig sc && sc == _draggingSubtitle)
                { b.Cursor = System.Windows.Input.Cursors.Hand; break; }
            _draggingSubtitle = null;
            var subCmd = FinalizeMoveCommand(); if (subCmd is not null) PushUndo(subCmd);
            SeqPanel.RefreshItems(); HighlightEditingButton(); return;
        }

        if (_resizingButton is not null)
        {
            _resizingButton = null;
            SeqPanel.RefreshItems();
            HighlightEditingButton();
            return;
        }

        if (_draggingButton is not null)
        {
            // Restaurer le curseur Hand sur le bouton draggue
            foreach (UIElement el in OverlayCanvas.Children)
                if (el is Border b && b.Tag is ButtonConfig bc && bc == _draggingButton)
                { b.Cursor = System.Windows.Input.Cursors.Hand; break; }
            _draggingButton = null;
            var moveCmd = FinalizeMoveCommand();
            if (moveCmd is not null) PushUndo(moveCmd);
            SeqPanel.RefreshItems();
            HighlightEditingButton();
            return;
        }

        // Mode UntilClick : clic sur le canvas (pas sur un bouton) → item suivant
        var seq = _zone?.Sequence;
        if (seq is not null && seq.CurrentIndex >= 0 && seq.CurrentIndex < seq.Items.Count)
        {
            var item = seq.Items[seq.CurrentIndex];
            if (item.IsImageSlide && item.SlideDuration == ImageSlideDuration.UntilClick)
            {
                _slideStopwatch?.Stop();
                _slideStopwatch = null;
                _sequencePlayer?.OnSlideCompleted();
            }
        }
    }

    public void ExecuteButtonActions(ButtonConfig btn)
    {
        if (btn.IsToggle) { btn.IsToggleActive = !btn.IsToggleActive; }
        var actions = btn.IsToggle && btn.IsToggleActive ? btn.ActionsOn : btn.Actions;
        foreach (var a in actions) ExecuteButtonAction(a);
    }

    private void ExecuteButtonAction(ButtonAction action)
    {
        // Actions cross-zone : dans l'editeur une seule zone est affichee a la fois,
        // on ignore donc les actions qui ciblent une autre zone (elles prennent
        // effet en Apercu et dans le Player). Sans ce garde, un bouton
        // "Stop zone 2" clique dans l'editeur stopperait la zone courante.
        if (!string.IsNullOrEmpty(action.ZoneId) && _zone is not null && action.ZoneId != _zone.Id)
            return;
        switch (action.Type)
        {
            case ButtonActionType.Play:
            case ButtonActionType.PlaySequence:
                _timer?.Stop(); EnsureSequencePlayer(); _sequencePlayer!.Start(); break;
            case ButtonActionType.Pause:
            case ButtonActionType.PauseSequence:
                ActivePause(); break;
            case ButtonActionType.Stop:
            case ButtonActionType.StopSequence:
                _jumpEndIndex = -1; _sequencePlayer?.Stop(); ActiveStop(); _timer?.Stop(); break;
            case ButtonActionType.ToggleSequence:
                if (_isPlaying) ActivePause();
                else { _timer?.Stop(); EnsureSequencePlayer(); _sequencePlayer!.Start(); }
                break;
            case ButtonActionType.JumpToItem:
                if (action.TargetItemIndex.HasValue)
                { _timer?.Stop(); _mediaModule!.IsLooping = false; EnsureSequencePlayer();
                  _sequencePlayer!.JumpTo(action.TargetItemIndex.Value); } break;
            case ButtonActionType.PlayMedia:
                if (!string.IsNullOrEmpty(action.MediaPath))
                { _timer?.Stop(); ActivePlay(action.MediaPath); } break;
            case ButtonActionType.SwitchMedia:
                if (!string.IsNullOrEmpty(action.MediaPath))
                {
                    // Capturer la position courante avant de switcher
                    var switchPos = 0f;
                    try { switchPos = _mediaModule?.Player?.Position ?? 0f; } catch { }
                    _timer?.Stop();
                    ActivePlay(action.MediaPath);
                    // Seek a la meme position apres un court delai
                    if (switchPos > 0)
                    {
                        var pos = switchPos;
                        Task.Run(async () =>
                        {
                            // Attendre que la lecture demarre
                            for (int i = 0; i < 30; i++)
                            {
                                await Task.Delay(100);
                                try
                                {
                                    if (_mediaModule?.Player?.State == LibVLCSharp.Shared.VLCState.Playing)
                                    {
                                        _mediaModule.Player.Position = pos;
                                        break;
                                    }
                                }
                                catch { break; }
                            }
                        });
                    }
                    StartTimerSafe();
                    SetPlaying(true);
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
                    if (el is Border tpb && tpb.Tag is PipConfig tpc)
                    {
                        if (_pipToggledVisible.Contains(tpc.Id))
                        { _pipToggledVisible.Remove(tpc.Id); tpb.Visibility = Visibility.Collapsed; }
                        else
                        { _pipToggledVisible.Add(tpc.Id); tpb.Visibility = Visibility.Visible; }
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

    private void OnStopAllFromEditor()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!VideoView.IsLoaded || VideoView.MediaPlayer is null) return;
            _jumpEndIndex = -1;
            _sequencePlayer?.Stop();
            _timer?.Stop();
            _slideStopwatch?.Stop(); _slideStopwatch = null;
            var seq = _zone?.Sequence;
            if (seq is not null && seq.Items.Count > 0)
            {
                seq.CurrentIndex = 0;
                var item = seq.Items[0];
                var path = item.EffectivePath;
                if (!string.IsNullOrEmpty(path))
                {
                    ActivePlay(path);
                    SeqPanel.SetActiveIndex(0);
                    return;
                }
            }
            var mm = _mediaModule;
            if (mm is not null) Task.Run(() => { try { mm.Stop(); } catch { } });
            SetPlaying(false);
        });
    }

    private void OnJumpToItemAllFromEditor(int startIndex, int endIndex)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Ne pas jouer si le VideoView n'est pas dans le visual tree
            if (!VideoView.IsLoaded || VideoView.MediaPlayer is null) return;
            var seq = _zone?.Sequence;
            if (seq is null || seq.Items.Count == 0) return;
            var idx = Math.Clamp(startIndex, 0, seq.Items.Count - 1);
            _jumpEndIndex = Math.Clamp(endIndex, idx, seq.Items.Count - 1);
            _timer?.Stop();
            seq.CurrentIndex = idx;
            var item = seq.Items[idx];
            var path = item.EffectivePath;
            if (!string.IsNullOrEmpty(path))
            {
                _lastFilePath = path;
                ActivePlay(path);
                StartTimerSafe();
                SetPlaying(true);
                SeqPanel.SetActiveIndex(idx);
            }
        });
    }

    public void Cleanup()
    {
        StopAllScreensRequested -= OnStopAllFromEditor;
        JumpToItemAllScreensRequested -= OnJumpToItemAllFromEditor;
        _timer?.Stop(); _timer = null;
        _sequencePlayer?.Dispose(); _sequencePlayer = null;
        _slideStopwatch?.Stop(); _slideStopwatch = null;
        if (_mediaModule is not null) _mediaModule.OnMediaEnded -= OnMediaEnded;
        _mediaModule?.Dispose(); _mediaModule = null;
        DisposePipViews();
        OverlayCanvas.Children.Clear();
    }

    private readonly Dictionary<string, Border> _pipViews = new();

    /// <summary>Libere TOUS les decodeurs FFmpeg des PiP et vide le cache.</summary>
    private void DisposePipViews()
    {
        foreach (var b in _pipViews.Values)
            if (b.Child is AlphaVideoView av)
                try { av.Dispose(); } catch { }
        _pipViews.Clear();
    }

    /// <summary>Libere les PiP dont l'id n'est plus present dans l'item courant.</summary>
    private void PrunePipViews(IEnumerable<string> currentIds)
    {
        var keep = new HashSet<string>(currentIds);
        foreach (var key in _pipViews.Keys.ToList())
        {
            if (keep.Contains(key)) continue;
            if (_pipViews[key].Child is AlphaVideoView av)
                try { av.Dispose(); } catch { }
            OverlayCanvas.Children.Remove(_pipViews[key]);
            _pipViews.Remove(key);
        }
    }
}
