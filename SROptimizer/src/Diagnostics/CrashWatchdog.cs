using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using SRML;
using SROptimizer.Config;
using UnityEngine;
using UnityEngine.Profiling;

namespace SROptimizer.Diagnostics
{
    /// <summary>
    /// Journal de survie, ecrit et vide a chaque ligne.
    ///
    /// <para>Le jeu a plante deux fois sans laisser la moindre trace : pas d'exception managee,
    /// pas de fichier de crash, et un <c>srml.log</c> qui s'arrete net. C'est le profil d'un
    /// plantage natif — le processus meurt avant que les tampons d'ecriture soient vides.</para>
    ///
    /// <para>Ce journal ecrit donc en mode <c>FileShare.ReadWrite</c> et appelle <c>Flush</c>
    /// apres chaque ligne. C'est volontairement couteux : sans cela, les dernieres secondes
    /// avant le crash — les seules qui comptent — seraient perdues. Il est desactivable.</para>
    /// </summary>
    public static class CrashWatchdog
    {
        private const string FileName = "watchdog.log";

        private static StreamWriter _writer;
        private static bool _failed;
        private static float _nextHeartbeat;
        private static readonly Process Self = Process.GetCurrentProcess();

        public static bool IsActive => _writer != null && !_failed;

        public static void Start()
        {
            if (_failed || _writer != null) return;
            if (!SROptimizerConfig.Diagnostics.watchdogEnabled) return;

            try
            {
                var path = Path.Combine(FileSystem.GetMyConfigPath(), FileName);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                Write($"=== session demarree, SROptimizer {SRModInfo.GetCurrentInfo()?.Version} ===");
                Write($"config : profil={SROptimizerConfig.profile} lod={SROptimizerConfig.Modules.behaviourLod} " +
                      $"ab={SROptimizerConfig.Benchmark.abEnabled} unlockFrameRate={SROptimizerConfig.Benchmark.unlockFrameRate}");
            }
            catch (Exception e)
            {
                _failed = true;
                SROptimizerMod.Log.LogWarning($"Journal de survie indisponible : {e.Message}");
            }
        }

        /// <summary>Ecrit une ligne horodatee et la vide immediatement sur le disque.</summary>
        public static void Write(string message)
        {
            if (_writer == null || _failed) return;
            try
            {
                _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
            catch (Exception)
            {
                _failed = true;
            }
        }

        /// <summary>
        /// Ligne periodique d'etat. La memoire du processus est le point important : le jeu est
        /// un build 32 bits, donc plafonne autour de 4 Go, et un depassement se termine par un
        /// plantage natif silencieux exactement comme ceux observes.
        /// </summary>
        public static void Heartbeat()
        {
            if (_writer == null || _failed) return;

            var now = Time.realtimeSinceStartup;
            if (now < _nextHeartbeat) return;
            _nextHeartbeat = now + Mathf.Max(1f, SROptimizerConfig.Diagnostics.watchdogIntervalSeconds);

            try
            {
                Self.Refresh();
                var privateMo = Self.PrivateMemorySize64 / 1048576;
                var workingMo = Self.WorkingSet64 / 1048576;
                var unityMo = Profiler.GetTotalAllocatedMemoryLong() / 1048576;
                var geree = GC.GetTotalMemory(false) / 1048576;

                Write($"etat : privee {privateMo} Mo | travail {workingMo} Mo | unity {unityMo} Mo | " +
                      $"geree {geree} Mo | acteurs {ActorCounter.FixedUpdateActors} | " +
                      $"fps {(Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f):F0}");
            }
            catch (Exception)
            {
                _failed = true;
            }
        }

        public static void Stop(string reason)
        {
            if (_writer == null) return;
            try
            {
                Write($"=== fin normale : {reason} ===");
                _writer.Dispose();
            }
            catch (Exception)
            {
                // Rien a faire de plus a l'arret.
            }
            finally
            {
                _writer = null;
            }
        }
    }
}
