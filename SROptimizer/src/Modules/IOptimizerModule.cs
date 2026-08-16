using HarmonyLib;

namespace SROptimizer.Modules
{
    /// <summary>
    /// Un module d'optimisation. Chaque module est independant, peut etre active ou desactive
    /// a chaud, et doit pouvoir revenir a l'etat d'origine du jeu via <see cref="Disable"/>.
    /// </summary>
    public interface IOptimizerModule
    {
        /// <summary>Identifiant court, utilise dans la config et les commandes console.</summary>
        string Id { get; }

        /// <summary>Description d'une ligne affichee par "sropt status".</summary>
        string Description { get; }

        /// <summary>Vrai si le module est actuellement actif.</summary>
        bool IsEnabled { get; }

        /// <summary>Applique les patchs du module. Doit etre idempotent.</summary>
        void Enable(Harmony harmony);

        /// <summary>Retire les patchs du module et restaure l'etat d'origine. Doit etre idempotent.</summary>
        void Disable(Harmony harmony);

        /// <summary>Ligne d'etat detaillee (compteurs, seuils effectifs) pour le diagnostic.</summary>
        string GetStatusLine();
    }
}
