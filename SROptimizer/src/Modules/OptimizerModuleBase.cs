using System;
using HarmonyLib;

namespace SROptimizer.Modules
{
    /// <summary>
    /// Implementation commune : gestion de l'etat actif/inactif et cycle de vie des patchs.
    ///
    /// Chaque module possede sa PROPRE instance Harmony, nommee "net.xiisuma.sroptimizer.&lt;id&gt;".
    /// C'est ce qui permet de desactiver un module a chaud sans toucher aux patchs des autres :
    /// Harmony.UnpatchAll(id) ne retire que les patchs portant cet identifiant.
    /// </summary>
    public abstract class OptimizerModuleBase : IOptimizerModule
    {
        public const string HarmonyIdPrefix = "net.xiisuma.sroptimizer.";

        private Harmony _harmony;

        public abstract string Id { get; }
        public abstract string Description { get; }

        public bool IsEnabled { get; private set; }

        /// <summary>Instance Harmony dediee a ce module.</summary>
        protected Harmony ModuleHarmony => _harmony ?? (_harmony = new Harmony(HarmonyIdPrefix + Id));

        public void Enable(Harmony _)
        {
            if (IsEnabled) return;
            try
            {
                OnEnable(ModuleHarmony);
                IsEnabled = true;
                SROptimizerMod.Log.Log($"Module '{Id}' active.");
            }
            catch (Exception e)
            {
                SROptimizerMod.Log.LogError($"Module '{Id}' n'a pas pu etre active : {e}");
                ForceDisable();
            }
        }

        public void Disable(Harmony _)
        {
            if (!IsEnabled) return;
            ForceDisable();
            SROptimizerMod.Log.Log($"Module '{Id}' desactive.");
        }

        private void ForceDisable()
        {
            try
            {
                OnDisable(ModuleHarmony);
            }
            catch (Exception e)
            {
                SROptimizerMod.Log.LogError($"Module '{Id}' n'a pas pu etre desactive proprement : {e}");
            }
            finally
            {
                try
                {
                    ModuleHarmony.UnpatchAll(ModuleHarmony.Id);
                }
                catch (Exception e)
                {
                    SROptimizerMod.Log.LogError($"Echec du retrait des patchs de '{Id}' : {e}");
                }
                IsEnabled = false;
            }
        }

        /// <summary>Pose les patchs du module. Appele une seule fois par activation.</summary>
        protected abstract void OnEnable(Harmony harmony);

        /// <summary>
        /// Restaure l'etat non-Harmony du module (valeurs globales modifiees, caches, objets crees).
        /// Le retrait des patchs Harmony est fait automatiquement apres cet appel.
        /// </summary>
        protected abstract void OnDisable(Harmony harmony);

        public virtual string GetStatusLine() => IsEnabled ? "actif" : "inactif";
    }
}
