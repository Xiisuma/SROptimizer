using System.Reflection;

namespace SROptimizer.Diagnostics
{
    /// <summary>
    /// Lit le nombre d'acteurs enregistres dans ActorRegistry.
    ///
    /// Les listes du registre sont privees, mais ce sont des ExposedArrayList dont GetCount()
    /// est O(1) : on lit donc les compteurs par reflexion, sans jamais parcourir la scene.
    /// C'est volontaire : Object.FindObjectsOfType couterait plusieurs millisecondes et
    /// polluerait la mesure de frametime que ce module sert justement a etablir.
    ///
    /// Les FieldInfo sont resolus une seule fois puis mis en cache.
    /// </summary>
    public static class ActorCounter
    {
        private static bool _resolved;
        private static FieldInfo _fixedUpdateList;
        private static FieldInfo _updateList;
        private static FieldInfo _lateUpdateList;
        private static MethodInfo _getCount;

        /// <summary>Nombre d'acteurs recevant RegistryFixedUpdate, ou -1 si indisponible.</summary>
        public static int FixedUpdateActors => Count(_fixedUpdateList);

        /// <summary>Nombre d'acteurs recevant RegistryUpdate, ou -1 si indisponible.</summary>
        public static int UpdateActors => Count(_updateList);

        /// <summary>Nombre d'acteurs recevant RegistryLateUpdate, ou -1 si indisponible.</summary>
        public static int LateUpdateActors => Count(_lateUpdateList);

        /// <summary>Vrai si un ActorRegistry est present, donc si une partie est chargee.</summary>
        public static bool IsAvailable => GetRegistry() != null;

        private static ActorRegistry GetRegistry()
        {
            var scene = SRSingleton<SceneContext>.Instance;
            return scene == null ? null : scene.ActorRegistry;
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(ActorRegistry);

            _fixedUpdateList = type.GetField("fixedUpdateActorsList", flags);
            _updateList = type.GetField("updateActorsList", flags);
            _lateUpdateList = type.GetField("lateUpdateActorsList", flags);

            var listType = _fixedUpdateList?.FieldType;
            _getCount = listType?.GetMethod("GetCount");

            if (_fixedUpdateList == null || _getCount == null)
            {
                SROptimizerMod.Log.LogWarning(
                    "Champs internes d'ActorRegistry introuvables : les compteurs d'acteurs " +
                    "resteront a -1. La version du jeu a probablement change.");
            }
        }

        private static int Count(FieldInfo field)
        {
            Resolve();
            if (field == null || _getCount == null) return -1;

            var registry = GetRegistry();
            if (registry == null) return -1;

            var list = field.GetValue(registry);
            if (list == null) return -1;

            return (int)_getCount.Invoke(list, null);
        }
    }
}
