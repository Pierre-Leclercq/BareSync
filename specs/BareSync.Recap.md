# BareSync — Recap (Architecture + Specs + Écarts)

Ce document synthétise :
1) l’architecture **actuelle** (code implémenté),
2) les **contraintes des specs** (fonctionnelles + UI),
3) la **carte cible** (ce qui reste à implémenter, notamment le Batch mode),
4) les **écarts majeurs** entre code et specs.

## 1) Architecture actuelle (implémentée)

### 1.1 Projets
- **BareSync** : exécutable principal (`BareSync.csproj`).
- **BareSync.Tui** : utilitaires console (input, clear, throttle, pickers).
- **Tests** : `BareSync.Tests` et `BareSync.Tui.Tests`.

### 1.2 Couches / dossiers

**Domain/** (modèle simple)
- `AppConfig` : configuration interactive (SourceRoot, MirrorRoot, index CSV, 7zip, etc.).
- `FileIndex`, `FileIndexEntry` : structure d’index CRC.
- `ConfigValidationError` : erreurs de validation.

**Infra/** (I/O, persistance technique)
- `ConfigService` : load/save/validate de `appsettings.json` + normalisation.
- `FileScanner` : build d’index CRC + reprise (work/checkpoint) + incremental.
- `CsvIndexReader` / `CsvIndexWriter` : I/O CSV de l’index.
- `Crc64Service` : calcul CRC64.
- `SingleInstanceLock` : lock fichier.

**UI/** (rendu et orchestration UI)
- `ScreenRenderer` + `ScreenModel` + `IScreen` : rendu écran.
- `ConsoleMenu` : menus digit-only + footer “Last status”.
- `OperationRunner` : exécution avec progression + annulation `ESC`.
- `SettingsEditor` : édition interactive des settings.

**App/** (use cases)
- `Program` : main loop, menu, validation, appels d’opérations.
- `SyncOneWay` : comparaison indices + copie + logs/reports + refresh index.
- `EncryptedFolderService` : index chiffré + archives via 7-Zip.

### 1.3 Flux principaux existants
- **Refresh indexes** : `Program` -> `OperationRunner` -> `FileScanner` -> `CsvIndexWriter`.
- **One-way sync** : `Program` -> `OperationRunner` -> `SyncOneWay`.
- **Encrypted** : `Program` -> `EncryptedFolderService` -> 7-Zip.

---

## 2) Contraintes majeures des specs

### 2.1 Menu niveau 0 (UI Specs)
- `S0.1` DOIT afficher exactement :
  - `1. Interactive mode`
  - `2. Batch mode`
  - `0. Exit`

### 2.2 Règles UI globales
- Écrans menu : **un chiffre** `0..9`.
- Si >9 options : pagination/selector.
- Pagination normative : **9 éléments/page**.
- `ESC` :
  - équivaut à `0` si `0` est proposé,
  - annule les écrans de progress.
- **Last status** séparé pour Interactive vs Batch.
- Langue UI : anglais (ASCII si possible).

### 2.3 Batch mode (Specs + Technical Specs)
- **BatchStoreRoot** = `AppDataRoot/batches` (non configurable).
- Chaque batch est une **unité indépendante** : un batch invalide ne bloque pas les autres.
- Statuts statiques : `Valid / Invalid / Incompatible`.
- Exécution : préflight -> confirmation globale (si besoin) -> secrets runtime -> exécution séquentielle -> summary.
- Stop policy : `Warning` continue ; `Fail`/`Canceled` stop ; restants = `NotRun`.
- Secrets : jamais persistés, jamais affichés, jamais loggés. Slot = (`EncryptionPassword`, `EncryptedOutputRoot` effectif).
- Artifacts : **chemins uniquement** (pas de contenu), ordre déterministe, sans secrets.

---

## 3) Carte cible (prévu à implémenter)

### 3.1 UI / Navigation

**Main menu S0.1** (nouveau)
- Point d’entrée vers `Interactive` et `Batch`.

**Interactive (S1.*)**
- `S1.1` Home (Index / Sync / Encrypted / Settings)
- `S1.2` Index menu
- `S1.3` Sync menu
- `S1.4` Encrypted menu
- `S1.5` Settings menu (réutilise `SettingsEditor`)
- `S1.5a` Validation errors
- `S1.6` Confirmation prompt
- `S1.7` Secret prompt
- `S1.8` Progress (OperationRunner)

**Batch (S2.*)**
- `S2.1` Home (List / Create)
- `S2.2` Batch list (paginé)
- `S2.3` Batch details (hub)
- `S2.4` Identity editor
- `S2.5` Context editor (+ copy snapshot)
- `S2.6` Steps editor (paginé)
- `S2.7` Add step (type picker)
- `S2.8` Step editor
- `S2.9` Overrides editor
- `S2.10` Reorder
- `S2.11` Remove confirm
- `S2.12` Preflight plan
- `S2.12a` Preflight errors
- `S2.13` Secret prompt(s)
- `S2.14` Run progress (par étape)
- `S2.15` Run summary
- `S2.15a/b` Artifacts view
- `S2.16` Validity details
- `S2.17` Unsaved changes confirm

### 3.2 Modèle Batch (Domain)
À créer (d’après `BareSync.Technical.Specs.md`) :
- `BatchDefinition`, `BatchMetadata`, `BatchContextDefaults`.
- `StepDefinition`, `StepOperationParams`, `StepContextOverrides`.
- `RunSummary`, `StepRunResult`, `ArtifactDescriptor`.
- Enums : `BatchValidity`, `ExecutionStatus`, `OperationType`.

### 3.3 Persistance Batch (Infra)
À créer :
- `BatchStore`/`BatchRepository`.
- `BatchStoreRoot = AppDataRoot/batches`.
- Un batch = un fichier `{BatchId}.json`.
- Écriture crash-safe (temp + flush + replace).
- Classification `Valid/Invalid/Incompatible`.

### 3.4 Validation + Preflight (App/Infra)
À créer :
- `ErrorDescriptor` (format stable et tri déterministe).
- Validation statique + runtime par `OperationType`.
- Rendu erreurs via `S1.5a` / `S2.12a`.

### 3.5 Runner Batch (App)
À créer :
- Pipeline : preflight -> confirm -> secrets -> exécution séquentielle -> summary.
- Cache secret par slot (run only).
- Stop policy respectée.
- Intégration avec opérations existantes (`SyncOneWay`, `FileScanner`, `EncryptedFolderService`).

---

## 4) Écarts majeurs (code actuel vs specs)

1. **Menu principal** : doit devenir `Interactive / Batch / Exit`.
2. **Batch mode** : entièrement absent (modèle, persistance, UI, runner).
3. **Last status** : doit être **séparé par mode**.
4. **Secret safety** : specs demandent d’éviter les secrets en CLI; le code actuel passe `-p"password"` à 7-Zip.
5. **Préflight / Errors** : format `ErrorDescriptor` et écrans dédiés non implémentés.
6. **Artifacts contract** : standardiser (paths only, order déterministe) pour Interactive + Batch.
7. **AppDataRoot** : doit être défini (OS mapping) et utilisé pour `BatchStoreRoot`.

---

## 5) Notes de test (issues connues)
- Les tests UI en console redirigée peuvent planter avec `Console.CursorTop` (Windows). Une stratégie de mock console est documentée dans `Docs/Progress/ConsoleMock.Progress.md`.
