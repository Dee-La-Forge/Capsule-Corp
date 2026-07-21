using System.IO;
using System.Windows;
using System.Windows.Controls;
using UMP.Core.Models;
using UMP.Shared;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace UMP.App.Controls;

public partial class PhysicalButtonsPanel : System.Windows.Controls.UserControl
{
    private List<PhysicalButtonConfig>? _buttons;
    private List<Zone>? _zones;
    private System.Windows.Input.KeyEventHandler? _captureKeyHandler;
    private System.Windows.Threading.DispatcherTimer? _joyCaptureTimer;

    public event Action? CloseRequested;
    public event Action? Changed;

    public PhysicalButtonsPanel() { InitializeComponent(); }

    private void StopCapture()
    {
        if (_captureKeyHandler is not null)
        {
            var w = Window.GetWindow(this);
            if (w is not null) w.PreviewKeyDown -= _captureKeyHandler;
            _captureKeyHandler = null;
        }
        _joyCaptureTimer?.Stop();
        _joyCaptureTimer = null;
    }

    public void Load(List<PhysicalButtonConfig> buttons, List<Zone> zones)
    {
        _buttons = buttons;
        _zones = zones;
        Refresh();
    }

    public void Refresh()
    {
        StopCapture();
        ItemsPanel.Children.Clear();
        if (_buttons is null) return;

        foreach (var btn in _buttons)
            ItemsPanel.Children.Add(BuildRow(btn));

        // Bouton ajouter
        var addBtn = new Button
        {
            Content = "+ Ajouter un bouton physique",
            Height = 32, Margin = new Thickness(0, 8, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
            Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 11
        };
        addBtn.Click += (s, e) =>
        {
            _buttons.Add(new PhysicalButtonConfig());
            Refresh();
            Changed?.Invoke();
        };
        ItemsPanel.Children.Add(addBtn);
    }

    private UIElement BuildRow(PhysicalButtonConfig btn)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 58))
        };
        var stack = new StackPanel();

        // Label
        var lblRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var lblLabel = new TextBlock { Text = "Nom", Foreground = Brushes.Gray, FontSize = 9 };
        var txtLabel = new TextBox
        {
            Text = btn.Label, Height = 26, FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2, 6, 2)
        };
        txtLabel.TextChanged += (s, e) => { btn.Label = txtLabel.Text; Changed?.Invoke(); };
        stack.Children.Add(lblLabel);
        stack.Children.Add(txtLabel);

        // Binding
        var bindRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var lblBind = new TextBlock
        {
            Text = InputBindingService.FormatBinding(btn.Binding),
            Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var btnAssign = new Button
        {
            Content = "Assigner", Height = 26, Width = 80,
            Background = new SolidColorBrush(Color.FromRgb(14, 165, 233)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand, FontSize = 10
        };
        btnAssign.Click += (s, e) =>
        {
            lblBind.Text = "Appuyez sur une touche ou un bouton...";
            lblBind.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            btnAssign.IsEnabled = false;

            StopCapture();

            void CompleteCapture(string binding)
            {
                StopCapture();
                btn.Binding = binding;
                lblBind.Text = InputBindingService.FormatBinding(binding);
                lblBind.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                btnAssign.IsEnabled = true;
                Changed?.Invoke();
            }

            // Clavier : capture via PreviewKeyDown (fiable, event-driven)
            var window = Window.GetWindow(this);
            if (window is not null)
            {
                _captureKeyHandler = (s2, e2) =>
                {
                    var key = e2.Key == System.Windows.Input.Key.System ? e2.SystemKey : e2.Key;
                    if (key == System.Windows.Input.Key.None) return;
                    e2.Handled = true;
                    CompleteCapture($"key:{key}");
                };
                window.PreviewKeyDown += _captureKeyHandler;
            }

            // Joystick : capture par polling (pas d'evenement WPF pour les joysticks)
            var pressedJoy = new HashSet<string>();
            _joyCaptureTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(100) };
            _joyCaptureTimer.Tick += (s3, e3) =>
            {
                try
                {
                    var hit = JoystickService.ScanAllButtons();
                    if (hit.HasValue)
                    {
                        var joyBinding = $"joy:{hit.Value.joystickId}:{hit.Value.buttonIndex}";
                        if (!pressedJoy.Contains(joyBinding))
                        {
                            pressedJoy.Add(joyBinding);
                            CompleteCapture(joyBinding);
                        }
                    }
                    else { pressedJoy.Clear(); }
                }
                catch { }
            };
            _joyCaptureTimer.Start();
        };

        var btnDelete = new Button
        {
            Content = "\u2715", Width = 26, Height = 26,
            Background = new SolidColorBrush(Color.FromRgb(60, 30, 30)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand, FontSize = 10,
            Margin = new Thickness(6, 0, 0, 0)
        };
        btnDelete.Click += (s, e) =>
        {
            _buttons!.Remove(btn);
            Refresh();
            Changed?.Invoke();
        };

        DockPanel.SetDock(btnDelete, Dock.Right);
        DockPanel.SetDock(btnAssign, Dock.Right);
        bindRow.Children.Add(btnDelete);
        bindRow.Children.Add(btnAssign);
        bindRow.Children.Add(lblBind);
        stack.Children.Add(bindRow);

        // Actions
        var lblActions = new TextBlock { Text = "Actions", Foreground = Brushes.Gray, FontSize = 9, Margin = new Thickness(0, 6, 0, 2) };
        stack.Children.Add(lblActions);

        foreach (var action in btn.Actions)
            stack.Children.Add(BuildActionRow(action, btn));

        var addAction = new Button
        {
            Content = "+ Action", Height = 24,
            Background = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
            Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 10, Margin = new Thickness(0, 4, 0, 0)
        };
        addAction.Click += (s, e) =>
        {
            btn.Actions.Add(new ButtonAction());
            Refresh();
            Changed?.Invoke();
        };
        stack.Children.Add(addAction);

        card.Child = stack;
        return card;
    }

    private UIElement BuildActionRow(ButtonAction action, PhysicalButtonConfig parent)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

        var delBtn = new Button
        {
            Content = "\u2715", Width = 22, Height = 22,
            Background = new SolidColorBrush(Color.FromRgb(60, 30, 30)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand, FontSize = 9,
            Margin = new Thickness(4, 0, 0, 0)
        };
        delBtn.Click += (s, e) =>
        {
            parent.Actions.Remove(action);
            Refresh();
            Changed?.Invoke();
        };
        DockPanel.SetDock(delBtn, Dock.Right);
        row.Children.Add(delBtn);

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
        // Mapper les anciens types equivalents
        var normalizedType = action.Type switch
        {
            ButtonActionType.Play => ButtonActionType.PlaySequence,
            ButtonActionType.PauseSequence => ButtonActionType.Pause,
            ButtonActionType.StopSequence => ButtonActionType.Stop,
            _ => action.Type
        };
        action.Type = normalizedType;
        foreach (var (label, type) in types)
            cmb.Items.Add(new ComboBoxItem { Content = label, Tag = type });
        foreach (ComboBoxItem ci in cmb.Items)
            if (ci.Tag is ButtonActionType t && t == normalizedType)
            { cmb.SelectedItem = ci; break; }
        if (cmb.SelectedIndex < 0) cmb.SelectedIndex = 0;

        var paramPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        BuildActionParams(paramPanel, action);

        cmb.SelectionChanged += (s, e) =>
        {
            if (cmb.SelectedItem is ComboBoxItem ci && ci.Tag is ButtonActionType t)
            {
                action.Type = t;
                paramPanel.Children.Clear();
                BuildActionParams(paramPanel, action);
                Changed?.Invoke();
            }
        };
        row.Children.Add(cmb);

        return new StackPanel { Children = { row, paramPanel } };
    }

    private void BuildActionParams(StackPanel panel, ButtonAction action)
    {
        switch (action.Type)
        {
            case ButtonActionType.JumpToItem:
            case ButtonActionType.JumpToItemAllScreens:
                var refZone = _zones?.FirstOrDefault(z => z.Sequence is not null && z.Sequence.Items.Count > 0);
                if (refZone?.Sequence is null) break;
                var lblStart = new TextBlock { Text = "Item debut", Foreground = Brushes.Gray, FontSize = 9 };
                var cmbItem = new ComboBox();
                ApplyDarkComboStyle(cmbItem);
                for (int i = 0; i < refZone.Sequence.Items.Count; i++)
                {
                    var itm = refZone.Sequence.Items[i];
                    var name = itm.IsImageSlide
                        ? $"Img {Path.GetFileName(itm.ImageSlidePath ?? "")}"
                        : $"Vid {Path.GetFileName(itm.MediaPath ?? "")}";
                    cmbItem.Items.Add(new ComboBoxItem { Content = $"{i + 1}. {name}", Tag = i });
                }
                if (action.TargetItemIndex.HasValue && action.TargetItemIndex.Value < cmbItem.Items.Count)
                    cmbItem.SelectedIndex = action.TargetItemIndex.Value;
                cmbItem.SelectionChanged += (s, e) =>
                { if (cmbItem.SelectedItem is ComboBoxItem ci && ci.Tag is int idx) { action.TargetItemIndex = idx; Changed?.Invoke(); } };
                panel.Children.Add(lblStart);
                panel.Children.Add(cmbItem);

                if (action.Type == ButtonActionType.JumpToItemAllScreens)
                {
                    var lblEnd = new TextBlock { Text = "Item fin (inclus)", Foreground = Brushes.Gray, FontSize = 9, Margin = new Thickness(0, 4, 0, 0) };
                    var cmbEnd = new ComboBox();
                    ApplyDarkComboStyle(cmbEnd);
                    for (int i = 0; i < refZone.Sequence.Items.Count; i++)
                    {
                        var itm = refZone.Sequence.Items[i];
                        var name = itm.IsImageSlide
                            ? $"Img {Path.GetFileName(itm.ImageSlidePath ?? "")}"
                            : $"Vid {Path.GetFileName(itm.MediaPath ?? "")}";
                        cmbEnd.Items.Add(new ComboBoxItem { Content = $"{i + 1}. {name}", Tag = i });
                    }
                    var endVal = action.EndItemIndex ?? action.TargetItemIndex ?? 0;
                    if (endVal < cmbEnd.Items.Count) cmbEnd.SelectedIndex = endVal;
                    cmbEnd.SelectionChanged += (s, e) =>
                    { if (cmbEnd.SelectedItem is ComboBoxItem ci && ci.Tag is int idx) { action.EndItemIndex = idx; Changed?.Invoke(); } };
                    panel.Children.Add(lblEnd);
                    panel.Children.Add(cmbEnd);
                }
                break;

            case ButtonActionType.PlayMedia:
            case ButtonActionType.SwitchMedia:
                var dp = new DockPanel();
                var btnBrowse = new Button
                {
                    Content = "...", Width = 36, Height = 26,
                    Background = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var txtPath = new TextBox
                {
                    Text = action.MediaPath ?? "", Height = 26, FontSize = 10,
                    Background = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0),
                    Padding = new Thickness(6, 2, 6, 2), IsReadOnly = true
                };
                btnBrowse.Click += (s, e) =>
                {
                    var d = new Microsoft.Win32.OpenFileDialog
                    { Filter = "Media|*.mp4;*.mkv;*.avi;*.mov;*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tous|*.*" };
                    if (d.ShowDialog() == true)
                    {
                        action.MediaPath = d.FileName;
                        txtPath.Text = Path.GetFileName(d.FileName);
                        Changed?.Invoke();
                    }
                };
                DockPanel.SetDock(btnBrowse, Dock.Right);
                dp.Children.Add(btnBrowse);
                dp.Children.Add(txtPath);
                if (!string.IsNullOrEmpty(action.MediaPath))
                    txtPath.Text = Path.GetFileName(action.MediaPath);
                panel.Children.Add(dp);
                break;
        }
    }

    private void ApplyDarkComboStyle(ComboBox cmb)
    {
        if (TryFindResource("DarkCombo") is System.Windows.Style comboStyle)
            cmb.Style = comboStyle;
        if (TryFindResource("DarkComboItem") is System.Windows.Style itemStyle)
            cmb.ItemContainerStyle = itemStyle;
    }

    private void BtnClose_Click(object s, RoutedEventArgs e)
        => CloseRequested?.Invoke();
}
