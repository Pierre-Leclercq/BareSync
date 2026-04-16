# BareSync — Specs Progress (tampon cognitif)

Ce document est **non normatif**. Il sert à capturer les éléments non stabilisés, les hypothèses et les décisions repoussées.

## Journal des itérations (Specs.md)

### Itération 1 — 1er jet (structure + concepts)

Déplacé ici (non stabilisé à ce stade) :
- Format exact de persistance des batchs (un fichier vs plusieurs, structure, canonicalisation).
- Stratégie précise de gestion des secrets en batch (prompt par étape vs mutualisation).
- Politique “stop-on-fail” configurable ou non (par défaut stop).

### Itération 2 — Modèle de contexte + catalogue minimal

Décidé/stabilisé dans la spec :
- Un batch persisté est **autoportant** : pas de dépendance implicite à la configuration interactive au moment de l’exécution.
- Ajout d’un **catalogue minimal batchable** et des champs de contexte requis (niveau conceptuel).

Resté volontairement ouvert (donc maintenu ici) :
- UX exacte d’“import/copie” depuis la configuration interactive lors de la création/édition (convenience).
- Politique détaillée de confirmation (par étape vs confirmation globale après préflight, et règles).

### Itération 3 — États/transitions + runner + statuts

Décidé/stabilisé dans la spec :
- Définition de statuts normalisés (Success/Warning/Fail/Canceled/NotRun).
- Modèle conceptuel “Batch Run” séparé de la définition.
- Transitions minimales attendues (MainMenu ↔ modes, Batch/List/Editor/Runner).
- Politique d’exécution : séquentiel ; stop sur Fail/Canceled ; Warning continue.

Resté volontairement ouvert :
- Persistance de l’historique des runs (conservé ou non, et si oui où/comment).

### Itération 4 — Invariants/contraintes renforcés

Décidé/stabilisé dans la spec :
- Invariants explicites : un batch run ne modifie pas la définition ; batch mode ne modifie pas la config interactive.
- Contraintes de compatibilité : batchs versionnés, gestion d’incompatibilité/corruption sans bloquer la bibliothèque.
- Exigence d’ordre de liste déterministe (prévisibilité).

Resté volontairement ouvert :
- Choix exact du tri par défaut (et éventuelles options de tri côté UX).

### Itération 5 — Critères mesurables + tri par défaut

Décidé/stabilisé dans la spec :
- Critères de succès reformulés en critères testables (navigation, persistance, préflight, exécution, sécurité).
- Tri par défaut de la liste des batchs : nom (insensible à la casse), puis identifiant.

Resté volontairement ouvert :
- Détails UX du prompt secret (moment exact, mutualisation) tant que la contrainte “non persisté” est respectée.

### Itération 6 — Persistance & nommage (BatchStoreRoot + identité)

Décidé/stabilisé dans la spec :
- La persistance des batchs est sous `AppDataRoot`, dans un `BatchStoreRoot` dédié (séparé de la config interactive).
- Stockage conceptuel par unités indépendantes (un batch corrompu n’empêche pas l’accès aux autres).
- Règles d’identité/noms : BatchId unique/stable, BatchName obligatoire mais non unique, désambiguïsation via id court.
- Statuts statiques des définitions : Valid / Invalid / Incompatible.
- Exigence crash-safe explicitée (pas d’état partiellement écrit sans signalement).

Resté volontairement ouvert :
- Nom exact et convention de nommage du répertoire `BatchStoreRoot`.

### Itération 7 — UX Batch (liste/éditeur/runner) + secrets

Décidé/stabilisé dans la spec :
- Écran de liste : affiche statut de validité et désambiguïsation.
- Éditeur : “copie” depuis Interactive Context = snapshot (sans lien dynamique) + confirmation si écrasement.
- Runner structuré (préflight → confirmation → secrets runtime → exécution → résumé).
- Secrets : jamais persistés, jamais affichés en clair ; réutilisation possible au sein d’un run.

Resté volontairement ouvert :
- Niveau de détail des écrans “détails” et navigation fine (ex : drill-down complet sur paramètres effectifs).
- Comportement UX en cas d’échec lié au secret (proposer “réessayer le secret” ?).

### Itération 8 — Paramètres significatifs + règles de préflight + scope secret

Décidé/stabilisé dans la spec :
- Définition des paramètres “significatifs” et liste minimale par opération (7.4).
- Matrice minimale de validation préflight par opération (7.5).
- Scope/slots de secrets : (`EncryptionPassword`, `EncryptedOutputRoot` effectif) recommandé par défaut (7.6).
- Concept d’artifacts (6.5.3) et affichage des chemins en post-run.

Resté volontairement ouvert :
- Validation plus fine des chemins de sortie (création de répertoires, droits, collisions) : à préciser sans figer d’implémentation.

### Itération 9 — Clôtures fonctionnelles (BatchStoreRoot / confirmations / historique)

Décidé/stabilisé dans la spec :
- `BatchStoreRoot` fixé à `AppDataRoot/batches` et non configurable.
- Confirmation batch : unique et globale après préflight (si requise).
- Secrets : slot normatif + réutilisation obligatoire au sein d’un run ; pas de ré-essai automatique dans le même run.
- Historique persistant des Batch Runs : hors portée (résumé du run courant uniquement).
- `Interactive mode` : validation préflight par opération (7.5) + guidage vers `Edit settings` en cas d’échec.

### Itération 11 — Finalisation CLI /EXTRACT (spec normative)

Décidé/stabilisé dans la spec :
- Ajout d’un mode normatif `9.3) CLI /EXTRACT mode`.
- Règle d’exclusivité explicite : `/BATCH` et `/EXTRACT` ne peuvent pas coexister sur une même commande.
- Anti-divulgation UX : les options de destination ne sont exposées qu’après validation du secret.
- Résolution secret documentée dans l’ordre : vault -> tentative sans secret -> prompt masqué (+ sauvegarde optionnelle).
- UX extraction alignée archive tools :
  - source dossier : extraction récursive par défaut vers sous-dossier proposé,
  - source fichier : destination par défaut `Extract to` + alternatives.
- Clarification normative d’échec actionnable si la source ne contient aucune archive native exploitable.

Correctif de structure appliqué :
- `9.2.2 Runner` a été remis dans son ordre complet A -> E.
- `9.3 CLI /EXTRACT` est désormais une section séparée cohérente.

## Points ouverts

- Faut-il permettre une configuration avancée de l’emplacement du `BatchStoreRoot` (délibérément hors portée) ?
- Détails UI : niveau de drill-down dans le préflight (affichage compact vs écran détaillé par étape).
- Validation plus fine des chemins de sortie : droits, collisions, écrasements implicites (au-delà du “créable / non-fichier”).

## Hypothèses à valider

- (aucune)

## Idées envisagées mais non retenues (pour l’instant)

- Exécution parallèle d’étapes (rejettée : complexité, risque d’effets de bord, déterminisme).
- “Batch templates” et système de plugins (hors portée).
- Mécanisme de scheduling (hors portée).

## Décisions repoussées

- Schéma de sérialisation exact (et stratégie d’évolution/versioning).
- Stratégie d’historique des exécutions de batch (conserver les runs ? où ?).
- Options avancées d’exécution (continue-on-fail, retries, timeouts).

## TODO (specs)

- Affiner les validations préflight sur les chemins (collisions, impossibilités d’écriture, répertoires non créables) sans figer de mécanique.
- Préciser la granularité minimale des “détails” d’une étape en préflight (affichage complet des valeurs effectives + surcharges).
- (aucun TODO fonctionnel bloquant identifié à ce stade ; le reste est UI/ergonomie ou hors portée)
