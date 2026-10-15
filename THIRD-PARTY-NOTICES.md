# Composants tiers

NHMonitor est distribué sous licence MIT (voir `LICENSE`). Il embarque les composants suivants, non modifiés, sous leurs propres licences :

| Composant | Version | Licence | Source |
|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | https://github.com/Blacktempel/BlackSharp |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| HidSharp | 2.6.4 | Apache-2.0 | https://www.zer7.com/software/hidsharp |
| Mono.Posix.NETStandard | 1.0.0 | MIT | https://github.com/mono/mono |
| System.Management, System.IO.Ports et runtime .NET | 10.x | MIT | https://github.com/dotnet/runtime |

Le code source des composants sous MPL-2.0 est disponible aux adresses ci-dessus. Le texte complet des licences : https://www.mozilla.org/MPL/2.0/, https://www.apache.org/licenses/LICENSE-2.0, https://opensource.org/license/mit.

## Driver PawnIO (non inclus)

Les températures CPU s'appuient sur le driver signé [PawnIO](https://pawnio.eu/) (namazso). Il n'est **pas** redistribué avec NHMonitor : l'installeur propose de l'installer depuis sa source officielle, via winget (`namazso.PawnIO`).
