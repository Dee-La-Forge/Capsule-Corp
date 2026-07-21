using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using UMP.Core.Models;
using UMP.Core.Services;
using UMP.Modules.Media;
using UMP.Shared;
using UMP.App.Controls;

namespace UMP.App;

public partial class MainWindow : Window
{
    private readonly List<(Zone Zone, MediaModule Module,
        ZoneControl Control, ZoneThumbnail Thumbnail)> _zones = new();
    private readonly ProjectService _projectService = new();
    private readonly Services.UndoRedoService _undoRedo = new();
    private ZoneControl? _activeControl;
    private ZoneThumbnail? _activeThumbnail;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && System.IO.File.Exists(args[1]) && args[1].EndsWith(".ump.json"))
                LoadProjectFile(args[1]);
        };
    }


    private void Pillule_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        var win = new Window
        {
            Title = "A propos",
            Width = 400, Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize
        };
        var border = new System.Windows.Controls.Border
        {
            Background = (System.Windows.Media.Brush)FindResource("BgDarkBrush"),
            CornerRadius = new CornerRadius(14),
            BorderBrush = (System.Windows.Media.Brush)FindResource("BgBorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(30)
        };
        var stack = new System.Windows.Controls.StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
        stack.Children.Add(new System.Windows.Controls.Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Pillule.png")),
            Width = 80, Height = 80, Stretch = System.Windows.Media.Stretch.Uniform,
            Margin = new Thickness(0, 0, 0, 16),
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = new System.Windows.Media.RotateTransform(-15)
        });
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "CapsuleMedia", FontSize = 26, FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.White,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        });
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Systeme de pilotage multimedia multi-ecrans",
            FontSize = 10, Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 16)
        });
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Version 1.00", FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Concu et developpe par", FontSize = 10,
            Foreground = (System.Windows.Media.Brush)FindResource("TextTertiaryBrush"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        });
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Meddy BOUKHEDDOUMA", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.White,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 8)
        });
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "\u00A9 2025 — Tous droits reserves", FontSize = 9,
            Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        });
        border.Child = stack;
        win.Content = border;
        win.MouseLeftButtonDown += (s, ev) => win.Close();
        win.ShowDialog();
    }

    private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var hit = e.OriginalSource as DependencyObject;
        while (hit is not null)
        {
            if (hit is System.Windows.Controls.Button || hit is System.Windows.Controls.Image)
                return;
            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        }
        if (e.ClickCount == 2)
            BtnMaxRestore_Click(sender, e);
        else if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void BtnMaxRestore_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;
        BtnMaxRestore.Content = WindowState == WindowState.Maximized ? "\u2750" : "\u25A1";
    }

    private void BtnCloseWindow_Click(object sender, RoutedEventArgs e)
        => Close();

    private void BtnAddZone_Click(object sender, RoutedEventArgs e)
    {
        SaveUndoState();
        var zone = new Zone { Name = $"Zone {_zones.Count + 1}" };
        var mm = new MediaModule();
        var ctrl = new ZoneControl();
        ctrl.Initialize(zone, mm);
        var thumb = new ZoneThumbnail();
        thumb.Initialize(zone);
        thumb.Selected += OnThumbnailSelected;

        WireZoneEvents(ctrl, thumb);

        _zones.Add((zone, mm, ctrl, thumb));
        ThumbnailStrip.Children.Add(thumb);
        RefreshZonesRuntime();
        SelectZone(thumb, ctrl);
    }

    private void BtnRemoveZone_Click(object sender, RoutedEventArgs e)
    {
        if (_activeThumbnail is null) return;
        if (System.Windows.MessageBox.Show("Supprimer cette zone ?", "Confirmation",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        SaveUndoState();
        var entry = _zones.FirstOrDefault(z => z.Thumbnail == _activeThumbnail);
        if (entry == default) return;

        entry.Thumbnail.Selected -= OnThumbnailSelected;
        entry.Control.Cleanup();
        ThumbnailStrip.Children.Remove(entry.Thumbnail);
        _zones.Remove(entry);
        RefreshZonesRuntime();

        if (_zones.Count > 0)
            SelectZone(_zones[^1].Thumbnail, _zones[^1].Control);
        else
            ClearActiveZone();
    }

    private void OnThumbnailSelected(ZoneThumbnail thumb)
    {
        var entry = _zones.FirstOrDefault(z => z.Thumbnail == thumb);
        if (entry == default) return;
        SelectZone(thumb, entry.Control);
    }

    private void SelectZone(ZoneThumbnail thumb, ZoneControl ctrl)
    {
        // Stopper toutes les autres zones
        foreach (var (_, _, otherCtrl, _) in _zones)
        {
            if (otherCtrl != ctrl)
                otherCtrl.StopPlayback();
        }

        _activeThumbnail?.SetSelected(false);
        _activeThumbnail = thumb;
        _activeControl = ctrl;
        _activeThumbnail.SetSelected(true);
        ActiveZonePresenter.Content = ctrl;
        PanelNoZone.Visibility = Visibility.Collapsed;

        // Lancer le premier item de la sequence
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() => ctrl.PlayFirstItem()));
    }

    private void ClearActiveZone()
    {
        _activeThumbnail?.SetSelected(false);
        _activeThumbnail = null;
        _activeControl = null;
        ActiveZonePresenter.Content = null;
        PanelNoZone.Visibility = Visibility.Visible;
    }

    private void RefreshZonesRuntime()
    {
        var runtime = _zones.ToDictionary(
            z => z.Zone.Id,
            z => new ZoneRuntime(z.Zone, z.Module));
        foreach (var (_, _, ctrl, _) in _zones)
        {
            ctrl.SetZonesRuntime(runtime);
            ctrl.UpdateRuntime(runtime[ctrl.GetZoneId()]);
        }
    }

    private string? _lastSavePath;
    private bool _hasUnsavedChanges;
    private string? _clipboardJson;
    private string? _clipboardType;

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
        if (ctrl && e.Key == System.Windows.Input.Key.S)
        { QuickSave(); e.Handled = true; }
        else if (ctrl && e.Key == System.Windows.Input.Key.Z)
        { _activeControl?.FinalizeAllPending(); PerformUndo(); e.Handled = true; }
        else if (ctrl && e.Key == System.Windows.Input.Key.Y)
        { PerformRedo(); e.Handled = true; }
        else if (ctrl && e.Key == System.Windows.Input.Key.N)
        { NewProject(); e.Handled = true; }
        else if (ctrl && e.Key == System.Windows.Input.Key.O)
        { BtnLoad_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == System.Windows.Input.Key.C)
        { CopySelected(); e.Handled = true; }
        else if (ctrl && e.Key == System.Windows.Input.Key.V)
        { PasteSelected(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Delete)
        { DeleteSelectedItem(); e.Handled = true; }
        else if (ctrl)
        {
            var k = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (k == System.Windows.Input.Key.Left || k == System.Windows.Input.Key.Right
                || k == System.Windows.Input.Key.Up || k == System.Windows.Input.Key.Down)
            { _activeControl?.MoveSelectedOverlay(k); e.Handled = true; }
        }
        else
        {
            // Touche non-fleche : finaliser le deplacement en cours
            FinalizeMove();
        }
    }

    private void FinalizeMove()
    {
        if (_activeControl is null) return;
        var cmd = _activeControl.FinalizeMoveCommand();
        if (cmd is not null)
            _undoRedo.Push(cmd);
    }

    private void NewProject()
    {
        if (_hasUnsavedChanges)
        {
            var r = System.Windows.MessageBox.Show("Sauvegarder avant de creer un nouveau projet ?",
                "Modifications non sauvegardees", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) QuickSave();
        }
        foreach (var (_, _, ctrl, _) in _zones) { ctrl.StopPlayback(); ctrl.Cleanup(); }
        _zones.Clear(); ThumbnailStrip.Children.Clear();
        ActiveZonePresenter.Content = null;
        _activeControl = null; _activeThumbnail = null;
        PanelNoZone.Visibility = Visibility.Visible;
        _projectService.CurrentProject = new UMP.Core.Models.Project();
        _undoRedo.Clear();
        _lastSavePath = null;
        _hasUnsavedChanges = false;
        Title = "CapsuleMedia";
    }

    private void CopySelected()
    {
        if (_activeControl is null) return;
        var (type, json) = _activeControl.CopySelectedOverlay();
        if (type is not null) { _clipboardType = type; _clipboardJson = json; }
    }

    private void PasteSelected()
    {
        if (_activeControl is null || _clipboardType is null || _clipboardJson is null) return;
        SaveUndoState();
        _activeControl.PasteOverlay(_clipboardType, _clipboardJson);
    }

    private void DeleteSelectedItem()
    {
        _activeControl?.DeleteSelectedOverlay();
    }

    public void SaveUndoState()
    {
        // Legacy — garde pour compatibilite avec BeforeModification
        MarkUnsaved();
    }

    public void PushUndoCommand(Services.IUndoCommand command)
    {
        _undoRedo.Push(command);
        MarkUnsaved();
    }

    /// <summary>
    /// Câblage commun des événements d'une zone. Utilisé à la fois pour les zones
    /// créées en session (BtnAddZone_Click) et les zones rechargées depuis un projet,
    /// afin d'éviter toute divergence (ex. undo non branché sur les zones ajoutées).
    /// </summary>
    private void WireZoneEvents(ZoneControl ctrl, ZoneThumbnail thumb)
    {
        // REGLE : VideoLoaded → BeginInvoke pour thumbnail.Refresh
        ctrl.VideoLoaded += () =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => thumb.Refresh()));
        ctrl.ScreenChanged += () => thumb.Refresh();
        ctrl.BeforeModification += SaveUndoState;
        // Push dans la pile d'undo ET marque le projet non sauvegardé.
        ctrl.UndoCommandPushed += PushUndoCommand;
    }

    private void MarkUnsaved()
    {
        if (_hasUnsavedChanges) return;
        _hasUnsavedChanges = true;
        if (!Title.EndsWith(" *")) Title += " *";
    }

    private void MarkSaved()
    {
        _hasUnsavedChanges = false;
        Title = Title.TrimEnd(' ', '*');
    }

    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        _activeControl?.FinalizeAllPending();
        PerformUndo();
    }
    private void BtnRedo_Click(object sender, RoutedEventArgs e) => PerformRedo();

    private void PerformUndo()
    {
        _undoRedo.Undo();
        _activeControl?.RefreshAfterUndo();
    }

    private void PerformRedo()
    {
        _undoRedo.Redo();
        _activeControl?.RefreshAfterUndo();
    }


    private void QuickSave()
    {
        try
        {
            if (string.IsNullOrEmpty(_lastSavePath))
            {
                var d = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Projet CapsuleMedia|*.ump.json",
                    DefaultExt = ".ump.json",
                    FileName = "projet"
                };
                if (d.ShowDialog(this) != true) return;
                _lastSavePath = d.FileName;
            }
            _projectService.CurrentProject.Zones = _zones.Select(z => z.Zone).ToList();
            _projectService.Save(_lastSavePath);
            Title = $"CapsuleMedia \u2014 {System.IO.Path.GetFileName(_lastSavePath)}";
            MarkSaved();
        }
        catch (Exception ex)
        {
            UMP.Core.Log.Error($"QuickSave echec ({_lastSavePath})", ex);
            System.Windows.MessageBox.Show($"Erreur de sauvegarde :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        // Finaliser les editions en cours (panneau de proprietes ouvert,
        // deplacement clavier non commite) AVANT de tester _hasUnsavedChanges,
        // sinon ces modifications sont perdues sans prompt.
        _activeControl?.FinalizeAllPending();
        if (_hasUnsavedChanges)
        {
            var r = System.Windows.MessageBox.Show("Sauvegarder avant de quitter ?",
                "Modifications non sauvegardees", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.Yes) QuickSave();
        }
        foreach (var (_, _, ctrl, _) in _zones)
            ctrl.Cleanup();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Projet CapsuleMedia|*.ump.json",
                DefaultExt = ".ump.json",
                FileName = !string.IsNullOrEmpty(_lastSavePath)
                    ? System.IO.Path.GetFileName(_lastSavePath) : "projet"
            };
            if (!string.IsNullOrEmpty(_lastSavePath))
                d.InitialDirectory = System.IO.Path.GetDirectoryName(_lastSavePath);
            if (d.ShowDialog(this) != true) return;
            _lastSavePath = d.FileName;
            _projectService.CurrentProject.Zones = _zones.Select(z => z.Zone).ToList();
            _projectService.Save(_lastSavePath);
            Title = $"CapsuleMedia \u2014 {System.IO.Path.GetFileName(_lastSavePath)}";
            MarkSaved();
        }
        catch (Exception ex)
        {
            UMP.Core.Log.Error($"Sauvegarde echec ({_lastSavePath})", ex);
            System.Windows.MessageBox.Show($"Erreur :\n{ex.Message}");
        }
    }

    private void LoadProjectFile(string filePath)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            await LoadProjectAsync(filePath);
        }));
    }

    private async void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog
            { Filter = "Projet|*.ump.json" };
        if (d.ShowDialog() != true) return;
        await LoadProjectAsync(d.FileName);
    }

    private async Task LoadProjectAsync(string filePath)
    {
        LoaderOverlay.Visibility = Visibility.Visible;
        LoaderText.Text = "Chargement du projet...";
        IsEnabled = false;
        try
        {
            foreach (var (_, _, ctrl, _) in _zones)
                ctrl.Cleanup();
            await Task.Delay(300);

            _zones.Clear();
            ThumbnailStrip.Children.Clear();
            ClearActiveZone();
            _projectService.Load(filePath);
            _lastSavePath = filePath;
            Title = $"CapsuleMedia — {System.IO.Path.GetFileName(filePath)}";
            MarkSaved();

            foreach (var zone in _projectService.CurrentProject.Zones)
            {
                var mm = new MediaModule();
                var ctrl = new ZoneControl();
                ctrl.SetAutoPlay(false);
                ctrl.Initialize(zone, mm);
                var thumb = new ZoneThumbnail();
                thumb.Initialize(zone);
                thumb.Refresh();
                thumb.Selected += OnThumbnailSelected;
                WireZoneEvents(ctrl, thumb);

                _zones.Add((zone, mm, ctrl, thumb));
                ThumbnailStrip.Children.Add(thumb);
            }

            RefreshZonesRuntime();
            if (_zones.Count > 0)
                SelectZone(_zones[0].Thumbnail, _zones[0].Control);

            await Task.Delay(600);
        }
        catch (Exception ex)
        {
            UMP.Core.Log.Error($"Chargement projet echec ({filePath})", ex);
            System.Windows.MessageBox.Show($"Erreur : {ex.Message}");
        }
        finally { IsEnabled = true; LoaderOverlay.Visibility = Visibility.Collapsed; }
    }

    private async void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (_zones.Count == 0)
        {
            System.Windows.MessageBox.Show("Aucune zone a exporter.", "Export",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using var fbd = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choisir le dossier d'export",
            UseDescriptionForTitle = true
        };
        if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        // Stopper toutes les zones avant l'export
        foreach (var (_, _, ctrl, _) in _zones)
            ctrl.StopPlayback();

        var outputDir = fbd.SelectedPath;
        var exportWin = new Windows.ExportWindow { Owner = this };
        exportWin.Show();

        await Task.Run(() => RunExport(outputDir, exportWin));
    }

    private void RunExport(string outputDir, Windows.ExportWindow ew)
    {
        try
        {
            UMP.Core.Log.Info($"Export demarre vers {outputDir}");
            var mediaDir = Path.Combine(outputDir, "media");
            Directory.CreateDirectory(mediaDir);

            ew.SetProgress(5, "Preparation du projet...");

            Project project = null!;
            Dispatcher.Invoke(() =>
            {
                _projectService.CurrentProject.Zones = _zones.Select(z => z.Zone).ToList();
                project = _projectService.CurrentProject;
            });

            // Collecter les fichiers
            ew.SetProgress(10, "Collecte des fichiers media...");
            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allPaths = new List<string?>();

            foreach (var zone in project.Zones)
            {
                allPaths.Add(zone.MediaFilePath);
                if (zone.Sequence is null) continue;
                foreach (var item in zone.Sequence.Items)
                {
                    allPaths.Add(item.MediaPath);
                    allPaths.Add(item.ImageSlidePath);
                    foreach (var btn in item.Buttons) { allPaths.Add(btn.ImagePath); allPaths.Add(btn.ImagePathOn); allPaths.Add(btn.CustomFontPath); }
                    foreach (var img in item.ImageOverlays) allPaths.Add(img.ImagePath);
                    foreach (var sub in item.Subtitles) { allPaths.Add(sub.FilePath); allPaths.Add(sub.CustomFontPath); }
                    foreach (var pip in item.PictureInPictures) allPaths.Add(pip.VideoPath);
                }
            }
            // Boutons physiques — collecter les MediaPath des actions
            foreach (var pb in project.PhysicalButtons)
                foreach (var a in pb.Actions)
                    allPaths.Add(a.MediaPath);

            // Fichiers references par le projet mais introuvables sur le disque :
            // impossibles a copier, leurs chemins resteront absolus dans project.json
            // → le Player ne les trouvera pas sur la machine cible. A signaler.
            var missingFiles = allPaths
                .Where(p => !string.IsNullOrEmpty(p) && !File.Exists(p))
                .Select(p => p!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var copyErrors = new List<string>();

            // Copie des fichiers
            var uniquePaths = allPaths.Where(p => !string.IsNullOrEmpty(p) && File.Exists(p)).Distinct().ToList();
            for (int i = 0; i < uniquePaths.Count; i++)
            {
                var path = uniquePaths[i]!;
                var pct = 15 + (int)(55.0 * i / Math.Max(1, uniquePaths.Count));
                var fileName = Path.GetFileName(path);
                ew.SetProgress(pct, $"Copie {i + 1}/{uniquePaths.Count}", fileName);

                var dest = Path.Combine(mediaDir, fileName);

                // Skip si le fichier destination est deja identique (meme taille + meme date)
                if (File.Exists(dest))
                {
                    var srcInfo = new FileInfo(path);
                    var dstInfo = new FileInfo(dest);
                    if (srcInfo.Length == dstInfo.Length
                        && srcInfo.LastWriteTimeUtc == dstInfo.LastWriteTimeUtc)
                    {
                        pathMap[path] = "media/" + fileName;
                        continue;
                    }
                }

                if (File.Exists(dest) && pathMap.Values.Any(v => v == "media/" + fileName))
                {
                    var name = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    int n = 1;
                    while (File.Exists(Path.Combine(mediaDir, $"{name}_{n}{ext}"))) n++;
                    fileName = $"{name}_{n}{ext}";
                    dest = Path.Combine(mediaDir, fileName);
                }
                // Une copie echouee ne doit PAS etre mappee : sinon project.json
                // pointe vers un fichier media/ inexistant sans que rien ne le signale.
                try { File.Copy(path, dest, true); }
                catch (Exception copyEx)
                {
                    copyErrors.Add($"{fileName} : {copyEx.Message}");
                    continue;
                }
                pathMap[path] = "media/" + fileName;
            }

            // Remapper les chemins
            ew.SetProgress(72, "Generation du project.json...");
            string Remap(string? p) => !string.IsNullOrEmpty(p) && pathMap.TryGetValue(p, out var rel) ? rel : p ?? "";

            var jsonOpts = new JsonSerializerOptions { WriteIndented = true };
            var cloneJson = JsonSerializer.Serialize(project, jsonOpts);
            var clone = JsonSerializer.Deserialize<Project>(cloneJson, jsonOpts)!;

            foreach (var zone in clone.Zones)
            {
                zone.MediaFilePath = Remap(zone.MediaFilePath);
                if (zone.Sequence is null) continue;
                foreach (var item in zone.Sequence.Items)
                {
                    item.MediaPath = Remap(item.MediaPath);
                    item.ImageSlidePath = Remap(item.ImageSlidePath);
                    foreach (var btn in item.Buttons) { btn.ImagePath = Remap(btn.ImagePath); btn.ImagePathOn = Remap(btn.ImagePathOn); btn.CustomFontPath = Remap(btn.CustomFontPath); }
                    foreach (var img in item.ImageOverlays) img.ImagePath = Remap(img.ImagePath);
                    foreach (var sub in item.Subtitles) { sub.FilePath = Remap(sub.FilePath); sub.CustomFontPath = Remap(sub.CustomFontPath); }
                    foreach (var pip in item.PictureInPictures) pip.VideoPath = Remap(pip.VideoPath);
                }
            }
            // Remapper les MediaPath des boutons physiques
            foreach (var pb in clone.PhysicalButtons)
                foreach (var a in pb.Actions)
                    a.MediaPath = Remap(a.MediaPath);

            File.WriteAllText(Path.Combine(outputDir, "project.json"), JsonSerializer.Serialize(clone, jsonOpts));

            // ===== Player : bundle pre-publie =====
            // Priorite 1 : dossier "player" livre a cote de CapsuleMedia.exe
            //   (produit par `dotnet publish UMP.App` — cible PublishPlayer du csproj).
            //   Une machine de production n'a ainsi besoin ni des sources ni du SDK .NET.
            // Priorite 2 : machine de developpement — cache UMP.Player/bin/publish,
            //   recompile via dotnet publish si les sources sont plus recentes.
            ew.SetProgress(75, "Recherche du Player...");

            var appBase = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var solRoot = appBase;
            for (int i = 0; i < 10; i++)
            {
                var parent = Path.GetDirectoryName(solRoot);
                if (parent is null) break;
                solRoot = parent;
                if (Directory.Exists(Path.Combine(solRoot, "UMP.Player"))) break;
            }

            string? playerBundle = null;
            var deployedBundle = Path.Combine(AppContext.BaseDirectory, "player");
            if (File.Exists(Path.Combine(deployedBundle, "CapsuleMedia.Player.exe")))
            {
                playerBundle = deployedBundle;
            }
            else
            {
                var playerProj = Path.Combine(solRoot, "UMP.Player");
                // Fallback : chercher dans le dossier du projet sauvegardé
                if (!Directory.Exists(playerProj) && !string.IsNullOrEmpty(_lastSavePath))
                {
                    var saveDir = Path.GetDirectoryName(_lastSavePath);
                    if (saveDir is not null)
                    {
                        var candidate = saveDir;
                        for (int i = 0; i < 5; i++)
                        {
                            if (Directory.Exists(Path.Combine(candidate, "UMP.Player")))
                            { playerProj = Path.Combine(candidate, "UMP.Player"); break; }
                            var p = Path.GetDirectoryName(candidate);
                            if (p is null) break;
                            candidate = p;
                        }
                    }
                }
                if (!Directory.Exists(playerProj))
                {
                    ew.Finish(false,
                        "Player introuvable :\n" +
                        "• pas de dossier 'player' a cote de CapsuleMedia.exe (installation deployee)\n" +
                        "• pas de sources UMP.Player (machine de developpement)\n\n" +
                        $"Les medias et project.json ont ete exportes dans :\n{outputDir}");
                    return;
                }

                // Verifier si un build existe deja et s'il est a jour
                var cachedDir = Path.Combine(playerProj, "bin", "publish");
                var cachedExe = Path.Combine(cachedDir, "CapsuleMedia.Player.exe");
                var needBuild = !File.Exists(cachedExe);
                if (!needBuild)
                {
                    // Rebuild si un fichier source est plus recent que l'exe
                    var exeDate = File.GetLastWriteTimeUtc(cachedExe);
                    var srcDirs = new[] { "UMP.Player", "UMP.Core", "UMP.Modules.Media", "Shared" };
                    foreach (var dir in srcDirs)
                    {
                        var dirPath = Path.Combine(solRoot, dir);
                        if (!Directory.Exists(dirPath)) continue;
                        foreach (var src in Directory.EnumerateFiles(dirPath, "*.cs", SearchOption.AllDirectories))
                            if (File.GetLastWriteTimeUtc(src) > exeDate) { needBuild = true; break; }
                        if (needBuild) break;
                        foreach (var src in Directory.EnumerateFiles(dirPath, "*.xaml", SearchOption.AllDirectories))
                            if (File.GetLastWriteTimeUtc(src) > exeDate) { needBuild = true; break; }
                        if (needBuild) break;
                    }
                }

                if (needBuild)
                {
                    ew.SetProgress(78, "Compilation du Player...", "Premier build, cela peut prendre 1-2 minutes...");
                    var psi = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"publish \"{playerProj}\" -c Release -o \"{cachedDir}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = solRoot
                    };
                    var process = Process.Start(psi);
                    if (process is not null)
                    {
                        process.WaitForExit();
                        if (process.ExitCode != 0)
                        {
                            var stderr = process.StandardError.ReadToEnd();
                            ew.Finish(false, $"Erreur de build (code {process.ExitCode}).\n{stderr}");
                            return;
                        }
                    }
                }
                playerBundle = cachedDir;
            }

            // Copier le bundle Player complet (exe + libvlc + ffmpeg)
            ew.SetProgress(88, "Copie du Player...");
            if (!File.Exists(Path.Combine(playerBundle, "CapsuleMedia.Player.exe")))
            {
                ew.Finish(false, "Executable Player introuvable apres le build.");
                return;
            }
            CopyDirectory(playerBundle, outputDir);

            // Filets de securite : si le bundle ne contenait pas libvlc/ffmpeg,
            // les recuperer depuis l'installation de l'App (son output les contient).
            ew.SetProgress(92, "Verification LibVLC / FFmpeg...");
            if (!Directory.Exists(Path.Combine(outputDir, "libvlc")))
            {
                var appLibvlc = Path.Combine(AppContext.BaseDirectory, "libvlc");
                if (Directory.Exists(appLibvlc))
                    CopyDirectory(appLibvlc, Path.Combine(outputDir, "libvlc"));
            }
            if (!Directory.Exists(Path.Combine(outputDir, "ffmpeg")))
            {
                var ffmpegSrc = Directory.Exists(Path.Combine(solRoot, "ffmpeg"))
                    ? Path.Combine(solRoot, "ffmpeg")
                    : Path.Combine(AppContext.BaseDirectory, "ffmpeg");
                if (Directory.Exists(ffmpegSrc))
                    CopyDirectory(ffmpegSrc, Path.Combine(outputDir, "ffmpeg"));
            }

            ew.SetProgress(95, "Nettoyage...");
            // Supprimer les fichiers inutiles (pdb, xml doc, etc.)
            foreach (var pdb in Directory.GetFiles(outputDir, "*.pdb", SearchOption.TopDirectoryOnly))
                try { File.Delete(pdb); } catch { }
            foreach (var xml in Directory.GetFiles(outputDir, "*.xml", SearchOption.TopDirectoryOnly))
                try { File.Delete(xml); } catch { }
            foreach (var deps in Directory.GetFiles(outputDir, "*.deps.json", SearchOption.TopDirectoryOnly))
                try { File.Delete(deps); } catch { }
            foreach (var runtimeConfig in Directory.GetFiles(outputDir, "*.runtimeconfig.json", SearchOption.TopDirectoryOnly))
                try { File.Delete(runtimeConfig); } catch { }
            // Le Player est win-x64 : la variante win-x86 de LibVLC (~100 Mo) est inutile
            var x86Dir = Path.Combine(outputDir, "libvlc", "win-x86");
            if (Directory.Exists(x86Dir))
                try { Directory.Delete(x86Dir, true); } catch { }
            // Supprimer les plugins LibVLC inutiles pour reduire la taille
            var unusedPlugins = new[] { "access_output", "lua", "meta_engine",
                "mux", "services_discovery", "spu", "stream_out", "video_filter",
                "video_splitter", "visualization", "stream_extractor", "d3d9" };
            // Supprimer les plugins d'acces reseau inutiles (garder filesystem + imem)
            var accessDir = Path.Combine(outputDir, "libvlc", "win-x64", "plugins", "access");
            if (Directory.Exists(accessDir))
            {
                var keepAccess = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "libfilesystem_plugin.dll", "libimem_plugin.dll", "libaccess_imem_plugin.dll",
                      "libidummy_plugin.dll", "libattachment_plugin.dll", "libtimecode_plugin.dll" };
                foreach (var f in Directory.GetFiles(accessDir))
                    if (!keepAccess.Contains(Path.GetFileName(f)))
                        try { File.Delete(f); } catch { }
            }
            foreach (var plugin in unusedPlugins)
            {
                var pluginDir = Path.Combine(outputDir, "libvlc", "win-x64", "plugins", plugin);
                if (Directory.Exists(pluginDir))
                    try { Directory.Delete(pluginDir, true); } catch { }
            }

            // Compter la taille totale
            var totalSize = new DirectoryInfo(outputDir).EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length) / (1024.0 * 1024.0);

            // Rapport final : ne jamais annoncer un succes si des medias manquent
            // ou si des copies ont echoue — la panne se decouvrirait sur site.
            var problems = new List<string>();
            if (missingFiles.Count > 0)
            {
                problems.Add($"{missingFiles.Count} media(s) INTROUVABLE(S) :");
                problems.AddRange(missingFiles.Take(8).Select(p => "  • " + p));
                if (missingFiles.Count > 8) problems.Add($"  ... et {missingFiles.Count - 8} autre(s)");
            }
            if (copyErrors.Count > 0)
            {
                problems.Add($"{copyErrors.Count} copie(s) echouee(s) :");
                problems.AddRange(copyErrors.Take(8).Select(p => "  • " + p));
                if (copyErrors.Count > 8) problems.Add($"  ... et {copyErrors.Count - 8} autre(s)");
            }

            var summary =
                $"{uniquePaths.Count - copyErrors.Count} fichier(s) media copies\n" +
                $"Taille totale : {totalSize:F1} Mo\n" +
                $"Dossier : {outputDir}\n\n" +
                $"Lancez CapsuleMedia.Player.exe pour lire le projet.";

            if (problems.Count > 0)
            {
                UMP.Core.Log.Warn($"Export INCOMPLET vers {outputDir} :\n" + string.Join("\n", problems));
                ew.Finish(false,
                    "EXPORT INCOMPLET — le projet risque de ne pas fonctionner sur la machine cible.\n\n"
                    + string.Join("\n", problems) + "\n\n" + summary);
            }
            else
            {
                UMP.Core.Log.Info($"Export OK vers {outputDir} ({uniquePaths.Count} media(s), {totalSize:F1} Mo)");
                ew.Finish(true, summary);
            }
        }
        catch (Exception ex)
        {
            UMP.Core.Log.Error($"Export echec vers {outputDir}", ex);
            ew.Finish(false, $"Erreur : {ex.Message}");
        }
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(src))
        {
            var destFile = Path.Combine(dest, Path.GetFileName(file));
            try { File.Copy(file, destFile, true); } catch { }
        }
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    private void BtnPhysicalButtons_Click(object sender, RoutedEventArgs e)
    {
        if (PhysicalBtnsPanel.Visibility == Visibility.Visible)
        {
            PhysicalBtnsPanel.Visibility = Visibility.Collapsed;
            ColPhysicalBtns.Width = new GridLength(0);
            return;
        }
        PhysicalBtnsPanel.CloseRequested -= HidePhysicalBtnsPanel;
        PhysicalBtnsPanel.Changed -= MarkUnsaved;
        PhysicalBtnsPanel.Load(_projectService.CurrentProject.PhysicalButtons, _zones.Select(z => z.Zone).ToList());
        PhysicalBtnsPanel.CloseRequested += HidePhysicalBtnsPanel;
        // Toute modification des boutons physiques (binding, actions, ajout/suppression)
        // doit marquer le projet non sauvegarde — cet event n'etait abonne nulle part.
        PhysicalBtnsPanel.Changed += MarkUnsaved;
        PhysicalBtnsPanel.Visibility = Visibility.Visible;
        ColPhysicalBtns.Width = new GridLength(400);
    }

    private void HidePhysicalBtnsPanel()
    {
        PhysicalBtnsPanel.Visibility = Visibility.Collapsed;
        ColPhysicalBtns.Width = new GridLength(0);
    }

    private readonly List<Windows.PreviewWindow> _previewWindows = new();
    private InputBindingService? _previewInputService;

    private void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_zones.Count == 0) return;

        // Stopper toutes les zones dans l'editeur
        foreach (var (_, _, ctrl, _) in _zones)
            ctrl.StopPlayback();

        // Ouvrir une fenetre preview par zone sur son ecran
        foreach (var (zone, _, _, _) in _zones)
        {
            var pw = new Windows.PreviewWindow(zone);
            pw.Closed += (s2, e2) =>
            {
                _previewWindows.Remove(pw);
                // Si c'est la derniere fenetre, redonner le focus a l'editeur
                if (_previewWindows.Count == 0)
                    Dispatcher.BeginInvoke(new Action(() => Activate()));
            };
            _previewWindows.Add(pw);
            pw.Show();
        }

        Windows.PreviewWindow.EscapePressed += CloseAllPreviews;

        // Boutons physiques en preview
        if (_projectService.CurrentProject.PhysicalButtons.Count > 0)
        {
            _previewInputService = new InputBindingService(
                _projectService.CurrentProject.PhysicalButtons,
                ExecutePreviewAction);
            _previewInputService.Start();
        }
    }

    private void ExecutePreviewAction(ButtonAction action)
    {
        switch (action.Type)
        {
            case ButtonActionType.StopAllScreens:
                Windows.PreviewWindow.InvokeStopAllScreens();
                break;
            case ButtonActionType.JumpToItemAllScreens:
                if (action.TargetItemIndex.HasValue)
                    Windows.PreviewWindow.InvokeJumpToItemAllScreens(
                        action.TargetItemIndex.Value,
                        action.EndItemIndex ?? action.TargetItemIndex.Value);
                break;
            case ButtonActionType.TogglePip:
            case ButtonActionType.ShowPip:
            case ButtonActionType.HidePip:
                // Le PiP peut se trouver sur n'importe quel ecran : diffuser a toutes les
                // fenetres d'apercu (chacune agit sur ses propres PiP).
                foreach (var w in _previewWindows)
                    w.ExecuteActionPublic(action);
                break;
            default:
                // Actions per-zone : les executer sur la premiere PreviewWindow
                if (_previewWindows.Count > 0)
                    _previewWindows[0].ExecuteActionPublic(action);
                break;
        }
    }

    private bool _closingPreviews;

    private void CloseAllPreviews()
    {
        if (_closingPreviews) return;
        _closingPreviews = true;
        _previewInputService?.Dispose(); _previewInputService = null;
        Windows.PreviewWindow.EscapePressed -= CloseAllPreviews;
        foreach (var pw in _previewWindows.ToList())
        {
            try { pw.Close(); } catch { }
        }
        _previewWindows.Clear();
        _closingPreviews = false;
        Dispatcher.BeginInvoke(new Action(() => Activate()));
    }
}
