using System;
using System.Collections.Generic;
using SROptimizer.Config;

namespace SROptimizer
{
    /// <summary>
    /// Profils predefinis. Un profil ne fait que fixer l'etat actif/inactif des modules ;
    /// les seuils fins restent dans le fichier de configuration.
    ///
    /// safe       : aucun comportement de jeu observable n'est modifie.
    /// balanced   : gains significatifs, comportements distants degrades de facon imperceptible.
    /// aggressive : gains maximaux, comportements distants et rendu visiblement degrades.
    /// custom     : le profil ne touche a rien, les interrupteurs de la section MODULES font foi.
    /// </summary>
    public static class Profiles
    {
        public const string Safe = "safe";
        public const string Balanced = "balanced";
        public const string Aggressive = "aggressive";

        /// <summary>
        /// Profil qui n'impose rien. Sans lui, ApplyToConfig ecraserait au demarrage les
        /// interrupteurs de la section MODULES : un utilisateur activant un module a la main
        /// dans le fichier verrait son reglage ignore sans le moindre message.
        /// </summary>
        public const string Custom = "custom";

        public static readonly string[] All = { Safe, Balanced, Aggressive, Custom };

        /// <summary>
        /// Modules actives par chaque profil. Les modules absents de la liste sont desactives.
        /// Les identifiants correspondent a <see cref="Modules.IOptimizerModule.Id"/>.
        /// </summary>
        private static readonly Dictionary<string, string[]> ModulesByProfile =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [Safe] = new[] { "alloc", "registry" },
                [Balanced] = new[] { "alloc", "registry", "lod", "cache", "autosave" },
                [Aggressive] = new[] { "alloc", "registry", "lod", "cache", "autosave", "culling", "physics" },
                [Custom] = new string[0]
            };

        public static bool IsKnown(string profile) =>
            !string.IsNullOrEmpty(profile) && ModulesByProfile.ContainsKey(profile.Trim());

        public static string Normalize(string profile)
        {
            if (string.IsNullOrEmpty(profile)) return Safe;
            var trimmed = profile.Trim();
            return ModulesByProfile.ContainsKey(trimmed) ? trimmed.ToLowerInvariant() : Safe;
        }

        /// <summary>Vrai si le module donne doit etre actif sous ce profil.</summary>
        public static bool Includes(string profile, string moduleId)
        {
            if (!ModulesByProfile.TryGetValue(Normalize(profile), out var ids)) return false;
            for (var i = 0; i < ids.Length; i++)
            {
                if (string.Equals(ids[i], moduleId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Reporte le profil dans les interrupteurs individuels du fichier de configuration,
        /// pour que l'utilisateur voie explicitement ce que le profil a active.
        /// </summary>
        public static void ApplyToConfig(string profile)
        {
            var p = Normalize(profile);

            // En custom, les interrupteurs du fichier font foi : ne rien ecraser.
            if (p == Custom) return;

            SROptimizerConfig.Modules.behaviourLod = Includes(p, "lod");
            SROptimizerConfig.Modules.consumableCache = Includes(p, "cache");
            SROptimizerConfig.Modules.allocationTrimming = Includes(p, "alloc");
            SROptimizerConfig.Modules.actorRegistryBudget = Includes(p, "registry");
            SROptimizerConfig.Modules.smoothAutosave = Includes(p, "autosave");
            SROptimizerConfig.Modules.renderCulling = Includes(p, "culling");
            SROptimizerConfig.Modules.physicsTuning = Includes(p, "physics");
        }

        /// <summary>Etat souhaite d'un module d'apres les interrupteurs de configuration.</summary>
        public static bool IsEnabledInConfig(string moduleId)
        {
            switch (moduleId)
            {
                case "lod": return SROptimizerConfig.Modules.behaviourLod;
                case "cache": return SROptimizerConfig.Modules.consumableCache;
                case "alloc": return SROptimizerConfig.Modules.allocationTrimming;
                case "registry": return SROptimizerConfig.Modules.actorRegistryBudget;
                case "autosave": return SROptimizerConfig.Modules.smoothAutosave;
                case "culling": return SROptimizerConfig.Modules.renderCulling;
                case "physics": return SROptimizerConfig.Modules.physicsTuning;
                default: return false;
            }
        }
    }
}
