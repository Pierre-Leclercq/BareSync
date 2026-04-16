# Analyse Ergonomique des Écrans BareSync

D'après les spécifications dans `BareSync.UI.Specs.md` et `BareSync.UI.StateMachine.md`, on constate que le workflow actuel de l'application (TUI Console) est conçu de manière très "hiérarchique". Bien que ce soit très "propre" (chaque écran a sa petite responsabilité bien définie), cela génère beaucoup d'étapes (frappes claviers) et d'écrans pour réaliser des opérations simples. 

Voici un état des lieux des zones où une simplification amènerait un gros gain en confort utilisateur.

## 1. La Cascade de Sauvegarde (Le bouton "Save" redondant)

**Constat actuel :**
Dans plusieurs éditeurs (Identité `S2.4`, Contexte `S2.5`, Étapes `S2.6`), il y a toujours une étape explicite :
- Menu de l'éditeur : `1. Edit X` -> Remplir la valeur
- Retour au Menu de l'éditeur
- Devoir taper l'option : `Save` (ex: `S2.5` opt 9) pour appliquer les modifs au batch courant et revenir à l'écran détail
- Option alternative : `0. Back` qui demande confirmation `S2.17` s'il y a des modifications non-sauvegardées.

**Pourquoi c'est lourd :**
L'utilisateur a déjà modifié sa valeur, revenir en arrière avec `0` pour s'entendre dire "Vous avez des modifications, voulez vous oublier?" et devoir faire un `Save` explicite casse la fluidité. 
C'est comme devoir faire "Fichier > Enregistrer" dans chaque fenêtre de configuration d'un logiciel.

**Piste d'amélioration :**
- **Auto-Save sur le `0. Back` :** Puisque le Batch est unitairement sauvegardable (`context.SaveBatch`), on pourrait tout simplement auto-sauvegarder les changements validés lorsque l'on quitte l'écran via "0. Back", ou intégrer un système de transaction implicite. L'écrasement ou l'oubli des changements deviendrait une option mineure plutôt que l'inverse. L'option `Save` serait supprimée, et `S2.17` (Confirm discard) ne s'afficherait que si l'on a explicitement demandé "Discard".

## 2. Le Tunnel pour Éditer une Étape (Nidification)

**Constat actuel pour modifier une étape existante:**
Pour éditer `S2.6`, disons l'override `MirrorRoot` de l'étape 3 :
1. `S2.6` (Steps) -> Taper `2` (Edit step)
2. `S2.6a` (Prompt) -> Taper `3` (l'index de l'étape)
3. `S2.8` (Step detail) -> Taper `2` (Edit overrides)
4. `S2.9` (Overrides picker) -> Taper `2` (Edit MirrorRoot)
5. La popup interactive charge `MirrorRoot`... on valide.
6. Retour à `S2.9` -> Taper `0` (Back)
7. Retour à `S2.8` -> Taper `3` / `Save` (Save step) -> si on l'oublie, on a `S2.17`.
8. Retour à `S2.6`.

**C'est une expérience "Menu Hell" (L'enfer des menus). On tape 7 à 8 touches/menus pour changer un simple path.**

**Pistes d'amélioration :**
- Aplatir `S2.8` et `S2.9`. Plutôt que d'avoir un "hub" pour une seule step (`S2.8`) qui redirige vers les `Operation parameters` et les `Overrides`, tous les champs pourraient être listés *sur le même menu*. 
- Exemple: 
  - `1. Edit Op Params`
  - `2. Edit SourceRoot override`
  - `3. Edit MirrorRoot override` ... etc.
- Supprimer le "Save" intermédiaire sur l'étape.

## 3. Demander des Entiers de Sélection Séparément des Choix d'Actions

**Constat actuel sur les actions de listes (Edit / Remove / Reorder) :**
- Pour la suppression (`2.11`) ou le reorder (`2.10`), on doit d'abord sélectionner dans le menu l'action (`3. Remove step`), ce qui nous amène à un prompt séparé `S2.6a` ("Quel index d'étape voulez vous supprimer ?").

**Pistes d'amélioration :**
Une approche plus TUI-moderne consisterait à se servir des touches fléchées (ou à inverser le paradigme) : 
- L'utilisateur entre juste le numéro de l'étape qu'il veut manipuler `[1..N]` dans `S2.6`. 
- Cela le transporte directement sur `S2.8` (Aplatit) où il trouve les actions (ex: `8. Move Up`, `9. Move Down`, `D. Delete step`).
- Les options de liste (`1. Add`, `5. Append from batch`) restent les options génériques quand on ne sélectionne pas d'étape.

*Alternative très fluide (mais nécessitant une refonte Controller) :* Coder un List-Selector interactif basé sur `Console.ReadKey()` où l'on déplace la sélection avec les flèches haut/bas, et où taper `E` fait Edit, `DEL` fait Remove, etc. (Comme Midnight Commander). La librairie TUI custom semble déjà capable de dessiner des pickers.

## 4. Préflight & Exécution (Découplage)

**Constat actuel :**
- `S2.3` (Hub) -> On sélectionne `4. Run`.
- On arrive sur `S2.12` (Preflight). Si tout est bon, c'est juste un résumé informatif. On doit de nouveau appuyer sur `1. Confirm & run`.

**Piste d'amélioration :**
Ne pas bloquer sur un écran pour le résumé s'il n'y a pas d'erreurs ou de `risky confirmation` / `secrets`.
- Si `S2.12` indique que la Batch est sûre et ne nécessite ni secret ni confirmation de risque, alors on pourrait avoir une option `Run Directly` depuis `S2.3` qui bypass `S2.12` et exécute automatiquement. Mieux, on intègre the Preflight UI *à l'intérieur* de l'écran principal de Run, pour que la touche Entrée déclenche la suite de manière fluide.

## Synthèse Visuelle des Simplifications Proposées :

1. **Suppression universelle des boutons "Save"** intermédiaires. Modification = Auto-Save ou retour au parent avec le delta state en "commit in-memory".
2. **Fusion de `S2.8`, `S2.8a` et `S2.9` :** Un seul grand éditeur de configuration de l'étape au lieu d'une hiérarchie.
3. **Optimiser `S2.6` (Steps Editor) :** Permettre l'input direct de l'index de l'étape dans la "console line" du menu (ex: "Tapez `A` pour Ajouter, ou le `{Numéro}` de l'étape pour la manipuler ou l'éditer".) Cela retire l'écran `S2.6A`. 

En implémentant ces simples réajustements de routage, on réduirait de ~40% le nombre de pressions de touches ("Keystrokes") nécessaires pour concevoir une Batch.
