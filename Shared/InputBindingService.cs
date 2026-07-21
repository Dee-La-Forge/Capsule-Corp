using System.Windows.Threading;
using UMP.Core.Models;

namespace UMP.Shared;

public class InputBindingService : IDisposable
{
    private readonly List<PhysicalButtonConfig> _buttons;
    private readonly Action<ButtonAction> _executeAction;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _pressedBindings = new();
    private bool _disposed;

    /// <summary>Mode capture : quand actif, le prochain input est retourne via ce callback au lieu d'executer une action</summary>
    public Action<string>? OnBindingCaptured { get; set; }

    private readonly System.Windows.Input.Key[] _boundKeys;
    private readonly bool _hasJoystickBindings;

    public InputBindingService(List<PhysicalButtonConfig> buttons, Action<ButtonAction> executeAction)
    {
        _buttons = buttons;
        _executeAction = executeAction;
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += Poll;
        // Pre-calculer les touches bindees pour ne scanner que celles-la
        _boundKeys = buttons
            .Where(b => b.Binding.StartsWith("key:"))
            .Select(b => Enum.TryParse<System.Windows.Input.Key>(b.Binding[4..], out var k) ? k : System.Windows.Input.Key.None)
            .Where(k => k != System.Windows.Input.Key.None)
            .Distinct().ToArray();
        _hasJoystickBindings = buttons.Any(b => b.Binding.StartsWith("joy:"));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private static readonly System.Windows.Input.Key[] _allScanKeys = Enum.GetValues(typeof(System.Windows.Input.Key))
        .Cast<System.Windows.Input.Key>()
        .Where(k => k != System.Windows.Input.Key.None && (int)k > 0 && (int)k < 256)
        .ToArray();

    private void Poll(object? sender, EventArgs e)
    {
        try
        {
            // Scan joystick (seulement si des bindings joystick existent)
            if (_hasJoystickBindings || OnBindingCaptured is not null)
            {
                var joyHit = JoystickService.ScanAllButtons();
                if (joyHit.HasValue)
                {
                    HandleBinding($"joy:{joyHit.Value.joystickId}:{joyHit.Value.buttonIndex}");
                }
            }

            // Scan clavier via GetAsyncKeyState : fiable et insensible au focus/thread.
            if (OnBindingCaptured is not null)
            {
                // Mode capture : premiere touche enfoncee
                foreach (var key in _allScanKeys)
                {
                    if ((GetAsyncKeyState(System.Windows.Input.KeyInterop.VirtualKeyFromKey(key)) & 0x8000) != 0)
                    { HandleBinding($"key:{key}"); break; }
                }
            }
            else
            {
                foreach (var key in _boundKeys)
                {
                    var binding = $"key:{key}";
                    short state = GetAsyncKeyState(System.Windows.Input.KeyInterop.VirtualKeyFromKey(key));
                    bool down = (state & 0x8000) != 0;
                    // bit 0 = touche appuyee depuis la derniere scrutation (capture les appuis brefs entre 2 polls)
                    bool tappedSinceLastPoll = (state & 0x1) != 0;
                    if (down || tappedSinceLastPoll) HandleBinding(binding);
                    if (!down) _pressedBindings.Remove(binding); // relachee -> peut se redeclencher
                }
            }

            // Relacher les bindings joystick qui ne sont plus appuyes (le clavier est gere ci-dessus)
            _pressedBindings.RemoveWhere(b =>
            {
                try
                {
                    if (b.StartsWith("joy:"))
                    {
                        var parts = b.Split(':');
                        if (parts.Length == 3 && int.TryParse(parts[1], out var jId) && int.TryParse(parts[2], out var bIdx))
                            return (JoystickService.GetButtons(jId) & (1 << bIdx)) == 0;
                        return true;
                    }
                }
                catch { }
                return false;
            });
        }
        catch (Exception ex)
        {
            // Empecher le crash du service de polling (le logger deduplique le spam)
            UMP.Core.Log.Warn($"InputBindingService.Poll : {ex.Message}");
        }
    }

    private void HandleBinding(string binding)
    {
        // Eviter les repetitions tant que le bouton est maintenu
        if (_pressedBindings.Contains(binding)) return;
        _pressedBindings.Add(binding);

        // Mode capture (binding a la volee)
        if (OnBindingCaptured is not null)
        {
            OnBindingCaptured(binding);
            OnBindingCaptured = null;
            return;
        }

        // Chercher le PhysicalButtonConfig correspondant
        var config = _buttons.FirstOrDefault(b => b.Binding == binding);
        if (config is null) return;
        foreach (var action in config.Actions)
            _executeAction(action);
    }

    /// <summary>Formate un binding pour affichage</summary>
    public static string FormatBinding(string binding)
    {
        if (string.IsNullOrEmpty(binding)) return "(non assigne)";
        if (binding.StartsWith("key:")) return $"Clavier: {binding[4..]}";
        if (binding.StartsWith("joy:"))
        {
            var parts = binding.Split(':');
            if (parts.Length == 3) return $"Joystick {parts[1]}: Bouton {parts[2]}";
        }
        return binding;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
    }
}
