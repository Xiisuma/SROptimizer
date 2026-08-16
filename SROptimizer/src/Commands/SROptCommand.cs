using System.Collections.Generic;
using SRML.Console;
using SROptimizer.Config;

namespace SROptimizer.Commands
{
    /// <summary>
    /// Commande console unique du mod : "sropt &lt;sous-commande&gt;".
    /// </summary>
    public class SROptCommand : ConsoleCommand
    {
        public override string ID => "sropt";
        public override string Usage => "sropt <status|profile|module|overlay|bench|reset|save>";
        public override string Description => "Controle et diagnostic de SROptimizer.";

        public override string ExtendedDescription =>
            "sropt status               Etat du mod, profil actif, modules et mesures courantes\n" +
            "sropt profile              Affiche le profil actif\n" +
            "sropt profile <nom>        Applique un profil : safe, balanced, aggressive\n" +
            "sropt module <id> on|off   Active ou desactive un module individuellement\n" +
            "sropt overlay [on|off]     Bascule ou fixe l'affichage de l'overlay de diagnostic\n" +
            "sropt bench start [note]   Demarre l'enregistrement CSV des mesures\n" +
            "sropt bench stop           Arrete l'enregistrement\n" +
            "sropt bench status         Etat de l'enregistrement en cours\n" +
            "sropt reset                Vide la fenetre de mesure du frametime\n" +
            "sropt save                 Ecrit la configuration courante sur disque";

        public override bool Execute(string[] args)
        {
            var mod = SROptimizerMod.Instance;
            if (mod == null)
            {
                SROptimizerMod.Log.LogError("SROptimizer n'est pas initialise.");
                return false;
            }

            if (args == null || args.Length == 0)
            {
                PrintStatus();
                return true;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "status":
                    PrintStatus();
                    return true;

                case "profile":
                    return HandleProfile(args);

                case "module":
                    return HandleModule(args);

                case "overlay":
                    return HandleOverlay(args);

                case "bench":
                    return HandleBench(args);

                case "reset":
                    mod.Monitor?.Reset();
                    SROptimizerMod.Log.LogSuccess("Fenetre de mesure videe.");
                    return true;

                case "save":
                    mod.SaveConfig();
                    SROptimizerMod.Log.LogSuccess("Configuration ecrite.");
                    return true;

                default:
                    SROptimizerMod.Log.LogError($"Sous-commande inconnue : '{args[0]}'. Usage : {Usage}");
                    return false;
            }
        }

        private static void PrintStatus()
        {
            var mod = SROptimizerMod.Instance;
            SROptimizerMod.Log.Log($"SROptimizer - profil actif : {SROptimizerMod.ActiveProfile}");

            if (mod.Modules.Count == 0)
            {
                SROptimizerMod.Log.Log("  modules : aucun module d'optimisation n'est encore implemente (version d'observation).");
            }
            else
            {
                foreach (var module in mod.Modules)
                {
                    SROptimizerMod.Log.Log($"  {module.Id,-10} {(module.IsEnabled ? "actif  " : "inactif")}  {module.Description}");
                }
            }

            var monitor = mod.Monitor;
            if (monitor != null && monitor.TryGetStats(out var s))
            {
                SROptimizerMod.Log.Log($"  FPS moyen {s.AverageFps:F1} | 1% low {s.OnePercentLowFps:F1} | " +
                            $"0.1% low {s.PointOnePercentLowFps:F1} | frametime moyen {s.AverageFrameTimeMs:F2} ms " +
                            $"(sur {s.SampleCount} frames)");
                SROptimizerMod.Log.Log($"  GC {monitor.ManagedBytes / 1048576.0:F1} Mo retenus, " +
                            $"{monitor.AllocRateBytesPerSecond / 1048576.0:F2} Mo/s alloues");
            }
            else
            {
                SROptimizerMod.Log.Log("  mesures : collecte en cours.");
            }
        }

        private static bool HandleProfile(string[] args)
        {
            if (args.Length < 2)
            {
                SROptimizerMod.Log.Log($"Profil actif : {SROptimizerMod.ActiveProfile}. " +
                            $"Valeurs possibles : {string.Join(", ", Profiles.All)}.");
                return true;
            }

            var requested = args[1];
            if (!Profiles.IsKnown(requested))
            {
                SROptimizerMod.Log.LogError($"Profil inconnu : '{requested}'. Valeurs possibles : {string.Join(", ", Profiles.All)}.");
                return false;
            }

            SROptimizerMod.Instance.SetProfile(requested);
            return true;
        }

        private static bool HandleModule(string[] args)
        {
            var mod = SROptimizerMod.Instance;

            if (args.Length < 3)
            {
                SROptimizerMod.Log.LogError("Usage : sropt module <id> on|off");
                return false;
            }

            var module = mod.FindModule(args[1]);
            if (module == null)
            {
                SROptimizerMod.Log.LogError($"Module inconnu : '{args[1]}'.");
                return false;
            }

            var state = args[2].ToLowerInvariant();
            if (state != "on" && state != "off")
            {
                SROptimizerMod.Log.LogError("Le troisieme argument doit etre 'on' ou 'off'.");
                return false;
            }

            var enable = state == "on";
            SetModuleSwitch(module.Id, enable);
            mod.SyncModulesWithConfig();
            SROptimizerMod.Log.LogSuccess($"Module '{module.Id}' {(enable ? "active" : "desactive")}. " +
                               "Utiliser 'sropt save' pour rendre le changement permanent.");
            return true;
        }

        private static void SetModuleSwitch(string moduleId, bool enabled)
        {
            switch (moduleId)
            {
                case "lod": SROptimizerConfig.Modules.behaviourLod = enabled; break;
                case "cache": SROptimizerConfig.Modules.consumableCache = enabled; break;
                case "alloc": SROptimizerConfig.Modules.allocationTrimming = enabled; break;
                case "registry": SROptimizerConfig.Modules.actorRegistryBudget = enabled; break;
                case "autosave": SROptimizerConfig.Modules.smoothAutosave = enabled; break;
                case "culling": SROptimizerConfig.Modules.renderCulling = enabled; break;
                case "physics": SROptimizerConfig.Modules.physicsTuning = enabled; break;
            }
        }

        private static bool HandleBench(string[] args)
        {
            var bench = SROptimizerMod.Instance.Runner?.Bench;
            if (bench == null)
            {
                SROptimizerMod.Log.LogError("L'enregistreur de mesure n'est pas encore initialise.");
                return false;
            }

            var action = args.Length < 2 ? "status" : args[1].ToLowerInvariant();

            switch (action)
            {
                case "start":
                    // Tout ce qui suit "start" devient la note de la capture.
                    var note = args.Length > 2 ? string.Join(" ", args, 2, args.Length - 2) : "manuel";
                    return bench.Start(note);

                case "stop":
                    if (!bench.IsRecording)
                    {
                        SROptimizerMod.Log.LogWarning("Aucune capture en cours.");
                        return true;
                    }
                    bench.Stop();
                    return true;

                case "status":
                    if (bench.IsRecording)
                    {
                        SROptimizerMod.Log.Log($"Capture en cours : {bench.RowsWritten} ligne(s) ecrite(s) " +
                                               $"dans {bench.OutputPath}");
                    }
                    else
                    {
                        SROptimizerMod.Log.Log("Aucune capture en cours. Demarrer avec 'sropt bench start [note]'.");
                    }
                    return true;

                default:
                    SROptimizerMod.Log.LogError("Usage : sropt bench start [note] | stop | status");
                    return false;
            }
        }

        private static bool HandleOverlay(string[] args)
        {
            var overlay = SROptimizerMod.Instance.Overlay;
            if (overlay == null)
            {
                SROptimizerMod.Log.LogError("L'overlay n'est pas encore cree.");
                return false;
            }

            if (args.Length < 2)
            {
                overlay.Toggle();
            }
            else
            {
                var state = args[1].ToLowerInvariant();
                if (state != "on" && state != "off")
                {
                    SROptimizerMod.Log.LogError("Usage : sropt overlay [on|off]");
                    return false;
                }
                overlay.SetVisible(state == "on");
            }

            SROptimizerConfig.Diagnostics.overlayEnabled = overlay.Visible;
            SROptimizerMod.Log.LogSuccess($"Overlay {(overlay.Visible ? "affiche" : "masque")}.");
            return true;
        }

        public override List<string> GetAutoComplete(int argIndex, string argText)
        {
            switch (argIndex)
            {
                case 0:
                    return new List<string> { "status", "profile", "module", "overlay", "bench", "reset", "save" };
                default:
                    return null;
            }
        }
    }
}
