using System;
using SROptimizer.Config;
using UnityEngine;

namespace SROptimizer.Diagnostics
{
    /// <summary>
    /// Composant pilote du mod : c'est lui qui bat la mesure a chaque frame.
    ///
    /// Separe de PerfOverlay volontairement : l'overlay ne fait que dessiner, et son OnGUI
    /// n'est appele que s'il est visible. L'echantillonnage du frametime et l'enregistrement
    /// doivent tourner meme overlay masque, sinon une capture lancee sans affichage ne
    /// mesurerait rien.
    /// </summary>
    public sealed class SROptimizerRunner : MonoBehaviour
    {
        private PerfMonitor _monitor;
        private PerfOverlay _overlay;
        private KeyCode _toggleKey = KeyCode.F9;
        private bool _autoStartDone;
        private float _nextAbToggleTime;

        public BenchRecorder Bench { get; private set; }

        public void Initialize(PerfMonitor monitor, PerfOverlay overlay)
        {
            _monitor = monitor;
            _overlay = overlay;
            Bench = new BenchRecorder(monitor);
            CrashWatchdog.Start();
            _toggleKey = ParseKey(SROptimizerConfig.Diagnostics.overlayToggleKey, KeyCode.F9);
        }

        private void Update()
        {
            if (_monitor == null) return;

            _monitor.Sample(Time.unscaledDeltaTime);
            ReportFrameSpike();

            if (_toggleKey != KeyCode.None && Input.GetKeyDown(_toggleKey))
            {
                _overlay?.Toggle();
            }

            CrashWatchdog.Heartbeat();
            TryAutoStartBench();
            TickAbToggle();
            Bench?.Tick();
        }

        /// <summary>
        /// Signale toute frame anormalement longue dans le journal de survie.
        ///
        /// Un gel de plusieurs secondes ne laisse aucune trace dans les mesures glissantes :
        /// il est noye dans la moyenne et le battement periodique passe a cote. Cette ligne,
        /// ecrite des la frame suivante, date le blocage a la milliseconde et dit combien
        /// d'acteurs etaient simules a cet instant.
        /// </summary>
        private void ReportFrameSpike()
        {
            var ms = Time.unscaledDeltaTime * 1000f;
            if (ms < SpikeThresholdMs) return;

            CrashWatchdog.Write($"frame longue : {ms:F0} ms | acteurs {ActorCounter.FixedUpdateActors} | " +
                                $"lod {(SROptimizerMod.Instance.FindModule("lod")?.IsEnabled == true ? "on" : "off")}");
        }

        private const float SpikeThresholdMs = 250f;

        /// <summary>
        /// Mode A/B : bascule le module cible a intervalle regulier pendant la capture.
        ///
        /// Comparer deux sessions differentes ne prouve rien — la sauvegarde, le parcours et la
        /// zone changent en meme temps que le module, et l'ecart mesure peut venir de n'importe
        /// laquelle de ces variables. En alternant dans une seule partie, les deux etats sont
        /// mesures au meme endroit, a quelques secondes d'intervalle.
        /// </summary>
        private void TickAbToggle()
        {
            if (Bench == null || !Bench.IsRecording || Bench.IsWarmingUp) return;
            if (!SROptimizerConfig.Benchmark.abEnabled) return;

            var interval = Mathf.Max(5f, SROptimizerConfig.Benchmark.abIntervalSeconds);
            var now = Time.realtimeSinceStartup;

            if (_nextAbToggleTime <= 0f)
            {
                // Premiere phase : on part de l'etat courant du module, sans rien basculer.
                _nextAbToggleTime = now + interval;
                AnnouncePhase();
                return;
            }

            if (now < _nextAbToggleTime) return;
            _nextAbToggleTime = now + interval;

            var module = SROptimizerMod.Instance.FindModule(SROptimizerConfig.Benchmark.abModuleId);
            if (module == null)
            {
                SROptimizerMod.Log.LogError(
                    $"Mode A/B : module '{SROptimizerConfig.Benchmark.abModuleId}' introuvable, mode arrete.");
                SROptimizerConfig.Benchmark.abEnabled = false;
                return;
            }

            // Tracage fin de la bascule. Les trois incidents observes sont survenus a moins de
            // deux secondes de la premiere bascule, sans jamais laisser de trace exploitable.
            // Chaque etape est donc journalisee et videe sur le disque separement : la derniere
            // ligne presente dans le fichier designera l'instruction qui bloque.
            var cible = !module.IsEnabled;
            CrashWatchdog.Write($"bascule A/B : debut, {module.Id} -> {(cible ? "on" : "off")}, " +
                                $"acteurs {ActorCounter.FixedUpdateActors}");

            SROptimizerMod.Instance.SetModuleEnabled(module.Id, cible);
            CrashWatchdog.Write("bascule A/B : etat du module change");

            AnnouncePhase();
            CrashWatchdog.Write("bascule A/B : terminee");
        }

        private void AnnouncePhase()
        {
            var module = SROptimizerMod.Instance.FindModule(SROptimizerConfig.Benchmark.abModuleId);
            if (module == null) return;

            var note = $"ab {module.Id}={(module.IsEnabled ? "on" : "off")}";
            Bench.BeginPhase(note, SROptimizerConfig.Benchmark.abSettleSeconds);
            SROptimizerMod.Log.Log($"Mode A/B : phase '{note}'.");
        }

        // La capture leve la vsync : ne jamais laisser le jeu se fermer, ou l'objet disparaitre,
        // sans avoir rendu au joueur ses reglages d'origine.
        private void OnApplicationQuit()
        {
            Bench?.Stop();
            CrashWatchdog.Stop("fermeture du jeu");
        }

        private void OnDestroy() => Bench?.RestoreFrameRate();

        /// <summary>
        /// Demarre la capture automatique une fois qu'une partie est reellement chargee.
        /// Attendre l'ActorRegistry evite d'enregistrer les frames du menu principal, qui
        /// n'ont rien a voir avec la charge en jeu et fausseraient la moyenne.
        /// </summary>
        private void TryAutoStartBench()
        {
            if (_autoStartDone) return;
            if (!SROptimizerConfig.Benchmark.autoStart) return;
            if (!ActorCounter.IsAvailable) return;

            _autoStartDone = true;
            Bench.Start("auto");
        }

        private static KeyCode ParseKey(string name, KeyCode fallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            try
            {
                return (KeyCode)Enum.Parse(typeof(KeyCode), name.Trim(), true);
            }
            catch (Exception)
            {
                SROptimizerMod.Log.LogWarning(
                    $"Touche d'overlay '{name}' inconnue, repli sur {fallback}. " +
                    "Utiliser un nom de UnityEngine.KeyCode (F9, F10, BackQuote, ...).");
                return fallback;
            }
        }
    }
}
