using System.Windows;
using System.Windows.Controls;
using UMP.Core.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using ComboBox = System.Windows.Controls.ComboBox;

namespace UMP.App.Controls;

public partial class PipPropertiesPanel : System.Windows.Controls.UserControl
{
    private const string VideoFilter = "Video|*.mp4;*.mkv;*.avi;*.mov;*.wmv|Tous|*.*";

    private PipConfig _pip = null!;
    private bool _loading = true;
    private System.Windows.Threading.DispatcherTimer? _debounce;

    public event Action? Applied;
    public event Action? CloseRequested;

    public PipPropertiesPanel()
    {
        InitializeComponent();
        TxtOpacity.TextChanged += (s, e) => DebouncedApply();
        TxtVolume.TextChanged += (s, e) => DebouncedApply();
        TxtCorner.TextChanged += (s, e) => DebouncedApply();
        TxtBorderWidth.TextChanged += (s, e) => DebouncedApply();
        TxtX.TextChanged += (s, e) => DebouncedApply();
        TxtY.TextChanged += (s, e) => DebouncedApply();
        TxtW.TextChanged += (s, e) => DebouncedApply();
        TxtH.TextChanged += (s, e) => DebouncedApply();
    }

    public void LoadPip(PipConfig pip)
    {
        _loading = true;
        _pip = pip;
        TxtFilePath.Text = string.IsNullOrEmpty(pip.VideoPath) ? "(aucun fichier)" : System.IO.Path.GetFileName(pip.VideoPath);
        TxtOpacity.Text = (pip.Opacity * 100).ToString("F0");
        TxtVolume.Text = pip.Volume.ToString();
        TxtCorner.Text = (pip.CornerRadius * 100).ToString("F0");
        ChkLoop.IsChecked = pip.IsLooping;
        ChkStartHidden.IsChecked = pip.StartHidden;
        TxtBorderWidth.Text = pip.BorderWidth.ToString("F0");
        TxtBorderColor.Text = pip.BorderColor;
        UpdateColorPreview(BorderPreview, pip.BorderColor);
        TxtX.Text = (pip.X * 100).ToString("F0");
        TxtY.Text = (pip.Y * 100).ToString("F0");
        TxtW.Text = (pip.Width * 100).ToString("F0");
        TxtH.Text = (pip.Height * 100).ToString("F0");
        _loading = false;
    }

    private void DebouncedApply()
    {
        if (_loading) return;
        _debounce?.Stop();
        _debounce = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += (s, e) => { _debounce.Stop(); ApplyChanges(); };
        _debounce.Start();
    }

    private void ApplyChanges()
    {
        if (_loading || _pip is null) return;
        if (double.TryParse(TxtOpacity.Text, out var op)) _pip.Opacity = Math.Clamp(op / 100.0, 0, 1);
        if (int.TryParse(TxtVolume.Text, out var vol)) _pip.Volume = Math.Clamp(vol, 0, 100);
        if (double.TryParse(TxtCorner.Text, out var cr)) _pip.CornerRadius = Math.Clamp(cr / 100.0, 0, 1);
        _pip.IsLooping = ChkLoop.IsChecked == true;
        _pip.StartHidden = ChkStartHidden.IsChecked == true;
        if (double.TryParse(TxtBorderWidth.Text, out var bw)) _pip.BorderWidth = bw;
        _pip.BorderColor = TxtBorderColor.Text;
        if (double.TryParse(TxtX.Text, out var x)) _pip.X = x / 100.0;
        if (double.TryParse(TxtY.Text, out var y)) _pip.Y = y / 100.0;
        if (double.TryParse(TxtW.Text, out var w)) _pip.Width = w / 100.0;
        if (double.TryParse(TxtH.Text, out var h)) _pip.Height = h / 100.0;
        Applied?.Invoke();
    }

    private void BtnApply_Click(object s, RoutedEventArgs e) => ApplyChanges();
    private void ChkLoop_Changed(object s, RoutedEventArgs e) => DebouncedApply();
    private void ChkStartHidden_Changed(object s, RoutedEventArgs e) => DebouncedApply();

    private void BtnBrowseVideo_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = VideoFilter };
        if (d.ShowDialog() != true) return;
        _pip.VideoPath = d.FileName;
        TxtFilePath.Text = System.IO.Path.GetFileName(d.FileName);
        ApplyChanges();
    }

    private void BtnCloseProps_Click(object s, RoutedEventArgs e)
        => CloseRequested?.Invoke();

    // Color picker
    private void UpdateColorPreview(Border preview, string hex)
    {
        if (preview is null) return;
        try { preview.Background = new System.Windows.Media.BrushConverter()
            .ConvertFromString(hex) as Brush ?? Brushes.Gray; }
        catch { preview.Background = Brushes.Gray; }
    }

    private void TxtBorderColor_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(BorderPreview, TxtBorderColor.Text); DebouncedApply(); }

    private System.Windows.Controls.TextBox? _colorTarget;
    private void BorderColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtBorderColor);

    private void OpenColorPicker(System.Windows.Controls.TextBox target)
    {
        _colorTarget = target;
        ColorPicker.SetColor(target.Text);
        ColorPicker.ColorSelected -= OnColorSelected;
        ColorPicker.CloseRequested -= OnColorPickerClose;
        ColorPicker.ColorSelected += OnColorSelected;
        ColorPicker.CloseRequested += OnColorPickerClose;
        ColorPicker.Visibility = Visibility.Visible;
    }
    private void OnColorSelected(string hex)
    { if (_colorTarget is not null) _colorTarget.Text = hex; ColorPicker.Visibility = Visibility.Collapsed; _colorTarget = null; }
    private void OnColorPickerClose()
    { ColorPicker.Visibility = Visibility.Collapsed; _colorTarget = null; }
}
