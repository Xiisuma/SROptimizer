# Module A — résultats de mesure

## Manche 1 — 16 août 2026, 17:49, framerate plafonné

Première comparaison A/B valide : bascule du module toutes les 30 s dans une seule session,
même sauvegarde, même parcours. 71 lignes, 36 en phase active, 35 en phase inactive.

Charges appariées : médiane 108 acteurs en phase active, 111 en phase inactive (écart −3).

Appels de comportement évités en phase active : médiane **51,0 %**, plage 36,5 à 71,1 %.

### Résultats

Test de permutation à 20 000 tirages, sans hypothèse de distribution — adapté à 35 échantillons.

| Métrique | lod off | lod on | Écart | p | Conclusion |
|---|---|---|---|---|---|
| Frametime pire | 50,25 ms | 46,37 ms | **−3,88 ms** | **0,001** | **Gain réel** |
| 1% low | 82,72 fps | 93,56 fps | +10,84 fps | 0,255 | Indistinguable du bruit |
| Frametime moyen | 8,85 ms | 8,79 ms | −0,06 ms | 0,284 | Aucun effet |
| Frametime médian | 8,33 ms | 8,33 ms | 0,00 ms | — | Aucun effet |
| Allocations | 15,74 Mo/s | 15,08 Mo/s | −0,66 Mo/s | 0,271 | Aucun effet |

### Limite majeure de cette manche

Le frametime médian vaut **8,33 ms dans les deux phases**, soit exactement 120 Hz.
`unlockFrameRate` était à `False` : le jeu était plafonné et disposait de marge. Tout ce que le
module économisait partait en temps d'attente au lieu d'apparaître dans la mesure.

**Cette manche ne peut donc pas mesurer le gain principal.** Elle établit seulement que le
module réduit le pire frametime, ce qui reste significatif : c'est la métrique des à-coups.

Le +13,1 % apparent sur le 1% low ne résiste pas au test statistique et ne doit pas être
annoncé comme un gain.

### Ce qui est établi

1. Le module fonctionne : 51 % des appels de comportement évités en médiane.
2. Il réduit le pire frametime de 7,7 %, avec p = 0,001.
3. Il ne dégrade aucune métrique mesurée.
4. Aucune erreur, aucun plantage, comportement de jeu correct sur toutes les sessions.

### Ce qui reste à mesurer

Une manche identique avec `unlockFrameRate = True`, seule variable changée. Sans plafond, le
frametime moyen redevient une mesure de charge réelle et le gain, s'il existe, devient visible.

## Gels multi-secondes — problème indépendant

Relevés sur toutes les sessions, **y compris sans aucun module actif** :

| Session | Pire frame | Module |
|---|---|---|
| Référence | 5 607 ms | aucun |
| Référence | 5 837 ms | aucun |
| Référence | 5 700 ms | aucun |
| 17:23 | 4 433 ms | lod actif |
| 17:49 | 5 831 ms | lod actif |
| 17:49 | 2 019 ms | lod actif |

Ces gels préexistent au mod et sont d'un ordre de grandeur au-dessus de tout ce que les modules
peuvent gagner : 5 secondes contre quelques millisecondes. Ils sont la piste la plus rentable du
projet, et leur cause n'est pas établie.

Deux observations utiles : le gel de 5 831 ms du 17:49 est survenu avec **7 acteurs** seulement,
donc la charge de simulation n'est pas en cause ; et la mémoire est restée en plateau, donc ce
n'est pas une saturation.
