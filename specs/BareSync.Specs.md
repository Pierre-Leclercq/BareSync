# BareSync — Spécification du menu (Interactive vs Batch)

Statut : **spécification normative (SPEC ONLY)** — proposition de contrat fonctionnel avant implémentation.

## Iteration 10 changes (non-normative)

- Clarified Batch preflight/run: confirmation is requested only when at least one step requires it.
- Clarified Interactive flow order when both are required: confirmation first, then secret, then execution.

## Iteration 11 changes (non-normative)

- Added command-line extract entrypoint `/EXTRACT:<path>` (file or folder source auto-detection).
- Added explicit CLI argument exclusivity rule between `/BATCH` and `/EXTRACT`.
- Added secret-gating rule for extraction: destination choices are shown only after secret validation.
- Added extraction UX defaults aligned with archive tools:
  - folder source: recursive extraction by default, into a default sub-folder,
  - file source: default "Extract to" sub-folder with alternatives (current folder or custom path).

## 0) Terminologie normative

Dans ce document :
- **DOIT** / **NE DOIT PAS** exprime une exigence obligatoire.
- **PEUT** exprime une capacite optionnelle explicitement permise (sans obligation).

## 1) Objectifs

- Réduire le **menu principal** à 3 entrées : `Interactive mode`, `Batch mode`, `Exit`.
- Conserver les **fonctionnalités existantes** accessibles en `Interactive mode` (via sous-menus), sans changer leur sémantique métier.
- Introduire `Batch mode` pour :
  - Construire une **liste ordonnée** d’opérations.
  - **Paramétrer** chaque opération.
  - **Sauvegarder** plusieurs batchs persistants.
  - **Lister**, sélectionner et **exécuter** un batch.
- Garantir une UX claire, une exécution déterministe “au sens BareSync” (mêmes entrées → mêmes décisions), et une base testable.

## 2) Portée fonctionnelle

Cette spécification couvre :

- La structure du **menu principal** et des sous-menus.
- La séparation des responsabilités entre `Interactive mode` et `Batch mode`.
- La modélisation conceptuelle des batchs (définitions, étapes, paramètres, persistance).
- Les flux UX et les états/transitions de navigation.
- Les invariants, contraintes et critères de succès.

## 3) Non-objectifs (explicites)

- Ne décrit **aucune implémentation** (pas de code, pas de choix de framework UI/TUI, pas de format de sérialisation figé).
- Ne redéfinit pas les algorithmes métiers (CRC, index, sync, chiffrement) : ils sont **réutilisés tels quels**.
- Ne couvre pas :
  - La suppression de batch, la duplication de batch, les templates de batch.
  - L’orchestration CLI générique multi-opérations (scripting avancé) ; les switches ciblés `/BATCH` et `/EXTRACT` sont couverts, mais pas un moteur CLI complet de composition/scénarios.
  - La planification (scheduler), l’exécution parallèle, ou le “retry policy” avancé.
  - La gestion multi-profil / multi-utilisateur.

## 4) Concepts clés & terminologie

- **Opération** : action atomique existante ou équivalente (ex : refresh indexes, sync, encrypted folder, restore).
- **Interactive mode** : mode “opérateur” orienté exécution immédiate, basé sur l’état courant unique de l’utilisateur.
- **Batch mode** : mode “orchestrateur” permettant de composer, stocker et exécuter des séquences d’opérations.
- **Batch** : définition persistante d’un scénario exécutable, composée d’étapes ordonnées.
- **Étape (step)** : occurrence d’une opération dans un batch, avec ses paramètres propres (et éventuellement des valeurs par défaut de batch).
- **Contexte d’exécution** : ensemble de paramètres nécessaires à l’exécution (chemins, options, etc.).
- **Secret** : information sensible (ex : mot de passe) jamais persistée dans un batch.

### 4.1) Principes directeurs

- **Clarté UX avant densité fonctionnelle** : le niveau 0 ne doit pas refléter toute la richesse des opérations.
- **Réutilisation du métier existant** : la refonte porte sur l’orchestration/menu, pas sur les algorithmes.
- **Paramètres explicites** : pas d’état implicite requis pour reproduire un batch.
- **Sécurité par défaut** : confirmations explicites pour actions à risque ; secrets jamais persistés.

## 5) Menu cible (niveau 0)

Le menu principal doit présenter exactement :

1. Interactive mode
2. Batch mode
0. Exit

Rationnel :
- Réduction de la charge cognitive.
- Clarification : “agir maintenant” vs “préparer/exécuter un scénario”.
- Préparation à l’automatisation (batchs persistants) sans figer de choix techniques.

## 6) Modélisation conceptuelle (sans code)

### 6.1) État utilisateur (Interactive)

- Il existe un **état courant unique** de l’utilisateur pour une exécution donnée (appelé ici *Interactive Context*).
- Cet état inclut au minimum : la configuration persistante existante (chemins source/destination, chemins d’index, etc.).
- Les actions réalisées en `Interactive mode` opèrent sur cet état courant et conservent les validations existantes.

### 6.2) Bibliothèque de batchs

- Une **bibliothèque de batchs** (Batch Library) est un ensemble persistant de batchs.
- Les batchs sont :
  - Persistants.
  - Sérialisables (format non figé par cette spec).
  - Indépendants les uns des autres (édition/exécution de A n’altère pas B).

#### 6.2.1) Emplacement de persistance (normatif)

Définitions :
- **AppDataRoot** : répertoire contenant la configuration persistante de BareSync (fichier `appsettings.json`).
- **BatchStoreRoot** : répertoire persistant dédié aux batchs, situé à `AppDataRoot/batches`.

Règles normatives :
- `BatchStoreRoot` est un **sous-répertoire** de `AppDataRoot` et son nom est **`batches`**.
- L’emplacement de `BatchStoreRoot` n’est **pas configurable** par l’utilisateur.
- L’emplacement doit être **prévisible** et **documenté** (pour sauvegarde et audit).
- Le stockage des batchs est **distinct** de la configuration interactive : aucune définition de batch ne doit être stockée dans le même fichier que la configuration interactive.

#### 6.2.2) Structure de persistance (conceptuelle)

Règles normatives :
- Chaque batch est persisté comme une **unité indépendante** (afin d’isoler la corruption/incompatibilité d’un batch).
- La bibliothèque est l’agrégat de ces unités ; toute donnée dérivée (ex : index de listing) est optionnelle et DOIT être **reconstructible** à partir des unités.
- Si une unité de batch est invalide ou incompatible :
  - Elle est signalée comme telle.
  - Elle ne doit pas empêcher l’accès, la liste, l’édition ou l’exécution des autres batchs.

### 6.3) Batch (définition)

Un batch est défini par :

- **Identité**
  - Identifiant stable (opaque, non réutilisé).
  - Nom affiché (lisible, modifiable).
- **Métadonnées**
  - Description libre (optionnelle).
  - Version de schéma (pour compatibilité future).
- **Contexte d’exécution (batch-level)**
  - Valeurs par défaut utilisées par les étapes si elles ne redéfinissent pas un champ.
  - Doit contenir toutes les informations nécessaires pour exécuter le batch sans dépendre d’un autre batch **ni de l’état interactif courant**.
- **Liste ordonnée d’étapes**
  - Ordre explicite.
  - Chaque étape référence un type d’opération et ses paramètres.

#### 6.3.1) Identité & nommage (règles normatives)

- **BatchId** :
  - Doit être unique au sein de `BatchStoreRoot`.
  - Ne doit pas changer lors d’un renommage.
  - Ne doit pas être réutilisé (même après suppression).
- **Nom (BatchName)** :
  - Obligatoire (non vide après trim).
  - L’unicité du nom n’est **pas** requise (les collisions sont permises).
  - En cas de collision, l’UX doit permettre la désambiguïsation (ex : afficher aussi une forme courte du BatchId).
- **Description** :
  - Optionnelle.
  - Purement informative (ne doit pas impacter l’exécution).

#### 6.3.2) Validité d’une définition de batch (statique)

Chaque batch stocké a un état de validité “statique” (indépendant de l’état du filesystem au moment T) :

- **Valid** : conforme au schéma supporté ; contient des valeurs syntaxiquement acceptables.
- **Invalid** : données corrompues ou non interprétables.
- **Incompatible** : schéma reconnu comme “futur” ou non supporté.

Règles normatives :
- `Invalid`/`Incompatible` ne doivent pas bloquer la liste des batchs.
- Un batch `Invalid`/`Incompatible` ne doit pas être exécutable tant qu’il n’est pas rendu `Valid`.

### 6.4) Étape (step)

Une étape contient :

- Type d’opération (ex : “Refresh indexes (full)”).
- Paramètres propres à l’opération (ex : incremental oui/non, dry-run oui/non).
- Paramètres de contexte (chemins, options) :
  - Soit hérités du contexte batch.
  - Soit surchargés localement pour l’étape.
- Stricte interdiction d’inclure des **secrets persistés**.

### 6.5) Résultats d’exécution (concept)

Sans imposer un format, l’exécution doit produire une représentation consultable de :

- Statut de batch (succès / warning / échec / annulé).
- Statut par étape (au minimum les mêmes catégories).
- Messages utilisateur (résumé, erreurs, chemins d’artifacts si existants).

### 6.5.1) Statuts normalisés (batch et étapes)

Les statuts suivants sont définis (et doivent être utilisables dans l’UX, les tests et les résumés) :

- **Success** : l’opération a terminé sans erreur.
- **Warning** : l’opération a terminé mais signale un risque ou une situation anormale non bloquante.
- **Fail** : l’opération a échoué.
- **Canceled** : l’utilisateur (ou le système) a annulé l’opération.
- **NotRun** : l’étape n’a pas été exécutée (ex : arrêt sur échec d’une étape précédente).

### 6.5.2) Batch Run (exécution d’un batch)

Un “Batch Run” est une exécution datée d’un batch, conceptuellement séparée de la définition :

- Référence le batch (identité + version de schéma).
- Capture :
  - Un identifiant de run.
  - L’instant de départ/fin (informatif, non utilisé pour décider).
  - Le statut global (dérivé des statuts d’étapes).
  - Le détail par étape (statut + message).

Règle normative :
- L’exécution ne doit pas muter la **définition** du batch. Toute information d’historique, si elle existe, est séparée.
- La conservation d’un historique persistant de Batch Runs n’est pas dans la portée de cette spécification : seul le résumé du run courant est requis.

### 6.5.3) Artifacts (concept)

Définition :
- Un **artifact** est un résultat matérialisé (souvent un fichier) produit par une opération : log, report, index, archive, etc.

Règles normatives :
- Une étape peut produire zéro ou plusieurs artifacts.
- Lorsque des artifacts existent, l’UX (interactive et batch) doit pouvoir afficher leurs chemins.
- Les secrets ne doivent jamais apparaître en clair dans les artifacts produits par la couche “menu/orchestration”.

### 6.6) Contexte d’exécution — champs conceptuels

Le contexte d’exécution manipule des valeurs conceptuelles (noms indicatifs) :

| Champ | Intention | Utilisé par |
|---|---|---|
| SourceRoot | Racine de la source | refresh, sync, encrypted (data) |
| MirrorRoot | Racine de la destination | refresh, sync |
| SourceIndexCsvPath | Index CRC source | refresh (écrit), sync (lit), encrypted (lit) |
| DestIndexCsvPath | Index CRC destination | refresh (écrit), sync (lit/écrit selon mode) |
| EncryptedOutputRoot | Racine du dossier chiffré | encrypted create/refresh/restore |
| RestoreRoot | Racine de restauration | restore |
| SevenZipPath (ou équivalent) | Accès à l’outil d’archivage/chiffrement | encrypted create/refresh/restore |

Règles normatives :
- Un batch **persisté** doit stocker explicitement les valeurs nécessaires à son exécution (hors secrets).
- À l’exécution, un batch ne doit pas “tomber par défaut” sur la configuration interactive : toute dépendance doit être explicitée (copie de valeurs au moment de l’édition).

### 6.7) Héritage (batch → step) et surcharge

- Le contexte batch fournit des valeurs par défaut.
- Une étape peut surcharger localement un champ de contexte.
- Résolution des valeurs au runtime :
  1) Valeur d’étape si définie.
  2) Sinon valeur du contexte batch.
  3) Sinon **erreur de préflight** (champ requis manquant).

## 7) Catalogue d’opérations (portée batch/interactive)

### 7.1) Opérations disponibles en Interactive mode (existantes)

Les opérations actuelles doivent rester disponibles (au moins) :

- Refresh CRC indexes (full).
- Refresh CRC indexes (smart/incremental).
- One-way sync (dry run).
- One-way sync (apply).
- Create encrypted folder.
- Refresh encrypted folder.
- Restore encrypted files.
- Edit settings.

### 7.2) Opérations batchables

Par défaut, `Batch mode` doit permettre d’utiliser les opérations “métier” (index/sync/encryption/restore).
L’édition des paramètres utilisateurs (“Edit settings”) reste une fonctionnalité `Interactive mode` (non batchée).

### 7.3) Catalogue minimal batchable (paramètres conceptuels)

Le catalogue ci-dessous fixe les attentes fonctionnelles, sans figer d’API.

| Opération (type) | Paramètres d’opération | Champs de contexte requis | Secret requis | Confirmation requise |
|---|---|---|---|---|
| Refresh indexes (full) | mode = full | SourceRoot, MirrorRoot, SourceIndexCsvPath, DestIndexCsvPath | non | non |
| Refresh indexes (smart) | mode = incremental | SourceRoot, MirrorRoot, SourceIndexCsvPath, DestIndexCsvPath | non | non |
| One-way sync (dry run) | dryRun = oui | SourceRoot, MirrorRoot, SourceIndexCsvPath, DestIndexCsvPath | non | non |
| One-way sync (apply) | dryRun = non | SourceRoot, MirrorRoot, SourceIndexCsvPath, DestIndexCsvPath | non | oui (risque réel) |
| Create encrypted folder | (aucun obligatoire au-delà du type) | SourceRoot, SourceIndexCsvPath, EncryptedOutputRoot, SevenZipPath | oui | oui |
| Refresh encrypted folder | (aucun obligatoire au-delà du type) | EncryptedOutputRoot, SevenZipPath | oui | oui |
| Restore encrypted files | (aucun obligatoire au-delà du type) | EncryptedOutputRoot, RestoreRoot, SevenZipPath | oui | oui |

Notes normatives :
- En `Batch mode`, si au moins une étape requiert une confirmation, le runner DOIT demander une **confirmation globale unique** après le préflight, avant toute exécution.
- Les secrets sont fournis au runtime ; le batch ne doit jamais les stocker.

### 7.4) Paramètres “significatifs” (affichage préflight)

Définition :
- Un paramètre est “significatif” s’il influence de manière compréhensible pour l’utilisateur : **quoi** sera fait, **où** (sur quels chemins), et **avec quel niveau de risque**.

Règles normatives :
- Le runner doit afficher au moins les paramètres significatifs **effectifs** (après héritage/surcharge) avant toute confirmation.
- Si une étape surcharge des champs du contexte batch, le runner doit rendre visibles ces surcharges (ex : “overrides: EncryptedOutputRoot”).

Paramètres significatifs minimaux par opération :

| Opération (type) | Paramètres significatifs à afficher |
|---|---|
| Refresh indexes (full) | mode=full, SourceRoot, MirrorRoot, SourceIndexCsvPath, DestIndexCsvPath |
| Refresh indexes (smart) | mode=incremental, SourceRoot, MirrorRoot, SourceIndexCsvPath, DestIndexCsvPath |
| One-way sync (dry run) | dryRun=true, SourceRoot, MirrorRoot, SourceIndexCsvPath, DestIndexCsvPath, “no file writes” |
| One-way sync (apply) | dryRun=false, SourceRoot, MirrorRoot, SourceIndexCsvPath, DestIndexCsvPath, “may overwrite destination files” |
| Create encrypted folder | SourceRoot, SourceIndexCsvPath, EncryptedOutputRoot, SevenZipPath, “secret required” |
| Refresh encrypted folder | EncryptedOutputRoot, SevenZipPath, “secret required” |
| Restore encrypted files | EncryptedOutputRoot, RestoreRoot, SevenZipPath, “secret required” |

### 7.5) Préflight — règles de validation (par opération)

Le préflight est une validation avant exécution. Il combine :

- **Validation statique** : vérifie la présence et la validité “syntaxique” des champs (sans dépendre du filesystem au moment T).
- **Validation runtime** : vérifie l’existence des ressources indispensables (répertoires/fichiers) au moment du lancement.

Règles normatives :
- Un préflight en échec doit produire une liste d’erreurs actionnables (étape + champ + raison).
- Les champs de type chemin (fichier/répertoire) DOIVENT respecter les règles BareSync de sécurité de chemin : valeur canonicalisable et ne permettant pas d’injection / traversal non sûre.
- Les opérations qui nécessitent un secret ne doivent pas exécuter d’action irréversible avant que le secret n’ait été fourni (et, si possible, validé).
- Si une opération requiert `SevenZipPath`, la validation runtime DOIT échouer si l’outil n’est pas disponible/invocable.
- Si une opération écrit des artifacts (index, archives, restauration, logs/reports), la validation runtime DOIT échouer si les répertoires cibles ne peuvent pas être créés (ou si un répertoire attendu est un fichier existant).

Validation minimale attendue par opération :

| Opération (type) | Validation statique | Validation runtime |
|---|---|---|
| Refresh indexes (full/smart) | SourceRoot/MirrorRoot non vides ; SourceIndexCsvPath/DestIndexCsvPath valides (.csv, conforme règles BareSync de sécurité de chemin) | SourceRoot et MirrorRoot existent ; répertoires parents des index créables |
| One-way sync (dry run) | idem | SourceRoot et MirrorRoot existent ; SourceIndexCsvPath existe (fichier) ; DestIndexCsvPath peut être absent (considéré vide) |
| One-way sync (apply) | idem | SourceRoot et MirrorRoot existent ; SourceIndexCsvPath existe (fichier) ; DestIndexCsvPath peut être absent (considéré vide) ; répertoire parent de DestIndexCsvPath créable |
| Create encrypted folder | SourceRoot non vide ; SourceIndexCsvPath non vide ; EncryptedOutputRoot non vide ; SevenZipPath non vide | SourceRoot existe ; SourceIndexCsvPath existe (fichier) ; EncryptedOutputRoot créable (répertoire) ; SevenZipPath invocable |
| Refresh encrypted folder | EncryptedOutputRoot non vide ; SevenZipPath non vide | Encrypted index présent dans EncryptedOutputRoot ; SevenZipPath invocable |
| Restore encrypted files | EncryptedOutputRoot non vide ; RestoreRoot non vide ; SevenZipPath non vide | Encrypted index présent dans EncryptedOutputRoot ; RestoreRoot créable (répertoire) ; SevenZipPath invocable |

### 7.6) Secrets — rôles, scope et réutilisation (normatif)

Définition :
- Un **secret** est une entrée sensible demandée à l’utilisateur au runtime (ex : mot de passe de chiffrement).
- Un secret a un **rôle** (ex : `EncryptionPassword`) et un **scope** (ce à quoi il s’applique).

Règles normatives :
- Le runner doit déterminer les **slots de secrets** nécessaires à partir des valeurs effectives des étapes.
- BareSync NE DOIT PAS afficher, journaliser ou persister des secrets (en clair) dans aucun output sous son contrôle (UI, messages, artifacts, fichiers).
- Si une exécution implique un outil externe susceptible de logguer/afficher, BareSync DOIT éviter de transmettre des secrets via des canaux exposés (ex : arguments de ligne de commande).
- Un slot de secret est défini par le couple : (`EncryptionPassword`, `EncryptedOutputRoot` effectif).
- Le runner DOIT demander le secret au plus une fois par slot et DOIT le réutiliser pour toutes les étapes partageant ce slot, au sein d’un même run.
- Si un batch contient plusieurs étapes avec des `EncryptedOutputRoot` différents, le runner doit traiter ces secrets comme **distincts** (prompts séparés).

## 8) États & transitions (navigation)

### 8.1) États principaux

- `MainMenu`
- `Interactive/Home`
- `Interactive/SubMenu/*` (groupes fonctionnels)
- `Batch/Home` (accueil batch)
- `Batch/List` (liste des batchs)
- `Batch/Editor` (création/édition d’un batch)
- `Batch/Runner` (préflight + exécution)
- `Exit`

### 8.2) Transitions (règles générales)

- Toute entrée dans un mode doit permettre un retour explicite vers le `MainMenu`.
- Toute action potentiellement destructive (ex : sync apply) doit imposer une confirmation explicite, dans les deux modes.
- En cas d’erreur de validation (configuration incomplète), l’utilisateur doit être guidé vers l’action de correction (ex : settings / éditer contexte batch).

### 8.3) Transitions détaillées (minimum attendu)

| État | Actions utilisateur (exemples) | État cible |
|---|---|---|
| MainMenu | Choisir Interactive | Interactive/Home |
| MainMenu | Choisir Batch | Batch/Home |
| Interactive/Home | Choisir un sous-menu | Interactive/SubMenu/* |
| Interactive/SubMenu/* | Lancer une opération | (écran d’opération/progrès), puis retour au sous-menu |
| Interactive/* | Back | niveau supérieur (jusqu’à MainMenu) |
| Batch/Home | Lister batchs | Batch/List |
| Batch/Home | Créer batch | Batch/Editor |
| Batch/List | Sélectionner batch | Batch/Editor (édition) ou Batch/Runner (exécution) selon action |
| Batch/Editor | Sauvegarder | Batch/List ou Batch/Home |
| Batch/Editor | Back sans sauvegarde | Batch/List ou Batch/Home (avec avertissement si modifications) |
| Batch/Runner | Préflight OK + lancer (si confirmation requise : confirmer) | Exécution séquentielle, puis Batch/List ou Batch/Home |
| Batch/* | Back | niveau supérieur (jusqu’à MainMenu) |

## 9) UX — flux attendus (résumé)

### 9.1) Interactive mode

- Objectif : exécuter rapidement une opération sur l’état courant.
- Doit offrir :
  - Des sous-menus regroupant les opérations (index / sync / encrypted / restore / settings).
  - Un retour “Back” à chaque niveau.
  - Un statut de dernière exécution (succès/échec + message) affichable sans ambiguïté.

Règles normatives :
- Avant d’exécuter une opération, `Interactive mode` DOIT appliquer la validation correspondante (préflight) selon 7.5.
- En cas d’échec de validation, `Interactive mode` DOIT afficher les erreurs et DOIT guider vers `Edit settings`.
- Si l’opération requiert une confirmation, `Interactive mode` DOIT demander une confirmation explicite avant exécution.
- Si l'operation requiert a la fois confirmation et secret, l'ordre DOIT etre : confirmation, puis secret, puis execution.
- Si l’opération requiert un secret, `Interactive mode` DOIT demander le secret au runtime (non persisté, non affiché en clair).

Structuration minimale attendue (conceptuelle) :
- **Index**
  - Refresh indexes (full)
  - Refresh indexes (smart)
- **Sync**
  - One-way sync (dry run)
  - One-way sync (apply)
- **Encrypted**
  - Create encrypted folder
  - Refresh encrypted folder
  - Restore encrypted files
- **Settings**
  - Edit settings

### 9.2) Batch mode

- Objectif : composer et gérer des scénarios réutilisables.
- Doit offrir :
  - Créer un batch.
  - Lister les batchs existants.
  - Sélectionner un batch.
  - Éditer un batch : ajouter/supprimer des étapes, réordonner, paramétrer.
  - Sauvegarder un batch (persistance).
  - Exécuter un batch sélectionné.

#### 9.2.0) Liste des batchs (attendus UX)

La liste des batchs doit permettre au minimum :

- Voir : nom, désambiguïsation (ex : id court), nombre d’étapes, statut de validité (Valid/Invalid/Incompatible).
- Accéder aux actions :
  - Ouvrir/éditer un batch.
  - Exécuter un batch (si `Valid`).

Règles normatives :
- L’ordre d’affichage respecte le tri défini en 12.5.
- Un batch `Invalid`/`Incompatible` est listé mais non exécutable ; l’UX doit proposer une action “détails” (raison) plutôt qu’un échec silencieux.

#### 9.2.1) Éditeur de batch (attendus UX)

L’éditeur de batch doit permettre, au minimum :

- Éditer le nom et la description.
- Éditer le contexte batch (valeurs par défaut).
- Initialiser le contexte batch par **copie** depuis la configuration interactive courante (*Interactive Context*).
- Gérer la liste d’étapes :
  - Ajouter une étape (choix du type d’opération).
  - Supprimer une étape.
  - Réordonner les étapes.
  - Éditer les paramètres d’une étape (opération + surcharges de contexte).

Règles normatives :
- L’écran doit permettre de visualiser l’**ordre** et le **type** de chaque étape.
- Si des champs requis manquent, l’éditeur doit pouvoir signaler “batch non exécutable” (sans empêcher la sauvegarde si l’utilisateur accepte un batch incomplet).
- La copie depuis l’Interactive Context est un **snapshot** : elle ne crée aucun lien dynamique entre settings et batch.
- Si une copie écrase des valeurs existantes, une confirmation explicite est requise.

#### 9.2.2) Runner (préflight + exécution)

Le runner est responsable de transformer une définition de batch en exécution contrôlée et vérifiable.

**A. Préflight (avant toute exécution)**
- Calcule, pour chaque étape, les **valeurs effectives** (héritage batch → step).
- Valide au minimum :
  - Champs requis présents (par type d’opération).
  - Valeurs syntaxiquement acceptables (ex : chemins non vides si requis).
- Produit un rapport lisible :
  - Liste des étapes (ordre + type).
  - Paramètres effectifs “significatifs” (définis par opération).
  - Indicateurs : confirmation requise, secret requis.

**B. Confirmation**
- Si au moins une étape requiert confirmation, le runner DOIT afficher un résumé de ces risques et DOIT demander un accord explicite (Yes/No).
- La confirmation DOIT être **globale** et **unique** ; le résumé DOIT lister clairement toutes les étapes à risque.

**C. Saisie des secrets (runtime, sans persistance)**
- Si des secrets sont requis, le runner doit :
  - Indiquer quels secrets seront demandés (sans les afficher).
  - Demander les secrets **après** la confirmation, et **avant** l’exécution de la première étape qui en dépend.
- Règles normatives :
  - Les secrets ne sont jamais persistés.
  - Les secrets ne sont jamais affichés en clair (saisie non écho).
  - Le runner DOIT appliquer la règle de slot définie en 7.6 (prompt unique par slot, puis réutilisation au sein du run).
  - Si un secret fourni est invalide et qu’une étape échoue, le run suit la politique d’échec (stop) ; il n’y a pas de ré-essai automatique du secret dans le même run.

**D. Exécution**
- Exécution séquentielle, dans l’ordre.
- Politique normative :
  - **Warning** : continue.
  - **Fail** ou **Canceled** : stop ; étapes restantes = **NotRun**.

**E. Résumé post-run**
- Afficher un résumé par étape : statut + message utilisateur.
- Si l’opération produit des artifacts (logs, reports, fichiers), l’UX doit pouvoir afficher leurs chemins.

### 9.3) CLI /EXTRACT mode

Objectif :
- Exécuter une extraction ad-hoc sans entrer dans le menu principal.

Règles normatives :
- Le mode est déclenché par `/EXTRACT:<path>`.
- `/EXTRACT` et `/BATCH` ne doivent pas être combinés dans la même commande.
- La source doit être résolue automatiquement :
  - fichier `.bse` natif,
  - ou dossier contenant des archives `.bse` natives.
- Si la source ne contient aucune archive native exploitable, l’exécution doit échouer avec un message actionnable.
- La validation du secret doit précéder toute exposition d’options de destination (anti-divulgation UX).
- Résolution du secret (ordre) :
  1) secret store (slot scope),
  2) tentative sans secret,
  3) prompt masqué avec possibilité de sauvegarde en secret store.
- Source dossier : extraction récursive par défaut vers un sous-dossier `Extract to` proposé.
- Source fichier : proposer par défaut une extraction vers un sous-dossier dédié (style archive tool), avec alternatives dossier courant ou chemin personnalisé.

## 10) Différences Interactive vs Batch (résumé)

| Sujet | Interactive mode | Batch mode |
|---|---|---|
| Unité de travail | une opération ad-hoc | séquence persistée (batch) |
| État | unique (configuration courante) | multiples batchs indépendants |
| Paramétrage | via settings + prompts | paramètres par batch/étape, persistés (hors secrets) |
| Répétabilité | manuelle | élevée (réexécution du batch) |
| UX | rapide, guidée | composition, gestion, exécution |

## 11) Invariants

- Le menu principal expose **exactement** 3 choix (Interactive/Batch/Exit).
- `Interactive mode` n’introduit pas de second état utilisateur : **un seul** état courant.
- Un batch est **sérialisable** et **rechargeable** sans perte d’information (hors éléments explicitement non persistables : secrets).
- Aucune donnée sensible (mot de passe, secret) n’est persistée dans les définitions de batchs.
- Les opérations exécutées en batch conservent la **même sémantique** que leurs équivalents en interactive.
- L’ordre des étapes d’un batch est **significatif** et respecté strictement.
- Les validations d’entrée (chemins requis, existence, etc.) sont effectuées avant exécution (préflight).
- L’édition/exécution d’un batch ne doit pas modifier la configuration interactive persistante, sauf action explicite `Interactive mode` dédiée (hors portée de `Batch mode`).
- Une exécution (Batch Run) ne modifie pas la définition du batch.

## 12) Contraintes

### 12.1) Déterminisme & intégrité

- Les décisions métier doivent dépendre uniquement des entrées (fichiers/indices/paramètres), pas d’un état implicite.
- La persistance des batchs doit être stable et robuste face aux interruptions (pas de corruption silencieuse).

### 12.2) Testabilité

- Les comportements doivent être spécifiables et vérifiables via des scénarios reproductibles (navigation, validation, exécution séquentielle, persistance).

### 12.3) UX & sécurité

- Les confirmations doivent être explicites pour les actions à risque.
- Les secrets sont fournis au moment de l’exécution et ne doivent pas apparaître en clair dans l’historique d’affichage.

### 12.4) Persistance & compatibilité

- La bibliothèque de batchs doit être :
  - **Versionnée** (version de schéma).
  - **Tolérante** aux évolutions (ajout de champs futurs sans casser les batchs existants, lorsque possible).
  - **Explicite** en cas d’incompatibilité (message clair, exécution refusée si nécessaire).
- Les opérations de sauvegarde (création/édition/suppression) doivent être **crash-safe** :
  - Après interruption (crash, coupure), l’outil doit retrouver soit l’ancienne version, soit la nouvelle, mais pas un état partiellement écrit sans signalement.
- La liste des batchs doit rester exploitable en cas d’entrée invalide :
  - Un batch corrompu/incompatible ne doit pas “casser” l’accès aux autres batchs.
  - L’UX doit signaler l’état (valide/invalide/incompatible) par batch.

### 12.5) Ordonnancement et prévisibilité

- L’affichage “Liste des batchs” doit avoir un ordre **prévisible et déterministe**.
- Tri par défaut (normatif) : **nom (insensible à la casse) puis identifiant**.

## 13) Critères de succès (mesurables)

### 13.1) Navigation & UX

- Le menu principal expose exactement `1. Interactive mode`, `2. Batch mode`, `0. Exit`.
- `Interactive mode` expose les sous-menus minimaux (Index/Sync/Encrypted/Settings) et donne accès à l’ensemble des opérations existantes listées en 7.1.
- `Batch mode` permet : créer, lister, sélectionner, éditer, sauvegarder et exécuter un batch (au minimum).
- La liste des batchs affiche : nom + désambiguïsation (id court), nombre d’étapes, et statut (Valid/Invalid/Incompatible).

### 13.2) Persistance des batchs

- Créer 2 batchs distincts, les sauvegarder, quitter l’application, relancer, puis :
  - Les deux batchs sont listés.
  - Leur contenu est inchangé (nom, étapes, ordre, paramètres, contexte).
- Le listing est stable et respecte le tri par défaut défini en 12.5.

### 13.3) Exécution & préflight

- Pour un batch exécutable, le runner affiche un préflight (étapes + paramètres effectifs) puis lance l’exécution ; si au moins une étape requiert confirmation, il demande une confirmation globale avant toute exécution.
- Pour un batch non exécutable (champ requis manquant), le runner refuse l’exécution et indique clairement :
  - Quelle étape est invalide.
  - Quel champ requis manque.
- Le préflight affiche les paramètres “significatifs” par opération (cf. 7.4) et rend visibles les surcharges (batch → step).
- À l’exécution :
  - Les étapes sont exécutées séquentiellement dans l’ordre.
  - Sur **Fail** ou **Canceled**, l’exécution s’arrête et les étapes restantes sont **NotRun**.
  - Sur **Warning**, l’exécution continue.

### 13.4) Sécurité & invariants

- Un batch persisté ne contient aucun secret ; les opérations chiffrées/restauration déclenchent une demande de secret au runtime.
- Les secrets peuvent être demandés une seule fois par slot (rôle + scope) et réutilisés au sein d’un run (cf. 7.6).
- L’exécution d’un batch ne modifie pas sa définition.
- L’édition/exécution d’un batch ne modifie pas la configuration interactive persistante.
