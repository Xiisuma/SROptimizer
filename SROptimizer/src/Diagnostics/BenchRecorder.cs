using System;
using System.Globalization;
using System.IO;
using System.Text;
using SRML;
using SROptimizer.Config;
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
            "memoire_geree_mo,alloc_mo_par_s,acteurs_fixedupdate,acteurs_update,acteurs_lateupdate,note";

        private readonly PerfMonitor _monitor;
        private readonly StringBuilder _line = new StringBuilder(256);

        private float _nextSampleTime;
        private float _startTime;
        private string _note = "";
        private string _path;

        public bool IsRecording { get; private set; }
        public int RowsWritten { get; private set; }
        public string OutputPath => _path;

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
            _startTime = Time.realtimeSinceStartup;
            _nextSampleTime = _startTime + SampleInterval;
            RowsWritten = 0;
            IsRecording = true;

            SROptimizerMod.Log.LogSuccess($"Capture demarree, ecriture dans {_path}");
            return true;
        }

        public void Stop()
        {
            if (!IsRecording) return;
            IsRecording = false;
            SROptimizerMod.Log.Log($"Capture arretee apres {RowsWritten} ligne(s) dans {_path}");
        }

        /// <summary>A appeler une fois par frame, apres PerfMonitor.Sample.</summary>
        public void Tick()
        {
            if (!IsRecording) return;
            if (Time.realtimeSinceStartup < _nextSampleTime) return;

            _nextSampleTime = Time.realtimeSinceStartup + SampleInterval;
            WriteRow();
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
                 .Append(_note);
        }

        private static float SampleInterval =>
            Mathf.Max(1f, SROptimizerConfig.Benchmark.sampleIntervalSeconds);

        private static string ResolvePath()
        {
            try
            {
                var fileName = SROptimizerConfig.Benchmark.outputFile;
                if (string.IsNullOrEmpty(fileName)) fileName = "baseline.csv";
                return Path.Combine(FileSystem.GetMyConfigPath(), fileName);
            }
            catch (Exception e)
            {
                SROptimizerMod.Log.LogError($"Chemin du fichier de mesure irresolvable : {e.Message}");
                return null;
            }
        }

        /// <summary>La note finit dans une colonne CSV : on retire ce qui casserait le format.</summary>
        private static string SanitizeNote(string note)
        {
            if (string.IsNullOrEmpty(note)) return "";
            return note.Replace(',', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }
}
