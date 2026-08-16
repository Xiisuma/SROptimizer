# SROptimizer — Plan d'implémentation

Mod d'optimisation pour Slime Rancher v1.4.4 (Unity 2019.4.29f1, Mono, x86), livré sous forme de `SROptimizer.dll` chargé par SRML.

---

## 0. Contexte technique vérifié

| Élément | Valeur constatée |
|---|---|
| Moteur | Unity 2019.4.29f1, build **x86 (32 bits)** — `SlimeRancher_Data/Plugins/x86` |
| Scripting backend | Mono (`MonoBleedingEdge` présent) |
| Assembly jeu | `Assembly-CSharp.dll` — 2,95 Mo, 1168 classes décompilées |
| Mod loader | SRML (`SRML.dll`, `ModEntryPoint` avec `PreLoad` / `Load` / `PostLoad`) |
| Patching | Harmony (`SRML/Libs/0Harmony.dll`) |
| Système de culling | SECTR (asset tiers) — `SECTR_Culler`, `SECTR_CullingCamera`, `SECTR_Occluder`, `SECTR_Hibernator`, `SECTR_Sector`, `SECTR_Portal` |
| Poids assets | `sharedassets3.assets` 430 Mo, `sharedassets2.resource` 134 Mo, `level3` 54 Mo |
| Mods déjà installés | AssetsLib, ExtraVacSlot, MoreVaccing, ShowUnfedGordos, SlimeCollection (+ 15 autres dans `Mods List/`) |

**Note repo :** `https://github.com/Xiisuma/SROptimizer` existe (créé le 2026-08-16) mais est vide : branche `main`, un seul fichier `README.md` de 13 octets. Aucune base de code externe à reprendre ; le plan ci-dessous est construit à partir de l'analyse directe du jeu, et ce dépôt est la destination du mod.

---

## 1. Goulots d'étranglement identifiés (analyse du code décompilé)

### 1.1 Simulation IA des slimes — coût dominant

`SlimeSubbehaviourPlexer.RegistryFixedUpdate()` s'exécute **pour chaque slime, à chaque FixedUpdate (50 Hz)**, sans aucune notion de distance au joueur ni de visibilité :

- Recalcul de `distToGround` via `ownCollider.bounds.extents` à chaque tick ;
- Raycast sol toutes les 0,25 s par slime (batché, mais toujours payé) ;
- « Rethink » toutes les 1 s : `GetBestBehaviour()` appelle `Relevancy()` sur **tous** les sous-comportements ;
- `IsBlocked()` déclenche un `Physics.SphereCast` synchrone (1 s de cache par cible).

Avec `cullSlimesLimit = 250` par `CellDirector` et plusieurs cellules actives, on atteint facilement plusieurs centaines d'acteurs simulés à plein régime, y compris hors champ.

### 1.2 Recherche de nourriture — O(slimes × ids × entités)

`FindConsumable.FindNearestConsumable()` est appelé depuis `Relevancy()`, donc **à chaque rethink de chaque slime**. Il fait :

```
CellDirector.Get(searchIds.Keys, member, entries)  // parcourt toutes les régions du membre
  -> pour chaque région : parcours de la liste d'entités par Id
  -> puis boucle linéaire sur toutes les entrées collectées
```

Rien n'est mutualisé entre slimes : 100 slimes cherchant les mêmes carottes refont 100 fois le même balayage dans la même frame.

### 1.3 Allocations en boucle chaude (pression GC)

- `GatherIdentifiableItems.FindItemNotOfType()` : `new List<GameObject>()` **à chaque appel**.
- `CellDirector.UpdateToTime()` : `new Dictionary<DirectedSlimeSpawner, float>()` + `new List<DirectedAnimalSpawner>()` à chaque cycle de spawn.
- 112 fichiers utilisent LINQ ; `CellDirector.Update()` appelle `identifiableIndex.GetSlimes().Count()` jusqu'à 4 fois par seconde et par cellule.
- Le GC de Mono en 32 bits est non-incrémental ici → pics de frametime.

### 1.4 `ActorRegistry` — dispatch inefficace

`ActorRegistry.FixedUpdate/Update/LateUpdate` :

- `fixedUpdateActorsList.Data.CopyTo(localData, 0)` copie **toute la capacité** du tableau, pas seulement `count` ;
- `Array.Clear(localData, 0, localData.Length)` efface **toute la capacité** à chaque frame ;
- coût payé 3× par frame (Fixed + Update + Late).

Aucun budget temporel : si 800 acteurs sont enregistrés, les 800 sont traités dans la même frame.

### 1.5 Sauvegarde automatique — freeze périodique

`AutoSaveDirector` : `nextSaveTime = Time.time + 1440f` (24 min), puis `SaveAllNow()` appelle `SaveGame()` + `SaveProfile()` **de façon synchrone sur le thread principal**. C'est la cause classique du gros freeze périodique signalé par les joueurs.

### 1.6 Rendu / culling

- `QualitySettings.shadowDistance` n'est lu qu'à un seul endroit (`SECTR_CullingCamera:340`) — aucune distance de culling par layer n'est configurée (`Camera.layerCullDistances` inutilisé).
- Les slimes, plorts et particules sont rendus à pleine distance quelle que soit leur taille à l'écran.
- vsync forcé à 1 par `OptionsDirector.SetVsync`, pas de `Application.targetFrameRate` exposé.

### 1.7 Confirmations externes

Les retours communautaires convergent avec l'analyse : le jeu est **CPU-bound** à cause du nombre d'objets physiques simultanés, avec des chutes de FPS proportionnelles au nombre de slimes visibles, et une dégradation marquée en fin de partie sur le ranch.

---

## 2. Ce que le mod va ajouter — modules

Chaque module est indépendant, activable/désactivable, et par défaut réglé sur un profil « sûr » qui ne change aucun comportement de jeu observable.

### Module A — LOD comportemental (gain attendu : le plus élevé)

Patch Harmony `Prefix` sur `SlimeSubbehaviourPlexer.RegistryFixedUpdate`.

Trois paliers selon la distance au joueur et la visibilité caméra :
- **Proche** (< 30 m) ou visible : comportement d'origine, aucun changement ;
- **Moyen** (30–70 m) : période de rethink × 3, raycast sol toutes les 0,75 s, exécution 1 frame sur 2 ;
- **Loin** (> 70 m) et hors champ : exécution 1 frame sur 6, rethink × 8.

Les distances et facteurs sont configurables. Le module ne touche jamais aux slimes captifs, tenus, ou en cours d'action non-interruptible (`CanRethink() == false`).

Cibles annexes du même traitement : `SlimeFaceAnimator.RegistryUpdate`, `SlimeEmotions`, `GlintController`, `Reproduce.RegistryUpdate`.

### Module B — Cache partagé de recherche de nourriture

Patch de `FindConsumable.FindNearestConsumable(out float)`.

Cache par couple *(région, ensemble d'Ids recherchés)*, invalidé au bout de N frames (défaut 15) ou sur enregistrement/désenregistrement dans `GameObjectActorModelIdentifiableIndex`. Le balayage `CellDirector.Get` n'est plus fait qu'une fois par groupe de slimes partageant le même régime alimentaire au lieu d'une fois par slime.

Le tri par distance reste individuel (chaque slime a sa position), seule la **collecte** est mutualisée.

### Module C — Élimination des allocations en boucle chaude

- `GatherIdentifiableItems.FindItemNotOfType` : remplacement de la `List` locale par une liste statique réutilisée (transpile ou reimplémentation).
- `CellDirector.UpdateToTime` : dictionnaires et listes de spawners mis en champ réutilisable.
- `CellDirector.Update` : remplacement des `.Count()` LINQ par un accès direct au `Count` de la liste sous-jacente.
- Pool réutilisable pour les tampons de `CellDirector.Get`.

### Module D — Budget de mise à jour dans `ActorRegistry`

Patch des trois méthodes de dispatch :
- copier et effacer uniquement `count` éléments au lieu de `Data.Length` ;
- répartition round-robin optionnelle : au-delà d'un seuil d'acteurs (défaut 400), les acteurs de palier « loin » sont traités par tranches sur plusieurs frames, avec compensation du delta time pour ne pas ralentir la simulation perçue.

### Module E — Sauvegarde automatique sans à-coup

- Intervalle d'autosave configurable (défaut : conserver 24 min).
- Sérialisation déclenchée pendant une frame déjà creuse, ou fractionnée en coroutine.
- Écriture disque déportée hors du thread principal (la sérialisation Unity reste sur le thread principal ; seul le flush I/O est déporté).
- Option : indicateur visuel + décalage de l'autosave si le joueur est en combat/déplacement rapide.

### Module F — Culling et rendu

- `Camera.layerCullDistances` par layer : plorts, particules décoratives, petits props coupés bien avant le terrain.
- Plafond configurable sur `QualitySettings.shadowDistance` et `shadowCascades`.
- Réduction dynamique de `maxParticles` sur les systèmes de particules décoratifs quand le frametime dépasse un seuil.
- Option `Application.targetFrameRate` + contrôle vsync indépendant du menu du jeu.

### Module G — Réglages physiques

- `Physics.autoSyncTransforms = false` (Unity 2019 le laisse activé par compatibilité ; le désactiver supprime des resynchronisations coûteuses).
- `Rigidbody.sleepThreshold` relevé sur les acteurs distants.
- Réduction de `Physics.defaultSolverIterations` / `defaultSolverVelocityIterations` sur profil « performance ».
- Audit de la matrice de collision pour désactiver les paires inutiles (à valider prudemment, risque de régression gameplay).

### Module H — Infrastructure

- Configuration INI via `SRML.FileSystem.GetMyConfigPath()` (INIFileParser est déjà fourni par SRML) — chaque module a son interrupteur et ses seuils.
- Trois profils prêts à l'emploi : `safe` (défaut), `balanced`, `aggressive`.
- Commandes console SRML : `sropt status`, `sropt profile <nom>`, `sropt reload`, `sropt bench`.
- Overlay de diagnostic optionnel : FPS, frametime, nombre d'acteurs par palier LOD, allocations GC/s.
- Journalisation via `SRML.Console.Console` et `FileLogger`.

---

## 3. Ordre d'implémentation

1. **Squelette** — projet C# ciblant .NET Framework 3.5/4.x compatible, références `Assembly-CSharp.dll`, `UnityEngine.*`, `SRML.dll`, `0Harmony.dll`. `modinfo.json` + `ModEntryPoint`. Sortie : `SROptimizer.dll`.
2. **Module H** (config + console + overlay) — indispensable pour mesurer avant d'optimiser.
3. **Mesure de référence** — scénario reproductible : ranch chargé, N slimes, parcours fixe. Relevé frametime avant tout patch.
4. **Module A** — le plus gros gain, à valider seul.
5. **Module C** puis **Module D** — gains sans risque comportemental.
6. **Module B** — le plus délicat (risque de comportement IA figé), à valider soigneusement.
7. **Module E**.
8. **Modules F et G** — réglages, désactivés par défaut hors profil `aggressive`.

## 4. Critères de vérification

Aucun module n'est marqué terminé sans :
- mesure avant/après sur le même scénario (frametime moyen **et** 1 % low) ;
- absence de régression comportementale : les slimes mangent, se reproduisent, fusionnent en largos, les gordos réagissent ;
- compatibilité vérifiée avec les mods déjà installés (AssetsLib, SlimeCollection, MoreVaccing, ExtraVacSlot, ShowUnfedGordos) ;
- absence d'erreur dans le log SRML sur une session complète incluant au moins un autosave.

## 5. Risques connus

- **Build 32 bits** : la mémoire adressable est limitée à ~4 Go ; toute mise en cache ajoutée doit rester bornée.
- **`FindConsumable` mis en cache** peut faire poursuivre à un slime un objet déjà consommé → invalidation obligatoire sur désenregistrement.
- **Round-robin sur `ActorRegistry`** peut désynchroniser des comportements couplés → à limiter aux acteurs distants.
- **Matrice de collision** : modification à haut risque de régression, à traiter en dernier et derrière une option explicite.
- SRML patche `Assembly-CSharp.dll` (`Assembly-CSharp_SRMLpatched.bak` présent) — les signatures ciblées par Harmony doivent être vérifiées contre la version patchée, pas contre `Assembly-CSharp_old.dll`.

---

## 6. Suivi

- [x] Étape 1 — squelette du projet + build `SROptimizer.dll` *(net472, `modinfo.json` embarqué, déploiement auto dans `SRML/Mods`)*
- [x] Étape 2 — Module H (config, console, overlay) *(build sans erreur ni avertissement ; chargement en jeu non encore vérifié)*
- [x] Étape 3 — mesure de référence établie *(2 sessions, 108 lignes, résultats dans `tasks/baseline.md`)*
- [~] Étape 4 — Module A (LOD comportemental) *(implémenté, build propre ; mesure en jeu à faire)*
- [ ] Étape 5 — Module C (allocations)
- [ ] Étape 6 — Module D (ActorRegistry)
- [ ] Étape 7 — Module B (cache de recherche)
- [ ] Étape 8 — Module E (autosave)
- [ ] Étape 9 — Modules F et G (rendu, physique)
- [ ] Étape 10 — passe de compatibilité mods + validation finale
