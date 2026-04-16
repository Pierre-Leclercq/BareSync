# BareSync — UI Specs Progress (tampon cognitif)

Ce document est **non normatif**. Il capture les points UI non stabilisés, alternatives, hypothèses et TODO.

## Journal des itérations (UI Specs)

### Itération 1 — Catalogue initial des écrans

Ajouté dans `BareSync.UI.Specs.md` :
- Écrans main/interactive/batch avec rendus texte (mock) et menus.
- Écrans génériques : confirmation, secret prompt, progress, summary.
- Scénarios d’usage (enchaînements d’écrans).

Resté ouvert :
- Granularité exacte des écrans d’édition (faut-il découper davantage : 1 écran par champ vs éditeur multi-champs).
- Détails d’ergonomie console : pagination, recherche, sélection par index vs par nom.
- Gestion UX d’annulation en cours de batch (ESC) : écran intermédiaire de confirmation d’annulation ?

### Itération 2 — Compléments d’écrans (create/select/preflight/artifacts)

Ajouté/clarifié dans `BareSync.UI.Specs.md` :
- Primitives réutilisables (saisie texte, pickers).
- Création batch avec prompt de nom (`S2.1a`).
- Sélection d’étape par numéro (`S2.6a`).
- Préflight errors (`S2.12a`).
- Détails artifacts (`S2.15a` / `S2.15b`).
- Écran paramètres d’opération (`S2.8a`) pour couvrir le menu “Edit operation parameters”.

Resté ouvert :
- Règles exactes de pagination (quand N dépasse la hauteur console).
- Règles d’affichage “last status” (toujours visible vs contextualisé).

### Itération 3 — Navigation détaillée (menus -> écrans)

Ajouté/clarifié dans `BareSync.UI.Specs.md` :
- Matrice de navigation (par écran : option -> écran cible) + routing erreurs (validation settings, préflight fail).
- Clarification des retours post-opération avec "last status".

Resté ouvert :
- Gestion UX exacte des annulations (ex : cancel secret prompt, cancel operation en cours).

### Itération 4 — Scénarios d’usage (enchaînements)

Ajouté/clarifié dans `BareSync.UI.Specs.md` :
- Scénarios interactifs (succès, settings invalides, confirmation, secret).
- Scénarios batch (création, run avec artifacts, préflight fail, invalid/incompatible, cancel, unsaved changes).

Resté ouvert :
- Ajouter des scénarios détaillés “édition d’étape” (sélection via `S2.6a`, overrides via `S2.9`, reorder via `S2.10`) si on veut tester chaque sous-flux séparément.

### Itération 5 — Clôtures UI (langue / pagination / annulation / portée)

Décidé/stabilisé dans la spec :
- Menus à choix unique sur un chiffre `0..9` et `ESC` équivalent à `0` lorsqu’applicable.
- Pagination normative : 9 éléments par page pour les listes “1 ligne par élément”, avec indication `Page X/Y`.
- Règle `Last status` affichée par mode (hors écrans de progress).
- Langue UI : anglais (ASCII lorsque possible).
- Portée batch : pas de suppression/duplication/templates ; pas d’affichage de la configuration interactive comme “état courant”.
- Prompts multi-lignes : non retenus (P2 supprimé).

### Iteration 6 - Consolidation (listes paginees + navigation)

Ajoute/clarifie dans `BareSync.UI.Specs.md` :
- Normalisation des ecrans de liste (steps, preflight, errors, summary, artifacts) avec menus `Next page` / `Previous page`.
- Ajout de prompts dedies pour la selection : `S2.15a1`, `P3a`, `P4a`.
- Mise a jour de la navigation (6.1) et des scenarios (7) pour integrer `S2.2a` et `S2.15a1`.

### Iteration 11 - CLI /EXTRACT flow (outside menu stack)

Ajoute/clarifie dans `BareSync.UI.Specs.md` :
- section `## 8) CLI /EXTRACT prompt flow (outside menu stack)`.
- regle anti-disclosure explicite : aucune option de destination avant validation du secret.
- ordre de resolution secret documente : secret store -> tentative sans secret -> prompt masque (+ save optionnel).
- prompts destination aligns archive tools :
  - source dossier : warning recursion + destination `Extract to` par defaut,
  - source fichier : destination par defaut + menu alternatives (current/sub-folder/custom/cancel).
- outcomes explicites success/failure avec messages actionnables.

## Points ouverts

Reportés (hors portée actuelle) :
- Écran `Batch/Delete`, action `Duplicate batch`, templates.
- Écran de drill-down “détails étape” depuis le préflight.
- Ré-essai guidé du secret (mot de passe) dans le même run.
- Vues artifacts plus granulaires (par type d’artifact).

Ouverts (non bloquants) :
- Niveau de granularité de l’édition (un écran par champ vs édition groupée) si l’ergonomie console devient insuffisante.
- Stratégies de recherche/filtrage pour listes volumineuses (si besoin futur).

## Hypothèses

- Le style d’UI reste cohérent avec l’existant : header `** BareSync **`, menus numérotés, `0. Back`.
- Les écrans progress et prompts secrets restent réutilisables entre interactive et batch.

## TODO (UI)

- Ajouter des scénarios unitaires UI “édition d’étape” si nécessaire (sélection, overrides, reorder).
