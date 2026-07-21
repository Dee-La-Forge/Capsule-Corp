using System.Windows;

namespace UMP.App;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) =>
        {
            System.Windows.MessageBox.Show(args.Exception.ToString(),
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            LibVLCSharp.Shared.Core.Initialize();

            var splash = new SplashWindow();
            await splash.RunAsync();

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.ToString(),
                "Erreur au demarrage", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
