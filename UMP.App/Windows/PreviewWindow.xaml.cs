using System.Windows;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using UMP.Core.Models;
using UMP.Shared;

namespace UMP.App.Windows;

/// <summary>
/// Coquille fenetre de l'Apercu : positionnement sur l'ecran assigne, gestion
/// Escape, cycle de vie. Tout le rendu et l'enchainement vivent dans
/// ZonePlaybackView — le MEME moteur que le Player final.
/// </summary>
public partial class PreviewWindow : Window
{
    public static event Action? EscapePressed;

    private readonly ZonePlaybackView _playback;
    private System.Windows.Threading.DispatcherTimer? _escTimer;
    private bool _closed;

    public PreviewWindow(Zone zone)
    {
        InitializeComponent();

        // Positionner sur l'ecran assigne — DeviceName d'abord, fallback sur index
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

        // Moteur de lecture partage — chemins absolus en apercu (pas de resolution)
        _playback = new ZonePlaybackView(zone);
        Root.Children.Add(_playback);

        // Poll Escape car le HWND natif de la video capture le focus clavier
        _escTimer = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(100) };
        _escTimer.Tick += (s, e) =>
        {
            if ((System.Windows.Input.Keyboard.GetKeyStates(Key.Escape) & System.Windows.Input.KeyStates.Down) != 0)
                EscapePressed?.Invoke();
        };
        _escTimer.Start();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            EscapePressed?.Invoke();
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_closed) { base.OnClosed(e); return; }
        _closed = true;
        _escTimer?.Stop(); _escTimer = null;
        _playback.Shutdown();
        base.OnClosed(e);
    }
}
