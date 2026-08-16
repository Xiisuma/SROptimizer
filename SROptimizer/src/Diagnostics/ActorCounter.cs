using System;
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
    /// Les trois listes sont des types generiques fermes DIFFERENTS
    /// (ExposedArrayList&lt;RegistryFixedUpdateable&gt;, &lt;RegistryUpdateable&gt;,
    /// &lt;RegistryLateUpdateable&gt;). Un MethodInfo de GetCount obtenu sur l'un ne s'applique
    /// pas aux autres : chaque accesseur garde donc le sien.
    /// </summary>
    public static class ActorCounter
    {
        /// <summary>Champ prive du registre, avec le GetCount de son propre type ferme.</summary>
        private sealed class ListAccessor
        {
            private readonly string _fieldName;
            private FieldInfo _field;
            private MethodInfo _getCount;
            private bool _resolved;

            public ListAccessor(string fieldName) => _fieldName = fieldName;

            public int Read(ActorRegistry registry)
            {
                Resolve();
                if (_field == null || _getCount == null || registry == null) return -1;

                var list = _field.GetValue(registry);
                if (list == null) return -1;

                return (int)_getCount.Invoke(list, null);
            }

            private void Resolve()
            {
                if (_resolved) return;
                _resolved = true;

                _field = typeof(ActorRegistry).GetField(
                    _fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

                if (_field == null)
                {
                    SROptimizerMod.Log.LogWarning(
                        $"Champ '{_fieldName}' introuvable dans ActorRegistry : ce compteur restera a -1.");
                    return;
                }

                _getCount = _field.FieldType.GetMethod("GetCount");
                if (_getCount == null)
                {
                    SROptimizerMod.Log.LogWarning(
                        $"GetCount() introuvable sur {_field.FieldType} : ce compteur restera a -1.");
                }
            }
        }

        private static readonly ListAccessor FixedUpdate = new ListAccessor("fixedUpdateActorsList");
        private static readonly ListAccessor Update = new ListAccessor("updateActorsList");
        private static readonly ListAccessor LateUpdate = new ListAccessor("lateUpdateActorsList");

        /// <summary>
        /// Passe a true si la reflexion echoue. Une erreur ici survient a chaque frame :
        /// sans ce verrou, la moindre incompatibilite noie le journal du jeu.
        /// </summary>
        private static bool _disabled;

        /// <summary>Nombre d'acteurs recevant RegistryFixedUpdate, ou -1 si indisponible.</summary>
        public static int FixedUpdateActors => Read(FixedUpdate);

        /// <summary>Nombre d'acteurs recevant RegistryUpdate, ou -1 si indisponible.</summary>
        public static int UpdateActors => Read(Update);

        /// <summary>Nombre d'acteurs recevant RegistryLateUpdate, ou -1 si indisponible.</summary>
        public static int LateUpdateActors => Read(LateUpdate);

        /// <summary>Vrai si les compteurs sont exploitables et si une partie est chargee.</summary>
        public static bool IsAvailable => !_disabled && GetRegistry() != null;

        private static ActorRegistry GetRegistry()
        {
            var scene = SRSingleton<SceneContext>.Instance;
            return scene == null ? null : scene.ActorRegistry;
        }

        private static int Read(ListAccessor accessor)
        {
            if (_disabled) return -1;

            try
            {
                return accessor.Read(GetRegistry());
            }
            catch (Exception e)
            {
                _disabled = true;
                SROptimizerMod.Log.LogError(
                    "Lecture des compteurs d'ActorRegistry abandonnee definitivement pour cette " +
                    $"session apres une erreur : {e.Message}");
                return -1;
            }
        }
    }
}
