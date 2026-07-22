using System.IO;
using System.Windows;
using System.Windows.Controls;
using UMP.Core.Models;
using UMP.Shared;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using ComboBox = System.Windows.Controls.ComboBox;

namespace UMP.App.Controls;

public partial class ButtonPropertiesPanel : System.Windows.Controls.UserControl
{
    private const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tous|*.*";
    private const string MediaFilter = "Media|*.mp4;*.mkv;*.avi|Tous|*.*";

    private ButtonConfig _btn = null!;
    private Zone _zone = null!;
    private Dictionary<string, ZoneRuntime> _zones = null!;
    private bool _showingOnActions;
    private bool _loading = true;
    private System.Windows.Threading.DispatcherTimer? _debounce;

    public event Action? Applied;
    public event Action? CloseRequested;

    private static readonly string[] _fonts = new[]
    {
        "Segoe UI", "Arial", "Arial Black", "Bahnschrift", "Calibri",
        "Cambria", "Candara", "Century Gothic", "Comic Sans MS", "Consolas",
        "Constantia", "Corbel", "Courier New", "Franklin Gothic Medium",
        "Garamond", "Georgia", "Impact", "Lucida Console", "Lucida Sans Unicode",
        "Malgun Gothic", "Microsoft Sans Serif", "Palatino Linotype",
        "Segoe Print", "Segoe Script", "Segoe UI Light", "Segoe UI Semibold",
        "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana", "Yu Gothic"
    };
    private static readonly string[] _weights = new[]
    {
        "Thin", "Light", "Normal", "Medium", "SemiBold", "Bold", "ExtraBold", "Black"
    };

    public ButtonPropertiesPanel()
    {
        InitializeComponent();
        foreach (var f in _fonts)
            CmbFontFamily.Items.Add(new System.Windows.Controls.ComboBoxItem
            { Content = f, Tag = f, FontFamily = new System.Windows.Media.FontFamily(f), FontSize = 13 });
        foreach (var w in _weights) { CmbFontWeight.Items.Add(w); CmbFontWeightHover.Items.Add(w); CmbFontWeightOn.Items.Add(w); }
        CmbFontFamily.SelectionChanged += (s, e) => DebouncedApply();
        CmbFontWeight.SelectionChanged += (s, e) => DebouncedApply();
        TxtW.TextChanged += (s, e) => DebouncedApply();
        TxtH.TextChanged += (s, e) => DebouncedApply();
        TxtFontSize.TextChanged += (s, e) => DebouncedApply();
        TxtLabelOn.TextChanged += (s, e) => DebouncedApply();
        TxtBgOn.TextChanged += (s, e) => DebouncedApply();
        TxtFgOn.TextChanged += (s, e) => DebouncedApply();
        TxtCorner.TextChanged += (s, e) => DebouncedApply();
        TxtPadding.TextChanged += (s, e) => DebouncedApply();
        TxtOpacity.TextChanged += (s, e) => DebouncedApply();
        TxtBorderWidth.TextChanged += (s, e) => DebouncedApply();
        TxtFontSizeHover.TextChanged += (s, e) => DebouncedApply();
        CmbFontWeightHover.SelectionChanged += (s, e) => DebouncedApply();
        TxtBorderWidthHover.TextChanged += (s, e) => DebouncedApply();
        TxtFontSizeOn.TextChanged += (s, e) => DebouncedApply();
        CmbFontWeightOn.SelectionChanged += (s, e) => DebouncedApply();
        TxtBorderWidthOn.TextChanged += (s, e) => DebouncedApply();
    }

    public void LoadButton(ButtonConfig btn, Zone zone, Dictionary<string, ZoneRuntime> zones)
    {
        _loading = true;
        _btn = btn;
        _zone = zone;
        _zones = zones;
        _showingOnActions = false;
        LoadValues();
        _loading = false;
    }

    private void LoadValues()
    {
        RbSimple.IsChecked = !_btn.IsToggle;
        RbToggle.IsChecked = _btn.IsToggle;
        UpdateToggle(_btn.IsToggle);
        TxtLabel.Text = _btn.Label;
        TxtBg.Text = _btn.BackgroundColor;
        TxtFg.Text = _btn.TextColor;
        TxtW.Text = (_btn.Width * 100).ToString("F1");
        TxtH.Text = (_btn.Height * 100).ToString("F1");
        TxtBgHover.Text = _btn.BackgroundColorHover;
        TxtFgHover.Text = _btn.TextColorHover;
        TxtBorderHover.Text = _btn.BorderColorHover;
        TxtFontSizeHover.Text = _btn.FontSizeHover.ToString("F0");
        CmbFontWeightHover.SelectedItem = _btn.FontWeightHover;
        if (CmbFontWeightHover.SelectedIndex < 0) CmbFontWeightHover.SelectedIndex = 4;
        TxtBorderWidthHover.Text = _btn.BorderWidthHover.ToString("F1");
        TxtBorderOn.Text = _btn.BorderColorOn;
        TxtFontSizeOn.Text = _btn.FontSizeOn.ToString("F0");
        CmbFontWeightOn.SelectedItem = _btn.FontWeightOn;
        if (CmbFontWeightOn.SelectedIndex < 0) CmbFontWeightOn.SelectedIndex = 5;
        TxtBorderWidthOn.Text = _btn.BorderWidthOn.ToString("F1");
        TxtLabelOn.Text = _btn.LabelOn;
        TxtBgOn.Text = _btn.BackgroundColorOn;
        TxtFgOn.Text = _btn.TextColorOn;
        // Typo
        UpdateCustomFontUI(_btn);
        foreach (System.Windows.Controls.ComboBoxItem ci in CmbFontFamily.Items)
            if (ci.Tag is string t && t == _btn.FontFamily) { CmbFontFamily.SelectedItem = ci; break; }
        if (CmbFontFamily.SelectedIndex < 0) CmbFontFamily.SelectedIndex = 0;
        CmbFontWeight.SelectedItem = _btn.FontWeight;
        if (CmbFontWeight.SelectedIndex < 0) CmbFontWeight.SelectedIndex = 4; // SemiBold
        TxtFontSize.Text = _btn.FontSize.ToString("F0");
        TxtCorner.Text = (_btn.CornerRadius * 100).ToString("F0");
        TxtPadding.Text = _btn.Padding.ToString("F0");
        TxtOpacity.Text = (_btn.Opacity * 100).ToString("F0");
        TxtBorderWidth.Text = _btn.BorderWidth.ToString("F0");
        TxtBorderColor.Text = _btn.BorderColor;
        var isInside = _btn.BorderPos == UMP.Core.Models.BorderPosition.Inside;
        RbBorderIn.IsChecked = isInside; RbBorderOut.IsChecked = !isInside;
        RbBorderInH.IsChecked = isInside; RbBorderOutH.IsChecked = !isInside;
        RbBorderInO.IsChecked = isInside; RbBorderOutO.IsChecked = !isInside;
        TxtImgPath.Text = _btn.ImagePath ?? "";
        TxtImgPathOn.Text = _btn.ImagePathOn ?? "";
        UpdateImagePreview(_btn.ImagePath);
        UpdatePreview();
        UpdateColorPreview(BgPreview, _btn.BackgroundColor);
        UpdateColorPreview(FgPreview, _btn.TextColor);
        UpdateColorPreview(BorderColorPreview, _btn.BorderColor);
        UpdateColorPreview(BgHoverPreview, _btn.BackgroundColorHover);
        UpdateColorPreview(FgHoverPreview, _btn.TextColorHover);
        UpdateColorPreview(BorderHoverPreview, _btn.BorderColorHover);
        UpdateColorPreview(BorderOnPreview, _btn.BorderColorOn);
        UpdateColorPreview(FgHoverPreview, _btn.TextColorHover);
        UpdateColorPreview(BgOnPreview, _btn.BackgroundColorOn);
        UpdateColorPreview(FgOnPreview, _btn.TextColorOn);
        RefreshActions();
    }

    private void UpdateToggle(bool isToggle)
    {
        if (SectionOn is null || TabsActions is null || LblStyle is null) return;
        SectionOn.Visibility = isToggle ? Visibility.Visible : Visibility.Collapsed;
        TabsActions.Visibility = isToggle ? Visibility.Visible : Visibility.Collapsed;
        LblStyle.Text = isToggle ? "APPARENCE OFF" : "APPARENCE";
    }

    private void RbType_Changed(object s, RoutedEventArgs e)
    {
        if (_loading) return;
        UpdateToggle(RbToggle.IsChecked == true);
    }

    private List<ButtonAction> CurrentActions =>
        _showingOnActions ? _btn.ActionsOn : _btn.Actions;

    private void RefreshActions()
    {
        ActionsPanel.Children.Clear();
        foreach (var a in CurrentActions)
            ActionsPanel.Children.Add(BuildActionRow(a));
    }

    private UIElement BuildActionRow(ButtonAction action)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cmb = new ComboBox();
        ApplyDarkComboStyle(cmb);
        var types = new (string, ButtonActionType)[]
        {
            ("\u25B6  Lecture", ButtonActionType.PlaySequence),
            ("\u23F8  Pause", ButtonActionType.Pause),
            ("\u23F9  Stop", ButtonActionType.Stop),
            ("\u21C4  Basculer Play/Pause", ButtonActionType.ToggleSequence),
            ("\u2192  Aller a l'item...", ButtonActionType.JumpToItem),
            ("\uD83D\uDCC2  Lire un fichier...", ButtonActionType.PlayMedia),
            ("\uD83C\uDF10  Changer la langue...", ButtonActionType.SwitchMedia),
            ("\u23F9  Stop tous les ecrans", ButtonActionType.StopAllScreens),
            ("\u23ED  Aller a l'item (tous les ecrans)...", ButtonActionType.JumpToItemAllScreens),
            ("\uD83D\uDCF9  Afficher/Masquer PiP", ButtonActionType.TogglePip),
            ("\uD83D\uDCF9  Afficher PiP", ButtonActionType.ShowPip),
            ("\uD83D\uDCF9  Masquer PiP", ButtonActionType.HidePip),
        };
        foreach (var (label, type) in types)
            cmb.Items.Add(new ComboBoxItem { Content = label, Tag = type });
        foreach (ComboBoxItem ci in cmb.Items)
            if (ci.Tag is ButtonActionType t && t == action.Type)
            { cmb.SelectedItem = ci; break; }
        if (cmb.SelectedIndex < 0) cmb.SelectedIndex = 0;

        var paramsPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        BuildParams(paramsPanel, action, action.Type);

        cmb.SelectionChanged += (s, e) =>
        {
            if (cmb.SelectedItem is ComboBoxItem ci && ci.Tag is ButtonActionType t)
            {
                action.Type = t;
                paramsPanel.Children.Clear();
                BuildParams(paramsPanel, action, t);
            }
        };
        Grid.SetColumn(cmb, 0);

        var delBtn = new System.Windows.Controls.Button
        {
            Content = "\u2715", Width = 28, Height = 28,
            Background = new SolidColorBrush(Color.FromRgb(60, 30, 30)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(6, 0, 0, 0)
        };
        delBtn.Click += (s, e) => { CurrentActions.Remove(action); RefreshActions(); };
        Grid.SetColumn(delBtn, 1);

        row.Children.Add(cmb); row.Children.Add(delBtn);
        stack.Children.Add(row); stack.Children.Add(paramsPanel);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(20, 20, 36)),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 4), Child = stack
        };
    }

    /// <summary>Zone visee par l'action : ZoneId si defini et connu, sinon la zone locale.</summary>
    private Zone ResolveTargetZone(ButtonAction action)
        => !string.IsNullOrEmpty(action.ZoneId)
           && _zones is not null
           && _zones.TryGetValue(action.ZoneId, out var rt)
            ? rt.Zone : _zone;

    /// <summary>
    /// Selecteur de zone cible (actions cross-zone). "Cette zone" = ZoneId null.
    /// onChanged est rappele apres changement pour reconstruire les parametres
    /// dependants de la zone (ex. liste d'items de JumpToItem).
    /// </summary>
    private UIElement BuildZoneSelector(ButtonAction action, Action onChanged)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        sp.Children.Add(new TextBlock
        {
            Text = "Zone cible", FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 95, 150)),
            Margin = new Thickness(0, 0, 0, 2)
        });
        var cmb = new ComboBox();
        ApplyDarkComboStyle(cmb);
        cmb.Items.Add(new ComboBoxItem { Content = "Cette zone", Tag = null });
        foreach (var rt in _zones.Values)
        {
            if (rt.Zone.Id == _zone.Id) continue;
            cmb.Items.Add(new ComboBoxItem
            { Content = $"{rt.Zone.Name} (ecran {rt.Zone.ScreenIndex + 1})", Tag = rt.Zone.Id });
        }
        cmb.SelectedIndex = 0;
        if (!string.IsNullOrEmpty(action.ZoneId))
        {
            var found = false;
            foreach (ComboBoxItem ci in cmb.Items)
                if (ci.Tag as string == action.ZoneId) { cmb.SelectedItem = ci; found = true; break; }
            // Zone supprimee depuis : revenir a "Cette zone"
            if (!found) action.ZoneId = null;
        }
        cmb.SelectionChanged += (s, e) =>
        {
            if (cmb.SelectedItem is ComboBoxItem ci)
            {
                var newId = ci.Tag as string;
                if (newId != action.ZoneId) { action.ZoneId = newId; onChanged(); }
            }
        };
        sp.Children.Add(cmb);
        return sp;
    }

    private void BuildParams(StackPanel panel, ButtonAction action, ButtonActionType type)
    {
        // Selecteur de zone cible pour les actions per-zone (si plusieurs zones)
        if (ButtonActionTypes.IsPerZone(type))
        {
            if (_zones is not null && _zones.Count > 1)
                panel.Children.Add(BuildZoneSelector(action,
                    () => { panel.Children.Clear(); BuildParams(panel, action, type); }));
        }
        else
            action.ZoneId = null; // actions globales : pas de cible

        switch (type)
        {
            case ButtonActionType.JumpToItem:
                var cmb = new ComboBox();
                ApplyDarkComboStyle(cmb);
                var jumpZone = ResolveTargetZone(action);
                if (jumpZone.Sequence is not null)
                {
                    for (int i = 0; i < jumpZone.Sequence.Items.Count; i++)
                    {
                        var itm = jumpZone.Sequence.Items[i];
                        var name = itm.IsImageSlide
                            ? $"Img {Path.GetFileName(itm.ImageSlidePath ?? "")}"
                            : $"Vid {Path.GetFileName(itm.MediaPath ?? "")}";
                        cmb.Items.Add(new ComboBoxItem
                            { Content = $"{i + 1}. {name}", Tag = i });
                    }
                }
                if (action.TargetItemIndex.HasValue &&
                    action.TargetItemIndex.Value < cmb.Items.Count)
                    cmb.SelectedIndex = action.TargetItemIndex.Value;
                cmb.SelectionChanged += (s, e) =>
                {
                    if (cmb.SelectedItem is ComboBoxItem ci && ci.Tag is int idx)
                        action.TargetItemIndex = idx;
                };
                panel.Children.Add(cmb);
                break;

            case ButtonActionType.JumpToItemAllScreens:
                var lblS = new TextBlock { Text = "Item debut", Foreground = Brushes.White, FontSize = 10, Margin = new Thickness(0, 4, 0, 2) };
                var cmbStart = new ComboBox();
                ApplyDarkComboStyle(cmbStart);
                var lblE = new TextBlock { Text = "Item fin (inclus)", Foreground = Brushes.White, FontSize = 10, Margin = new Thickness(0, 4, 0, 2) };
                var cmbEnd = new ComboBox();
                ApplyDarkComboStyle(cmbEnd);
                if (_zone.Sequence is not null)
                {
                    for (int i = 0; i < _zone.Sequence.Items.Count; i++)
                    {
                        var itm = _zone.Sequence.Items[i];
                        var name = itm.IsImageSlide
                            ? $"Img {Path.GetFileName(itm.ImageSlidePath ?? "")}"
                            : $"Vid {Path.GetFileName(itm.MediaPath ?? "")}";
                        var label = $"{i + 1}. {name}";
                        cmbStart.Items.Add(new ComboBoxItem { Content = label, Tag = i });
                        cmbEnd.Items.Add(new ComboBoxItem { Content = label, Tag = i });
                    }
                }
                if (action.TargetItemIndex.HasValue && action.TargetItemIndex.Value < cmbStart.Items.Count)
                    cmbStart.SelectedIndex = action.TargetItemIndex.Value;
                var endVal = action.EndItemIndex ?? action.TargetItemIndex ?? 0;
                if (endVal < cmbEnd.Items.Count)
                    cmbEnd.SelectedIndex = endVal;
                cmbStart.SelectionChanged += (s, e) =>
                { if (cmbStart.SelectedItem is ComboBoxItem ci && ci.Tag is int idx) action.TargetItemIndex = idx; };
                cmbEnd.SelectionChanged += (s, e) =>
                { if (cmbEnd.SelectedItem is ComboBoxItem ci && ci.Tag is int idx) action.EndItemIndex = idx; };
                panel.Children.Add(lblS);
                panel.Children.Add(cmbStart);
                panel.Children.Add(lblE);
                panel.Children.Add(cmbEnd);
                break;

            case ButtonActionType.PlayMedia:
                var dp = new DockPanel();
                var btn = new System.Windows.Controls.Button
                {
                    Content = "...", Width = 36, Height = 30,
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 64)),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0)
                };
                var txt = new System.Windows.Controls.TextBox
                {
                    Text = action.MediaPath ?? "",
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 64)),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0),
                    Padding = new Thickness(6, 4, 6, 4), Height = 30
                };
                txt.TextChanged += (s, e) => action.MediaPath = txt.Text;
                btn.Click += (s, e) =>
                {
                    var d = new Microsoft.Win32.OpenFileDialog
                        { Filter = MediaFilter };
                    if (d.ShowDialog() == true)
                    { action.MediaPath = d.FileName; txt.Text = d.FileName; }
                };
                DockPanel.SetDock(btn, Dock.Right);
                dp.Children.Add(btn); dp.Children.Add(txt);
                panel.Children.Add(dp);
                break;

            case ButtonActionType.SwitchMedia:
                var dp2 = new DockPanel();
                var btn2 = new System.Windows.Controls.Button
                {
                    Content = "...", Width = 36, Height = 30,
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 64)),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0)
                };
                var txt2 = new System.Windows.Controls.TextBox
                {
                    Text = action.MediaPath ?? "",
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 64)),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0),
                    Padding = new Thickness(6, 4, 6, 4), Height = 30
                };
                txt2.TextChanged += (s, e) => action.MediaPath = txt2.Text;
                btn2.Click += (s, e) =>
                {
                    var d = new Microsoft.Win32.OpenFileDialog
                        { Filter = MediaFilter };
                    if (d.ShowDialog() == true)
                    { action.MediaPath = d.FileName; txt2.Text = d.FileName; }
                };
                DockPanel.SetDock(btn2, Dock.Right);
                dp2.Children.Add(btn2); dp2.Children.Add(txt2);
                panel.Children.Add(new TextBlock
                {
                    Text = "Reprend a la meme position de lecture",
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 95, 150)),
                    FontSize = 9, Margin = new Thickness(0, 4, 0, 4)
                });
                panel.Children.Add(dp2);
                break;
        }
    }

    private void BtnAddAction_Click(object s, RoutedEventArgs e)
    { CurrentActions.Add(new ButtonAction()); RefreshActions(); }

    private void BtnTabOff_Click(object s, RoutedEventArgs e)
    {
        _showingOnActions = false;
        BtnTabOff.Background = new SolidColorBrush(Color.FromRgb(92, 79, 191));
        BtnTabOn.Background = new SolidColorBrush(Color.FromRgb(42, 42, 64));
        RefreshActions();
    }

    private void BtnTabOn_Click(object s, RoutedEventArgs e)
    {
        _showingOnActions = true;
        BtnTabOn.Background = new SolidColorBrush(Color.FromRgb(92, 79, 191));
        BtnTabOff.Background = new SolidColorBrush(Color.FromRgb(42, 42, 64));
        RefreshActions();
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
        if (_loading || _btn is null) return;
        _btn.IsToggle = RbToggle.IsChecked == true;
        _btn.Label = TxtLabel.Text;
        _btn.BackgroundColor = TxtBg.Text;
        _btn.TextColor = TxtFg.Text;
        _btn.BackgroundColorHover = TxtBgHover.Text;
        _btn.TextColorHover = TxtFgHover.Text;
        if (double.TryParse(TxtFontSizeHover.Text, out var fzh)) _btn.FontSizeHover = fzh;
        if (CmbFontWeightHover.SelectedItem is string fwh) _btn.FontWeightHover = fwh;
        _btn.BorderColorHover = TxtBorderHover.Text;
        if (double.TryParse(TxtBorderWidthHover.Text, out var bwh)) _btn.BorderWidthHover = Math.Max(0, bwh);
        if (double.TryParse(TxtFontSizeOn.Text, out var fzo)) _btn.FontSizeOn = fzo;
        if (CmbFontWeightOn.SelectedItem is string fwo) _btn.FontWeightOn = fwo;
        _btn.BorderColorOn = TxtBorderOn.Text;
        if (double.TryParse(TxtBorderWidthOn.Text, out var bwo)) _btn.BorderWidthOn = Math.Max(0, bwo);
        _btn.LabelOn = TxtLabelOn.Text;
        _btn.BackgroundColorOn = TxtBgOn.Text;
        _btn.TextColorOn = TxtFgOn.Text;
        if (double.TryParse(TxtW.Text, out var w)) _btn.Width = w / 100.0;
        if (double.TryParse(TxtH.Text, out var h)) _btn.Height = h / 100.0;
        if (CmbFontFamily.SelectedItem is System.Windows.Controls.ComboBoxItem cfi && cfi.Tag is string ff) _btn.FontFamily = ff;
        if (CmbFontWeight.SelectedItem is string fw) _btn.FontWeight = fw;
        if (double.TryParse(TxtFontSize.Text, out var fz)) _btn.FontSize = fz;
        if (double.TryParse(TxtCorner.Text, out var cr)) _btn.CornerRadius = cr / 100.0;
        if (double.TryParse(TxtPadding.Text, out var pd)) _btn.Padding = pd;
        if (double.TryParse(TxtOpacity.Text, out var op)) _btn.Opacity = Math.Clamp(op / 100.0, 0, 1);
        if (double.TryParse(TxtBorderWidth.Text, out var bw2)) _btn.BorderWidth = Math.Max(0, bw2);
        _btn.BorderColor = TxtBorderColor.Text;
        _btn.BorderPos = RbBorderOut.IsChecked == true
            ? UMP.Core.Models.BorderPosition.Outside
            : UMP.Core.Models.BorderPosition.Inside;
        _btn.ImagePath = string.IsNullOrWhiteSpace(TxtImgPath?.Text) ? null : TxtImgPath.Text;
        _btn.ImagePathOn = string.IsNullOrWhiteSpace(TxtImgPathOn?.Text) ? null : TxtImgPathOn.Text;
        Applied?.Invoke();
    }

    private void BtnApply_Click(object s, RoutedEventArgs e) => ApplyChanges();

    private void BtnImportFont_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog
        { Filter = "Polices (*.ttf, *.otf)|*.ttf;*.TTF;*.otf;*.OTF|Tous|*.*" };
        if (d.ShowDialog() != true) return;
        _btn.CustomFontPath = d.FileName;
        try
        {
            var ff = SubtitlePropertiesPanel.CreateFontFamily(d.FileName);
            var familyName = ff.FamilyNames.Values.FirstOrDefault() ?? ff.Source;
            _btn.FontFamily = familyName;
            var exists = false;
            foreach (System.Windows.Controls.ComboBoxItem ci in CmbFontFamily.Items)
                if (ci.Tag is string t && t == familyName) { exists = true; break; }
            if (!exists)
                CmbFontFamily.Items.Insert(0, new System.Windows.Controls.ComboBoxItem
                { Content = familyName, Tag = familyName, FontFamily = new System.Windows.Media.FontFamily(familyName), FontSize = 13 });
            _loading = true;
            foreach (System.Windows.Controls.ComboBoxItem ci in CmbFontFamily.Items)
                if (ci.Tag is string t2 && t2 == familyName) { CmbFontFamily.SelectedItem = ci; break; }
            _loading = false;
        }
        catch { }
        UpdateCustomFontUI(_btn);
        ApplyChanges();
    }

    private void BtnRemoveCustomFont_Click(object s, RoutedEventArgs e)
    {
        _btn.CustomFontPath = "";
        _btn.FontFamily = "Segoe UI";
        _loading = true;
        foreach (System.Windows.Controls.ComboBoxItem ci in CmbFontFamily.Items)
            if (ci.Tag is string t && t == "Segoe UI") { CmbFontFamily.SelectedItem = ci; break; }
        _loading = false;
        UpdateCustomFontUI(_btn);
        ApplyChanges();
    }

    private void UpdateCustomFontUI(ButtonConfig btn)
    {
        var hasCustom = !string.IsNullOrEmpty(btn.CustomFontPath);
        BtnRemoveCustomFont.Visibility = hasCustom ? Visibility.Visible : Visibility.Collapsed;
        TxtCustomFontInfo.Text = hasCustom
            ? $"Police: {Path.GetFileName(btn.CustomFontPath)}"
            : "";
        CmbFontFamily.IsEnabled = !hasCustom;
    }

    private void BtnCloseProps_Click(object s, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }

    // Color preview + pickers
    private void UpdatePreview()
    {
        Brush bg;
        try { bg = new System.Windows.Media.BrushConverter().ConvertFromString(
            TxtBg?.Text ?? "#2A2A40") as Brush ?? Brushes.DimGray; }
        catch { bg = Brushes.DimGray; }
        // Preview supprimee du header
    }

    private void UpdateColorPreview(Border preview, string hex)
    {
        if (preview is null) return;
        try { preview.Background = new System.Windows.Media.BrushConverter()
            .ConvertFromString(hex) as Brush ?? Brushes.Gray; }
        catch { preview.Background = Brushes.Gray; }
    }

    private void TxtLabel_Changed(object s, TextChangedEventArgs e) { UpdatePreview(); DebouncedApply(); }
    private void TxtBg_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(BgPreview, TxtBg.Text); UpdatePreview(); DebouncedApply(); }
    private void TxtFg_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(FgPreview, TxtFg.Text); DebouncedApply(); }

    private void BgColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtBg);
    private void FgColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtFg);

    private void BorderPos_Changed(object s, RoutedEventArgs e)
    {
        if (_loading) return;
        // Synchroniser tous les toggles Int./Ext.
        var isOut = s == RbBorderOut || s == RbBorderOutH || s == RbBorderOutO;
        _loading = true;
        RbBorderIn.IsChecked = !isOut; RbBorderOut.IsChecked = isOut;
        RbBorderInH.IsChecked = !isOut; RbBorderOutH.IsChecked = isOut;
        RbBorderInO.IsChecked = !isOut; RbBorderOutO.IsChecked = isOut;
        _loading = false;
        DebouncedApply();
    }

    // Bordure
    private void TxtBorderColor_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(BorderColorPreview, TxtBorderColor.Text); DebouncedApply(); }
    private void BorderColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtBorderColor);

    // Survol — color pickers + previews
    private void TxtBgHover_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(BgHoverPreview, TxtBgHover.Text); DebouncedApply(); }
    private void TxtFgHover_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(FgHoverPreview, TxtFgHover.Text); DebouncedApply(); }
    private void BgHoverColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtBgHover);
    private void FgHoverColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtFgHover);

    private void TxtBorderHover_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(BorderHoverPreview, TxtBorderHover.Text); DebouncedApply(); }
    private void BorderHoverColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtBorderHover);

    private void TxtBorderOn_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(BorderOnPreview, TxtBorderOn.Text); DebouncedApply(); }
    private void BorderOnColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtBorderOn);

    // Style ON — color pickers + previews
    private void TxtBgOn_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(BgOnPreview, TxtBgOn.Text); DebouncedApply(); }
    private void TxtFgOn_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(FgOnPreview, TxtFgOn.Text); DebouncedApply(); }
    private void BgOnColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtBgOn);
    private void FgOnColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => OpenColorPicker(TxtFgOn);

    private void BtnBrowseImageOn_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog
        { Filter = ImageFilter };
        if (d.ShowDialog() != true) return;
        TxtImgPathOn.Text = d.FileName;
        ApplyChanges();
    }

    private void BtnClearImageOn_Click(object s, RoutedEventArgs e)
    {
        TxtImgPathOn.Text = "";
        ApplyChanges();
    }

    private void BtnBrowseImage_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog
        { Filter = ImageFilter };
        if (d.ShowDialog() != true) return;
        TxtImgPath.Text = d.FileName;
        UpdateImagePreview(d.FileName);
        ApplyChanges();
    }

    private void BtnClearImage_Click(object s, RoutedEventArgs e)
    {
        TxtImgPath.Text = "";
        ImgPreview.Visibility = Visibility.Collapsed;
        ImgPreviewImg.Source = null;
        ApplyChanges();
    }

    private void UpdateImagePreview(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        { ImgPreview.Visibility = Visibility.Collapsed; return; }
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.DecodePixelHeight = 120;
            bmp.EndInit(); bmp.Freeze();
            ImgPreviewImg.Source = bmp;
            ImgPreview.Visibility = Visibility.Visible;
        }
        catch { ImgPreview.Visibility = Visibility.Collapsed; }
    }

    private void ApplyDarkComboStyle(ComboBox cmb)
    {
        if (TryFindResource("DarkCombo") is System.Windows.Style comboStyle)
            cmb.Style = comboStyle;
        if (TryFindResource("DarkComboItem") is System.Windows.Style itemStyle)
            cmb.ItemContainerStyle = itemStyle;
    }

    private System.Windows.Controls.TextBox? _colorTarget;

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
    {
        if (_colorTarget is not null) _colorTarget.Text = hex;
        ColorPicker.Visibility = Visibility.Collapsed;
        _colorTarget = null;
    }

    private void OnColorPickerClose()
    {
        ColorPicker.Visibility = Visibility.Collapsed;
        _colorTarget = null;
    }
}
