using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NHMonitor;

internal static class Program
{
    private const long MaxLogBytes = 1024 * 1024;

    [STAThread]
    private static int Main(string[] args)
    {
        // Processus administrateur : on interdit le chargement de DLL depuis le PATH ou le dossier courant
        // (souvent modifiables par l'utilisateur). Seuls System32 et le dossier de l'application restent.
        SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);

        if (args.Length > 0)
            return RunCommand(args);

        using var mutex = new Mutex(true, @"Local\NHMonitor.SingleInstance", out bool isFirst);
        if (!isFirst) return 0;

        Application.ThreadException += (_, e) => Log(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
        return 0;
    }

    /// <summary>
    /// Commandes utilisées par l'installeur et pour le diagnostic :
    ///   --enable-startup / --disable-startup : démarrage automatique ;
    ///   --dump &lt;fichier&gt; : écrit le rapport des capteurs.
    /// </summary>
    private static int RunCommand(string[] args)
    {
        try
        {
            switch (args[0])
            {
                case "--enable-startup":
                    return StartupManager.SetEnabled(true) ? 0 : 1;
                case "--disable-startup":
                    return StartupManager.SetEnabled(false) ? 0 : 1;
                case "--dump" when args.Length >= 2:
                    using (var hw = new HardwareService())
                        File.WriteAllText(args[1], hw.GetReport());
                    return 0;
                default:
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Log(ex);
            return 1;
        }
    }

    /// <summary>
    /// Ouvre le Gestionnaire des tâches par son chemin complet : un nom seul serait résolu via
    /// "App Paths" de HKCU, modifiable par n'importe quel programme de l'utilisateur.
    /// </summary>
    public static void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskmgr.exe")) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    public static void Log(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NHMonitor");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "error.log");

            // Taille bornée : une erreur répétée à chaque mesure ne doit pas remplir le disque.
            var info = new FileInfo(file);
            if (info.Exists && info.Length > MaxLogBytes)
                File.Move(file, file + ".old", overwrite: true);

            File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // Rien à faire si même le log échoue.
        }
    }

    private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);
}
