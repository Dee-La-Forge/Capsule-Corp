using System.IO;
using System.Windows;
using System.Windows.Controls;
using UMP.Core.Models;
using WpfColor = System.Windows.Media.Color;
using SolidBrush = System.Windows.Media.SolidColorBrush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCursors = System.Windows.Input.Cursors;
using WpfFontFam = System.Windows.Media.FontFamily;

namespace UMP.App.Controls;

public partial class SequencePanel : System.Windows.Controls.UserControl
{
    private const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tous|*.*";

    // Design system — couleurs centralisees
    // Dark Pro palette
    private static readonly SolidBrush CardBg = new(WpfColor.FromRgb(26, 26, 26));
    private static readonly SolidBrush CardBgActive = new(WpfColor.FromRgb(40, 40, 40));
    private static readonly SolidBrush CardBorder = new(WpfColor.FromRgb(58, 58, 58));
    private static readonly SolidBrush CardBorderActive = new(WpfColor.FromRgb(14, 165, 233));
    private static readonly SolidBrush TextLight = new(WpfColor.FromRgb(232, 232, 232));
    private static readonly SolidBrush TextDim = new(WpfColor.FromRgb(153, 153, 153));
    private static readonly SolidBrush BtnBg = new(WpfColor.FromRgb(51, 51, 51));
    private static readonly SolidBrush AccentFg = new(WpfColor.FromRgb(56, 189, 248));
    private static readonly SolidBrush SectionLabel = new(WpfColor.FromRgb(153, 153, 153));
    private static readonly SolidBrush InColor = new(WpfColor.FromRgb(34, 197, 94));
    private static readonly SolidBrush OutColor = new(WpfColor.FromRgb(239, 68, 68));

    private Sequence? _sequence;
    private Zone? _zone;
    private Func<long>? _getCurrentMs;
    private int _activeIndex = -1;
    private readonly List<Border> _itemCards = new();
    private bool _buildingCard = false;

    private Action<SequenceItem, ButtonConfig>? _openButtonProps;
    private Action<ButtonConfig>? _selectButton;
    private Action<ImageOverlayConfig>? _selectImage;
    private Action<SubtitleConfig>? _selectSubtitle;
    private Action<PipConfig>? _selectPip;
    private Action? _refreshCanvas;
    private string? _editingButtonId;
    private string? _editingImageId;
    private string? _editingSubtitleId;
    private string? _editingPipId;
    private bool _suppressItemSelect;

    public event Action? SequenceChanged;
    public event Action<UMP.App.Services.IUndoCommand>? UndoCommandPushed;
    public event Action<SequenceItem>? ItemSelected;
    public event Action<SequenceItem>? ItemFileChangeRequested;
    public event Action? CloseRequested;


    public SequencePanel() { InitializeComponent(); }

    public void Initialize(Zone zone, Func<long> getCurrentMs)
    {
        _zone = zone;
        _getCurrentMs = getCurrentMs;
        zone.Sequence ??= new Sequence();
        _sequence = zone.Sequence;
        _sequence.Mode = SequenceMode.Scenario;

        if (_sequence.Items.Count == 0 && !string.IsNullOrEmpty(zone.MediaFilePath))
            _sequence.Items.Add(new SequenceItem { MediaPath = zone.MediaFilePath });

        ChkLoop.Checked -= ChkLoop_Changed;
        ChkLoop.Unchecked -= ChkLoop_Changed;
        ChkLoop.IsChecked = _sequence.IsLooping;
        ChkLoop.Checked += ChkLoop_Changed;
        ChkLoop.Unchecked += ChkLoop_Changed;

        AddButtonsPanel.Visibility = Visibility.Visible;
        RefreshItems();
    }

    public void RefreshTriggers() => RefreshItems();

    public void RefreshItems()
    {
        _buildingCard = true;
        ItemsPanel.Children.Clear();
        _itemCards.Clear();
        if (_sequence is null) { _buildingCard = false; return; }

        for (int idx = 0; idx < _sequence.Items.Count; idx++)
            ItemsPanel.Children.Add(BuildItemCard(_sequence.Items[idx], idx));

        _buildingCard = false;
        if (_activeIndex >= 0) SetActiveIndex(_activeIndex);
    }

    // ========== CARD ==========

    private UIElement BuildItemCard(SequenceItem item, int itemIndex = -1)
    {
        var card = new Border
        {
            Background = CardBg,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1),
            BorderBrush = WpfBrushes.Transparent,
            Cursor = WpfCursors.Hand
        };
        _itemCards.Add(card);

        var stack = new StackPanel();
        var capturedItem = item;

        // --- HEADER ---
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Un item avec DurationMs defini mais sans MediaPath est un plan fixe en attente
        var isImgItem = item.IsImageSlide ||
            (item.DurationMs.HasValue && string.IsNullOrEmpty(item.MediaPath));
        var icon = isImgItem ? "\U0001F5BC" : "\U0001F3AC";
        var fileName = isImgItem
            ? (string.IsNullOrEmpty(item.ImageSlidePath) ? "Choisir image..."
                : Path.GetFileName(item.ImageSlidePath))
            : (string.IsNullOrEmpty(item.MediaPath) ? "Choisir video..."
                : Path.GetFileName(item.MediaPath));

        var indexPrefix = itemIndex >= 0 ? $"{itemIndex + 1}. " : "";
        var fileLabel = new TextBlock
        {
            Text = $"{indexPrefix}{icon} {fileName}",
            Foreground = isImgItem
                ? new SolidBrush(WpfColor.FromRgb(245, 158, 11))
                : TextLight,
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Cursor = WpfCursors.Hand,
            ToolTip = "Cliquer pour changer le fichier"
        };

        // Clic sur le label → ouvrir dialog fichier
        fileLabel.MouseLeftButtonUp += (s, e) =>
        {
            e.Handled = true;
            _suppressItemSelect = true;
            ItemFileChangeRequested?.Invoke(capturedItem);
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    var isImg2 = capturedItem.IsImageSlide ||
                        (capturedItem.DurationMs.HasValue && string.IsNullOrEmpty(capturedItem.MediaPath));
                    var ic = isImg2 ? "Img" : "Vid";
                    var n = isImg2
                        ? Path.GetFileName(capturedItem.ImageSlidePath ?? "")
                        : Path.GetFileName(capturedItem.MediaPath ?? "");
                    fileLabel.Text = $"{ic} {(string.IsNullOrEmpty(n) ? "Choisir..." : n)}";
                    fileLabel.Foreground = isImg2
                        ? new SolidBrush(WpfColor.FromRgb(245, 158, 11))
                        : TextLight;
                }));
        };

        Grid.SetColumn(fileLabel, 0);

        var btnPanel = new StackPanel
            { Orientation = System.Windows.Controls.Orientation.Horizontal };

        var loopTgl = new System.Windows.Controls.Primitives.ToggleButton
        {
            Content = "\u21BB", Width = 24, Height = 22, FontSize = 13,
            IsChecked = capturedItem.IsLooping,
            Cursor = WpfCursors.Hand,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Lire en boucle"
        };
        loopTgl.Template = MakeToggleBtnTemplate();
        loopTgl.Click += (s, e) =>
        {
            _suppressItemSelect = true;
            capturedItem.IsLooping = loopTgl.IsChecked == true;
            SequenceChanged?.Invoke();
        };
        btnPanel.Children.Add(loopTgl);

        btnPanel.Children.Add(MakeSmallBtn("\u25B2", () =>
        {
            var idx = _sequence!.Items.IndexOf(capturedItem);
            if (idx <= 0) return;
            var comp = new Services.CompositeCommand("Monter item");
            Action refresh = () => { RefreshItems(); _refreshCanvas?.Invoke(); };
            comp.Add(new Services.ListRemoveCommand<UMP.Core.Models.SequenceItem>("Retirer item", _sequence.Items, capturedItem, onApply: refresh));
            comp.Add(new Services.ListAddCommand<UMP.Core.Models.SequenceItem>("Inserer item", _sequence.Items, capturedItem, idx - 1, onApply: refresh));
            comp.Execute();
            UndoCommandPushed?.Invoke(comp);
            SequenceChanged?.Invoke();
        }));

        btnPanel.Children.Add(MakeSmallBtn("\u25BC", () =>
        {
            var idx = _sequence!.Items.IndexOf(capturedItem);
            if (idx >= _sequence.Items.Count - 1) return;
            var comp = new Services.CompositeCommand("Descendre item");
            Action refresh = () => { RefreshItems(); _refreshCanvas?.Invoke(); };
            comp.Add(new Services.ListRemoveCommand<UMP.Core.Models.SequenceItem>("Retirer item", _sequence.Items, capturedItem, onApply: refresh));
            comp.Add(new Services.ListAddCommand<UMP.Core.Models.SequenceItem>("Inserer item", _sequence.Items, capturedItem, idx + 1, onApply: refresh));
            comp.Execute();
            UndoCommandPushed?.Invoke(comp);
            SequenceChanged?.Invoke();
        }));

        var dupBtn = MakeSmallBtn("\u2398", () =>
        {
            var idx = _sequence!.Items.IndexOf(capturedItem);
            if (idx < 0) return;
            var json = System.Text.Json.JsonSerializer.Serialize(capturedItem);
            var clone = System.Text.Json.JsonSerializer.Deserialize<UMP.Core.Models.SequenceItem>(json);
            if (clone is null) return;
            clone.Id = Guid.NewGuid().ToString();
            foreach (var b in clone.Buttons) b.Id = Guid.NewGuid().ToString();
            foreach (var s in clone.Subtitles) s.Id = Guid.NewGuid().ToString();
            foreach (var p in clone.PictureInPictures) p.Id = Guid.NewGuid().ToString();
            foreach (var i in clone.ImageOverlays) i.Id = Guid.NewGuid().ToString();
            var addCmd = new Services.ListAddCommand<UMP.Core.Models.SequenceItem>("Dupliquer item", _sequence.Items, clone, idx + 1,
                onApply: () => { RefreshItems(); _refreshCanvas?.Invoke(); });
            addCmd.Execute();
            UndoCommandPushed?.Invoke(addCmd);
            SequenceChanged?.Invoke();
        });
        dupBtn.ToolTip = "Dupliquer";
        btnPanel.Children.Add(dupBtn);

        btnPanel.Children.Add(MakeSmallBtn("\u2715", () =>
        {
            var rmCmd = new Services.ListRemoveCommand<UMP.Core.Models.SequenceItem>("Supprimer item", _sequence!.Items, capturedItem,
                onApply: () => { RefreshItems(); _refreshCanvas?.Invoke(); });
            rmCmd.Execute();
            UndoCommandPushed?.Invoke(rmCmd);
            SequenceChanged?.Invoke();
        }));

        Grid.SetColumn(btnPanel, 1);
        header.Children.Add(fileLabel);
        header.Children.Add(btnPanel);
        stack.Children.Add(header);

        if (item.IsImageSlide && _sequence?.Mode == SequenceMode.Scenario)
            stack.Children.Add(BuildSlideConfig(capturedItem));
        stack.Children.Add(BuildButtonsSection(capturedItem));
        stack.Children.Add(BuildImageOverlaysSection(capturedItem));
        stack.Children.Add(BuildSubtitlesSection(capturedItem));
        stack.Children.Add(BuildPipSection(capturedItem));

        card.Child = stack;
        card.MouseLeftButtonUp += (s, e) =>
        {
            if (_suppressItemSelect) { _suppressItemSelect = false; return; }
            ItemSelected?.Invoke(capturedItem);
        };
        return card;
    }

    // ========== SLIDE CONFIG ==========

    private UIElement BuildSlideConfig(SequenceItem item)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var grpDur = $"dur_{item.Id}";

        var durPanel = new StackPanel
            { Orientation = System.Windows.Controls.Orientation.Horizontal };

        var rbFixed = new System.Windows.Controls.RadioButton
        {
            Content = "Duree fixe :", Foreground = WpfBrushes.White, FontSize = 10,
            GroupName = grpDur, IsChecked = item.SlideDuration == ImageSlideDuration.Fixed,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
        };

        var durBox = new System.Windows.Controls.TextBox
        {
            Text = ((item.DurationMs ?? 5000) / 1000).ToString(),
            Width = 40, Height = 22,
            Background = new SolidBrush(WpfColor.FromRgb(20, 20, 40)),
            Foreground = WpfBrushes.White, BorderThickness = new Thickness(0),
            FontSize = 10, Padding = new Thickness(4, 2, 4, 2),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        durBox.LostFocus += (s, e) =>
        { if (int.TryParse(durBox.Text, out var sec)) item.DurationMs = sec * 1000L; SequenceChanged?.Invoke(); };

        var rbClick = new System.Windows.Controls.RadioButton
        {
            Content = "Plan fixe", Foreground = WpfBrushes.White, FontSize = 10,
            GroupName = grpDur, IsChecked = item.SlideDuration == ImageSlideDuration.UntilClick,
            VerticalAlignment = VerticalAlignment.Center
        };

        rbFixed.Checked += (s, e) =>
        { if (_buildingCard) return; item.SlideDuration = ImageSlideDuration.Fixed; durBox.IsEnabled = true; SequenceChanged?.Invoke(); };
        rbClick.Checked += (s, e) =>
        { if (_buildingCard) return; item.SlideDuration = ImageSlideDuration.UntilClick; durBox.IsEnabled = false; SequenceChanged?.Invoke(); };

        durPanel.Children.Add(rbFixed);
        durPanel.Children.Add(durBox);
        durPanel.Children.Add(new TextBlock
        {
            Text = "sec", Foreground = new SolidBrush(WpfColor.FromRgb(100, 100, 140)),
            FontSize = 10, Margin = new Thickness(4, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        durPanel.Children.Add(rbClick);
        panel.Children.Add(durPanel);
        return panel;
    }

    // ========== IN/OUT INLINE ==========

    private UIElement BuildInOutRow(long inMs, long outMs,
        Action<long> setIn, Action<long> setOut)
    {
        var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 3, 0, 0) };

        var inTxt = new TextBlock
        {
            Text = $"\u25B6 {FormatMs(inMs)}",
            Foreground = InColor,
            FontSize = 8, FontFamily = new WpfFontFam("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var outTxt = new TextBlock
        {
            Text = $"\u25A0 {(outMs > 0 ? FormatMs(outMs) : "\u221E")}",
            Foreground = OutColor,
            FontSize = 8, FontFamily = new WpfFontFam("Consolas"),
            VerticalAlignment = VerticalAlignment.Center
        };
        sp.Children.Add(inTxt);
        sp.Children.Add(outTxt);
        return sp;
    }

    // ========== BUTTONS OVERLAY ==========

    private UIElement BuildButtonsSection(SequenceItem item)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        if (item.Buttons.Count > 0)
        {
            var header = new TextBlock
            {
                Text = "BOUTONS OVERLAY",
                Foreground = new SolidBrush(WpfColor.FromRgb(100, 100, 140)),
                FontSize = 9, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(header);
        }

        foreach (var btn in item.Buttons)
        {
            var capturedBtn = btn;

            // Card bouton — surlignee si selectionne
            var isBtnEditing = _editingButtonId == capturedBtn.Id;
            var card = new Border
            {
                Background = isBtnEditing
                    ? CardBgActive
                    : CardBg,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                BorderThickness = new Thickness(isBtnEditing ? 1.5 : 1),
                BorderBrush = isBtnEditing
                    ? CardBorderActive
                    : CardBorder,
                Cursor = WpfCursors.Hand
            };
            // Clic sur la card = selectionner le bouton sur le player
            card.MouseLeftButtonUp += (s, e) =>
            {
                _editingButtonId = capturedBtn.Id;
                _selectButton?.Invoke(capturedBtn);
                RefreshItems();
                e.Handled = true; _suppressItemSelect = true;
            };

            var cardGrid = new Grid();
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Preview bouton (mini apercu realiste)
            System.Windows.Media.Brush bgBrush;
            try { bgBrush = new System.Windows.Media.BrushConverter().ConvertFromString(
                btn.BackgroundColor) as System.Windows.Media.Brush ?? WpfBrushes.DimGray; }
            catch { bgBrush = WpfBrushes.DimGray; }

            System.Windows.Media.Brush fgBrush;
            try { fgBrush = new System.Windows.Media.BrushConverter().ConvertFromString(
                btn.TextColor) as System.Windows.Media.Brush ?? WpfBrushes.White; }
            catch { fgBrush = WpfBrushes.White; }

            var colorPreview = new Border
            {
                Width = 36, Height = 24,
                CornerRadius = new CornerRadius(5),
                Background = bgBrush,
                BorderBrush = new SolidBrush(WpfColor.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var previewLabel = new TextBlock
            {
                Text = btn.Label.Length > 4 ? btn.Label.Substring(0, 4) : btn.Label,
                FontSize = 7, FontWeight = FontWeights.SemiBold,
                Foreground = fgBrush,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            colorPreview.Child = previewLabel;
            Grid.SetColumn(colorPreview, 0);

            // Info
            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var lblName = new TextBlock
            {
                Text = btn.Label,
                Foreground = TextLight,
                FontSize = 11, FontWeight = FontWeights.SemiBold
            };
            // Info utile : type + nombre d'actions
            var typeInfo = btn.IsToggle ? "Toggle" : "Simple";
            var actionCount = btn.Actions.Count + btn.ActionsOn.Count;
            var actionInfo = actionCount > 0 ? $" \u2022 {actionCount} action(s)" : " \u2022 aucune action";
            var lblInfo = new TextBlock
            {
                Text = $"{typeInfo}{actionInfo}",
                Foreground = TextDim,
                FontSize = 9
            };
            infoStack.Children.Add(lblName);
            infoStack.Children.Add(lblInfo);
            Grid.SetColumn(infoStack, 1);

            // Actions
            var actions = new StackPanel
                { Orientation = System.Windows.Controls.Orientation.Horizontal,
                  VerticalAlignment = VerticalAlignment.Center };
            var isEditing = _editingButtonId == capturedBtn.Id;
            var editBtn = new System.Windows.Controls.Button
            {
                Content = "\u270E", Width = 24, Height = 22,
                Background = isEditing
                    ? CardBorderActive
                    : BtnBg,
                Foreground = WpfBrushes.White, BorderThickness = new Thickness(0),
                Cursor = WpfCursors.Hand, FontSize = 11,
                Margin = new Thickness(2, 0, 2, 0)
            };
            editBtn.Click += (s, e) => { _suppressItemSelect = true; _openButtonProps?.Invoke(item, capturedBtn); };
            actions.Children.Add(editBtn);
            actions.Children.Add(MakeSmallBtn("\u2715", () =>
            {
                var rmCmd = new Services.ListRemoveCommand<ButtonConfig>("Supprimer bouton", item.Buttons, capturedBtn,
                    onApply: () => { RefreshItems(); _refreshCanvas?.Invoke(); });
                rmCmd.Execute();
                UndoCommandPushed?.Invoke(rmCmd);
                SequenceChanged?.Invoke();
            }));
            Grid.SetColumn(actions, 2);

            cardGrid.Children.Add(colorPreview);
            cardGrid.Children.Add(infoStack);
            cardGrid.Children.Add(actions);

            var btnCardContent = new StackPanel();
            btnCardContent.Children.Add(cardGrid);
            btnCardContent.Children.Add(BuildInOutRow(capturedBtn.InMs, capturedBtn.OutMs,
                v => capturedBtn.InMs = v, v => capturedBtn.OutMs = v));
            card.Child = btnCardContent;
            stack.Children.Add(card);
        }

        var addBtn = new System.Windows.Controls.Button
        {
            Content = "+ Bouton overlay", Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            Background = BtnBg,
            Foreground = AccentFg,
            BorderThickness = new Thickness(0), FontSize = 10,
            Cursor = WpfCursors.Hand,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        addBtn.Click += (s, e) =>
        {
            _suppressItemSelect = true;
            var newBtn = new ButtonConfig { Label = "Bouton" };
            var addCmd = new Services.ListAddCommand<ButtonConfig>("Ajouter bouton", item.Buttons, newBtn,
                onApply: () => { RefreshItems(); _refreshCanvas?.Invoke(); });
            addCmd.Execute();
            UndoCommandPushed?.Invoke(addCmd);
            _openButtonProps?.Invoke(item, newBtn);
            SequenceChanged?.Invoke();
        };
        stack.Children.Add(addBtn);
        return stack;
    }

    // ========== HELPERS ==========

    private static System.Windows.Controls.ControlTemplate MakeToggleBtnTemplate()
    {
        var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Primitives.ToggleButton));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, BtnBg);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        template.VisualTree = border;

        var checkedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, CardBorderActive, "Bd"));
        template.Triggers.Add(checkedTrigger);
        return template;
    }

    private System.Windows.Controls.Button MakeSmallBtn(string content, Action onClick)
    {
        var btn = new System.Windows.Controls.Button
        {
            Content = content, Width = 24, Height = 22,
            Background = BtnBg,
            Foreground = WpfBrushes.White, BorderThickness = new Thickness(0),
            Cursor = WpfCursors.Hand, FontSize = 11,
            Margin = new Thickness(2, 0, 2, 0)
        };
        btn.Click += (s, e) => { _suppressItemSelect = true; onClick(); };
        return btn;
    }

    // ========== IMAGE OVERLAYS ==========

    private UIElement BuildImageOverlaysSection(SequenceItem item)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = "IMAGES OVERLAY",
            Foreground = TextDim,
            FontSize = 9, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        foreach (var img in item.ImageOverlays)
        {
            var capturedImg = img;
            var fileName = string.IsNullOrEmpty(img.ImagePath) ? "(vide)"
                : System.IO.Path.GetFileName(img.ImagePath);

            var isImgEditing = _editingImageId == capturedImg.Id;
            var card = new Border
            {
                Background = isImgEditing
                    ? CardBgActive
                    : CardBg,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                BorderThickness = new Thickness(isImgEditing ? 1.5 : 1),
                BorderBrush = isImgEditing
                    ? CardBorderActive
                    : CardBorder,
                Cursor = WpfCursors.Hand
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                _editingImageId = capturedImg.Id;
                _editingButtonId = null;
                _selectImage?.Invoke(capturedImg);
                RefreshItems();
                e.Handled = true; _suppressItemSelect = true;
            };

            var cardGrid = new Grid();
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Mini preview
            var preview = new Border
            {
                Width = 36, Height = 24, CornerRadius = new CornerRadius(3),
                Background = new SolidBrush(WpfColor.FromRgb(30, 30, 50)),
                Margin = new Thickness(0, 0, 8, 0),
                ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center
            };
            if (!string.IsNullOrEmpty(img.ImagePath))
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(img.ImagePath, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.DecodePixelHeight = 48;
                    bmp.EndInit(); bmp.Freeze();
                    preview.Child = new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Stretch = System.Windows.Media.Stretch.UniformToFill
                    };
                }
                catch { }
            }
            else
            {
                preview.Child = new TextBlock
                {
                    Text = "\uD83D\uDDBC", FontSize = 12, Foreground = WpfBrushes.Gray,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            Grid.SetColumn(preview, 0);

            // Info
            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoStack.Children.Add(new TextBlock
            {
                Text = fileName,
                Foreground = TextLight,
                FontSize = 10, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Opacite {img.Opacity * 100:F0}%",
                Foreground = TextDim,
                FontSize = 9
            });
            Grid.SetColumn(infoStack, 1);

            // Actions
            var actions = new StackPanel
                { Orientation = System.Windows.Controls.Orientation.Horizontal,
                  VerticalAlignment = VerticalAlignment.Center };

            // Bouton editer
            var isImgEdit2 = _editingImageId == capturedImg.Id;
            var editImgBtn = new System.Windows.Controls.Button
            {
                Content = "\u270E", Width = 24, Height = 22,
                Background = isImgEdit2 ? CardBorderActive : BtnBg,
                Foreground = WpfBrushes.White, BorderThickness = new Thickness(0),
                Cursor = WpfCursors.Hand, FontSize = 11,
                Margin = new Thickness(2, 0, 2, 0)
            };
            editImgBtn.Click += (s, e) =>
            {
                e.Handled = true; _suppressItemSelect = true;
                _editingImageId = capturedImg.Id;
                _editingButtonId = null; _editingSubtitleId = null; _editingPipId = null;
                _selectImage?.Invoke(capturedImg);
                RefreshItems();
            };
            actions.Children.Add(editImgBtn);
            // Bouton supprimer
            actions.Children.Add(MakeSmallBtn("\u2715", () =>
            {
                var rmCmd = new Services.ListRemoveCommand<UMP.Core.Models.ImageOverlayConfig>("Supprimer image overlay", item.ImageOverlays, capturedImg,
                    onApply: () => { RefreshItems(); _refreshCanvas?.Invoke(); });
                rmCmd.Execute();
                UndoCommandPushed?.Invoke(rmCmd);
                SequenceChanged?.Invoke();
            }));
            Grid.SetColumn(actions, 2);

            cardGrid.Children.Add(preview);
            cardGrid.Children.Add(infoStack);
            cardGrid.Children.Add(actions);

            var imgCardContent = new StackPanel();
            imgCardContent.Children.Add(cardGrid);
            imgCardContent.Children.Add(BuildInOutRow(capturedImg.InMs, capturedImg.OutMs,
                v => capturedImg.InMs = v, v => capturedImg.OutMs = v));
            card.Child = imgCardContent;
            stack.Children.Add(card);
        }

        // Bouton ajouter
        var addBtn = new System.Windows.Controls.Button
        {
            Content = "+ Image overlay", Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            Background = BtnBg,
            Foreground = AccentFg,
            BorderThickness = new Thickness(0), FontSize = 10,
            Cursor = WpfCursors.Hand,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        addBtn.Click += (s, e) =>
        {
            _suppressItemSelect = true;
            var d = new Microsoft.Win32.OpenFileDialog
            { Filter = ImageFilter };
            if (d.ShowDialog() != true) return;
            var newImg = new ImageOverlayConfig { ImagePath = d.FileName };
            var addCmd = new Services.ListAddCommand<UMP.Core.Models.ImageOverlayConfig>("Ajouter image overlay", item.ImageOverlays, newImg,
                onApply: () => { RefreshItems(); _refreshCanvas?.Invoke(); });
            addCmd.Execute();
            UndoCommandPushed?.Invoke(addCmd);
            SequenceChanged?.Invoke();
        };
        stack.Children.Add(addBtn);
        return stack;
    }

    // ========== SUBTITLES ==========

    private UIElement BuildSubtitlesSection(SequenceItem item)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = "SOUS-TITRES",
            Foreground = TextDim,
            FontSize = 9, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        foreach (var sub in item.Subtitles)
        {
            var capturedSub = sub;
            var isSubEditing = _editingSubtitleId == capturedSub.Id;
            var fileName = string.IsNullOrEmpty(sub.FilePath) ? "(aucun fichier)"
                : System.IO.Path.GetFileName(sub.FilePath);

            var card = new Border
            {
                Background = isSubEditing
                    ? CardBgActive
                    : CardBg,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                BorderThickness = new Thickness(isSubEditing ? 1.5 : 1),
                BorderBrush = isSubEditing
                    ? CardBorderActive
                    : CardBorder,
                Cursor = WpfCursors.Hand
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                _editingSubtitleId = capturedSub.Id;
                _editingButtonId = null; _editingImageId = null;
                _selectSubtitle?.Invoke(capturedSub);
                RefreshItems();
                e.Handled = true; _suppressItemSelect = true;
            };

            var cardGrid = new Grid();
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icone T
            var icon = new Border
            {
                Width = 28, Height = 24, CornerRadius = new CornerRadius(4),
                Background = new SolidBrush(WpfColor.FromRgb(40, 40, 70)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.Child = new TextBlock
            {
                Text = "T", FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = new SolidBrush(WpfColor.FromRgb(200, 180, 255)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoStack.Children.Add(new TextBlock
            {
                Text = fileName,
                Foreground = TextLight,
                FontSize = 10, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"{sub.FontFamily} {sub.FontSize}pt",
                Foreground = TextDim,
                FontSize = 9
            });
            Grid.SetColumn(infoStack, 1);

            var actions = new StackPanel
                { Orientation = System.Windows.Controls.Orientation.Horizontal,
                  VerticalAlignment = VerticalAlignment.Center };
            var isSubEdit = _editingSubtitleId == capturedSub.Id;
            var editSubBtn = new System.Windows.Controls.Button
            {
                Content = "\u270E", Width = 24, Height = 22,
                Background = isSubEdit
                    ? CardBorderActive
                    : BtnBg,
                Foreground = WpfBrushes.White, BorderThickness = new Thickness(0),
                Cursor = WpfCursors.Hand, FontSize = 11,
                Margin = new Thickness(2, 0, 2, 0)
            };
            editSubBtn.Click += (s, e) =>
            {
                e.Handled = true; _suppressItemSelect = true;
                _editingSubtitleId = capturedSub.Id;
                _editingButtonId = null; _editingImageId = null;
                _selectSubtitle?.Invoke(capturedSub);
                RefreshItems();
            };
            actions.Children.Add(editSubBtn);
            actions.Children.Add(MakeSmallBtn("\u2715", () =>
            {
                var rmCmd = new Services.ListRemoveCommand<UMP.Core.Models.SubtitleConfig>("Supprimer sous-titre", item.Subtitles, capturedSub,
                    onApply: () => { RefreshItems(); _refreshCanvas?.Invoke(); });
                rmCmd.Execute();
                UndoCommandPushed?.Invoke(rmCmd);
                SequenceChanged?.Invoke();
            }));
            Grid.SetColumn(actions, 2);

            cardGrid.Children.Add(icon);
            cardGrid.Children.Add(infoStack);
            cardGrid.Children.Add(actions);

            card.Child = cardGrid;
            stack.Children.Add(card);
        }

        var addBtn = new System.Windows.Controls.Button
        {
            Content = "+ Sous-titre", Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            Background = BtnBg,
            Foreground = AccentFg,
            BorderThickness = new Thickness(0), FontSize = 10,
            Cursor = WpfCursors.Hand,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        addBtn.Click += (s, e) =>
        {
            _suppressItemSelect = true;
            var newSub = new SubtitleConfig();
            var addCmd = new Services.ListAddCommand<UMP.Core.Models.SubtitleConfig>("Ajouter sous-titre", item.Subtitles, newSub,
                onApply: () => { RefreshItems(); _refreshCanvas?.Invoke(); });
            addCmd.Execute();
            UndoCommandPushed?.Invoke(addCmd);
            SequenceChanged?.Invoke();
        };
        stack.Children.Add(addBtn);
        return stack;
    }

    // ========== PICTURE IN PICTURE ==========

    private UIElement BuildPipSection(SequenceItem item)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = "PICTURE IN PICTURE",
            Foreground = TextDim,
            FontSize = 9, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        foreach (var pip in item.PictureInPictures)
        {
            var capturedPip = pip;
            var fileName = string.IsNullOrEmpty(pip.VideoPath) ? "(aucune video)"
                : System.IO.Path.GetFileName(pip.VideoPath);
            var isPipEditing = _editingPipId == capturedPip.Id;

            var card = new Border
            {
                Background = isPipEditing
                    ? CardBgActive
                    : CardBg,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                BorderThickness = new Thickness(isPipEditing ? 1.5 : 1),
                BorderBrush = isPipEditing
                    ? CardBorderActive
                    : CardBorder,
                Cursor = WpfCursors.Hand
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                _editingPipId = capturedPip.Id;
                _editingButtonId = null; _editingImageId = null; _editingSubtitleId = null;
                _selectPip?.Invoke(capturedPip);
                RefreshItems();
                e.Handled = true; _suppressItemSelect = true;
            };

            var cardGrid = new Grid();
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Border
            {
                Width = 28, Height = 24, CornerRadius = new CornerRadius(4),
                Background = new SolidBrush(WpfColor.FromRgb(40, 50, 70)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.Child = new TextBlock
            {
                Text = "\uD83C\uDFA5", FontSize = 11,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoStack.Children.Add(new TextBlock
            {
                Text = fileName,
                Foreground = TextLight,
                FontSize = 10, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Vol {pip.Volume}% {(pip.IsLooping ? "\u21BB" : "")}",
                Foreground = TextDim,
                FontSize = 9
            });
            Grid.SetColumn(infoStack, 1);

            var actions = new StackPanel
                { Orientation = System.Windows.Controls.Orientation.Horizontal,
                  VerticalAlignment = VerticalAlignment.Center };
            var editPipBtn = new System.Windows.Controls.Button
            {
                Content = "\u270E", Width = 24, Height = 22,
                Background = isPipEditing
                    ? CardBorderActive
                    : BtnBg,
                Foreground = WpfBrushes.White, BorderThickness = new Thickness(0),
                Cursor = WpfCursors.Hand, FontSize = 11,
                Margin = new Thickness(2, 0, 2, 0)
            };
            editPipBtn.Click += (s, e) =>
            {
                e.Handled = true; _suppressItemSelect = true;
                _editingPipId = capturedPip.Id;
                _editingButtonId = null; _editingImageId = null; _editingSubtitleId = null;
                _selectPip?.Invoke(capturedPip);
                RefreshItems();
            };
            actions.Children.Add(editPipBtn);
            actions.Children.Add(MakeSmallBtn("\u2715", () =>
            {
                var rmCmd = new Services.ListRemoveCommand<UMP.Core.Models.PipConfig>("Supprimer PiP", item.PictureInPictures, capturedPip,
                    onApply: () => { RefreshItems(); _refreshCanvas?.Invoke(); });
                rmCmd.Execute();
                UndoCommandPushed?.Invoke(rmCmd);
                SequenceChanged?.Invoke();
            }));
            Grid.SetColumn(actions, 2);

            cardGrid.Children.Add(icon);
            cardGrid.Children.Add(infoStack);
            cardGrid.Children.Add(actions);

            var pipContent = new StackPanel();
            pipContent.Children.Add(cardGrid);
            pipContent.Children.Add(BuildInOutRow(capturedPip.InMs, capturedPip.OutMs,
                v => capturedPip.InMs = v, v => capturedPip.OutMs = v));
            card.Child = pipContent;
            stack.Children.Add(card);
        }

        var addBtn = new System.Windows.Controls.Button
        {
            Content = "+ Picture in Picture", Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            Background = BtnBg,
            Foreground = AccentFg,
            BorderThickness = new Thickness(0), FontSize = 10,
            Cursor = WpfCursors.Hand,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        addBtn.Click += (s, e) =>
        {
            _suppressItemSelect = true;
            var d = new Microsoft.Win32.OpenFileDialog
            { Filter = "Video|*.mp4;*.mkv;*.avi;*.mov;*.wmv|Tous|*.*" };
            if (d.ShowDialog() != true) return;
            var newPip = new PipConfig { VideoPath = d.FileName };
            var addCmd = new Services.ListAddCommand<UMP.Core.Models.PipConfig>("Ajouter PiP", item.PictureInPictures, newPip,
                onApply: () => { RefreshItems(); _refreshCanvas?.Invoke(); });
            addCmd.Execute();
            UndoCommandPushed?.Invoke(addCmd);
            SequenceChanged?.Invoke();
        };
        stack.Children.Add(addBtn);
        return stack;
    }

    public void SetActiveIndex(int index)
    {
        _activeIndex = index;
        for (int i = 0; i < _itemCards.Count; i++)
        {
            _itemCards[i].BorderThickness = new Thickness(1);
            _itemCards[i].BorderBrush = i == index
                ? CardBorderActive
                : WpfBrushes.Transparent;
            _itemCards[i].Background = i == index ? CardBgActive : CardBg;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void ChkLoop_Changed(object sender, RoutedEventArgs e)
    {
        if (_sequence is null) return;
        _sequence.IsLooping = ChkLoop.IsChecked ?? false;
        SequenceChanged?.Invoke();
    }

    private void BtnAddVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_sequence is null) return;
        var item = new SequenceItem();
        _sequence.Items.Add(item);
        _buildingCard = true;
        ItemsPanel.Children.Add(BuildItemCard(item));
        _buildingCard = false;
    }

    private void BtnAddSlide_Click(object sender, RoutedEventArgs e)
    {
        if (_sequence is null) return;
        // Item image vide — l'utilisateur clique [F] pour choisir le fichier
        var item = new SequenceItem { DurationMs = 5000 };
        _sequence.Items.Add(item);
        _buildingCard = true;
        ItemsPanel.Children.Add(BuildItemCard(item));
        _buildingCard = false;
    }

    private static string FormatMs(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }

    private static long ParseMs(string text)
    {
        try
        {
            var parts = text.Split(':');
            if (parts.Length != 2) return 0;
            var min = int.Parse(parts[0]);
            var secParts = parts[1].Split('.');
            var sec = int.Parse(secParts[0]);
            var ms = secParts.Length > 1
                ? int.Parse(secParts[1].PadRight(3, '0').Substring(0, 3)) : 0;
            return (min * 60 + sec) * 1000L + ms;
        }
        catch { return 0; }
    }

    public void SetEditingButton(string? buttonId)
    {
        _editingButtonId = buttonId;
        _editingImageId = null;
        RefreshItems();
    }

    public void SetEditingImage(string? imageId)
    {
        _editingImageId = imageId;
        _editingButtonId = null;
        _editingSubtitleId = null;
        RefreshItems();
    }

    public void SetEditingPip(string? pipId)
    {
        _editingPipId = pipId;
        _editingButtonId = null;
        _editingImageId = null;
        _editingSubtitleId = null;
        RefreshItems();
    }

    public void SetEditingSubtitle(string? subtitleId)
    {
        _editingSubtitleId = subtitleId;
        _editingButtonId = null;
        _editingImageId = null;
        RefreshItems();
    }

    public void SetZoneCallbacks(
        Action<SequenceItem, ButtonConfig> openButtonProps,
        Action<ButtonConfig> selectButton,
        Action<ImageOverlayConfig> selectImage,
        Action refreshCanvas)
    {
        _openButtonProps = openButtonProps;
        _selectButton = selectButton;
        _selectImage = selectImage;
        _refreshCanvas = refreshCanvas;
    }
    public void SetSubtitleCallback(Action<SubtitleConfig> selectSubtitle)
    {
        _selectSubtitle = selectSubtitle;
    }

    public void SetPipCallback(Action<PipConfig> selectPip)
    {
        _selectPip = selectPip;
    }
}
