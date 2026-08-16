# Mesure de référence — 16 août 2026

Établie avec SROptimizer 0.1.0, profil `safe`, **aucun module d'optimisation actif**.
Deux captures indépendantes, chauffe de 20 s, limite de fréquence levée.

| | Session 1 | Session 2 |
|---|---|---|
| Horodatage | 16:26:00 → 16:30:44 | 16:31:26 → 16:35:39 |
| Lignes | 57 | 51 |
| Durée mesurée | 289 s | 258 s |

Fichier brut : `SRML/Config/sroptimizer/baseline.csv` (les deux sessions y sont cumulées ;
depuis la version suivante chaque capture a son propre fichier horodaté).

---

## Résultat principal : le coût est proportionnel au nombre d'acteurs simulés

Regroupement par nombre d'acteurs recevant `RegistryFixedUpdate` :

| Acteurs | Frametime moyen | 1% low | Allocations | Mémoire gérée |
|---|---|---|---|---|
| **< 200** | 4,84 ms (S1) / 5,70 ms (S2) | 155 fps (S1) / 141 fps (S2) | 3,10 Mo/s (S1) / 3,22 Mo/s (S2) | 60 Mo |
| **200 – 600** | 11,71 ms (S1) / 11,96 ms (S2) | 54 fps (S1) / 58 fps (S2) | 9,31 Mo/s (S1) / 11,60 Mo/s (S2) | 140 Mo |

Les deux sessions donnent les mêmes valeurs à moins de 20 % près, sur des parcours différents.

Passer d'environ 120 à environ 270 acteurs, soit un facteur 2,2 :

- frametime moyen **× 2,2**
- 1% low **÷ 2,7**
- débit d'allocation **× 3,5**
- mémoire gérée **× 2,3**

Corrélation entre nombre d'acteurs et frametime moyen sur la session 2 : **0,721**.

Le débit d'allocation croît plus vite que le nombre d'acteurs (× 3,5 pour × 2,2). Ce n'est pas
seulement « plus d'acteurs à mettre à jour » : le coût par acteur augmente aussi.

## Chiffres bruts, session 2

| Mesure | min | médiane | max |
|---|---|---|---|
| FPS moyen | 56,5 | 102,0 | 246,2 |
| 1% low | 21,3 | 62,4 | 184,0 |
| Frametime moyen | 4,06 ms | 9,81 ms | 17,69 ms |
| Frametime médian | 4,08 ms | 7,98 ms | 14,00 ms |
| Allocations | 0,63 Mo/s | 10,79 Mo/s | 32,87 Mo/s |
| Mémoire gérée | 45 Mo | 117 Mo | 183 Mo |
| Acteurs `fixedUpdate` | 116 | 266 | 266 |

## Gels observés

| Instant | Pire frametime | Acteurs | Mémoire |
|---|---|---|---|
| 186 s (S1) | 1 774 ms | 24 | 108 Mo |
| 223 s (S1) | 5 607 ms | 278 | 142 Mo |
| 81 s (S2) | 5 837 ms | — | — |
| 178 s (S2) | 5 700 ms | — | — |

Des gels de plus de 5 secondes. **Cause non établie** : ce n'est pas l'autosave, dont
l'intervalle est de 1440 s alors que les sessions durent moins de 300 s. Pistes à instrumenter :
chargement de région SECTR, ou cycle de GC majeur.

Un même gel apparaît sur trois lignes consécutives : la fenêtre glissante de 1024 frames le
retient environ 15 secondes. C'est attendu, pas un doublon.

## Ce que cette baseline ne mesure pas

- Un ranch de fin de partie. Le maximum observé est de 266 acteurs stables ; le pic à 5127
  est une ligne isolée pendant un chargement de zone, sans valeur.
- La limite mémoire du build 32 bits. 183 Mo de mémoire gérée n'est qu'une fraction de
  l'empreinte totale du processus.

## Conséquences pour le plan

1. **Module A (LOD comportemental) est bien la priorité.** Le coût suit le nombre d'acteurs
   simulés, et `SlimeSubbehaviourPlexer.RegistryFixedUpdate` s'exécute pour chaque acteur à
   50 Hz sans notion de distance ni de visibilité.
2. **Module C (allocations) monte en priorité.** 33 Mo/s en pointe, et une croissance plus
   rapide que le nombre d'acteurs.
3. **Module D peut ignorer `LateUpdate`.** Le compteur `acteurs_lateupdate` vaut 0 la quasi
   totalité du temps, avec de rares valeurs de 10 et 28. Le dispatcher `LateUpdate` ne
   représente aucun coût mesurable ; seuls `FixedUpdate` et `Update` méritent un budget.
4. **Métrique de validation : le 1% low et le frametime moyen à charge comparable.** Le FPS
   moyen n'a de sens qu'avec la limite de fréquence levée, ce que la capture fait déjà.
5. **Comparer à nombre d'acteurs égal.** Toute comparaison avant/après doit se faire dans la
   même tranche de charge, sinon la variation du nombre d'acteurs masque l'effet du module.
