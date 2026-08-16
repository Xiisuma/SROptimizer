using System;
using HarmonyLib;

namespace SROptimizer.Modules
{
    /// <summary>
    /// Implementation commune : cycle de vie des modules et pose des patchs.
    ///
    /// <para><b>Les patchs Harmony sont poses une seule fois et ne sont jamais retires tant que
    /// le jeu tourne.</b> Une premiere version desinstallait les patchs a la desactivation :
    /// le jeu plantait en natif, sans aucune exception managee, exactement a l'instant du
    /// premier retrait. Retirer un patch pendant que la methode s'execute a 50 Hz sur des
    /// centaines d'acteurs revient a reecrire cette methode sous les pieds de l'appelant, ici
    /// <c>ActorRegistry.FixedUpdate</c> qui est en train d'iterer dessus.</para>
    ///
    /// <para>Consequence pour les modules : un patch pose doit consulter l'etat du module a
    /// chaque appel et se comporter comme le jeu d'origine quand le module est inactif. C'est
    /// ce que verifie <see cref="IsEnabled"/>. Le cout residuel est un test de booleen.</para>
    /// </summary>
    public abstract class OptimizerModuleBase : IOptimizerModule
    {
        public const string HarmonyIdPrefix = "net.xiisuma.sroptimizer.";

        private Harmony _harmony;
        private bool _patchesInstalled;

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
                if (!_patchesInstalled)
                {
                    InstallPatches(ModuleHarmony);
                    _patchesInstalled = true;
                }

                OnActivated();
                IsEnabled = true;
                SROptimizerMod.Log.Log($"Module '{Id}' active.");
            }
            catch (Exception e)
            {
                SROptimizerMod.Log.LogError($"Module '{Id}' n'a pas pu etre active : {e}");
                SafeDeactivate();
            }
        }

        public void Disable(Harmony _)
        {
            if (!IsEnabled) return;
            SafeDeactivate();
            SROptimizerMod.Log.Log($"Module '{Id}' desactive.");
        }

        private void SafeDeactivate()
        {
            try
            {
                OnDeactivated();
            }
            catch (Exception e)
            {
                SROptimizerMod.Log.LogError($"Module '{Id}' n'a pas pu etre desactive proprement : {e}");
            }
            finally
            {
                // Les patchs restent en place volontairement : voir la remarque de classe.
                IsEnabled = false;
            }
        }

        /// <summary>
        /// Pose les patchs Harmony du module. Appele une seule fois par session de jeu, a la
        /// premiere activation. Les patchs doivent consulter l'etat du module a chaque appel.
        /// </summary>
        protected abstract void InstallPatches(Harmony harmony);

        /// <summary>Bascule le module a l'etat actif. Doit rester bon marche : appele a chaque bascule.</summary>
        protected virtual void OnActivated()
        {
        }

        /// <summary>
        /// Bascule le module a l'etat inactif et restaure ce qui doit l'etre (valeurs globales
        /// modifiees, caches). Les patchs, eux, restent poses.
        /// </summary>
        protected virtual void OnDeactivated()
        {
        }

        public virtual string GetStatusLine() => IsEnabled ? "actif" : "inactif";
    }
}
