#if UNITY_EDITOR
using System;
using System.Collections.Generic;

using UnityEngine;

namespace EditorPlus.SceneTimeline
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class TimelinePaletteBinder : MonoBehaviour, ITimelineTrackLayerPaletteReceiver
    {
        public enum VisualizationMode { OutlinePaletteIndex, MaterialColor }
        public enum ColorSource { Fill, Outline }

        [SerializeField] private string _trackLabel;
        [SerializeField] private VisualizationMode _mode = VisualizationMode.OutlinePaletteIndex;
        [SerializeField] private ColorSource _colorSource = ColorSource.Outline;
        [SerializeField] private Renderer[] _renderers;
        [SerializeField] private bool _autoCollectRenderers = true;
        [SerializeField] private string _outlineIndexProperty = "_OutlineIndex";
        [SerializeField] private string _colorProperty = "_EmissionColor";
        [SerializeField] private float _colorIntensity = 1f;

        private int _cachedOutlineIndexId;
        private int _cachedColorPropertyId;
        private float _currentOutlineIndex = -1f;
        private float _lastOutlineIndex = float.NaN;
        private Color _lastColorValue = new Color(float.NaN, float.NaN, float.NaN, float.NaN);
        private IReadOnlyDictionary<string, PaletteEntry> _paletteSnapshot;

        private static MaterialPropertyBlock SharedPropertyBlock;
        private static readonly int GlobalOutlineIndexId = Shader.PropertyToID("_OutlineIndex");
        private static MaterialPropertyBlock GetSharedPropertyBlock()
        {
            if (SharedPropertyBlock == null) SharedPropertyBlock = new MaterialPropertyBlock();
            return SharedPropertyBlock;
        }

        string ITimelineTrackColorReceiver.TrackLabel => _trackLabel;

        private void Awake() { CachePropertyIds(); ResetCachedStates(); AutoCollectRenderersIfNeeded(); }
        private void OnEnable() { ResetCachedStates(); AutoCollectRenderersIfNeeded(); }
        private void OnDisable() { ((ITimelineTrackColorReceiver)this).ClearTimelinePalette(); ResetCachedStates(); }
        private void OnValidate() { CachePropertyIds(); AutoCollectRenderersIfNeeded(); }

        void ITimelineTrackColorReceiver.ApplyTimelinePalette(PaletteEntry entry)
        {
            switch (_mode)
            {
                case VisualizationMode.OutlinePaletteIndex:
                    if (_paletteSnapshot != null) UpdateOutlineIndexFromPalette();
                    else { _currentOutlineIndex = entry.IsActive ? entry.Index : -1f; PushOutlineIndex(); }
                    break;
                case VisualizationMode.MaterialColor:
                    Color baseColor = _colorSource == ColorSource.Fill ? entry.FillColor : entry.OutlineColor;
                    Color selectedColor = entry.IsActive ? baseColor * _colorIntensity : Color.black;
                    PushMaterialColor(selectedColor);
                    break;
            }
        }

        void ITimelineTrackColorReceiver.ClearTimelinePalette()
        {
            switch (_mode)
            {
                case VisualizationMode.OutlinePaletteIndex:
                    _paletteSnapshot = null;
                    ClearOutlineLayers();
                    break;
                case VisualizationMode.MaterialColor:
                    PushMaterialColor(Color.black);
                    break;
            }
        }

        bool ITimelineTrackLayerPaletteReceiver.ApplyLayeredTimelinePalette(IReadOnlyDictionary<string, PaletteEntry> palette)
        {
            _paletteSnapshot = palette;
            if (_mode == VisualizationMode.OutlinePaletteIndex)
            {
                UpdateOutlineIndexFromPalette();
                return _currentOutlineIndex >= 0f;
            }
            return false;
        }

        private void CachePropertyIds()
        {
            if (_outlineIndexProperty == "_QB_SceneTimelineOutlineIndex") _outlineIndexProperty = "_OutlineIndex";
            string outlinePropertyName = string.IsNullOrEmpty(_outlineIndexProperty) ? "_OutlineIndex" : _outlineIndexProperty;
            string colorPropertyName = string.IsNullOrEmpty(_colorProperty) ? "_EmissionColor" : _colorProperty;
            _cachedOutlineIndexId = Shader.PropertyToID(outlinePropertyName);
            _cachedColorPropertyId = Shader.PropertyToID(colorPropertyName);
        }

        private void AutoCollectRenderersIfNeeded()
        {
            if (!_autoCollectRenderers) return;
            if (_renderers != null && _renderers.Length > 0) return;
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void UpdateOutlineIndexFromPalette()
        {
            if (_paletteSnapshot != null)
            {
                int chosen = -1;
                if (!string.IsNullOrEmpty(_trackLabel) && _paletteSnapshot.TryGetValue(_trackLabel, out var labeled) && labeled.IsActive)
                {
                    chosen = labeled.Index;
                }
                else
                {
                    int min = int.MaxValue;
                    foreach (var kv in _paletteSnapshot)
                    {
                        var e = kv.Value;
                        if (e.IsActive && e.Index >= 0 && e.Index < min) min = e.Index;
                    }
                    if (min != int.MaxValue) chosen = min;
                }
                _currentOutlineIndex = chosen;
            }
            PushOutlineIndex();
        }

        private void PushOutlineIndex()
        {
            if (Mathf.Approximately(_currentOutlineIndex, _lastOutlineIndex)) return;
            _lastOutlineIndex = _currentOutlineIndex;

            if (_renderers != null && _renderers.Length > 0)
            {
                var mpb = GetSharedPropertyBlock();
                for (int i = 0; i < _renderers.Length; i++)
                {
                    var renderer = _renderers[i];
                    if (!renderer) continue;
                    renderer.GetPropertyBlock(mpb);
                    mpb.SetFloat(_cachedOutlineIndexId, _currentOutlineIndex);
                    renderer.SetPropertyBlock(mpb);
                    mpb.Clear();
                }
            }

            Shader.SetGlobalFloat(Shader.PropertyToID("_OutlineIndex"), _currentOutlineIndex);
        }

        private void ClearOutlineLayers()
        {
            _currentOutlineIndex = -1f;
            _paletteSnapshot = null;
            _lastOutlineIndex = float.NaN;
            PushOutlineIndex();
        }

        private void PushMaterialColor(Color color)
        {
            if (_renderers == null) return;
            if (_lastColorValue == color) return;

            _lastColorValue = color;
            var mpb = GetSharedPropertyBlock();
            for (int i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (!renderer) continue;
                renderer.GetPropertyBlock(mpb);
                mpb.SetColor(_cachedColorPropertyId, color);
                renderer.SetPropertyBlock(mpb);
                mpb.Clear();
            }
        }

        private void ResetCachedStates()
        {
            _lastOutlineIndex = float.NaN;
            _currentOutlineIndex = -1f;
            _lastColorValue = new Color(float.NaN, float.NaN, float.NaN, float.NaN);
            _paletteSnapshot = null;
        }
    }
}
#endif
