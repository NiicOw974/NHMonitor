## Installation

1. Téléchargez **`NHMonitor-Setup-x.y.z.exe`** ci-dessous et lancez-le.
2. Windows SmartScreen peut afficher « Windows a protégé votre ordinateur » : l'installeur n'est pas encore signé numériquement. Cliquez sur **Informations complémentaires**, puis **Exécuter quand même**.
3. Laissez cochée l'option **PawnIO** pour avoir les températures CPU.

Version portable (sans installation ni démarrage automatique) : `NHMonitor-x.y.z-portable-win-x64.zip`.

## Vérifier le téléchargement

Comparez l'empreinte du fichier avec celle de `SHA256SUMS.txt` :

```powershell
Get-FileHash .\NHMonitor-Setup-x.y.z.exe -Algorithm SHA256
```

Ces fichiers sont construits automatiquement par GitHub Actions à partir du code source de ce tag.
