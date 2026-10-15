# Sécurité

## Signaler une vulnérabilité

Merci de **ne pas** ouvrir d'issue publique. Utilisez le signalement privé de GitHub : onglet **Security** > **Report a vulnerability**. Vous recevrez une réponse dès que possible.

## Ce que fait NHMonitor sur votre système

- **Connexions réseau.** Aucune télémétrie. NHMonitor fait une seule requête par jour vers `api.github.com`, pour savoir si une nouvelle version existe. Cette vérification se désactive dans le menu (« Vérifier automatiquement les mises à jour »). Les fichiers d'une mise à jour ne sont téléchargés que si vous l'acceptez. L'installeur se connecte uniquement si vous cochez PawnIO. Le débit réseau affiché est lu localement, via les compteurs Windows.
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
| Mise à jour piégée (compte GitHub compromis, fichier altéré) | `SHA256SUMS.txt` est signé (ECDSA P-256) par une clé privée qui n'est jamais dans le dépôt. Seule la clé publique est intégrée à l'application. L'installeur doit avoir l'empreinte signée, sous le nom attendu pour la version proposée (pas de rejeu d'un ancien installeur). Toute anomalie annule la mise à jour sans rien exécuter. |
| Installeur échangé entre la vérification et l'exécution | Téléchargement dans `Program Files\NHMonitor\updates`, modifiable uniquement par un administrateur. |
| Navigateur lancé en administrateur | La page de téléchargement est ouverte via l'Explorateur, donc sans les droits de NHMonitor. |
| Chaîne de compilation | Les releases sont construites par GitHub Actions à partir du code du tag, avec uniquement des actions officielles GitHub. Les empreintes SHA-256 sont publiées. Dependabot surveille les dépendances. |

## Mises à jour

1. NHMonitor interroge `https://api.github.com/repos/NiicOw974/NHMonitor/releases/latest`, au plus une fois par 24 h.
2. Si une version plus récente existe, une notification la propose ; rien n'est téléchargé sans votre accord.
3. Après accord : téléchargement de `SHA256SUMS.txt` et de sa signature, puis vérification de la signature, puis téléchargement de l'installeur et vérification de son empreinte. L'installeur ne s'exécute qu'ensuite.
4. L'installeur met à jour les fichiers et relance NHMonitor.

La version portable et les copies lancées sans droits administrateur ne s'installent pas elles-mêmes : elles ouvrent la page de téléchargement.

Clé publique de signature : [`assets/release-signing-public.pem`](assets/release-signing-public.pem). Pour vérifier une release à la main :

```
openssl dgst -sha256 -verify release-signing-public.pem -signature <(base64 -d SHA256SUMS.txt.sig) SHA256SUMS.txt
```

## SmartScreen

Les exécutables ne sont pas encore signés par un certificat de signature de code. Au premier lancement, Windows SmartScreen peut donc afficher un avertissement. Vérifiez l'empreinte SHA-256 du fichier téléchargé avec `SHA256SUMS.txt`, publié dans la release.
