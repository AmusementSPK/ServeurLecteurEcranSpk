# Serveur d’affichage TV — Amusement SPK

Serveur centralisé pour les télévisions d’affichage d’Amusement SPK.

Le projet gère :

- les flux vidéo HLS pour les téléviseurs Roku et VIDAA;
- la boucle vidéo côté serveur;
- la création dynamique de télévisions;
- le nom des télévisions;
- l’envoi de médias depuis n’importe quel ordinateur du réseau local;
- la conversion automatique des vidéos et images;
- la surveillance automatique de FFmpeg;
- le redémarrage automatique du serveur en cas de crash ou de blocage;
- l’installation clé en main des dépendances sur Windows.

> **Principe important :** les télévisions ne bouclent pas les fichiers elles-mêmes.  
> La boucle est générée en permanence par le serveur avec FFmpeg.

---

## Table des matières

1. [Architecture](#architecture)
2. [Installation clé en main](#installation-clé-en-main)
3. [Après l’installation](#après-linstallation)
4. [Interface d’administration](#interface-dadministration)
5. [Gestion des télévisions](#gestion-des-télévisions)
6. [Gestion des médias](#gestion-des-médias)
7. [Conversion automatique](#conversion-automatique)
8. [Application Roku](#application-roku)
9. [Application VIDAA](#application-vidaa)
10. [URLs et API](#urls-et-api)
11. [Fiabilité et watchdog](#fiabilité-et-watchdog)
12. [Logs et diagnostic](#logs-et-diagnostic)
13. [Mise à jour du serveur](#mise-à-jour-du-serveur)
14. [Mode manuel](#mode-manuel)
15. [Désinstallation du démarrage automatique](#désinstallation-du-démarrage-automatique)
16. [Structure des dossiers](#structure-des-dossiers)
17. [Fichiers ignorés par Git](#fichiers-ignorés-par-git)
18. [Dépannage](#dépannage)
19. [Recommandations pour le PC serveur](#recommandations-pour-le-pc-serveur)

---

# Architecture

Le fonctionnement normal est :

```text
Média envoyé depuis un PC
        │
        ▼
Serveur ASP.NET Core
        │
        ├── conversion automatique FFmpeg
        │
        ▼
MP4 normalisé
        │
        ▼
FFmpeg -stream_loop -1
        │
        ▼
HLS live
        │
        ├── Roku
        └── VIDAA
```

Exemple pour la TV 3 :

```text
Media/tv3/Menu Bar.mp4
        ↓
FFmpeg
        ↓
Hls/tv3/index.m3u8
        ↓
http://IP_DU_SERVEUR:8090/hls/tv3/index.m3u8
```

L’adresse HLS dépend uniquement du numéro de la télévision.

Changer le nom de la télévision ou remplacer son média ne change donc pas son URL.

---

# Installation clé en main

## Prérequis

Le wizard est prévu pour :

- Windows 10 ou plus récent;
- Windows 64 bits x64;
- accès administrateur;
- connexion Internet lors de la première installation;
- connexion au réseau local SPK.

Il n’est **pas nécessaire** d’installer manuellement :

- .NET;
- FFmpeg;
- WinGet.

## Installation

Télécharger ou cloner le repo complet, puis lancer :

```text
INSTALLER_SPK.bat
```

Le fichier demande automatiquement les droits administrateur et ouvre le wizard graphique.

Le wizard effectue automatiquement :

1. vérification de Windows et de l’architecture;
2. arrêt d’une ancienne instance SPK si nécessaire;
3. installation locale de .NET 10;
4. installation locale de FFmpeg;
5. vérification SHA-256 de FFmpeg;
6. vérification de la présence de l’encodeur `libx264`;
7. restauration des dépendances .NET;
8. compilation Release du serveur;
9. ouverture du port TCP 8090 sur le réseau local;
10. configuration optionnelle de la mise en veille;
11. installation du watchdog;
12. création de la tâche Windows sous `SYSTEM`;
13. démarrage du serveur;
14. test réel de `/health`;
15. affichage des adresses réseau disponibles.

Le wizard n’annonce **« Serveur SPK prêt »** que si le serveur répond correctement.

## Dépendances locales

Les dépendances sont installées dans le repo :

```text
.spk-tools/
├── dotnet/
└── ffmpeg/
```

Cela évite de dépendre du `PATH` Windows ou du compte utilisateur actuellement connecté.

Le serveur lancé sous `SYSTEM` utilise donc les mêmes exécutables que ceux installés par le wizard.

> **Ne pas déplacer le dossier du serveur après l’installation.**  
> Si le dossier doit être déplacé, relancer `INSTALLER_SPK.bat` après le déplacement.

---

# Après l’installation

Le serveur tourne automatiquement en arrière-plan.

Il n’est pas nécessaire :

- de laisser une fenêtre PowerShell ouverte;
- de laisser un utilisateur connecté;
- de lancer manuellement le serveur chaque matin.

La tâche Windows créée est :

```text
Amusement SPK - Serveur TV
```

Elle s’exécute sous :

```text
SYSTEM
```

Le serveur écoute sur :

```text
0.0.0.0:8090
```

Adresse locale au serveur :

```text
http://localhost:8090/
```

Depuis un autre ordinateur du réseau :

```text
http://IP_DU_SERVEUR:8090/
```

---

# Interface d’administration

L’interface principale est disponible à :

```text
http://IP_DU_SERVEUR:8090/
```

Elle permet de :

- voir toutes les télévisions;
- voir si chaque flux est actif;
- renommer une télévision;
- ajouter une nouvelle télévision;
- choisir un nouveau média;
- remplacer le média d’une télévision;
- suivre l’envoi du fichier;
- suivre la conversion;
- copier l’URL HLS d’une télévision.

Aucune installation n’est nécessaire sur l’ordinateur utilisé pour administrer le serveur.

Il suffit que cet ordinateur puisse joindre le serveur sur le réseau local.

---

# Gestion des télévisions

Chaque télévision possède :

- un **ID stable**;
- un **nom modifiable**;
- un média courant;
- une URL HLS stable.

Exemple :

```text
ID : 3
Nom : Menu bar
Flux : /hls/tv3/index.m3u8
```

Le nom peut changer sans modifier l’ID.

Exemple :

```text
Menu bar
→
Boissons
```

La télévision reste :

```text
TV 3
```

et continue d’utiliser :

```text
/hls/tv3/index.m3u8
```

## Ajouter une télévision

Depuis l’interface Web :

```text
+ Ajouter une télé
```

Le serveur attribue automatiquement le prochain ID libre.

Il n’existe plus de limite fixe à 8 télévisions.

## Configuration persistante

La configuration réelle des télévisions est enregistrée dans :

```text
Data/tvs.json
```

Ce fichier contient notamment :

- ID;
- nom;
- chemin du média.

Il est volontairement ignoré par Git afin qu’un `git pull` n’écrase jamais la configuration réelle du restaurant.

---

# Gestion des médias

Les médias peuvent être envoyés depuis **n’importe quel ordinateur du réseau local** via l’interface Web.

Procédure :

1. ouvrir le panneau Web;
2. trouver la TV;
3. cliquer **Choisir / changer le fichier**;
4. sélectionner le fichier;
5. cliquer **Envoyer et appliquer**;
6. attendre la fin de l’envoi et de la conversion.

L’ancien média continue de jouer pendant :

- l’upload;
- la conversion.

Le flux est interrompu seulement au moment du remplacement final.

Le serveur redémarre ensuite automatiquement le flux HLS de cette télévision.

---

# Conversion automatique

Tous les médias sont convertis vers un format commun avant diffusion.

## Vidéos acceptées

Le serveur reconnaît notamment :

```text
.mp4
.mov
.m4v
.mkv
.webm
.avi
.mpeg
.mpg
.wmv
.flv
.mts
.m2ts
.3gp
```

Les fichiers vidéo sont normalisés vers :

- H.264 / AVC;
- encodeur `libx264`;
- profil High;
- niveau 4.1;
- `yuv420p`;
- maximum 1920 × 1080;
- ratio conservé;
- 30 FPS constant;
- keyframe toutes les 60 images, soit environ 2 secondes;
- CRF 20;
- preset `veryfast`;
- audio AAC 160 kb/s;
- 48 kHz;
- stéréo;
- `faststart`.

Ce profil a été choisi pour obtenir un comportement beaucoup plus stable en HLS sur Roku et VIDAA.

## Images acceptées

Le serveur reconnaît notamment :

```text
.jpg
.jpeg
.png
.webp
.bmp
.gif
.tif
.tiff
.avif
.heic
.heif
.jfif
```

Une image est automatiquement transformée en :

```text
MP4 H.264 fixe de 10 secondes
```

Caractéristiques :

- première image uniquement;
- même si le GIF ou WebP est animé;
- image complètement fixe;
- 30 FPS;
- 1080p maximum;
- pas d’audio.

Le MP4 de 10 secondes est ensuite bouclé indéfiniment côté serveur.

## Conservation du nom

Le nom de base du fichier original est conservé.

Exemples :

```text
Menu Burger.png
→ Menu Burger.mp4

Promo Karting.webm
→ Promo Karting.mp4

Boissons septembre.mov
→ Boissons septembre.mp4
```

Les caractères invalides pour Windows sont retirés automatiquement.

Chaque TV possède son propre dossier :

```text
Media/
├── tv1/
│   └── Menu Burger.mp4
├── tv2/
│   └── Promo Karting.mp4
└── tv3/
    └── Boissons septembre.mp4
```

Deux télévisions peuvent donc avoir un média portant le même nom sans conflit.

---

# Application Roku

Le ZIP Roku actuel est versionné directement dans le repo :

```text
Roku/SPK_Roku_App_v1.6_DYNAMIC.zip
```

## Fonctionnement

L’application Roku ne contient pas une liste TV 1 à TV 8 codée en dur.

Elle récupère la liste dynamique depuis :

```text
/api/roku/tvs
```

Le serveur retourne :

- l’ID;
- le nom;
- l’URL HLS.

Lorsqu’une télévision est créée ou renommée côté serveur, elle apparaît avec son nouveau nom dans le sélecteur Roku.

L’ID reste stable.

## Configuration réseau Roku

Le ZIP contient la configuration du serveur Roku.

La configuration utilise :

```text
SERVER_IP
SERVER_PORT
```

Le port est normalement :

```text
8090
```

Il est recommandé de donner au PC serveur :

- une IP fixe;
- ou une réservation DHCP.

Si l’IP du serveur change, les clients qui utilisent directement cette IP devront être reconfigurés.

## Sideload Roku

Sur une Roku en mode développeur :

1. activer le mode développeur;
2. ouvrir l’adresse IP de la Roku depuis un navigateur;
3. se connecter avec le compte développeur Roku;
4. utiliser **Install / Replace**;
5. envoyer :

```text
SPK_Roku_App_v1.6_DYNAMIC.zip
```

L’application demande ensuite quelle télévision elle représente.

Cette sélection est mémorisée.

---

# Application VIDAA

L’application VIDAA est une Web App hébergée directement par le serveur.

Adresse :

```text
http://IP_DU_SERVEUR:8090/vidaa/
```

## Sélection de la télévision

La liste des télévisions provient du serveur.

Le mode principal prévu pour le téléviseur WELCOME / VIDAA utilisé au SPK est la **souris**.

Fonctionnement :

- déplacement de la souris;
- clic gauche sur une télévision pour la sélectionner;
- pendant la vidéo, clic gauche pour revenir à la sélection;
- déplacement de la souris vers le haut ou le bas de l’écran pour faire défiler automatiquement la liste.

Les touches Haut / Bas / OK restent présentes comme compatibilité supplémentaire.

## Mémorisation

La TV choisie est mémorisée localement dans le navigateur VIDAA.

Au lancement suivant, la Web App tente de reprendre directement cette télévision.

L’ID est mémorisé, pas le nom.

Renommer une télévision côté serveur ne casse donc pas son association.

## Lecture

La Web App :

- lit le HLS fourni par le serveur;
- ne boucle pas la vidéo elle-même;
- tente automatiquement une reconnexion si le flux est interrompu;
- utilise le même système de médias que Roku.

---

# URLs et API

Remplacer `IP_DU_SERVEUR` par l’adresse du PC serveur.

## Administration

```text
http://IP_DU_SERVEUR:8090/
```

## VIDAA

```text
http://IP_DU_SERVEUR:8090/vidaa/
```

## Santé du serveur

```text
http://IP_DU_SERVEUR:8090/health
```

Un serveur sain retourne notamment :

```json
{
  "status": "OK",
  "mode": "SERVER_SIDE_LOOP_HLS",
  "hlsSupervisorHealthy": true
}
```

## Liste complète des télévisions

```text
GET /api/tvs
```

## Liste Roku

```text
GET /api/roku/tvs
```

## Diagnostic d’une télévision

Exemple TV 1 :

```text
GET /tv/1
```

## Créer une télévision

```text
POST /api/tvs
```

JSON :

```json
{
  "name": "Menu burgers"
}
```

## Renommer une télévision

```text
PUT /api/tvs/{id}/name
```

Exemple :

```text
PUT /api/tvs/3/name
```

JSON :

```json
{
  "name": "Boissons"
}
```

## Envoyer un média

```text
POST /api/tvs/{id}/video
```

Requête multipart/form-data.

Champ principal :

```text
video
```

Taille maximale configurée :

```text
2 Go
```

## HLS

Exemple TV 1 :

```text
http://IP_DU_SERVEUR:8090/hls/tv1/index.m3u8
```

Exemple TV 8 :

```text
http://IP_DU_SERVEUR:8090/hls/tv8/index.m3u8
```

---

# Fiabilité et watchdog

Le système est conçu pour éviter qu’un problème logiciel arrête définitivement l’affichage.

## Niveau 1 — FFmpeg

Le gestionnaire HLS vérifie les flux régulièrement.

Si un processus FFmpeg s’arrête :

```text
FFmpeg arrêté
→ détecté
→ relancé automatiquement
```

La boucle de surveillance est exécutée environ toutes les 3 secondes.

Une erreur sur une télévision ne doit pas empêcher la surveillance des autres.

## Niveau 2 — ASP.NET Core

Le gestionnaire HLS possède un heartbeat interne.

Le endpoint :

```text
/health
```

retourne un état dégradé si le superviseur HLS n’a plus effectué de maintenance récemment.

Les exceptions d’un `BackgroundService` sont également configurées pour ne pas faire tomber immédiatement tout le serveur Web.

## Niveau 3 — Watchdog Windows

Le fichier :

```text
SPK_Server_Watchdog.ps1
```

surveille le serveur.

Il :

- utilise les dépendances locales dans `.spk-tools`;
- empêche plusieurs watchdogs simultanés avec un mutex global;
- vérifie `/health`;
- détecte un serveur figé;
- peut tuer une ancienne instance bloquée sur le port 8090;
- redémarre le serveur après un crash;
- conserve des logs;
- conserve le dernier build fonctionnel si une future compilation échoue.

Après la période de démarrage, le watchdog vérifie la santé environ toutes les 10 secondes.

Après 3 échecs consécutifs :

```text
serveur considéré bloqué
→ arrêt forcé
→ redémarrage
```

## Niveau 4 — Planificateur de tâches Windows

Le wizard crée :

```text
Amusement SPK - Serveur TV
```

avec :

- utilisateur : `SYSTEM`;
- privilèges élevés;
- démarrage au boot Windows;
- exécution même sans ouverture de session;
- relance configurée;
- instances multiples ignorées.

---

# Logs et diagnostic

Les logs sont enregistrés dans :

```text
Logs/
```

Principaux fichiers :

```text
Logs/watchdog.log
Logs/build.log
Logs/installation-YYYYMMDD-HHMMSS.log
Logs/server-YYYYMMDD-HHMMSS.out.log
Logs/server-YYYYMMDD-HHMMSS.err.log
```

Les anciens logs serveur sont nettoyés automatiquement après environ 30 jours.

## Diagnostic rapide

Lancer :

```text
6_ETAT_SERVEUR.bat
```

Ce script affiche :

- l’état de la tâche Windows;
- le résultat de `/health`;
- l’emplacement des logs.

Un état normal doit contenir :

```text
status = OK
hlsSupervisorHealthy = true
```

---

# Mise à jour du serveur

Procédure recommandée :

```bat
git pull
```

Puis relancer :

```text
INSTALLER_SPK.bat
```

Le wizard peut être relancé sur une installation existante.

Il ne doit pas supprimer :

- `Data/tvs.json`;
- les médias présents dans `Media/`.

Il vérifie et réinstalle au besoin :

- .NET;
- FFmpeg;
- build Release;
- pare-feu;
- watchdog;
- tâche Windows.

Cette méthode est recommandée après une modification importante du code afin d’appliquer immédiatement le nouveau build.

---

# Mode manuel

Le fichier :

```text
3_DEMARRER_SERVEUR.bat
```

est réservé aux :

- tests;
- diagnostics;
- développements;
- maintenance.

Il n’est **pas** la méthode normale d’exploitation.

Le mode manuel utilise en priorité :

```text
.spk-tools/dotnet/dotnet.exe
.spk-tools/ffmpeg/bin/ffmpeg.exe
```

puis tente les versions du `PATH` Windows seulement si les versions locales sont absentes.

Fermer la fenêtre du mode manuel arrête cette instance.

Pour le fonctionnement quotidien, utiliser l’installation permanente avec le watchdog.

---

# Désinstallation du démarrage automatique

Pour retirer la tâche Windows :

```text
5_DESINSTALLER_DEMARRAGE_AUTO.bat
```

Cela retire le démarrage automatique du serveur.

Cela ne supprime pas les médias ni la configuration des télévisions.

---

# Structure des dossiers

Structure principale :

```text
ServeurLecteurEcranSpk/
│
├── INSTALLER_SPK.bat
├── 3_DEMARRER_SERVEUR.bat
├── 5_DESINSTALLER_DEMARRAGE_AUTO.bat
├── 6_ETAT_SERVEUR.bat
├── SPK_Server_Watchdog.ps1
├── SPK.Streaming.csproj
├── Program.cs
├── appsettings.json
│
├── Installer/
│   ├── SPK_Server_Setup_Wizard.ps1
│   └── Install-SPKServer.ps1
│
├── Services/
│   ├── HlsProcessManager.cs
│   ├── VideoNormalizer.cs
│   └── ...
│
├── Models/
│
├── wwwroot/
│   ├── index.html
│   ├── app.js
│   ├── styles.css
│   └── vidaa/
│
├── Roku/
│   └── SPK_Roku_App_v1.6_DYNAMIC.zip
│
├── Data/
│   └── tvs.json
│
├── Media/
│   ├── tv1/
│   ├── tv2/
│   └── ...
│
├── Hls/
│   ├── tv1/
│   ├── tv2/
│   └── ...
│
├── Logs/
│
└── .spk-tools/
    ├── dotnet/
    ├── ffmpeg/
    └── downloads/
```

---

# Fichiers ignorés par Git

Git ne versionne volontairement pas les données générées ou spécifiques au serveur.

Actuellement ignorés :

```text
bin/
obj/
Hls/*
Data/tvs.json
Data/*.tmp
Media/*
Logs/
.spk-tools/
```

Les fichiers `.gitkeep` nécessaires peuvent rester versionnés dans certains dossiers.

Cette séparation permet de faire :

```bat
git pull
```

sans écraser :

- les vidéos du restaurant;
- les images;
- les noms des télévisions;
- la configuration locale;
- les logs;
- les dépendances installées localement.

---

# Dépannage

## Le panneau Web ne répond pas

Tester sur le serveur :

```text
http://localhost:8090/health
```

Puis lancer :

```text
6_ETAT_SERVEUR.bat
```

Consulter :

```text
Logs/watchdog.log
```

et le dernier :

```text
Logs/server-*.err.log
```

---

## Le serveur est « injoignable »

Vérifier :

- que Windows est démarré;
- que la tâche `Amusement SPK - Serveur TV` existe;
- que le PC n’est pas en veille;
- que le port 8090 n’est pas utilisé par un autre programme;
- que le pare-feu n’a pas été remplacé par une politique externe.

Relancer si nécessaire :

```text
INSTALLER_SPK.bat
```

---

## Une seule télévision ne diffuse plus

Ouvrir :

```text
http://IP_DU_SERVEUR:8090/health
```

Regarder l’entrée correspondante dans `streams`.

Vérifier :

- `hasVideo`;
- `running`;
- le fichier dans `Media/tvX/`;
- les logs FFmpeg.

Le gestionnaire HLS doit normalement tenter de relancer FFmpeg automatiquement.

---

## La vidéo est très saccadée

Ne pas copier directement un MP4 dans `Media/`.

Passer par l’interface Web afin que le serveur applique la normalisation H.264 / 30 FPS / keyframes régulières.

---

## Une image ne fonctionne pas

Envoyer l’image via l’interface.

Le serveur doit la convertir automatiquement en MP4 fixe de 10 secondes.

Si la conversion échoue, consulter :

```text
Logs/server-*.err.log
```

---

## FFmpeg est introuvable

Relancer :

```text
INSTALLER_SPK.bat
```

Le wizard installe normalement FFmpeg ici :

```text
.spk-tools/ffmpeg/bin/ffmpeg.exe
```

---

## .NET est introuvable

Relancer :

```text
INSTALLER_SPK.bat
```

Le wizard installe normalement .NET ici :

```text
.spk-tools/dotnet/dotnet.exe
```

---

## Le serveur fonctionne mais les TV ne le trouvent plus

Vérifier l’adresse IPv4 du serveur.

Si elle a changé, les Roku ou autres clients utilisant une IP fixe peuvent encore viser l’ancienne adresse.

Il est fortement recommandé de configurer une **réservation DHCP** ou une **IP fixe** pour le PC serveur.

---

## Le serveur s’arrête après un redémarrage Windows

Lancer :

```text
6_ETAT_SERVEUR.bat
```

Vérifier que la tâche existe.

Si elle n’existe plus ou est corrompue :

```text
INSTALLER_SPK.bat
```

---

# Recommandations pour le PC serveur

Pour obtenir le maximum de disponibilité :

## 1. Réservation DHCP ou IP fixe

Le PC serveur devrait toujours conserver la même IP.

Cela évite de reconfigurer les Roku et les favoris VIDAA.

## 2. Désactiver la mise en veille

Le wizard propose cette option et l’active par défaut pour l’alimentation secteur.

Un ordinateur en veille ne peut pas continuer à diffuser les flux.

## 3. BIOS — retour après panne électrique

Activer si disponible :

```text
Restore on AC Power Loss
Power On
AC Recovery
After Power Failure
```

Le nom varie selon le BIOS.

L’objectif est que le PC redémarre automatiquement lorsque le courant revient.

## 4. UPS recommandé

Pour une installation critique, utiliser un UPS afin d’éviter :

- les microcoupures;
- les arrêts brutaux;
- les fluctuations électriques.

## 5. Ne pas exposer le port 8090 à Internet

Le serveur est conçu pour le réseau local SPK.

Le port :

```text
8090
```

ne doit pas être redirigé sur le routeur vers Internet.

---

# Résumé exploitation quotidienne

Pour les employés :

```text
1. Ouvrir http://IP_DU_SERVEUR:8090/
2. Choisir la TV.
3. Choisir / changer le fichier.
4. Envoyer et appliquer.
5. Attendre « maintenant en diffusion ».
```

Pour vérifier le serveur :

```text
6_ETAT_SERVEUR.bat
```

Pour réparer ou réinstaller :

```text
INSTALLER_SPK.bat
```

Pour mettre à jour :

```bat
git pull
```

puis :

```text
INSTALLER_SPK.bat
```

---

## Projet

**Amusement SPK**  
Serveur d’affichage Roku / VIDAA pour les écrans du restaurant et les affichages internes.

Repo :

```text
AmusementSPK/ServeurLecteurEcranSpk
```
