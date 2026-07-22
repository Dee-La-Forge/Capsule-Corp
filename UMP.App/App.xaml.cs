using System.Windows;

namespace UMP.App;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        UMP.Core.Log.AppName = "editor";
        UMP.Core.Log.Info("=== Demarrage CapsuleMedia (editeur) ===");
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            UMP.Core.Log.Error("UnhandledException (domaine)", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            UMP.Core.Log.Error("UnobservedTaskException (Task en arriere-plan)", args.Exception);
            args.SetObserved();
        };
        DispatcherUnhandledException += (s, args) =>
        {
            UMP.Core.Log.Error("DispatcherUnhandledException", args.Exception);
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
            UMP.Core.Log.Error("Erreur au demarrage", ex);
            System.Windows.MessageBox.Show(ex.ToString(),
                "Erreur au demarrage", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
