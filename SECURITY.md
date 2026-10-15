# Sécurité

## Signaler une vulnérabilité

Merci de **ne pas** ouvrir d'issue publique. Utilisez le signalement privé de GitHub : onglet **Security** > **Report a vulnerability**. Vous recevrez une réponse dès que possible.

## Ce que fait NHMonitor sur votre système

- **Aucune connexion réseau.** NHMonitor ne contacte aucun serveur : ni télémétrie, ni mise à jour automatique. Le débit réseau est lu localement via les compteurs Windows. Seul l'installeur se connecte, et uniquement si vous cochez PawnIO, pour télécharger ce driver depuis GitHub.
- **Droits administrateur.** Ils sont nécessaires pour lire les températures du processeur, comme pour HWiNFO ou LibreHardwareMonitor. Sur un compte standard, l'application démarre quand même, sans les températures.
- **Driver.** NHMonitor utilise le driver signé [PawnIO](https://pawnio.eu/), qui n'exécute que des modules signés. Il **n'utilise pas** WinRing0, l'ancien driver vulnérable que Microsoft Defender signale.
- **Fichiers écrits.** Uniquement `%APPDATA%\NHMonitor\settings.json` (réglages) et `error.log`, limité à 1 Mo.
- **Démarrage automatique.** Une tâche planifiée « NHMonitor » à l'ouverture de session, supprimée à la désinstallation.

## Mesures de durcissement

L'application tourne en administrateur ; elle évite donc tout ce qu'un programme non privilégié pourrait détourner pour obtenir ces droits :

| Risque | Protection |
|---|---|
| Remplacement de l'exécutable lancé en admin au démarrage | Installation forcée dans Program Files (non modifiable sans droits admin). Le démarrage automatique est refusé si l'exécutable est ailleurs (version portable). |
| Fichier temporaire modifié avant usage | La tâche planifiée est créée par l'API COM du Planificateur, sans fichier intermédiaire. |
| Programme détourné via le PATH, « App Paths » ou les associations de fichiers (HKCU) | Les programmes système sont lancés par chemin complet (`System32`). Le rapport des capteurs s'affiche dans une fenêtre interne au lieu d'ouvrir un `.txt` avec le programme associé. |
| DLL piégée dans un dossier du PATH | `SetDefaultDllDirectories` limite le chargement des DLL au dossier de l'application et à `System32`. |
| Réglages modifiés à la main | Chaque valeur lue est validée et ramenée dans une plage sûre. |
| Installeur PawnIO altéré | Version figée, téléchargée depuis le dépôt officiel et vérifiée par empreinte SHA-256 avant exécution. |
| Chaîne de compilation | Les releases sont construites par GitHub Actions à partir du code du tag, avec uniquement des actions officielles GitHub. Les empreintes SHA-256 sont publiées. Dependabot surveille les dépendances. |

## SmartScreen

Les exécutables ne sont pas encore signés par un certificat de signature de code. Au premier lancement, Windows SmartScreen peut donc afficher un avertissement. Vérifiez l'empreinte SHA-256 du fichier téléchargé avec `SHA256SUMS.txt`, publié dans la release.
