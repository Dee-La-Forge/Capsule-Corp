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

namespace UMP.App.Windows;

public partial class ButtonPropertiesWindow : Window
{
    private readonly ButtonConfig _btn;
    private readonly Zone _zone;
    private readonly Dictionary<string, ZoneRuntime> _zones;
    private bool _showingOnActions;
    private bool _loading = true;

    public ButtonPropertiesWindow(ButtonConfig btn, Zone zone,
        Dictionary<string, ZoneRuntime> zones)
    {
        InitializeComponent();
        _btn = btn;
        _zone = zone;
        _zones = zones;
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
        TxtLabelOn.Text = _btn.LabelOn;
        TxtBgOn.Text = _btn.BackgroundColorOn;
        TxtFgOn.Text = _btn.TextColorOn;
        UpdatePreview();
        UpdateColorPreview(BgPreview, _btn.BackgroundColor);
        UpdateColorPreview(FgPreview, _btn.TextColor);
        RefreshActions();
    }

    private void UpdateToggle(bool isToggle)
    {
        if (SectionOn is null || TabsActions is null || LblStyle is null) return;
        SectionOn.Visibility = isToggle ? Visibility.Visible : Visibility.Collapsed;
        TabsActions.Visibility = isToggle ? Visibility.Visible : Visibility.Collapsed;
        LblStyle.Text = isToggle ? "STYLE ETAT OFF" : "STYLE";
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

        var cmb = new ComboBox { Height = 30 };
        var types = new (string, ButtonActionType)[]
        {
            ("\u25B6 Play", ButtonActionType.Play),
            ("\u23F8 Pause", ButtonActionType.Pause),
            ("\u23F9 Stop", ButtonActionType.Stop),
            ("\u25B6 Sequence", ButtonActionType.PlaySequence),
            ("\u23F8 Pause seq.", ButtonActionType.PauseSequence),
            ("\u23F9 Stop seq.", ButtonActionType.StopSequence),
            ("\u23EF Toggle seq.", ButtonActionType.ToggleSequence),
            ("\u23ED Item", ButtonActionType.JumpToItem),
            ("\u1F4C2 Media", ButtonActionType.PlayMedia),
            ("\u23F9 Stop all screens", ButtonActionType.StopAllScreens),
            ("\u23ED Jump to item (all screens)", ButtonActionType.JumpToItemAllScreens),
            ("\uD83D\uDCF9 Toggle PiP", ButtonActionType.TogglePip),
            ("\uD83D\uDCF9 Show PiP", ButtonActionType.ShowPip),
            ("\uD83D\uDCF9 Hide PiP", ButtonActionType.HidePip),
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

    private void BuildParams(StackPanel panel, ButtonAction action, ButtonActionType type)
    {
        switch (type)
        {
            case ButtonActionType.JumpToItem:
                var cmb = new ComboBox { Height = 30 };
                if (_zone.Sequence is not null)
                {
                    for (int i = 0; i < _zone.Sequence.Items.Count; i++)
                    {
                        var itm = _zone.Sequence.Items[i];
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
                var cmbS = new ComboBox { Height = 30 };
                var cmbE = new ComboBox { Height = 30 };
                if (_zone.Sequence is not null)
                {
                    for (int i = 0; i < _zone.Sequence.Items.Count; i++)
                    {
                        var itm = _zone.Sequence.Items[i];
                        var name = itm.IsImageSlide
                            ? $"Img {Path.GetFileName(itm.ImageSlidePath ?? "")}"
                            : $"Vid {Path.GetFileName(itm.MediaPath ?? "")}";
                        var label = $"{i + 1}. {name}";
                        cmbS.Items.Add(new ComboBoxItem { Content = label, Tag = i });
                        cmbE.Items.Add(new ComboBoxItem { Content = label, Tag = i });
                    }
                }
                if (action.TargetItemIndex.HasValue && action.TargetItemIndex.Value < cmbS.Items.Count)
                    cmbS.SelectedIndex = action.TargetItemIndex.Value;
                var endV = action.EndItemIndex ?? action.TargetItemIndex ?? 0;
                if (endV < cmbE.Items.Count)
                    cmbE.SelectedIndex = endV;
                cmbS.SelectionChanged += (s, e) =>
                { if (cmbS.SelectedItem is ComboBoxItem ci && ci.Tag is int idx) action.TargetItemIndex = idx; };
                cmbE.SelectionChanged += (s, e) =>
                { if (cmbE.SelectedItem is ComboBoxItem ci && ci.Tag is int idx) action.EndItemIndex = idx; };
                panel.Children.Add(new TextBlock { Text = "Start item" });
                panel.Children.Add(cmbS);
                panel.Children.Add(new TextBlock { Text = "End item" });
                panel.Children.Add(cmbE);
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
                        { Filter = "Media|*.mp4;*.mkv;*.avi|Tous|*.*" };
                    if (d.ShowDialog() == true)
                    { action.MediaPath = d.FileName; txt.Text = d.FileName; }
                };
                DockPanel.SetDock(btn, Dock.Right);
                dp.Children.Add(btn); dp.Children.Add(txt);
                panel.Children.Add(dp);
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

    private void BtnApply_Click(object s, RoutedEventArgs e)
    {
        _btn.IsToggle = RbToggle.IsChecked == true;
        _btn.Label = TxtLabel.Text;
        _btn.BackgroundColor = TxtBg.Text;
        _btn.TextColor = TxtFg.Text;
        _btn.LabelOn = TxtLabelOn.Text;
        _btn.BackgroundColorOn = TxtBgOn.Text;
        _btn.TextColorOn = TxtFgOn.Text;
        if (double.TryParse(TxtW.Text, out var w)) _btn.Width = w / 100.0;
        if (double.TryParse(TxtH.Text, out var h)) _btn.Height = h / 100.0;
        Close();
    }

    private void BtnCancel_Click(object s, RoutedEventArgs e) => Close();

    // Color preview + pickers
    private void UpdatePreview()
    {
        Brush bg;
        try { bg = new System.Windows.Media.BrushConverter().ConvertFromString(
            TxtBg?.Text ?? "#2A2A40") as Brush ?? Brushes.DimGray; }
        catch { bg = Brushes.DimGray; }
        if (PreviewBox is not null) PreviewBox.Background = bg;
        if (PreviewLabel is not null) PreviewLabel.Text = TxtLabel?.Text ?? "Btn";
    }

    private void UpdateColorPreview(Border preview, string hex)
    {
        if (preview is null) return;
        try { preview.Background = new System.Windows.Media.BrushConverter()
            .ConvertFromString(hex) as Brush ?? Brushes.Gray; }
        catch { preview.Background = Brushes.Gray; }
    }

    private void TxtLabel_Changed(object s, TextChangedEventArgs e) => UpdatePreview();
    private void TxtBg_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(BgPreview, TxtBg.Text); UpdatePreview(); }
    private void TxtFg_Changed(object s, TextChangedEventArgs e)
    { UpdateColorPreview(FgPreview, TxtFg.Text); }

    private void BgColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
    { var c = ShowColorDialog(TxtBg.Text); if (c is not null) TxtBg.Text = c; }
    private void FgColor_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
    { var c = ShowColorDialog(TxtFg.Text); if (c is not null) TxtFg.Text = c; }

    private static string? ShowColorDialog(string current)
    {
        var d = new System.Windows.Forms.ColorDialog { FullOpen = true };
        try
        {
            var hex = current.TrimStart('#');
            if (hex.Length >= 6)
                d.Color = System.Drawing.ColorTranslator.FromHtml($"#{hex.Substring(0, 6)}");
        }
        catch { }
        if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
        return $"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}";
    }
}
