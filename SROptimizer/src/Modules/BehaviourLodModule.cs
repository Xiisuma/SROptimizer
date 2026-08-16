using System;
using HarmonyLib;
using SROptimizer.Config;
using UnityEngine;

namespace SROptimizer.Modules
{
    /// <summary>
    /// Module A — LOD comportemental.
    ///
    /// <para>Constat de la mesure de reference : le frametime suit le nombre d'acteurs simules
    /// (correlation 0,721), et <c>SlimeSubbehaviourPlexer.RegistryFixedUpdate</c> s'execute pour
    /// chaque slime a 50 Hz sans aucune notion de distance au joueur ni de visibilite.</para>
    ///
    /// <para>Ce module n'allege pas le travail d'un slime : il en espace l'execution selon la
    /// distance et la visibilite. Un slime proche ou visible garde exactement le comportement
    /// d'origine.</para>
    ///
    /// <para>La decision est purement fonctionnelle — aucun etat par slime n'est conserve. Le
    /// decalage de phase vient de <c>GetInstanceID()</c>, ce qui repartit les slimes sur des
    /// frames differentes : sans ce decalage, tous les slimes d'un meme palier se mettraient a
    /// jour sur la meme frame et creeraient le pic de frametime que le module cherche a
    /// supprimer.</para>
    /// </summary>
    public sealed class BehaviourLodModule : OptimizerModuleBase
    {
        public override string Id => "lod";

        public override string Description =>
            "Espace la simulation des slimes distants ou hors champ.";

        protected override void InstallPatches(Harmony harmony)
        {
            // Patch explicite plutot que PatchAll : la surcharge PatchAll(Type) n'existe pas
            // dans le Harmony fourni par SRML, et nommer la methode ciblee ici rend le point
            // d'accroche du module visible d'un coup d'oeil.
            var target = AccessTools.Method(typeof(SlimeSubbehaviourPlexer), "RegistryFixedUpdate");
            if (target == null)
            {
                throw new InvalidOperationException(
                    "SlimeSubbehaviourPlexer.RegistryFixedUpdate introuvable : version du jeu inattendue.");
            }

            harmony.Patch(target, prefix: new HarmonyMethod(
                AccessTools.Method(typeof(PlexerPatch), nameof(PlexerPatch.Prefix))));
        }

        protected override void OnActivated()
        {
            LodGate.Reset();
            LodGate.Active = true;
            Diagnostics.CrashWatchdog.Write("module lod : actif");
        }

        protected override void OnDeactivated()
        {
            // Le patch reste pose : c'est ce booleen qui le neutralise. Le desinstaller a chaud
            // faisait planter le jeu en natif, voir la remarque de OptimizerModuleBase.
            LodGate.Active = false;
            Diagnostics.CrashWatchdog.Write(
                $"module lod : inactif apres {LodGate.Skipped} appels evites sur {LodGate.Considered}");
        }

        public override string GetStatusLine()
        {
            if (!IsEnabled) return "inactif";
            return $"proche <{SROptimizerConfig.Lod.nearDistance:F0} m, " +
                   $"moyen <{SROptimizerConfig.Lod.midDistance:F0} m (1 frame sur {SROptimizerConfig.Lod.midDivisor}), " +
                   $"loin (1 sur {SROptimizerConfig.Lod.farDivisor}) | " +
                   $"{LodGate.SkipRatioPercent:F0}% d'appels evites";
        }

        /// <summary>
        /// Prefix sur le pilote de comportement des slimes. Retourner false saute l'execution
        /// d'origine pour cette frame.
        /// </summary>
        private static class PlexerPatch
        {
            internal static bool Prefix(SlimeSubbehaviourPlexer __instance)
            {
                return LodGate.ShouldRun(__instance);
            }
        }
    }

    /// <summary>
    /// Decide si un slime doit penser cette frame. Sorti du module pour que la logique reste
    /// testable et que le patch Harmony ne contienne rien d'autre qu'un appel.
    /// </summary>
    internal static class LodGate
    {
        // Accesseurs rapides vers les champs prives du plexer. AccessTools.FieldRefAccess
        // produit un delegue proche du natif : indispensable ici, ou le code tourne pour
        // chaque slime a 50 Hz. Une lecture par FieldInfo.GetValue serait bien trop couteuse.
        private static readonly AccessTools.FieldRef<SlimeSubbehaviourPlexer, SlimeSubbehaviour> CurrBehaviorRef =
            AccessTools.FieldRefAccess<SlimeSubbehaviourPlexer, SlimeSubbehaviour>("currBehavior");

        private static readonly AccessTools.FieldRef<SlimeSubbehaviourPlexer, int> BlockersRef =
            AccessTools.FieldRefAccess<SlimeSubbehaviourPlexer, int>("behaviorBlockers");

        // Contexte recalcule une fois par frame et partage par tous les slimes.
        private static int _contextFrame = -1;
        private static Vector3 _playerPos;
        private static Vector3 _cameraPos;
        private static Vector3 _cameraForward;
        private static bool _hasCamera;
        private static Camera _camera;

        /// <summary>
        /// Etat du module. Le patch n'etant jamais retire, c'est ce drapeau qui decide si la
        /// logique de LOD s'applique ou si le comportement d'origine passe intact.
        /// </summary>
        public static bool Active;

        private static long _considered;
        private static long _skipped;
        private static bool _failed;

        /// <summary>Part des appels evites depuis l'activation, en pourcentage.</summary>
        public static float SkipRatioPercent => _considered == 0 ? 0f : 100f * _skipped / _considered;

        public static long Considered => _considered;
        public static long Skipped => _skipped;

        public static void Reset()
        {
            _considered = 0;
            _skipped = 0;
            _contextFrame = -1;
            _camera = null;
            _failed = false;
        }

        /// <summary>
        /// Vrai si le comportement d'origine doit s'executer cette frame.
        /// En cas d'anomalie, retourne toujours vrai : le pire defaut acceptable pour ce module
        /// est de ne rien optimiser, jamais de casser le comportement d'un slime.
        /// </summary>
        public static bool ShouldRun(SlimeSubbehaviourPlexer plexer)
        {
            // Module inactif : le patch reste pose mais se comporte comme le jeu d'origine.
            if (!Active || _failed || plexer == null) return true;

            try
            {
                _considered++;

                var divisor = ResolveDivisor(plexer);
                if (divisor <= 1) return true;

                // Decalage de phase par slime : repartit les mises a jour sur les frames au lieu
                // de les grouper toutes sur la meme.
                var phase = plexer.GetInstanceID() & 0x7FFFFFFF;
                if ((Time.frameCount + phase) % divisor == 0) return true;

                _skipped++;
                return false;
            }
            catch (Exception e)
            {
                _failed = true;
                SROptimizerMod.Log.LogError(
                    $"LOD comportemental desactive pour cette session apres une erreur : {e.Message}");
                return true;
            }
        }

        /// <summary>
        /// Facteur d'espacement pour ce slime. 1 signifie « comportement d'origine ».
        /// </summary>
        private static int ResolveDivisor(SlimeSubbehaviourPlexer plexer)
        {
            // Un slime aspire ou tenu doit rester parfaitement reactif.
            if (plexer.IsCaptive()) return 1;

            // Un blocage externe est deja gere par le jeu ; ne pas s'y superposer.
            if (BlockersRef(plexer) > 0) return 1;

            // Comportement en cours non interruptible. Le cas critique est
            // GatherIdentifiableItems : tant que le slime porte un objet, son CanRethink()
            // est faux et c'est Action() qui libere le FixedJoint au bout de 10 s. Sauter
            // Action() laisserait le slime accroche a son objet indefiniment.
            var current = CurrBehaviorRef(plexer);
            if (current != null && !current.CanRethink()) return 1;

            EnsureContext();

            var position = plexer.transform.position;
            var sqrToPlayer = (position - _playerPos).sqrMagnitude;

            var near = SROptimizerConfig.Lod.nearDistance;
            if (sqrToPlayer <= near * near) return 1;

            var mid = SROptimizerConfig.Lod.midDistance;
            var isFar = sqrToPlayer > mid * mid;

            // Hors champ : teste par produit scalaire avec l'axe de la camera. Un test de
            // frustum complet par slime couterait plus cher que ce qu'il ferait economiser.
            if (!isFar && _hasCamera && SROptimizerConfig.Lod.treatOffscreenAsFar)
            {
                if (Vector3.Dot(position - _cameraPos, _cameraForward) < 0f) isFar = true;
            }

            return Mathf.Max(1, isFar
                ? SROptimizerConfig.Lod.farDivisor
                : SROptimizerConfig.Lod.midDivisor);
        }

        /// <summary>
        /// Position du joueur et orientation de la camera, calculees une fois par frame.
        /// Les relire pour chaque slime multiplierait par plusieurs centaines des acces qui
        /// donnent tous le meme resultat dans la frame.
        /// </summary>
        private static void EnsureContext()
        {
            var frame = Time.frameCount;
            if (_contextFrame == frame) return;
            _contextFrame = frame;

            var scene = SRSingleton<SceneContext>.Instance;
            var player = scene == null ? null : scene.Player;
            if (player != null) _playerPos = player.transform.position;

            if (_camera == null) _camera = Camera.main;
            _hasCamera = _camera != null;
            if (_hasCamera)
            {
                var camTransform = _camera.transform;
                _cameraPos = camTransform.position;
                _cameraForward = camTransform.forward;
            }
        }
    }
}
