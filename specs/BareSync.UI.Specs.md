# BareSync — UI Specs (Console/Texte)

Statut : **spécification UI normative (SPEC ONLY)**.

## Iteration 10 changes (non-normative)

- Made S2.12 option 1 label conditional: `Run` when no confirmation is required, else `Confirm & run`.
- Stated S2.12 is a menu screen (single digit input).
- Added a future-proof rule: menu screens stay within `0..9`; overflow uses selectors/pickers.
- Strengthened S1.8 progress header for sync: show `Mode: dry run/apply` summary line.
- Removed illegal `Delete` suggestion from S2.16.

## Iteration 11 changes (non-normative)

- Added console UX flow for CLI extraction mode `/EXTRACT:<path>` (outside main menu routing).
- Clarified anti-disclosure rule for extraction: destination options are shown only after secret validation.
- Added extraction destination prompts aligned with archive-tool UX:
  - folder source: recursive-by-default warning + default `Extract to` sub-folder,
  - file source: default `Extract to` sub-folder with alternatives.

## 0) Objectif

Définir tous les **écrans console** à implémenter (interactive + batch), leurs **menus**, la **navigation**, et des **rendus texte fidèles** (mock) utilisables pour préparer des specs techniques.

## 1) Principes UI (globaux)

### 1.0) Terminologie normative

Dans ce document :
- **DOIT** / **NE DOIT PAS** exprime une exigence obligatoire.
- **PEUT** exprime une capacité optionnelle explicitement permise (sans obligation).

### 1.1) Convention d’affichage

- En-tête : `** BareSync **` (constant).
- Chaque écran affiche :
  - Un bloc “contexte” si pertinent (ex : chemins).
  - Un bloc “contenu” (infos, liste, résumé).
  - Une ligne `Last status: ...` lorsque disponible (voir 1.1a).
  - Un bloc `** Menu **` avec options numérotées.
  - Une option `0.` pour `Back` ou `Exit` selon le niveau.
- Les valeurs non définies s’affichent comme `<not set>`.

### 1.1a) Règle “Last status”

Définition :
- `Last status` est un état UI par mode (`Interactive mode` et `Batch mode`) représentant le dernier résultat d’opération.

Règles normatives :
- `Last status` DOIT être maintenu séparément pour `Interactive mode` et `Batch mode` (états indépendants).
- Il DOIT être mis à jour après chaque opération/run terminé dans le mode concerné (y compris `Canceled`).
- Entrer/sortir de l’autre mode NE DOIT PAS modifier le `Last status` du mode courant.
- Après redémarrage de l’application, le `Last status` PEUT être réinitialisé (non spécifié au-delà de cela).
- `Last status` DOIT être affiché sur tous les écrans du mode (sauf écrans de progress) lorsqu’il est disponible.
- Il DOIT être affiché immédiatement avant `** Menu **`.

### 1.2) Convention d’entrée

- **Menu screens** : l’entrée utilisateur DOIT être un chiffre unique `0..9`.
- Les écrans de menu DOIVENT rester dans l’intervalle `0..9` ; si le nombre d’actions dépasse 9, l’UI DOIT utiliser un écran de sélection/pagination (prompt/selector) au lieu d’ajouter des options `10+`.
- **Prompt / selector screens** : l’entrée utilisateur DOIT être une ligne ; lorsqu’une sélection par numéro est demandée, la saisie DOIT accepter un entier (multi-chiffres permis) avec une plage valide explicitée (ex : `1..{N}`, `0 to cancel`).
- `ESC` :
  - Si `0` est proposé (Back/Cancel), `ESC` DOIT être équivalent à `0`.
  - Sur les écrans de progress, `ESC` DOIT annuler l’opération en cours même si aucun `0` n’est affiché.
  - Sinon, `ESC` n’a pas d’effet sauf si l’écran définit explicitement une règle différente.
- Une entrée invalide DOIT être ignorée (aucun crash, aucun état partiel) et l’UI DOIT re-demander une saisie.
- Toute opération longue DOIT pouvoir être annulée (`ESC`), avec un résultat explicite.
- Les écrans “prompt” (P1/P3/P4 et prompts dédiés) acceptent une saisie textuelle (ligne) validée selon les règles de l’écran.

### 1.3) Vocabulaire de statut (affiché)

Statuts (au minimum) : `Success`, `Warning`, `Fail`, `Canceled`, `NotRun`.

### 1.3a) Langue de l’UI

Règle normative :
- Les libellés, prompts et messages utilisateur affichés par l’UI DOIVENT être en **anglais** (ASCII lorsque possible).

### 1.4) Écrans “primitives” (réutilisables)

Ces écrans sont des briques utilisées par plusieurs menus (Interactive et Batch).

#### P1 — Saisie texte (1 ligne)

```
** BareSync **

{PromptLabel}:
{CurrentValue|optional}

Enter value:
```

Règles normatives :
- Le prompt DOIT expliciter le comportement de la valeur vide (ex : `empty to cancel` OU `empty to clear`).
- Si une valeur vide est autorisée (ex : “clear”), l’écran DOIT rendre l’intention explicite.

#### P3 — Sélecteur de répertoire (picker)

```
** BareSync **

Pick a directory
Current: {StartDir}

Page {Current}/{Total}

1) {DirEntry1}
2) {DirEntry2}
...

** Menu **

1. Select entry
2. Next page
3. Previous page
0. Cancel

Select an option:
```

Regles normatives :
- `1. Select entry` DOIT afficher `P3a`.
- `2` et `3` DOIVENT naviguer les pages selon 1.5.
- `0` DOIT annuler et retourner a l'ecran appelant (aucune selection).

#### P3a - Picker / Select entry (current page)

```
** BareSync **

Select entry number (1..{PageCount}, 0 to cancel):
```

Regles normatives :
- La saisie DOIT etre un entier.
- `0` DOIT annuler et retourner a `P3`.
- Une valeur hors de `1..{PageCount}` DOIT etre rejetee et le prompt DOIT etre re-affiche.
- Une valeur valide DOIT selectionner l'entree correspondante (page courante) et retourner a l'ecran appelant.

#### P4 — Sélecteur de fichier (picker)

```
** BareSync **

Pick a file
Current: {StartDir}
Suggested: {SuggestedFileName}

Page {Current}/{Total}

1) {Entry1}
2) {Entry2}
...

** Menu **

1. Select entry
2. Next page
3. Previous page
0. Cancel

Select an option:
```

Regles normatives :
- `1. Select entry` DOIT afficher `P4a`.
- `2` et `3` DOIVENT naviguer les pages selon 1.5.
- `0` DOIT annuler et retourner a l'ecran appelant (aucune selection).

#### P4a - Picker / Select entry (current page)

```
** BareSync **

Select entry number (1..{PageCount}, 0 to cancel):
```

Regles normatives :
- La saisie DOIT etre un entier.
- `0` DOIT annuler et retourner a `P4`.
- Une valeur hors de `1..{PageCount}` DOIT etre rejetee et le prompt DOIT etre re-affiche.
- Une valeur valide DOIT selectionner l'entree correspondante (page courante) et retourner a l'ecran appelant.

### 1.5) Pagination (listes) et tailles de page

Règles normatives :
- Toute liste susceptible de dépasser 9 éléments DOIT être paginée.
- Taille de page normative (listes “1 ligne par élément”) : **9 éléments par page**.
- L’écran DOIT afficher l’indication de page : `Page {Current}/{Total}`.
- Les actions `Next page` / `Previous page` DOIVENT être proposées lorsque pertinentes ; lorsqu’elles ne sont pas disponibles, elles DOIVENT apparaître comme `disabled` (ou être explicitement absentes).

## 2) Carte des écrans (catalogue)

Notation :
- ID écran : `Sx.y` (groupé par mode).
- Les rendus sont des exemples avec placeholders `{...}`.

## 3) Écrans — Main (niveau 0)

### S0.1 — Main Menu

**Menu**
- `1. Interactive mode`
- `2. Batch mode`
- `0. Exit`

**Rendu (exemple)**
```
** BareSync **

** Menu **

1. Interactive mode
2. Batch mode
0. Exit

Select an option:
```

## 4) Écrans — Interactive mode

### S1.1 — Interactive Home

**Intention**
- Point d’entrée “opérateur” : accéder aux sous-menus fonctionnels.

**Menu**
- `1. Index`
- `2. Sync`
- `3. Encrypted`
- `4. Settings`
- `0. Back` (retour `S0.1`)

**Rendu (exemple)**
```
** BareSync **

** Interactive mode **

Source = '{SourceRoot|<not set>}'
Mirror = '{MirrorRoot|<not set>}'

** Menu **

1. Index
2. Sync
3. Encrypted
4. Settings
0. Back

Select an option:
```

### S1.2 — Interactive / Index Menu

**Menu**
- `1. Refresh CRC indexes (full)`
- `2. Refresh CRC indexes (smart)`
- `0. Back` (retour `S1.1`)

**Rendu (exemple)**
```
** BareSync **

** Interactive / Index **

SourceIndexCsvPath = '{SourceIndexCsvPath|<not set>}'
DestIndexCsvPath   = '{DestIndexCsvPath|<not set>}'

** Menu **

1. Refresh CRC indexes (full)
2. Refresh CRC indexes (smart)
0. Back

Select an option:
```

### S1.3 — Interactive / Sync Menu

**Menu**
- `1. One-way sync (dry run)`
- `2. One-way sync (apply)`
- `0. Back` (retour `S1.1`)

**Rendu (exemple)**
```
** BareSync **

** Interactive / Sync **

Source = '{SourceRoot|<not set>}'
Mirror = '{MirrorRoot|<not set>}'

** Menu **

1. One-way sync (dry run)
2. One-way sync (apply)
0. Back

Select an option:
```

### S1.4 — Interactive / Encrypted Menu

**Menu**
- `1. Create encrypted folder`
- `2. Refresh encrypted folder`
- `3. Restore encrypted files`
- `0. Back` (retour `S1.1`)

**Rendu (exemple)**
```
** BareSync **

** Interactive / Encrypted **

EncryptedOutputRoot = '{EncryptedOutputRoot|<not set>}'
RestoreRoot         = '{RestoreRoot|<not set>}'
SevenZipPath        = '{SevenZipPath|<not set>}'

** Menu **

1. Create encrypted folder
2. Refresh encrypted folder
3. Restore encrypted files
0. Back

Select an option:
```

### S1.5 — Interactive / Settings Menu

**Intention**
- Éditer la configuration interactive persistante (hors batch).

**Menu (conceptuel, aligné sur l’existant)**
- `1. Edit Source Root`
- `2. Edit Mirror Root`
- `3. Edit Source Index Csv Path`
- `4. Edit Dest Index Csv Path`
- `5. Edit Encrypted Output Root (optional)`
- `6. Edit Restore Root (optional)`
- `7. Edit SevenZip Path (optional)`
- `0. Back` (retour `S1.1`)

**Rendu (exemple)**
```
** BareSync **

** Current config **

Source Root='{SourceRoot|<not set>}'
Mirror Root='{MirrorRoot|<not set>}'

Source Index Csv Path='{SourceIndexCsvPath|<not set>}'
Dest Index Csv Path  ='{DestIndexCsvPath|<not set>}'

Encrypted Output Root (optional)='{EncryptedOutputRoot|<not set>}'
Restore Root (optional)         ='{RestoreRoot|<not set>}'
SevenZip Path (optional)        ='{SevenZipPath|<not set>}'

** Menu **

1. Edit Source Root
2. Edit Mirror Root
3. Edit Source Index Csv Path
4. Edit Dest Index Csv Path
5. Edit Encrypted Output Root (optional)
6. Edit Restore Root (optional)
7. Edit SevenZip Path (optional)
0. Back

Select an option:
```

### S1.5a — Interactive / Validation Errors (avant opération)

**Intention**
- Lorsqu’une opération requiert des champs manquants/invalides, l’utilisateur doit voir les erreurs et être guidé vers `S1.5`.

**Menu**
- `1. Edit settings`
- `0. Back`

**Rendu (exemple)**
```
** BareSync **

Missing or invalid settings:
- {FieldA}: {ReasonA}
- {FieldB}: {ReasonB}

** Menu **

1. Edit settings
0. Back

Select an option:
```

### S1.6 — Interactive / Confirmation (action à risque)

**Intention**
- Confirmer une action à risque (ex : sync apply).

**Rendu (exemple)**
```
** BareSync **

WARNING: destination files may be overwritten.

Proceed? (y/n):
```

### S1.7 — Interactive / Secret Prompt (non écho)

**Intention**
- Saisir un secret au runtime (chiffrement / restore).

Regles normatives :
- La saisie DOIT etre non echo.
- Une saisie vide ou `ESC` DOIT annuler l'operation (resultat `Canceled`) et retourner a l'ecran appelant.

**Rendu (exemple)**
```
** BareSync **

Enter password (will not be echoed):
```

### S1.8 — Operation Progress (écran générique)

**Intention**
- Afficher la progression d’une opération longue.

Regles normatives :
- `ESC` DOIT annuler l'operation en cours et produire un resultat `Canceled`.
- Pour `One-way sync (dry run)` et `One-way sync (apply)`, l'ecran DOIT afficher une ligne `Mode: ...` indiquant le mode effectif :
  - `Mode: dry run (no file writes)`
  - `Mode: apply (may overwrite destination files)`

**Rendu (exemple)**
```
** BareSync **

Operation: {OperationTitle}
Mode: {ModeLine|optional}

Progress: Processed {Processed}/{Total|?}
Elapsed: {HH:MM:SS}
Last: {LastLine|optional}
Current: {CurrentItem|optional}
```

### S1.9 — Operation Result (retour menu + statut)

**Intention**
- Après une opération : afficher un statut court dans le menu précédent (ex : footer / ligne d’état).

**Rendu (exemple, intégré au menu)**
```
Last status: {Success|Warning|Fail|Canceled} — {StatusLine}
```

## 5) Écrans — Batch mode

Règles normatives (portée) :
- `Batch mode` DOIT fournir : lister, créer, éditer, sauvegarder et exécuter un batch.
- `Batch mode` NE DOIT PAS fournir : suppression de batch, duplication de batch, templates.
- `Batch mode` NE DOIT PAS présenter la configuration interactive comme “état courant” ; il n’affiche que les valeurs du batch sélectionné (sauf écran de copie snapshot).

### S2.1 — Batch Home

**Menu**
- `1. List batches`
- `2. Create new batch` (-> `S2.1a`)
- `0. Back` (retour `S0.1`)

**Rendu (exemple)**
```
** BareSync **

** Batch mode **

** Menu **

1. List batches
2. Create new batch
0. Back

Select an option:
```

### S2.1a - Batch / Create (name prompt)

**Intention**
- Create a new batch: ask for a name, then open `S2.3` for that batch.

**Rules**
- Empty input cancels: no batch is created.

**Rendu (exemple)**
```
** BareSync **

** Batch / Create **

Enter batch name (empty to cancel):
```

### S2.2 — Batch List

**Contenu**
- Liste triée (nom insensible casse, puis id).
- Chaque ligne affiche : `Name`, `IdShort`, `Steps`, `Validity`.

**Rules**
- Si la liste est vide, l’écran DOIT afficher `(no batches)`.
- Si la liste est vide, l’action `Open batch` DOIT être affichée comme `disabled` (ou être explicitement absente).
- Si la liste est vide, `Next page` / `Previous page` DOIVENT être affichées comme `disabled` (ou être explicitement absentes).

**Menu**
- `1. Open batch` (-> `S2.2a`)
- `2. Next page`
- `3. Previous page`
- `0. Back` (retour `S2.1`)

**Rendu (exemple)**
```
** BareSync **

** Batch / List **

Page {Current}/{Total}

1) {BatchNameA}  [{IdShortA}]  steps={3}  status={Valid}
2) {BatchNameB}  [{IdShortB}]  steps={5}  status={Invalid}
3) {BatchNameC}  [{IdShortC}]  steps={0}  status={Valid}

** Menu **

1. Open batch
2. Next page
3. Previous page
0. Back

Select an option:
```

**Rendu (exemple, liste vide)**
```
** BareSync **

** Batch / List **

Page 1/1

(no batches)

** Menu **

1. Open batch (disabled)
2. Next page (disabled)
3. Previous page (disabled)
0. Back

Select an option:
```

### S2.2a - Batch List / Select batch (current page)

**Intention**
- Select a batch from the currently displayed page.

**Rendu (exemple)**
```
** BareSync **

Select batch number (1..{PageCount}, 0 to cancel):
```

### S2.3 — Batch Details (hub)

**Intention**
- Point central pour un batch : consulter, éditer, lancer.

**Menu**
- `1. Edit identity (name/description)`
- `2. Edit batch context (defaults)`
- `3. Edit steps`
- `4. Run (preflight)`
- `5. Show validity details` (si Invalid/Incompatible)
- `0. Back` (retour `S2.2`)

**Rendu (exemple)**
```
** BareSync **

** Batch / Details **

Name: {BatchName}
Id:   {BatchId}
Desc: {BatchDescription|<empty>}
Steps: {StepCount}
Status: {Valid|Invalid|Incompatible}

** Menu **

1. Edit identity (name/description)
2. Edit batch context (defaults)
3. Edit steps
4. Run (preflight)
5. Show validity details
0. Back

Select an option:
```

### S2.4 — Batch Identity Editor

**Menu**
- `1. Edit name`
- `2. Edit description`
- `3. Save`
- `0. Back`

**Rendu (exemple)**
```
** BareSync **

** Batch / Identity **

Name: {BatchName}
Desc: {BatchDescription|<empty>}

** Menu **

1. Edit name
2. Edit description
3. Save
0. Back

Select an option:
```

### S2.5 — Batch Context Editor (defaults)

**Contenu**
- Liste des champs de contexte batch (valeurs par défaut).

**Menu**
- `1..7. Edit field`
- `8. Copy from interactive settings (snapshot)`
- `9. Save`
- `0. Back`

**Rendu (exemple)**
```
** BareSync **

** Batch / Context (defaults) **

SourceRoot          = '{SourceRoot|<not set>}'
MirrorRoot          = '{MirrorRoot|<not set>}'
SourceIndexCsvPath  = '{SourceIndexCsvPath|<not set>}'
DestIndexCsvPath    = '{DestIndexCsvPath|<not set>}'
EncryptedOutputRoot = '{EncryptedOutputRoot|<not set>}'
RestoreRoot         = '{RestoreRoot|<not set>}'
SevenZipPath        = '{SevenZipPath|<not set>}'

** Menu **

1. Edit SourceRoot
2. Edit MirrorRoot
3. Edit SourceIndexCsvPath
4. Edit DestIndexCsvPath
5. Edit EncryptedOutputRoot
6. Edit RestoreRoot
7. Edit SevenZipPath
8. Copy from interactive settings (snapshot)
9. Save
0. Back

Select an option:
```

### S2.5a — Batch Context / Copy Snapshot Confirm

**Intention**
- Confirmer l’écrasement potentiel des valeurs du contexte batch par une copie snapshot depuis l’Interactive Context.

**Rendu (exemple)**
```
** BareSync **

Copy interactive settings into batch context (snapshot).
This may overwrite existing batch values.

Proceed? (y/n):
```

### S2.6 — Batch Steps Editor (liste)

**Menu**
- `1. Add step`
- `2. Edit step` (-> `S2.6a`)
- `3. Remove step` (-> `S2.6a`)
- `4. Reorder steps` (-> `S2.6a`)
- `5. Append steps from existing batch`
- `6. Next page`
- `7. Previous page`
- `0. Back`

Temporary note (until S2.7+ is implemented):
- Options `1..4` MAY display `Not implemented yet.` and remain on `S2.6`.

**Rendu (exemple)**
```
** BareSync **

** Batch / Steps **

Page {Current}/{Total}

1) {OpType1}  {overrides: {FieldA,FieldB}|<none>}
2) {OpType2}  {overrides: <none>}
3) {OpType3}  {overrides: {EncryptedOutputRoot}}

** Menu **

1. Add step
2. Edit step
3. Remove step
4. Reorder steps
5. Append steps from existing batch
6. Next page
7. Previous page
0. Back

Select an option:
```

### S2.6a — Step Number Prompt (sélection d’étape)

**Intention**
- Sélectionner une étape par son index (1..N) pour les actions `Edit/Remove/Reorder`.

Regles normatives :
- La saisie DOIT etre un entier.
- `0` DOIT annuler et retourner a `S2.6` sans modification.
- Une valeur hors de `1..{N}` DOIT etre rejetee et le prompt DOIT etre re-affiche.
- Les numeros d'etape correspondent a l'index global de l'etape dans le batch (1-based), pas a l'index de page.

**Rendu (exemple)**
```
** BareSync **

Enter step number (1..{N}, 0 to cancel):
```

### S2.6b — Append Batch Selector (current list)

**Intention**
- L'utilisateur saisit l'index d'un autre batch pour en copier les étapes à la suite du batch courant.

**Regles normatives** :
- La liste des autres batchs valides DOIT être affichée (sans pagination, index allant de 1 au nombre de batchs).
- `0` DOIT annuler et retourner à `S2.6` sans modification.
- Une erreur DOIT être affichée si aucun autre batch valide n'est trouvé.

**Rendu (exemple)**
```
** BareSync **

** Select a batch **
1. Nightly sync (c0f5d7a0)
2. Weekly backup (8f6c6d30)
0. Cancel
Choice: 
```

### S2.7 — Step Type Picker (Add step)

**Menu**
- `1. Refresh indexes (full)`
- `2. Refresh indexes (smart)`
- `3. One-way sync (dry run)`
- `4. One-way sync (apply)`
- `5. Create encrypted folder`
- `6. Refresh encrypted folder`
- `7. Restore encrypted files`
- `0. Back`

**Rendu (exemple)**
```
** BareSync **

** Step / Select operation **

** Menu **

1. Refresh indexes (full)
2. Refresh indexes (smart)
3. One-way sync (dry run)
4. One-way sync (apply)
5. Create encrypted folder
6. Refresh encrypted folder
7. Restore encrypted files
0. Back

Select an option:
```

### S2.8 — Step Editor (paramètres + overrides)

**Intention**
- Éditer les paramètres d’opération (ex : dryRun) et les surcharges de contexte.

**Menu**
- `1. Edit operation parameters`
- `2. Edit context overrides`
- `3. Save`
- `0. Back`

**Rendu (exemple)**
```
** BareSync **

** Step / Edit **

Type: {OpType}

Operation params:
  {ParamA}={ValueA}

Context overrides:
  {FieldX}='{ValueX}'

** Menu **

1. Edit operation parameters
2. Edit context overrides
3. Save
0. Back

Select an option:
```

### S2.8a - Step / Operation parameters

**Intention**
- Edit operation parameters when the chosen operation type exposes any.

**Rules**
- For operation types where all parameters are fixed by the type, this screen must display `(none)` and return without changes.

**Rendu (exemple)**
```
** BareSync **

** Step / Operation parameters **

Type: {OpType}

Parameters:
  (none)

0. Back
```

### S2.9 — Step Overrides Editor

> **Note:** Saisie des chemins : l'édition des champs de type chemin (SourceRoot, MirrorRoot, SourceIndexCsvPath, DestIndexCsvPath, EncryptedOutputRoot, RestoreRoot, SevenZipPath) **DOIT** utiliser les contrôles de sélection interactifs (`IPathPromptService`).

**Menu**
- `1..7. Edit override field`
- `8. Clear one override`
- `9. Clear all overrides`
- `0. Back`

**Rendu (exemple)**
```
** BareSync **

** Step / Overrides **

Effective defaults come from batch context. Overrides replace defaults for this step only.

SourceRoot          = '{OverrideOr<inherit>}'
MirrorRoot          = '{OverrideOr<inherit>}'
SourceIndexCsvPath  = '{OverrideOr<inherit>}'
DestIndexCsvPath    = '{OverrideOr<inherit>}'
EncryptedOutputRoot = '{OverrideOr<inherit>}'
RestoreRoot         = '{OverrideOr<inherit>}'
SevenZipPath        = '{OverrideOr<inherit>}'

** Menu **

1. Edit SourceRoot
2. Edit MirrorRoot
3. Edit SourceIndexCsvPath
4. Edit DestIndexCsvPath
5. Edit EncryptedOutputRoot
6. Edit RestoreRoot
7. Edit SevenZipPath
8. Clear one override
9. Clear all overrides
0. Back

Select an option:
```

### S2.10 — Step Reorder

**Menu**
- `1. Move step up`
- `2. Move step down`
- `3. Save`
- `0. Back`

**Rendu (exemple)**
```
** BareSync **

** Steps / Reorder **

Selected step: #{k} — {OpTypeK}

** Menu **

1. Move step up
2. Move step down
3. Save
0. Back

Select an option:
```

### S2.11 — Step Remove (confirm)

**Rendu (exemple)**
```
** BareSync **

Remove step #{k} — {OpTypeK} ?

Proceed? (y/n):
```

### S2.12 — Batch Preflight (plan summary)

**Contenu**
- Liste des étapes avec paramètres significatifs effectifs.
- Indique : confirmation requise / secret requis (par étape).

**Menu**
- `1. Run` (if no step requires confirmation; otherwise the label is `Confirm & run`)
- `2. Back to batch`
- `3. Next page`
- `4. Previous page`
- `0. Back` (alias `2`)

**Rules**
- `S2.12` is a menu screen (single digit input `0..9`, see 1.2).
- Selecting option `1` MUST start the run:
  - If at least one step requires confirmation: show a global `Proceed? (y/n):` prompt; `n` returns to `S2.12` with no execution.
  - Otherwise: no confirmation prompt; continue to secrets (if any) then execution.

**Rendu (exemple)**
```
** BareSync **

** Batch / Preflight **

Batch: {BatchName} [{IdShort}]

Page {Current}/{Total}

Step {i}: {OpTypeI}  params: {ParamSummary}  requiresConfirmation={yes|no}  requiresSecret={yes|no} {SecretSlotHint|optional}
Step {i+1}: {OpTypeI+1}  params: {ParamSummary}  requiresConfirmation={yes|no}  requiresSecret={yes|no} {SecretSlotHint|optional}
...

** Menu **

1. {Run|Confirm & run}
2. Back to batch
3. Next page
4. Previous page
0. Back

Select an option:
```

### S2.12a — Batch Preflight Errors (batch non exécutable)

**Intention**
- Présenter une liste d’erreurs actionnables (étape + champ + raison) et guider vers l’édition.

**Menu**
- `1. Back to batch details`
- `2. Edit batch context`
- `3. Edit steps`
- `4. Next page`
- `5. Previous page`
- `0. Back`

**Rendu (exemple)**
```
** BareSync **

** Batch / Preflight (FAILED) **

Page {Current}/{Total}

Step {k}: Missing field: {FieldX} - {Reason}
Step {k2}: Invalid value: {FieldY}='{Value}' - {Reason}
...

** Menu **

1. Back to batch details
2. Edit batch context
3. Edit steps
4. Next page
5. Previous page
0. Back

Select an option:
```

### S2.13 — Batch Secret Prompt(s)

**Intention**
- Demander les secrets requis (apres l'action `Run`/`Confirm & run` ; apres confirmation lorsque celle-ci est requise).

Regles normatives :
- La saisie DOIT etre non echo.
- Une saisie vide ou `ESC` DOIT annuler le demarrage du run et retourner a `S2.12` (aucune etape executee).

**Rendu (exemple)**
```
** BareSync **

Secret required: {EncryptionPassword} for scope {EncryptedOutputRoot}
Enter password (will not be echoed):
```

### S2.14 — Batch Run Progress (par étape)

**Intention**
- Afficher l’avancement pendant l’exécution d’un batch.

Regles normatives :
- `ESC` DOIT annuler l'etape en cours. Le run DOIT se terminer avec statut global `Canceled` et les etapes restantes `NotRun`, puis afficher `S2.15`.

**Rendu (exemple)**
```
** BareSync **

** Batch / Running **

Batch: {BatchName} [{IdShort}]
Step:  {i}/{N} — {OpTypeI}

Operation: {OperationTitle}
Progress: Processed {Processed}/{Total|?}
Elapsed: {HH:MM:SS}
Current: {CurrentItem|optional}
```

### S2.15 — Batch Run Summary

**Contenu**
- Résumé final par étape (statut + message + artifacts).

**Menu**
- `1. View artifacts`
- `2. Back to batch`
- `3. Next page`
- `4. Previous page`
- `0. Back` (alias `2`)

**Rendu (exemple)**
```
** BareSync **

** Batch / Summary **

Batch: {BatchName} [{IdShort}]
Status: {Success|Warning|Fail|Canceled}

Page {Current}/{Total}

1) {OpType1} — {Success}  — {Message1}
2) {OpType2} — {Warning}  — {Message2}
3) {OpType3} — {NotRun}   — {ReasonNotRun}

Artifacts:
  - {PathA|optional}
  - {PathB|optional}

** Menu **

1. View artifacts
2. Back to batch
3. Next page
4. Previous page
0. Back

Select an option:
```

### S2.15a — Batch Artifacts (détails)

**Intention**
- Voir les artifacts par étape (si présents).

**Menu**
- `1. View step artifacts` (-> `S2.15a1`)
- `2. Next page`
- `3. Previous page`
- `0. Back`

**Rendu (exemple)**
```
** BareSync **

** Batch / Artifacts **

Page {Current}/{Total}

1) {OpType1} — {artifactCount1}
2) {OpType2} — {artifactCount2}
3) {OpType3} — {artifactCount3}

...

** Menu **

1. View step artifacts
2. Next page
3. Previous page
0. Back

Select an option:
```

### S2.15a1 - Batch Artifacts / Step Number Prompt

**Intention**
- Select a step by its global index (1..N) to view its artifacts.

Regles normatives :
- La saisie DOIT etre un entier.
- `0` DOIT annuler et retourner a `S2.15a`.
- Une valeur hors de `1..{N}` DOIT etre rejetee et le prompt DOIT etre re-affiche.

**Rendu (exemple)**
```
** BareSync **

Enter step number (1..{N}, 0 to cancel):
```

### S2.15b — Batch Artifacts / Step Details

**Rendu (exemple)**
```
** BareSync **

** Artifacts / Step {k} **

{OpTypeK}

Page {Current}/{Total}

- {ArtifactPath1}
- {ArtifactPath2}
...

** Menu **

1. Next page
2. Previous page
0. Back

Select an option:
```

### S2.16 — Batch Validity Details (Invalid/Incompatible)

**Intention**
- Expliquer pourquoi un batch n’est pas exécutable.

**Rendu (exemple)**
```
** BareSync **

** Batch / Validity details **

Status: {Invalid|Incompatible}
Reason: {ParseError|UnsupportedSchema|...}

Suggested action:
  - {Edit|Ignore|Upgrade BareSync|Recreate batch}

0. Back
```

### S2.17 — Unsaved Changes Confirm (éditeur)

**Intention**
- Lorsqu’un utilisateur quitte un éditeur avec des modifications non sauvegardées.

**Rendu (exemple)**
```
** BareSync **

You have unsaved changes.

Discard changes? (y/n):
```

## 6) Navigation — règles (résumé)

Règles normatives :
- `0. Back` remonte d’un niveau (sauf `S0.1` où c’est `Exit`).
- Après une opération, retour à l’écran appelant avec un statut de dernière exécution.
- `Batch/List` → `Batch/Details` est l’entrée unique pour éditer/lancer un batch.

### 6.1) Navigation matrix (menus -> screens)

Notation:
- `X -> Y` : transition vers l'écran `Y`.
- `prompt(...)` : écran de saisie (primitives ou prompts dédiés).

#### Main

- `S0.1`:
  - `1` -> `S1.1`
  - `2` -> `S2.1`
  - `0` -> Exit

#### Interactive

- `S1.1`:
  - `1` -> `S1.2`
  - `2` -> `S1.3`
  - `3` -> `S1.4`
  - `4` -> `S1.5`
  - `0` -> `S0.1`

- `S1.2`:
  - `1` -> `S1.8` (si settings OK) sinon `S1.5a`
  - `2` -> `S1.8` (si settings OK) sinon `S1.5a`
  - `0` -> `S1.1`

- `S1.3`:
  - `1` -> `S1.8` (si settings OK) sinon `S1.5a`
  - `2` -> `S1.6` -> `S1.8` (si settings OK) sinon `S1.5a`
  - `0` -> `S1.1`

- `S1.4`:
  - `1` -> `S1.6` -> `S1.7` -> `S1.8` (si settings OK) sinon `S1.5a`
  - `2` -> `S1.6` -> `S1.7` -> `S1.8` (si settings OK) sinon `S1.5a`
  - `3` -> `S1.6` -> `S1.7` -> `S1.8` (si settings OK) sinon `S1.5a`
  - `0` -> `S1.1`

- `S1.5`:
  - `1..7` -> `prompt(picker/text)` -> retour `S1.5`
  - `0` -> `S1.1`

- `S1.5a`:
  - `1` -> `S1.5`
  - `0` -> retour écran appelant

#### Batch

- `S2.1`:
  - `1` -> `S2.2`
  - `2` -> `S2.1a`
  - `0` -> `S0.1`

- `S2.1a`:
  - `prompt(name)` -> `S2.3` (si non vide)
  - `cancel` -> `S2.1`

- `S2.2`:
  - `1` -> `S2.2a` -> `S2.3` (batch sélectionné, si la page contient au moins un batch)
  - `2` -> next page -> `S2.2`
  - `3` -> previous page -> `S2.2`
  - `0` -> `S2.1`

- `S2.2a`:
  - `prompt(batch number)` -> `S2.3` (si valide)
  - `cancel` -> `S2.2`

- `S2.3`:
  - `1` -> `S2.4`
  - `2` -> `S2.5`
  - `3` -> `S2.6`
  - `4` -> `S2.12` (préflight)
  - `5` -> `S2.16` (si status invalid/incompatible)
  - `0` -> `S2.2`

- `S2.4`:
  - `1` -> `prompt(P1)` -> retour `S2.4`
  - `2` -> `prompt(P1)` -> retour `S2.4`
  - `3` -> save -> `S2.3`
  - `0` -> `S2.17` si unsaved, sinon `S2.3`

- `S2.5`:
  - `1..7` -> `prompt(picker/text)` -> retour `S2.5`
  - `8` -> `S2.5a` -> (copy) -> retour `S2.5`
  - `9` -> save -> `S2.3`
  - `0` -> `S2.17` si unsaved, sinon `S2.3`

- `S2.6`:
  - `1` -> `S2.7`
  - `2` -> `S2.6a` -> `S2.8`
  - `3` -> `S2.6a` -> `S2.11`
  - `4` -> `S2.6a` -> `S2.10`
  - `5` -> `S2.6b` -> `S2.6`
  - `6` -> next page -> `S2.6`
  - `7` -> previous page -> `S2.6`
  - `0` -> `S2.17` si unsaved, sinon `S2.3`

- `S2.7`:
  - `1..7` -> `S2.8` (nouvelle étape)
  - `0` -> `S2.6`

- `S2.8`:
  - `1` -> `S2.8a`
  - `2` -> `S2.9`
  - `3` -> save -> `S2.6`
  - `0` -> `S2.17` si unsaved, sinon `S2.6`

- `S2.8a`:
  - `0` -> `S2.8`

- `S2.9`:
  - `1..7` -> `prompt(picker/text)` -> retour `S2.9`
  - `8` -> `prompt(step override to clear)` -> retour `S2.9`
  - `9` -> `prompt(confirm clear all)` -> retour `S2.9`
  - `0` -> `S2.8`

- `S2.10`:
  - `1/2` -> move -> reste `S2.10`
  - `3` -> save -> `S2.6`
  - `0` -> `S2.6`

- `S2.11`:
  - `y` -> remove -> `S2.6`
  - `n` -> `S2.6`

- `S2.12`:
  - `preflight ok` -> afficher plan + menu:
    - `1` -> (si confirmation requise : prompt `Proceed? (y/n):`, `n` -> `S2.12`) -> `S2.13` si secret requis sinon `S2.14`
    - `2/0` -> `S2.3`
    - `3` -> next page -> `S2.12`
    - `4` -> previous page -> `S2.12`
  - `preflight fail` -> `S2.12a`

- `S2.12a`:
  - `1` -> `S2.3`
  - `2` -> `S2.5`
  - `3` -> `S2.6`
  - `4` -> next page -> `S2.12a`
  - `5` -> previous page -> `S2.12a`
  - `0` -> `S2.3`

- `S2.13`:
  - `prompt(secret)` -> `S2.14`
  - `cancel` -> `S2.12`

- `S2.14`:
  - `done` -> `S2.15`
  - `canceled` -> `S2.15` (statut global canceled + NotRun)

- `S2.15`:
  - `1` -> `S2.15a`
  - `2/0` -> `S2.3`
  - `3` -> next page -> `S2.15`
  - `4` -> previous page -> `S2.15`

- `S2.15a`:
  - `1` -> `S2.15a1` -> `S2.15b`
  - `2` -> next page -> `S2.15a`
  - `3` -> previous page -> `S2.15a`
  - `0` -> `S2.15`

- `S2.15a1`:
  - `prompt(step number)` -> `S2.15b` (si valide)
  - `cancel` -> `S2.15a`

- `S2.15b`:
  - `1` -> next page -> `S2.15b`
  - `2` -> previous page -> `S2.15b`
  - `0` -> `S2.15a`

- `S2.16`:
  - `0` -> `S2.3`

- `S2.17`:
  - `y` -> discard -> back target
  - `n` -> return editor

### 6.2) Retour d'opérations (statut)

Règle normative:
- Après `S1.8` (interactive) ou `S2.14` (batch), l'écran suivant doit afficher un "last status" (cf. `S1.9`) sur l'écran parent.

### 6.3) Règles d'erreur (routing)

- Validation settings interactive: utiliser `S1.5a` et proposer `S1.5`.
- Préflight batch non exécutable: utiliser `S2.12a` et proposer correction via `S2.5` / `S2.6`.

## 7) Scénarios d’usage (enchaînements d’écrans)

Notation : `A -> B -> C` (transitions).

### Scenario A - Lancer une operation simple (interactive)

`S0.1 -> S1.1 -> S1.2 -> S1.8 -> S1.2 -> S1.1 -> S0.1`

### Scenario B - Operation bloquee (settings invalides) puis correction

`S0.1 -> S1.1 -> S1.2 -> S1.5a -> S1.5 -> S1.2 -> S1.8 -> S1.2`

### Scenario C - Sync apply (interactive, confirmation)

`S0.1 -> S1.1 -> S1.3 -> S1.6 -> S1.8 -> S1.3 -> S1.1 -> S0.1`

### Scenario D - Restore (interactive, secret)

`S0.1 -> S1.1 -> S1.4 -> S1.6 -> S1.7 -> S1.8 -> S1.4 -> S1.1 -> S0.1`

### Scenario E - Creer un batch (copy snapshot) + ajouter 3 etapes + sauvegarder

`S0.1 -> S2.1 -> S2.1a -> S2.3 -> S2.5 -> S2.5a -> S2.5 -> save -> S2.3 -> S2.6 -> add -> S2.7 -> S2.8 -> save -> S2.6 (repeat add+edit 3x) -> save -> S2.3 -> S2.2`

### Scenario F - Executer un batch (preflight + secret + resume) + consulter artifacts

`S0.1 -> S2.1 -> S2.2 -> S2.2a -> S2.3 -> S2.12 -> S2.13 -> S2.14 -> S2.15 -> S2.15a -> S2.15a1 -> S2.15b -> S2.15a -> S2.15 -> S2.3 -> S2.2`

### Scenario G - Batch non executable (preflight fail -> correction -> retry)

`S0.1 -> S2.1 -> S2.2 -> S2.2a -> S2.3 -> S2.12 -> S2.12a -> S2.5/S2.6 -> save -> S2.3 -> S2.12`

### Scenario H - Batch invalide/incompatible (details)

`S0.1 -> S2.1 -> S2.2 -> S2.2a -> S2.3 -> S2.16 -> S2.3 -> S2.2`

### Scenario I - Annuler un batch run (cancel)

`S0.1 -> S2.1 -> S2.2 -> S2.2a -> S2.3 -> S2.12 -> S2.13 -> S2.14 (cancel) -> S2.15 -> S2.3`

### Scenario J - Quitter un editeur avec changements non sauvegardes

`S0.1 -> S2.1 -> S2.2 -> S2.2a -> S2.3 -> S2.6 -> (modify) -> 0.Back -> S2.17 (y/n) -> S2.3`

## 8) CLI /EXTRACT prompt flow (outside menu stack)

This section defines the expected console prompt flow when BareSync is started with `/EXTRACT:<path>`.

### 8.1) General rules

- This flow runs outside `S0.1`/`S1.*`/`S2.*` menu navigation.
- Source type is auto-detected from `<path>`:
  - single native `.bse` archive file,
  - or folder containing native `.bse` archives.
- Secret resolution always occurs before destination choices are displayed.

### 8.2) Secret resolution and anti-disclosure

Normative UI rules:
- Destination prompts MUST NOT be shown before secret validation succeeds.
- Secret resolution order is:
  1. try OS secret store,
  2. try empty secret (archive may be extractable without password),
  3. prompt masked password (with optional save to secret store).
- On password validation failure, the user can retry or cancel.

### 8.3) Folder source prompts

Expected console messaging:
- warning that folder extraction is recursive by default,
- default `Extract to` destination shown as a sub-folder,
- confirmation prompt to use default destination,
- optional custom destination path prompt.

### 8.4) File source prompts

Expected console messaging:
- default `Extract to` destination shown as a suggested sub-folder,
- confirmation prompt to use default destination,
- if declined, `Extract to` menu with:
  - current folder,
  - suggested sub-folder,
  - custom folder path,
  - cancel.

### 8.5) CLI extract outcomes

- Success: user receives a clear extraction status line.
- Failure: user receives an actionable status line (invalid source, wrong secret, CRC mismatch, missing index entry, etc.).
