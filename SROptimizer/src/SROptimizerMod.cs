using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SRML;
using SRML.Config;
using SRML.Console;
using SROptimizer.Commands;
using SROptimizer.Config;
using SROptimizer.Diagnostics;
using SROptimizer.Modules;
using UnityEngine;
using SRConsole = SRML.Console.Console;

namespace SROptimizer
{
    /// <summary>
    /// Point d'entree SRML.
    ///
    /// Ordre d'appel garanti par SRML : ConfigManager.PopulateConfigs -> PreLoad -> Load -> PostLoad.
    /// La configuration est donc deja lue quand PreLoad s'execute.
    /// </summary>
    public class SROptimizerMod : ModEntryPoint
    {
        public const string ModId = "sroptimizer";

        public static SROptimizerMod Instance { get; private set; }

        /// <summary>Profil effectivement applique. Peut differer du fichier si change via la console.</summary>
        public static string ActiveProfile { get; private set; } = Profiles.Safe;

        private static SRConsole.ConsoleInstance _log;

        /// <summary>Journal du mod. Retombe sur la console SRML si l'instance dediee n'existe pas encore.</summary>
        public static SRConsole.ConsoleInstance Log => _log ?? (_log = Instance?.ConsoleInstance ?? SRConsole.Instance);

        private readonly List<IOptimizerModule> _modules = new List<IOptimizerModule>();
        private ReadOnlyCollection<IOptimizerModule> _modulesView;

        private GameObject _overlayObject;
        private PerfOverlay _overlay;
        private PerfMonitor _monitor;

        /// <summary>Modules enregistres, dans leur ordre d'activation.</summary>
        public IList<IOptimizerModule> Modules =>
            _modulesView ?? (_modulesView = new ReadOnlyCollection<IOptimizerModule>(_modules));

        public PerfOverlay Overlay => _overlay;
        public PerfMonitor Monitor => _monitor;

        public override void PreLoad()
        {
            Instance = this;
            _log = ConsoleInstance ?? SRConsole.Instance;

            RegisterModules();

            ActiveProfile = Profiles.Normalize(SROptimizerConfig.profile);
            if (!Profiles.IsKnown(SROptimizerConfig.profile))
            {
                Log.LogWarning($"Profil '{SROptimizerConfig.profile}' inconnu, repli sur '{Profiles.Safe}'. " +
                               $"Valeurs acceptees : {string.Join(", ", Profiles.All)}.");
            }
            Profiles.ApplyToConfig(ActiveProfile);

            SRConsole.RegisterCommand(new SROptCommand());

            Log.Log($"SROptimizer {SRModInfo.GetCurrentInfo()?.Version} charge. " +
                    $"Profil '{ActiveProfile}', {_modules.Count} module(s) enregistre(s).");
        }

        public override void Load()
        {
            SyncModulesWithConfig();
        }

        public override void PostLoad()
        {
            CreateOverlay();
        }

        /// <summary>
        /// Enregistre les modules disponibles. Les modules A a G sont ajoutes ici au fur et a
        /// mesure de leur implementation ; l'infrastructure les prend en charge sans autre changement.
        /// </summary>
        private void RegisterModules()
        {
            _modules.Clear();
            _modulesView = null;
            // Aucun module d'optimisation n'est encore implemente : cette version ne fait
            // qu'observer. Les patchs arrivent aux etapes suivantes du plan.
        }

        /// <summary>Aligne l'etat reel des modules sur les interrupteurs de configuration.</summary>
        public void SyncModulesWithConfig()
        {
            foreach (var module in _modules)
            {
                var shouldBeEnabled = Profiles.IsEnabledInConfig(module.Id);
                if (shouldBeEnabled && !module.IsEnabled) module.Enable(HarmonyInstance);
                else if (!shouldBeEnabled && module.IsEnabled) module.Disable(HarmonyInstance);
            }

            if (SROptimizerConfig.verboseLogging)
            {
                foreach (var module in _modules)
                {
                    Log.Log($"  {module.Id,-10} {(module.IsEnabled ? "actif  " : "inactif")}  {module.GetStatusLine()}");
                }
            }
        }

        /// <summary>Change de profil a chaud et resynchronise les modules.</summary>
        public void SetProfile(string profile)
        {
            ActiveProfile = Profiles.Normalize(profile);
            SROptimizerConfig.profile = ActiveProfile;
            Profiles.ApplyToConfig(ActiveProfile);
            SyncModulesWithConfig();
            Log.LogSuccess($"Profil '{ActiveProfile}' applique.");
        }

        /// <summary>
        /// Ecrit la configuration courante sur disque.
        ///
        /// SRML.SRMod est internal dans cette version : on ne peut pas recuperer la liste des
        /// ConfigFile deja charges. ConfigFile.GenerateConfig reconstruit la vue sur les memes
        /// champs statiques, donc SaveToFile ecrit bien les valeurs courantes.
        /// </summary>
        public void SaveConfig()
        {
            try
            {
                ConfigFile.GenerateConfig(typeof(SROptimizerConfig))?.SaveToFile();
            }
            catch (Exception e)
            {
                Log.LogError($"Echec de l'ecriture de la configuration : {e.Message}");
            }
        }

        public IOptimizerModule FindModule(string id)
        {
            foreach (var module in _modules)
            {
                if (string.Equals(module.Id, id, StringComparison.OrdinalIgnoreCase)) return module;
            }
            return null;
        }

        private void CreateOverlay()
        {
            if (_overlayObject != null) return;

            _monitor = new PerfMonitor(SROptimizerConfig.Diagnostics.sampleWindow);

            _overlayObject = new GameObject("SROptimizer.PerfOverlay");
            UnityEngine.Object.DontDestroyOnLoad(_overlayObject);
            _overlayObject.hideFlags = HideFlags.HideAndDontSave;

            _overlay = _overlayObject.AddComponent<PerfOverlay>();
            _overlay.Initialize(_monitor);

            Log.Log($"Overlay de diagnostic pret (touche {SROptimizerConfig.Diagnostics.overlayToggleKey}).");
        }
    }
}
