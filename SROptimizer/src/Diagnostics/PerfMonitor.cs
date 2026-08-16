using System;
using UnityEngine;

namespace SROptimizer.Diagnostics
{
    /// <summary>
    /// Echantillonne le frametime dans un tampon circulaire et en derive FPS moyen,
    /// 1% low et 0.1% low, ainsi que le debit d'allocation du GC.
    ///
    /// Le calcul des percentiles est fait a la demande (au rythme de rafraichissement de
    /// l'overlay), jamais a chaque frame : le tri d'une copie du tampon coute trop cher
    /// pour etre paye 60 fois par seconde.
    /// </summary>
    public sealed class PerfMonitor
    {
        private readonly float[] _frameTimes;
        private readonly float[] _sortBuffer;
        private int _writeIndex;
        private int _filled;

        private long _lastGcBytes;
        private float _lastGcSampleTime;
        private double _allocRateBytesPerSecond;

        public PerfMonitor(int sampleWindow)
        {
            var size = Mathf.Clamp(sampleWindow, 64, 8192);
            _frameTimes = new float[size];
            _sortBuffer = new float[size];
            _lastGcBytes = GC.GetTotalMemory(false);
            _lastGcSampleTime = Time.realtimeSinceStartup;
        }

        public int SampleCount => _filled;

        /// <summary>Debit d'allocation gere, en octets par seconde, lisse sur une seconde.</summary>
        public double AllocRateBytesPerSecond => _allocRateBytesPerSecond;

        /// <summary>Memoire geree actuellement retenue, en octets.</summary>
        public long ManagedBytes { get; private set; }

        /// <summary>A appeler une fois par frame.</summary>
        public void Sample(float unscaledDeltaTime)
        {
            _frameTimes[_writeIndex] = unscaledDeltaTime;
            _writeIndex = (_writeIndex + 1) % _frameTimes.Length;
            if (_filled < _frameTimes.Length) _filled++;

            var now = Time.realtimeSinceStartup;
            var elapsed = now - _lastGcSampleTime;
            if (elapsed >= 1f)
            {
                var bytes = GC.GetTotalMemory(false);
                ManagedBytes = bytes;
                // Un cycle de GC fait chuter le total : on ignore les deltas negatifs plutot
                // que de reporter un debit d'allocation negatif.
                var delta = bytes - _lastGcBytes;
                if (delta > 0) _allocRateBytesPerSecond = delta / elapsed;
                _lastGcBytes = bytes;
                _lastGcSampleTime = now;
            }
        }

        /// <summary>
        /// Calcule les statistiques sur la fenetre courante. Retourne false si trop peu
        /// d'echantillons ont ete collectes pour que le resultat ait un sens.
        /// </summary>
        public bool TryGetStats(out PerfStats stats)
        {
            stats = default;
            if (_filled < 16) return false;

            Array.Copy(_frameTimes, _sortBuffer, _filled);
            Array.Sort(_sortBuffer, 0, _filled);

            double total = 0d;
            for (var i = 0; i < _filled; i++) total += _sortBuffer[i];

            // Les frametimes sont tries en ordre croissant : les pires sont en fin de tableau.
            var onePercentIndex = Mathf.Max(0, _filled - Mathf.Max(1, _filled / 100));
            var tenthPercentIndex = Mathf.Max(0, _filled - Mathf.Max(1, _filled / 1000));

            stats = new PerfStats
            {
                AverageFrameTimeMs = (float)(total / _filled) * 1000f,
                MedianFrameTimeMs = _sortBuffer[_filled / 2] * 1000f,
                WorstFrameTimeMs = _sortBuffer[_filled - 1] * 1000f,
                OnePercentLowFps = SafeFps(_sortBuffer[onePercentIndex]),
                PointOnePercentLowFps = SafeFps(_sortBuffer[tenthPercentIndex]),
                AverageFps = SafeFps((float)(total / _filled)),
                SampleCount = _filled
            };
            return true;
        }

        public void Reset()
        {
            Array.Clear(_frameTimes, 0, _frameTimes.Length);
            _writeIndex = 0;
            _filled = 0;
        }

        private static float SafeFps(float seconds) => seconds > 0.000001f ? 1f / seconds : 0f;
    }

    public struct PerfStats
    {
        public float AverageFps;
        public float OnePercentLowFps;
        public float PointOnePercentLowFps;
        public float AverageFrameTimeMs;
        public float MedianFrameTimeMs;
        public float WorstFrameTimeMs;
        public int SampleCount;
    }
}
