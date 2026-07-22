using System.Windows;
using System.Windows.Media.Animation;

namespace UMP.App;

public partial class SplashWindow : Window
{
    public SplashWindow() { InitializeComponent(); }

    public async Task RunAsync()
    {
        Show();
        await Task.Delay(100);

        var steps = new[]
        {
            (10, "Initialisation LibVLC..."),
            (30, "Chargement des modules..."),
            (60, "Preparation de l'interface..."),
            (85, "Verification des ressources..."),
            (100, "Pret !")
        };

        foreach (var (pct, msg) in steps)
        {
            TxtStatus.Text = msg;
            var targetWidth = Width * pct / 100.0;
            var anim = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressFill.BeginAnimation(WidthProperty, anim);
            await Task.Delay(400);
        }

        await Task.Delay(300);
        Close();
    }
}
