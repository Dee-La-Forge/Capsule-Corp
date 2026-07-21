using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UMP.Core.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using ComboBox = System.Windows.Controls.ComboBox;
using FontFamily = System.Windows.Media.FontFamily;

namespace UMP.App.Controls;

public partial class SubtitlePropertiesPanel : System.Windows.Controls.UserControl
{
    private const string SubFilter = "Sous-titres (*.srt, *.vtt, *.ass)|*.srt;*.SRT;*.vtt;*.VTT;*.ass;*.ASS;*.ssa;*.SSA;*.sub;*.SUB|Tous|*.*";

    private static readonly string[] _fonts = new[]
    {
        "Segoe UI", "Arial", "Arial Black", "Bahnschrift", "Calibri",
        "Cambria", "Candara", "Century Gothic", "Comic Sans MS", "Consolas",
        "Constantia", "Corbel", "Courier New", "Franklin Gothic Medium",
        "Garamond", "Georgia", "Impact", "Lucida Console", "Lucida Sans Unicode",
        "Palatino Linotype", "Segoe Print", "Segoe Script",
        "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana"
    };
    private static readonly string[] _weights = new[]
    { "Thin", "Light", "Normal", "Medium", "SemiBold", "Bold", "ExtraBold", "Black" };

    private SubtitleConfig _sub = null!;
    private bool _loading = true;
    private System.Windows.Threading.DispatcherTimer? _debounce;

    public event Action? Applied;
    public event Action? CloseRequested;

    public SubtitlePropertiesPanel()
    {
        InitializeComponent();
        foreach (var f in _fonts)
            CmbFontFamily.Items.Add(new System.Windows.Controls.ComboBoxItem
            {
                Content = f, Tag = f,
                FontFamily = new System.Windows.Media.FontFamily(f),
                FontSize = 13
            });
        foreach (var w in _weights) CmbFontWeight.Items.Add(w);
        CmbFontFamily.SelectionChanged += (s, e) => DebouncedApply();
        CmbFontWeight.SelectionChanged += (s, e) => DebouncedApply();
        TxtFontSize.TextChanged += (s, e) => DebouncedApply();
        TxtOpacity.TextChanged += (s, e) => DebouncedApply();
        TxtPadding.TextChanged += (s, e) => DebouncedApply();
        TxtX.TextChanged += (s, e) => DebouncedApply();
        TxtY.TextChanged += (s, e) => DebouncedApply();
        TxtW.TextChanged += (s, e) => DebouncedApply();
        TxtBorderWidth.TextChanged += (s, e) => DebouncedApply();
        TxtCorner.TextChanged += (s, e) => DebouncedApply();
        TxtShadowBlur.TextChanged += (s, e) => DebouncedApply();
        TxtShadowX.TextChanged += (s, e) => DebouncedApply();
        TxtShadowY.TextChanged += (s, e) => DebouncedApply();
        TxtOutlineWidth.TextChanged += (s, e) => DebouncedApply();
    }

    public void LoadSubtitle(SubtitleConfig sub)
    {
        _loading = true;
        _sub = sub;
        TxtFilePath.Text = string.IsNullOrEmpty(sub.FilePath) ? "(aucun fichier)" : System.IO.Path.GetFileName(sub.FilePath);
        TxtBg.Text = sub.BackgroundColor;
        TxtFg.Text = sub.TextColor;
        TxtOpacity.Text = (sub.Opacity * 100).ToString("F0");
        TxtPadding.Text = sub.Padding.ToString("F0");
        UpdateCustomFontUI(sub);
        foreach (System.Windows.Controls.ComboBoxItem ci in CmbFontFamily.Items)
            if (ci.Tag is string t && t == sub.FontFamily) { CmbFontFamily.SelectedItem = ci; break; }
        if (CmbFontFamily.SelectedIndex < 0) CmbFontFamily.SelectedIndex = 0;
        CmbFontWeight.SelectedItem = sub.FontWeight;
        if (CmbFontWeight.SelectedIndex < 0) CmbFontWeight.SelectedIndex = 5;
        TxtFontSize.Text = sub.FontSize.ToString("F0");
        TxtX.Text = (sub.X * 100).ToString("F0");
        TxtY.Text = (sub.Y * 100).ToString("F0");
        TxtW.Text = (sub.Width * 100).ToString("F0");
        RbAlignLeft.IsChecked = sub.TextAlign == "Left";
        RbAlignCenter.IsChecked = sub.TextAlign == "Center";
        RbAlignRight.IsChecked = sub.TextAlign == "Right";
        TxtBorderWidth.Text = sub.BorderWidth.ToString("F0");
        TxtBorderColor.Text = sub.BorderColor;
        TxtCorner.Text = (sub.CornerRadius * 100).ToString("F0");
        UpdateColorPreview(BgPreview, sub.BackgroundColor);
        UpdateColorPreview(FgPreview, sub.TextColor);
        UpdateColorPreview(BorderPreview, sub.BorderColor);
        TxtShadowBlur.Text = sub.ShadowBlur.ToString("F0");
        TxtShadowX.Text = sub.ShadowOffsetX.ToString("F0");
        TxtShadowY.Text = sub.ShadowOffsetY.ToString("F0");
        TxtShadowColor.Text = sub.ShadowColor;
        UpdateColorPreview(ShadowPreview, sub.ShadowColor);
        TxtOutlineWidth.Text = sub.OutlineWidth.ToString("F0");
        TxtOutlineColor.Text = sub.OutlineColor;
        UpdateColorPreview(OutlinePreview, sub.OutlineColor);
        UpdateEntryCount();
        _loading = false;
    }

    private void UpdateEntryCount()
    {
        if (string.IsNullOrEmpty(_sub.FilePath))
        { TxtEntryCount.Text = ""; return; }
        try
        {
            var entries = ParseSrtFile(_sub.FilePath);
            TxtEntryCount.Text = $"{entries.Count} sous-titre(s) charges";
        }
        catch { TxtEntryCount.Text = "Erreur de lecture"; }
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
        if (_loading || _sub is null) return;
        _sub.BackgroundColor = TxtBg.Text;
        _sub.TextColor = TxtFg.Text;
        if (double.TryParse(TxtOpacity.Text, out var op)) _sub.Opacity = Math.Clamp(op / 100.0, 0, 1);
        if (double.TryParse(TxtPadding.Text, out var pd)) _sub.Padding = pd;
        if (CmbFontFamily.SelectedItem is System.Windows.Controls.ComboBoxItem cfi && cfi.Tag is string ff) _sub.FontFamily = ff;
        if (CmbFontWeight.SelectedItem is string fw) _sub.FontWeight = fw;
        if (double.TryParse(TxtFontSize.Text, out var fz)) _sub.FontSize = fz;
        if (double.TryParse(TxtX.Text, out var x)) _sub.X = x / 100.0;
        if (double.TryParse(TxtY.Text, out var y)) _sub.Y = y / 100.0;
        if (double.TryParse(TxtW.Text, out var w)) _sub.Width = w / 100.0;
        _sub.TextAlign = RbAlignLeft.IsChecked == true ? "Left"
            : RbAlignRight.IsChecked == true ? "Right" : "Center";
        if (double.TryParse(TxtBorderWidth.Text, out var bw)) _sub.BorderWidth = Math.Max(0, bw);
        _sub.BorderColor = TxtBorderColor.Text;
        if (double.TryParse(TxtCorner.Text, out var cr)) _sub.CornerRadius = cr / 100.0;
        if (double.TryParse(TxtShadowBlur.Text, out var sb)) _sub.ShadowBlur = Math.Max(0, sb);
        if (double.TryParse(TxtShadowX.Text, out var sx)) _sub.ShadowOffsetX = sx;
        if (double.TryParse(TxtShadowY.Text, out var sy)) _sub.ShadowOffsetY = sy;
        _sub.ShadowColor = TxtShadowColor.Text;
        if (double.TryParse(TxtOutlineWidth.Text, out var ow)) _sub.OutlineWidth = Math.Max(0, ow);
        _sub.OutlineColor = TxtOutlineColor.Text;
        Applied?.Invoke();
    }

    private void BtnApply_Click(object s, RoutedEventArgs e) => ApplyChanges();
    private void Align_Changed(object s, RoutedEventArgs e) => DebouncedApply();

    private void BtnBrowseSrt_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = SubFilter };
        if (d.ShowDialog() != true) return;
        _sub.FilePath = d.FileName;
        TxtFilePath.Text = System.IO.Path.GetFileName(d.FileName);
        UpdateEntryCount();
        ApplyChanges();
    }

    private void BtnImportFont_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog
        { Filter = "Polices (*.ttf, *.otf)|*.ttf;*.TTF;*.otf;*.OTF|Tous|*.*" };
        if (d.ShowDialog() != true) return;
        _sub.CustomFontPath = d.FileName;
        // Lire le nom de la famille depuis le fichier
        try
        {
            var ff = CreateFontFamily(d.FileName);
            var familyName = ff.FamilyNames.Values.FirstOrDefault() ?? ff.Source;
            _sub.FontFamily = familyName;
            // Ajouter dans la combo si pas deja present
            var exists = false;
            foreach (System.Windows.Controls.ComboBoxItem ci in CmbFontFamily.Items)
                if (ci.Tag is string t && t == familyName) { exists = true; break; }
            if (!exists)
                CmbFontFamily.Items.Insert(0, new System.Windows.Controls.ComboBoxItem
                { Content = familyName, Tag = familyName, FontFamily = ff, FontSize = 13 });
            _loading = true;
            foreach (System.Windows.Controls.ComboBoxItem ci in CmbFontFamily.Items)
                if (ci.Tag is string t2 && t2 == familyName) { CmbFontFamily.SelectedItem = ci; break; }
            _loading = false;
        }
        catch { /* garder le fontfamily actuel */ }
        UpdateCustomFontUI(_sub);
        ApplyChanges();
    }

    private void BtnRemoveCustomFont_Click(object s, RoutedEventArgs e)
    {
        _sub.CustomFontPath = "";
        _sub.FontFamily = "Segoe UI";
        _loading = true;
        foreach (System.Windows.Controls.ComboBoxItem ci in CmbFontFamily.Items)
            if (ci.Tag is string t && t == "Segoe UI") { CmbFontFamily.SelectedItem = ci; break; }
        _loading = false;
        UpdateCustomFontUI(_sub);
        ApplyChanges();
    }

    private void UpdateCustomFontUI(SubtitleConfig sub)
    {
        var hasCustom = !string.IsNullOrEmpty(sub.CustomFontPath);
        BtnRemoveCustomFont.Visibility = hasCustom ? Visibility.Visible : Visibility.Collapsed;
        TxtCustomFontInfo.Text = hasCustom
            ? $"Police: {System.IO.Path.GetFileName(sub.CustomFontPath)}"
            : "";
        CmbFontFamily.IsEnabled = !hasCustom;
    }

    /// <summary>Cree un FontFamily a partir d'un fichier .ttf/.otf</summary>
    public static FontFamily CreateFontFamily(string fontFilePath)
    {
        var dir = System.IO.Path.GetDirectoryName(fontFilePath)!.Replace('\\', '/');
        var glue = new GlyphTypeface(new Uri(fontFilePath));
        var familyName = glue.FamilyNames.Values.FirstOrDefault() ?? "Unknown";
        return new FontFamily(new Uri("file:///" + dir + "/"), "./#" + familyName);
    }

    /// <summary>Resout le FontFamily pour un SubtitleConfig (custom ou systeme)</summary>
    public static FontFamily ResolveFontFamily(SubtitleConfig sub)
    {
        if (!string.IsNullOrEmpty(sub.CustomFontPath) && System.IO.File.Exists(sub.CustomFontPath))
            return CreateFontFamily(sub.CustomFontPath);
        return new FontFamily(sub.FontFamily);
    }

    /// <summary>Parse un nom de FontWeight en FontWeight WPF</summary>
    public static System.Windows.FontWeight ParseFontWeight(string weight)
    {
        return weight switch
        {
            "Thin" => FontWeights.Thin,
            "Light" => FontWeights.Light,
            "Normal" => FontWeights.Normal,
            "Medium" => FontWeights.Medium,
            "SemiBold" => FontWeights.SemiBold,
            "Bold" => FontWeights.Bold,
            "ExtraBold" => FontWeights.ExtraBold,
            "Black" => FontWeights.Black,
            _ => FontWeights.Bold
        };
    }

    /// <summary>Parse un nom d'alignement en TextAlignment WPF</summary>
    public static TextAlignment ParseTextAlignment(string align)
    {
        return align switch
        {
            "Left" => TextAlignment.Left,
            "Right" => TextAlignment.Right,
            _ => TextAlignment.Center
        };
    }

    private void BtnCloseProps_Click(object s, RoutedEventArgs e)
        => CloseRequested?.Invoke();

    // Color pickers
    private void UpdateColorPreview(Border preview, string hex)
    {
        if (preview is null) return;
        try { preview.Background = new System.Windows.Media.BrushConverter()
            .ConvertFromString(hex) as Brush ?? Brushes.Gray; }
        catch { preview.Background = Brushes.Gray; }
    }

    private void TxtBg_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(BgPreview, TxtBg.Text); DebouncedApply(); }
    private void TxtFg_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(FgPreview, TxtFg.Text); DebouncedApply(); }

    private System.Windows.Controls.TextBox? _colorTarget;
    private void BgColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtBg);
    private void FgColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtFg);
    private void TxtBorderColor_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(BorderPreview, TxtBorderColor.Text); DebouncedApply(); }
    private void BorderColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtBorderColor);
    private void TxtShadowColor_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(ShadowPreview, TxtShadowColor.Text); DebouncedApply(); }
    private void ShadowColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtShadowColor);
    private void TxtOutlineColor_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(OutlinePreview, TxtOutlineColor.Text); DebouncedApply(); }
    private void OutlineColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtOutlineColor);

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

    // SRT parser — delegue au parseur unique de UMP.Core
    public static List<SubtitleEntry> ParseSrtFile(string path)
        => UMP.Core.Services.SrtParser.Parse(path);
}
