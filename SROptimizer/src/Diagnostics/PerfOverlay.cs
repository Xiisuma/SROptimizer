using System;
using System.Text;
using SROptimizer.Config;
using UnityEngine;

namespace SROptimizer.Diagnostics
{
    /// <summary>
    /// Overlay IMGUI de diagnostic. Cree une seule fois, survit aux changements de scene.
    ///
    /// Le texte n'est reconstruit qu'a intervalle fixe (overlayRefreshInterval) : OnGUI est
    /// appele plusieurs fois par frame par Unity, y construire des chaines serait une source
    /// d'allocations exactement du type que ce mod cherche a supprimer.
    /// </summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
        private PerfMonitor _monitor;
        private readonly StringBuilder _builder = new StringBuilder(512);
        private string _cachedText = "";
        private float _nextRefreshTime;
        private KeyCode _toggleKey = KeyCode.F9;
        private GUIStyle _style;
        private Texture2D _background;

        public bool Visible { get; private set; }

        public PerfMonitor Monitor => _monitor;

        public void Initialize(PerfMonitor monitor)
        {
            _monitor = monitor;
            Visible = SROptimizerConfig.Diagnostics.overlayEnabled;
            _toggleKey = ParseKey(SROptimizerConfig.Diagnostics.overlayToggleKey, KeyCode.F9);
        }

        public void Toggle() => Visible = !Visible;

        public void SetVisible(bool visible) => Visible = visible;

        private void Update()
        {
            if (_monitor == null) return;
            _monitor.Sample(Time.unscaledDeltaTime);

            if (_toggleKey != KeyCode.None && Input.GetKeyDown(_toggleKey))
            {
                Toggle();
            }
        }

        private void OnGUI()
        {
            if (!Visible || _monitor == null) return;

            EnsureStyle();

            if (Time.realtimeSinceStartup >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.realtimeSinceStartup +
                                   Mathf.Max(0.05f, SROptimizerConfig.Diagnostics.overlayRefreshInterval);
                RebuildText();
            }

            var size = _style.CalcSize(new GUIContent(_cachedText));
            var rect = GetAnchoredRect(size.x + 16f, size.y + 12f);

            GUI.DrawTexture(rect, Background());
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width, rect.height), _cachedText, _style);
        }

        private void RebuildText()
        {
            _builder.Length = 0;
            _builder.Append("SROptimizer  [profil ").Append(SROptimizerMod.ActiveProfile).Append(']');

            if (_monitor.TryGetStats(out var s))
            {
                _builder.Append('\n')
                    .Append("FPS moy   ").Append(s.AverageFps.ToString("F1"))
                    .Append("   frametime ").Append(s.AverageFrameTimeMs.ToString("F2")).Append(" ms");
                _builder.Append('\n')
                    .Append("1% low    ").Append(s.OnePercentLowFps.ToString("F1"))
                    .Append("   0.1% low ").Append(s.PointOnePercentLowFps.ToString("F1"));
                _builder.Append('\n')
                    .Append("median    ").Append(s.MedianFrameTimeMs.ToString("F2")).Append(" ms")
                    .Append("   pire ").Append(s.WorstFrameTimeMs.ToString("F2")).Append(" ms");
                _builder.Append('\n')
                    .Append("fenetre   ").Append(s.SampleCount).Append(" frames");
            }
            else
            {
                _builder.Append("\ncollecte en cours...");
            }

            _builder.Append('\n')
                .Append("GC        ").Append((_monitor.ManagedBytes / 1048576.0).ToString("F1")).Append(" Mo retenus")
                .Append("   ").Append((_monitor.AllocRateBytesPerSecond / 1048576.0).ToString("F2")).Append(" Mo/s");

            var modules = SROptimizerMod.Instance?.Modules;
            if (modules != null)
            {
                _builder.Append("\nmodules   ");
                var any = false;
                foreach (var module in modules)
                {
                    if (!module.IsEnabled) continue;
                    if (any) _builder.Append(", ");
                    _builder.Append(module.Id);
                    any = true;
                }
                if (!any) _builder.Append("aucun");
            }

            _cachedText = _builder.ToString();
        }

        private Rect GetAnchoredRect(float width, float height)
        {
            const float margin = 10f;
            switch ((SROptimizerConfig.Diagnostics.overlayCorner ?? "topleft").Trim().ToLowerInvariant())
            {
                case "topright":
                    return new Rect(Screen.width - width - margin, margin, width, height);
                case "bottomleft":
                    return new Rect(margin, Screen.height - height - margin, width, height);
                case "bottomright":
                    return new Rect(Screen.width - width - margin, Screen.height - height - margin, width, height);
                default:
                    return new Rect(margin, margin, width, height);
            }
        }

        private void EnsureStyle()
        {
            if (_style != null) return;
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.UpperLeft,
                richText = false,
                wordWrap = false
            };
            _style.normal.textColor = Color.white;
        }

        private Texture2D Background()
        {
            if (_background != null) return _background;
            _background = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
            _background.Apply();
            _background.hideFlags = HideFlags.HideAndDontSave;
            return _background;
        }

        private void OnDestroy()
        {
            if (_background != null) Destroy(_background);
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
