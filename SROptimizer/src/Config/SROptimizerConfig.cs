using SRML.Config.Attributes;

namespace SROptimizer.Config
{
    /// <summary>
    /// Configuration du mod, persistee par SRML dans SRML/Configs/sroptimizer/SROptimizer.ini.
    /// SRML genere le fichier a partir des champs statiques : section par defaut pour les champs
    /// de la classe, une section supplementaire par classe imbriquee marquee [ConfigSection].
    /// </summary>
    [ConfigFile("SROptimizer", "GENERAL")]
    public static class SROptimizerConfig
    {
        [ConfigComment("Profil applique au demarrage : safe, balanced ou aggressive. " +
                       "safe ne change aucun comportement de jeu observable.")]
        public static string profile = "safe";

        [ConfigComment("Journalise dans la console SRML chaque module active et ses reglages effectifs.")]
        public static bool verboseLogging = false;

        [ConfigSection("DIAGNOSTICS")]
        public static class Diagnostics
        {
            [ConfigComment("Affiche l'overlay de diagnostic (FPS, frametime, 1% low, GC).")]
            public static bool overlayEnabled = false;

            [ConfigComment("Touche de bascule de l'overlay. Nom d'une valeur de UnityEngine.KeyCode.")]
            public static string overlayToggleKey = "F9";

            [ConfigComment("Coin d'ancrage de l'overlay : topleft, topright, bottomleft, bottomright.")]
            public static string overlayCorner = "topleft";

            [ConfigComment("Intervalle en secondes entre deux rafraichissements du texte de l'overlay.")]
            public static float overlayRefreshInterval = 0.5f;

            [ConfigComment("Nombre de frames conservees pour calculer moyenne, 1% low et 0.1% low.")]
            public static int sampleWindow = 1024;
        }

        [ConfigSection("BENCHMARK")]
        public static class Benchmark
        {
            [ConfigComment("Demarre l'enregistrement automatiquement des qu'une partie est chargee. " +
                           "Indispensable pour etablir une mesure de reference sans intervention.")]
            public static bool autoStart = false;

            [ConfigComment("Nom du fichier CSV ecrit dans le dossier de configuration du mod.")]
            public static string outputFile = "baseline.csv";

            [ConfigComment("Ecrit un fichier par capture, horodate. Sinon toutes les captures " +
                           "s'ajoutent au meme fichier et il faut les separer a la main avant " +
                           "toute comparaison avant/apres.")]
            public static bool oneFilePerSession = true;

            [ConfigComment("Intervalle en secondes entre deux lignes ecrites. Minimum 1.")]
            public static float sampleIntervalSeconds = 5f;

            [ConfigComment("Delai avant la premiere ligne, en secondes. La fenetre de mesure est videe " +
                           "a la fin de ce delai : sans cela les a-coups de chargement de zone " +
                           "(plus d'une seconde) ecrasent le 0.1% low pendant tout le reste de la capture.")]
            public static float warmupSeconds = 20f;

            [ConfigComment("Leve la limite de frequence pendant la capture (vsync et targetFrameRate). " +
                           "Le jeu est bloque par la vsync : frametime median fige a la periode de " +
                           "l'ecran, donc le FPS moyen ne peut pas mesurer un gain. Les reglages " +
                           "d'origine sont restaures a l'arret de la capture.")]
            public static bool unlockFrameRate = true;
        }

        [ConfigSection("MODULES")]
        public static class Modules
        {
            [ConfigComment("Module A - LOD comportemental sur les slimes distants ou hors champ.")]
            public static bool behaviourLod = false;

            [ConfigComment("Module B - Cache partage de la recherche de nourriture.")]
            public static bool consumableCache = false;

            [ConfigComment("Module C - Suppression des allocations en boucle chaude.")]
            public static bool allocationTrimming = false;

            [ConfigComment("Module D - Budget de mise a jour dans ActorRegistry.")]
            public static bool actorRegistryBudget = false;

            [ConfigComment("Module E - Sauvegarde automatique sans a-coup.")]
            public static bool smoothAutosave = false;

            [ConfigComment("Module F - Distances de culling et plafond d'ombres.")]
            public static bool renderCulling = false;

            [ConfigComment("Module G - Reglages physiques.")]
            public static bool physicsTuning = false;
        }
    }
}
