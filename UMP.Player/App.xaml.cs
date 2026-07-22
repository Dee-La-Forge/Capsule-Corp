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

        // Meme initialisation explicite que l'editeur : localise libvlc\win-x64
        // a cote de l'exe. Sans cet appel, l'echec de chargement survient plus
        // tard, dans un contexte ou le message d'erreur est moins clair.
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
        }
        catch (Exception ex)
        {
            UMP.Core.Log.Error("Initialisation LibVLC impossible", ex);
            System.Windows.MessageBox.Show(
                $"Impossible de charger LibVLC (dossier libvlc\\win-x64 a cote de l'executable) :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
