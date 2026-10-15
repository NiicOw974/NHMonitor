using System.Security;

namespace NHMonitor;

/// <summary>
/// Démarrage automatique via le Planificateur de tâches : contrairement à la clé "Run" du registre,
/// une tâche "droits les plus élevés" démarre en administrateur sans invite UAC.
///
/// Sécurité :
/// - la tâche est enregistrée directement par l'API COM du Planificateur (pas de fichier XML
///   temporaire modifiable par un autre processus, pas d'appel à schtasks.exe résolu via le PATH) ;
/// - elle n'est autorisée que si l'exécutable est dans Program Files, dossier non modifiable sans
///   droits administrateur. Sinon, n'importe quel programme de l'utilisateur pourrait remplacer
///   NHMonitor.exe et obtenir les droits administrateur à l'ouverture de session suivante.
/// </summary>
public static class StartupManager
{
    private const string TaskName = "NHMonitor";
    private const int TASK_CREATE_OR_UPDATE = 6;
    private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsInstalledInProtectedLocation()
    {
        string dir = Path.GetFullPath(Path.GetDirectoryName(ExePath)!) + Path.DirectorySeparatorChar;
        return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            }
            .Where(root => !string.IsNullOrEmpty(root))
            .Any(root => dir.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Vrai si la tâche existe et lance bien cet exécutable-ci.</summary>
    public static bool IsEnabled()
    {
        try
        {
            dynamic task = RootFolder().GetTask(TaskName);
            string xml = task.Xml;
            return xml.Contains($"<Command>{SecurityElement.Escape(ExePath)}</Command>", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (TaskExists()) RootFolder().DeleteTask(TaskName, 0);
                return true;
            }

            if (!IsInstalledInProtectedLocation()) return false;
            RootFolder().RegisterTask(TaskName, BuildXml(), TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
            return IsEnabled();
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            return false;
        }
    }

    private static bool TaskExists()
    {
        try
        {
            RootFolder().GetTask(TaskName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static dynamic RootFolder()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service.GetFolder("\\");
    }

    private static string BuildXml()
    {
        string user = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        // Réglages par défaut du Planificateur inadaptés à un portable : arrêt sur batterie et limite de 72 h.
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>NHMonitor - moniteur CPU/GPU/RAM/réseau dans la barre des tâches</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT5S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(ExePath)}</Command></Exec>
              </Actions>
            </Task>
            """;
    }
}
