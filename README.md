# SROptimizer

Mod d'optimisation des performances pour **Slime Rancher 1.4.4** (Unity 2019.4.29f1, build x86), chargé par [SRML](https://github.com/SlimeRancherModding/SRML).

## État

Version **0.1.0** — infrastructure et diagnostics. Aucun patch d'optimisation n'est encore appliqué : cette version sert à mesurer avant d'optimiser.

| Étape | État |
|---|---|
| Squelette du projet, build `SROptimizer.dll` | fait |
| Module H — configuration, commandes console, overlay de diagnostic | fait |
| Mesure persistante : CSV, compteurs d'acteurs, capture auto | fait |
| Relevé de référence sur une sauvegarde réelle | fait — voir [`tasks/baseline.md`](tasks/baseline.md) |
| Module A — LOD comportemental | à faire |
| Module C — allocations en boucle chaude | à faire |
| Module D — budget `ActorRegistry` | à faire |
| Module B — cache de recherche de nourriture | à faire |
| Module E — autosave sans à-coup | à faire |
| Modules F et G — culling, physique | à faire |

Le détail de l'analyse et du plan est dans [`tasks/todo.md`](tasks/todo.md).

## Installation

1. Installer SRML.
2. Copier `SROptimizer.dll` dans `Slime Rancher/SRML/Mods/`.
3. Lancer le jeu. La configuration est générée dans `SRML/Configs/sroptimizer/SROptimizer.ini`.

## Utilisation

Overlay de diagnostic : touche **F9** par défaut (configurable).

Commandes dans la console SRML :

```
sropt status               Profil actif, modules, FPS moyen, 1% low, 0.1% low, débit GC
sropt profile <nom>        Applique un profil : safe, balanced, aggressive
sropt module <id> on|off   Active ou désactive un module individuellement
sropt overlay [on|off]     Bascule ou fixe l'affichage de l'overlay
sropt bench start [note]   Démarre l'enregistrement CSV des mesures
sropt bench stop           Arrête l'enregistrement
sropt bench status         État de l'enregistrement en cours
sropt reset                Vide la fenêtre de mesure du frametime
sropt save                 Écrit la configuration courante sur disque
```

## Profils

| Profil | Effet |
|---|---|
| `safe` (défaut) | Aucun comportement de jeu observable n'est modifié |
| `balanced` | Gains significatifs, comportements distants dégradés de façon imperceptible |
| `aggressive` | Gains maximaux, comportements distants et rendu visiblement dégradés |

## Compilation

Le projet référence directement les DLL du jeu ; celles-ci ne sont pas versionnées.

```bash
dotnet build -c Release
```

Par défaut le chemin du jeu est `../Slime.Rancher.v1.4.4.ALL.DLC`. Pour une autre installation :

```bash
dotnet build -c Release -p:GameDir="C:\Chemin\Vers\Slime Rancher"
```

Le build copie automatiquement `SROptimizer.dll` dans `SRML/Mods` du jeu. Désactivable avec `-p:DeployToGame=false`.

## Licence

Non définie pour l'instant.
