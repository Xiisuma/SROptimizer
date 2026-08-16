using System;
using System.Globalization;
using System.IO;
using System.Text;
using SRML;
using SROptimizer.Config;
using SROptimizer.Modules;
using UnityEngine;

namespace SROptimizer.Diagnostics
{
    /// <summary>
    /// Enregistre periodiquement les statistiques de performance dans un fichier CSV, afin
    /// qu'une mesure de reference survive a la fermeture du jeu.
    ///
    /// Les nombres sont ecrits en culture invariante : le jeu tourne en fr, ou le separateur
    /// decimal est la virgule, ce qui casserait un CSV a separateur virgule et rendrait le
    /// fichier illisible par la plupart des outils d'analyse.
    /// </summary>
    public sealed class BenchRecorder
    {
        private const string Header =
            "horodatage,profil,secondes_ecoulees,fps_moyen,fps_1pct_low,fps_01pct_low," +
            "frametime_moyen_ms,frametime_median_ms,frametime_pire_ms,frames_echantillonnees," +
            "memoire_geree_mo,alloc_mo_par_s,acteurs_fixedupdate,acteurs_update,acteurs_lateupdate,modules_actifs,lod_pct_evite,note";

        private readonly PerfMonitor _monitor;
        private readonly StringBuilder _line = new StringBuilder(256);

        private float _nextSampleTime;
        private float _startTime;
        private float _warmupEndTime;
        private bool _warmupDone;
        private string _note = "";
        private string _path;

        // Reglages de frequence a restaurer a l'arret de la capture.
        private bool _frameRateOverridden;
        private int _savedVSyncCount;
        private int _savedTargetFrameRate;

        public bool IsRecording { get; private set; }
        public int RowsWritten { get; private set; }
        public string OutputPath => _path;

        /// <summary>Vrai tant que la periode de chauffe n'est pas terminee.</summary>
        public bool IsWarmingUp => IsRecording && !_warmupDone;

        /// <summary>Secondes restantes de chauffe, 0 si terminee.</summary>
        public float WarmupRemaining =>
            IsWarmingUp ? Mathf.Max(0f, _warmupEndTime - Time.realtimeSinceStartup) : 0f;

        public BenchRecorder(PerfMonitor monitor)
        {
            _monitor = monitor;
        }

        /// <summary>Demarre une capture. Une capture deja en cours est d'abord arretee.</summary>
        public bool Start(string note)
        {
            if (IsRecording) Stop();

            _note = SanitizeNote(note);
            _path = ResolvePath();
            if (_path == null) return false;

            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                if (!File.Exists(_path))
                {
                    File.WriteAllText(_path, Header + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception e)
            {
                SROptimizerMod.Log.LogError($"Impossible d'ouvrir le fichier de mesure '{_path}' : {e.Message}");
                return false;
            }

            _monitor.Reset();
            var warmup = Mathf.Max(0f, SROptimizerConfig.Benchmark.warmupSeconds);
            _startTime = Time.realtimeSinceStartup;
            _warmupEndTime = _startTime + warmup;
            _warmupDone = warmup <= 0f;
            _nextSampleTime = _warmupEndTime + SampleInterval;
            RowsWritten = 0;
            IsRecording = true;

            UnlockFrameRate();

            SROptimizerMod.Log.LogSuccess(
                $"Capture demarree, ecriture dans {_path}" +
                (warmup > 0f ? $" (chauffe de {warmup:F0} s avant la premiere ligne)" : ""));
            return true;
        }

        public void Stop()
        {
            if (!IsRecording) return;
            IsRecording = false;
            RestoreFrameRate();
            SROptimizerMod.Log.Log($"Capture arretee apres {RowsWritten} ligne(s) dans {_path}");
        }

        /// <summary>A appeler une fois par frame, apres PerfMonitor.Sample.</summary>
        public void Tick()
        {
            if (!IsRecording) return;

            var now = Time.realtimeSinceStartup;

            // Fin de la chauffe : on jette les frames accumulees. Le chargement de la partie
            // produit des a-coups de plus d'une seconde qui, gardes dans la fenetre, fixent le
            // 0.1% low a une valeur absurde pour toute la duree de la capture.
            if (!_warmupDone)
            {
                if (now < _warmupEndTime) return;
                _warmupDone = true;
                _startTime = now;
                _monitor.Reset();
                SROptimizerMod.Log.Log("Chauffe terminee, mesure en cours.");
            }

            if (now < _nextSampleTime) return;

            _nextSampleTime = now + SampleInterval;
            WriteRow();
        }

        /// <summary>
        /// Leve la limite de frequence le temps de la capture. Sans cela, le frametime median
        /// reste fige a la periode de rafraichissement de l'ecran et aucun gain de performance
        /// ne peut apparaitre dans la mesure.
        /// </summary>
        private void UnlockFrameRate()
        {
            if (_frameRateOverridden) return;
            if (!SROptimizerConfig.Benchmark.unlockFrameRate) return;

            _savedVSyncCount = QualitySettings.vSyncCount;
            _savedTargetFrameRate = Application.targetFrameRate;
            _frameRateOverridden = true;

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            SROptimizerMod.Log.Log(
                $"Limite de frequence levee pour la capture (vsync {_savedVSyncCount} -> 0, " +
                $"targetFrameRate {_savedTargetFrameRate} -> -1).");
        }

        /// <summary>
        /// Ouvre une nouvelle phase de mesure : change la note portee par les lignes, vide la
        /// fenetre de frametime et repousse la prochaine ecriture du delai de stabilisation.
        ///
        /// Vider la fenetre est le point essentiel. Elle porte 1024 frames, soit une dizaine de
        /// secondes : sans purge, les premieres lignes d'une phase decriraient encore la phase
        /// precedente et melangeraient les deux etats compares.
        /// </summary>
        public void BeginPhase(string note, float settleSeconds)
        {
            if (!IsRecording) return;

            _note = SanitizeNote(note);
            _monitor.Reset();

            // La premiere ligne tombe a la fin de la stabilisation, pas un intervalle plus tard.
            // Avec l'ancien calcul, stabilisation + intervalle pouvait depasser la duree d'une
            // phase : chaque bascule repoussait l'ecriture avant qu'elle n'arrive, et le fichier
            // restait vide de bout en bout.
            _nextSampleTime = Time.realtimeSinceStartup + Mathf.Max(0f, settleSeconds);

            WarnIfPhaseTooShort(settleSeconds);
        }

        /// <summary>
        /// Previent quand la duree de phase ne laisse pas la place a une seule ligne. Une capture
        /// A/B qui ne produit aucune donnee doit le dire pendant la partie, pas apres coup.
        /// </summary>
        private void WarnIfPhaseTooShort(float settleSeconds)
        {
            if (!SROptimizerConfig.Benchmark.abEnabled || _phaseWarningShown) return;

            var phase = SROptimizerConfig.Benchmark.abIntervalSeconds;
            if (settleSeconds + SampleInterval <= phase) return;

            _phaseWarningShown = true;
            SROptimizerMod.Log.LogWarning(
                $"Mode A/B : phases de {phase:F0} s, mais stabilisation {settleSeconds:F0} s + " +
                $"intervalle d'echantillon {SampleInterval:F0} s. Aucune ligne ne sera ecrite. " +
                $"Augmenter abIntervalSeconds a au moins {settleSeconds + SampleInterval:F0} s.");
        }

        private bool _phaseWarningShown;

        /// <summary>Restaure les reglages de frequence d'origine.</summary>
        public void RestoreFrameRate()
        {
            if (!_frameRateOverridden) return;
            _frameRateOverridden = false;

            QualitySettings.vSyncCount = _savedVSyncCount;
            Application.targetFrameRate = _savedTargetFrameRate;

            SROptimizerMod.Log.Log("Reglages de frequence restaures.");
        }

        /// <summary>
        /// Construit et ecrit une ligne. Tout est dans un seul try : cette methode est appelee
        /// depuis Update, donc une exception qui s'en echappe tue la boucle du runner a chaque
        /// frame. Mieux vaut arreter la capture que casser la mesure entiere.
        /// </summary>
        private void WriteRow()
        {
            try
            {
                if (!_monitor.TryGetStats(out var s)) return;
                BuildRow(s);
                File.AppendAllText(_path, _line.ToString() + Environment.NewLine, Encoding.UTF8);
                RowsWritten++;
            }
            catch (Exception e)
            {
                IsRecording = false;
                SROptimizerMod.Log.LogError($"Capture interrompue : {e.Message}");
            }
        }

        private void BuildRow(PerfStats s)
        {
            var c = CultureInfo.InvariantCulture;
            _line.Length = 0;
            _line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", c)).Append(',')
                 .Append(SROptimizerMod.ActiveProfile).Append(',')
                 .Append((Time.realtimeSinceStartup - _startTime).ToString("F1", c)).Append(',')
                 .Append(s.AverageFps.ToString("F2", c)).Append(',')
                 .Append(s.OnePercentLowFps.ToString("F2", c)).Append(',')
                 .Append(s.PointOnePercentLowFps.ToString("F2", c)).Append(',')
                 .Append(s.AverageFrameTimeMs.ToString("F3", c)).Append(',')
                 .Append(s.MedianFrameTimeMs.ToString("F3", c)).Append(',')
                 .Append(s.WorstFrameTimeMs.ToString("F3", c)).Append(',')
                 .Append(s.SampleCount.ToString(c)).Append(',')
                 .Append((_monitor.ManagedBytes / 1048576.0).ToString("F2", c)).Append(',')
                 .Append((_monitor.AllocRateBytesPerSecond / 1048576.0).ToString("F3", c)).Append(',')
                 .Append(ActorCounter.FixedUpdateActors.ToString(c)).Append(',')
                 .Append(ActorCounter.UpdateActors.ToString(c)).Append(',')
                 .Append(ActorCounter.LateUpdateActors.ToString(c)).Append(',')
                 .Append(ActiveModuleIds()).Append(',')
                 .Append(LodGate.SkipRatioPercent.ToString("F1", c)).Append(',')
                 .Append(_note);
        }

        private static float SampleInterval =>
            Mathf.Max(1f, SROptimizerConfig.Benchmark.sampleIntervalSeconds);

        /// <summary>
        /// Chemin du fichier de sortie. Par defaut une capture par fichier, horodate :
        /// des captures cumulees dans un meme fichier doivent etre reseparees a la main,
        /// et une comparaison avant/apres faite sur un melange de sessions ne veut rien dire.
        /// </summary>
        private static string ResolvePath()
        {
            try
            {
                var fileName = SROptimizerConfig.Benchmark.outputFile;
                if (string.IsNullOrEmpty(fileName)) fileName = "baseline.csv";

                if (SROptimizerConfig.Benchmark.oneFilePerSession)
                {
                    var stem = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    if (string.IsNullOrEmpty(ext)) ext = ".csv";
                    fileName = $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
                }

                return Path.Combine(FileSystem.GetMyConfigPath(), fileName);
            }
            catch (Exception e)
            {
                SROptimizerMod.Log.LogError($"Chemin du fichier de mesure irresolvable : {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Identifiants des modules actifs, separes par des points-virgules pour ne pas
        /// casser le CSV. Sans cette colonne, rien dans le fichier ne distingue une capture
        /// de reference d'une capture avec optimisations.
        /// </summary>
        private static string ActiveModuleIds()
        {
            var modules = SROptimizerMod.Instance?.Modules;
            if (modules == null || modules.Count == 0) return "aucun";

            var result = "";
            foreach (var module in modules)
            {
                if (!module.IsEnabled) continue;
                if (result.Length > 0) result += ";";
                result += module.Id;
            }
            return result.Length == 0 ? "aucun" : result;
        }

        /// <summary>La note finit dans une colonne CSV : on retire ce qui casserait le format.</summary>
        private static string SanitizeNote(string note)
        {
            if (string.IsNullOrEmpty(note)) return "";
            return note.Replace(',', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }
}
