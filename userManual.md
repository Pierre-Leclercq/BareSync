# BareSync — User Manual

Ce document donne un mode opératoire **pratique** pour utiliser BareSync, avec un focus sur :
- la mise en place d’un secret (mot de passe) dans le store OS,
- l’exécution d’un batch en ligne de commande avec `/BATCH:"..."`.

---

## 1) Point important sur le mode CLI `/BATCH`

En mode CLI, BareSync n’accepte que des arguments de type :

```text
/BATCH:<name>
```

Il n’existe pas d’argument `/PASSWORD:...` ou `/SECRET:...`.

Si le batch a une étape chiffrée (`CreateEncryptedFolder`, `RefreshEncryptedFolder`, `RestoreEncryptedFiles`), le mot de passe doit être récupéré depuis le **secret store OS** (vault).

---

## 2) Pré-requis

- .NET SDK installé.
- Batch déjà créé (dans BareSync) avec au moins une étape qui requiert un secret.
- `EncryptedOutputRoot` correctement défini (dans le contexte batch et/ou override d’étape).

> Le scope secret est lié à `EncryptedOutputRoot` effectif. Si ce chemin change, le slot secret change aussi.

---

## 3) Exemple complet : setup secret puis exécution CLI

### Étape A — Ouvrir BareSync en interactif

Depuis la racine du repo :

```bat
dotnet run --project src/BareSync/BareSync.csproj
```

### Étape B — Enregistrer le secret dans le vault pour le batch

1. Aller dans **Batch mode**.
2. Ouvrir le batch concerné.
3. Écran `Batch / Details` → choisir :
   - `8. Manage encryption secrets`
4. Sélectionner le scope proposé.
5. Entrer le mot de passe (masqué), puis valider.
6. Vérifier le message de confirmation (ex: `Secret saved in vault.`).

### Étape C — Lancer le batch en CLI

```bat
dotnet run --project src/BareSync/BareSync.csproj -- /BATCH:"MyBatch"
```

Résultat attendu :
- `** Batch CLI Summary **`
- statut `Success` / `Warning` / `Fail` selon le run,
- pas de prompt mot de passe en CLI si le secret est présent dans le vault.

---

## 4) Dépannage rapide

### Erreur : vault indisponible

Message typique :
- `Vault indisponible pour un batch nécessitant un secret.`

Causes possibles :
- secret store OS non disponible,
- variable d’environnement `BARESYNC_DISABLE_SECRET_STORE=1`.

### Erreur : secret manquant

Message typique :
- `Secret manquant dans le vault pour le scope ...`

Action :
- revenir en mode interactif,
- `Batch / Details` → `Manage encryption secrets`,
- enregistrer le secret pour le scope attendu.

### Erreur : argument non supporté

Message typique :
- `Unsupported argument: /PASSWORD:...`

Action :
- retirer cet argument,
- n’utiliser que `/BATCH:"..."` côté CLI.

---

## 5) Bonnes pratiques

- Ne pas stocker de mot de passe en clair dans scripts ou fichiers `.bat`.
- Garder un `EncryptedOutputRoot` stable pour éviter les erreurs de scope secret.
- Tester d’abord en interactif (préflight + secret OK), puis automatiser via CLI.

---

## 6) Vérification rapide après restore (Robocopy)

Lors de `RestoreEncryptedFiles`, BareSync réaligne le timestamp `LastWriteTimeUtc` sur la valeur attendue de l’index.

Ce réalignement est appliqué :
- sur les fichiers effectivement restaurés,
- **et aussi sur les fichiers “skippés”** (contenu déjà identique), afin de fiabiliser une vérification rapide basée sur les métadonnées.

Le statut de fin de run peut inclure des indicateurs utiles, par exemple :
- `skipped timestamp aligned <N>`
- `skipped timestamp align failures <M>` (si certains fichiers n’ont pas pu être réalignés, en mode best effort)

Commande recommandée pour une vérification rapide source vs dossier restauré :

```bat
robocopy "C:\All My Files" "D:\ExternalKey\Restored" /L /E /FFT /DST /XJ
```

Notes :
- `/L` : simulation (ne copie rien, affiche seulement les écarts),
- `/FFT` : tolérance timestamp compatible volumes non-NTFS,
- `/DST` : limite les faux écarts liés au décalage heure d’été/hiver,
- `/XJ` : ignore les jonctions pour éviter des parcours parasites.
