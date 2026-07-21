using System.IO;
using System.Text.Json;
using System.Windows;
using UMP.Core.Models;
using UMP.Shared;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace UMP.Player;

public partial class MainWindow : Window
{
    private readonly List<PlayerWindow> _playerWindows = new();
    private InputBindingService? _inputService;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("Initialisation video...", 10);
            await Task.Yield();
            // Pre-charger LibVLC une seule fois (le plus gros cout)
            await Task.Run(() => { var _ = UMP.Modules.Media.MediaModule.SharedLibVLC; });
            SetStatus("Lecture du projet...", 30);

            var baseDir = AppContext.BaseDirectory;
            var projectPath = Path.Combine(baseDir, "project.json");

            if (!File.Exists(projectPath))
            {
                MessageBox.Show("project.json introuvable a cote de l'executable.",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            var json = await File.ReadAllTextAsync(projectPath);
            var project = JsonSerializer.Deserialize<Project>(json);

            if (project is null || project.Zones.Count == 0)
            {
                MessageBox.Show("Le projet ne contient aucune zone.",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                Application.Current.Shutdown();
                return;
            }

            SetStatus($"Initialisation de {project.Zones.Count} zone(s)...", 50);
            await Task.Yield();

            PlayerWindow.EscapePressed += CloseAllAndExit;

            for (int i = 0; i < project.Zones.Count; i++)
            {
                var zone = project.Zones[i];
                SetStatus($"Zone {i + 1}/{project.Zones.Count} : {zone.Name}", 50 + 50 * i / project.Zones.Count);
                var pw = new PlayerWindow(zone);
                pw.Closed += OnPlayerWindowClosed;
                _playerWindows.Add(pw);
                pw.Show();
                await Task.Yield();
            }

            // Boutons physiques
            if (project.PhysicalButtons.Count > 0)
            {
                _inputService = new InputBindingService(project.PhysicalButtons, ExecuteGlobalAction);
                _inputService.Start();
            }

            // Cacher le splash
            Hide();
        }
        catch (Exception ex)
        {
            UMP.Core.Log.Error("Player : erreur au chargement du projet", ex);
            MessageBox.Show($"Erreur au chargement : {ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    private void SetStatus(string text, int percent)
    {
        TxtStatus.Text = text;
        ProgressBar.Width = Math.Clamp(percent * 2, 0, 200);
    }

    private void OnPlayerWindowClosed(object? sender, EventArgs e)
    {
        if (sender is PlayerWindow pw)
            _playerWindows.Remove(pw);

        if (_playerWindows.Count == 0)
            Application.Current.Shutdown();
    }

    private void ExecuteGlobalAction(ButtonAction action)
    {
        switch (action.Type)
        {
            case ButtonActionType.StopAllScreens:
                PlayerWindow.InvokeStopAllScreens();
                break;
            case ButtonActionType.JumpToItemAllScreens:
                if (action.TargetItemIndex.HasValue)
                    PlayerWindow.InvokeJumpToItemAllScreens(
                        action.TargetItemIndex.Value,
                        action.EndItemIndex ?? action.TargetItemIndex.Value);
                break;
            case ButtonActionType.TogglePip:
            case ButtonActionType.ShowPip:
            case ButtonActionType.HidePip:
                // Le PiP peut se trouver sur n'importe quel ecran : diffuser a toutes les
                // fenetres (chacune agit sur ses propres PiP). Corrige le cas ou le PiP
                // n'est pas sur la premiere zone.
                foreach (var w in _playerWindows)
                    w.ExecuteActionPublic(action);
                break;
            default:
                // Actions per-zone : les exécuter sur la première PlayerWindow
                if (_playerWindows.Count > 0)
                    _playerWindows[0].ExecuteActionPublic(action);
                break;
        }
    }

    private void CloseAllAndExit()
    {
        _inputService?.Dispose(); _inputService = null;
        PlayerWindow.EscapePressed -= CloseAllAndExit;
        foreach (var pw in _playerWindows.ToList())
        {
            try { pw.Close(); } catch { }
        }
        _playerWindows.Clear();
        Application.Current.Shutdown();
    }
}
