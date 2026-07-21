using System.Windows;
using Application = System.Windows.Application;

namespace UMP.Player;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        UMP.Core.Log.AppName = "player";
        UMP.Core.Log.Info("=== Demarrage CapsuleMedia.Player ===");
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            UMP.Core.Log.Error("UnhandledException (domaine)", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            UMP.Core.Log.Error("UnobservedTaskException (Task en arriere-plan)", args.Exception);
            args.SetObserved();
        };
        // En representation : une erreur UI isolee ne doit pas faire tomber l'ecran.
        // On loggue et on continue (avant ce handler, le Player crashait sans trace).
        DispatcherUnhandledException += (s, args) =>
        {
            UMP.Core.Log.Error("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };
    }
}
