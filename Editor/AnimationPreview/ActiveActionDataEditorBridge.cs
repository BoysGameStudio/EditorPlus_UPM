#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using EditorPlus.AnimationPreview;

namespace Quantum
{
    // Editor-side helper that holds per-instance editor state and provides extension-like helpers
    // migrated from the original ActiveActionData.Editor.cs implementation.
    internal static class ActiveActionDataEditorBridge
    {
    internal class EditorState
        {
            public GameObject TempPreviewGO;
            public bool PreviewPlaying;
            public double LastEditorTime;
            public bool SceneTimelineOutlineEnabled = true;

            // Preview window settings (migrated from fields)
            public GameObject PreviewModelPrefab;
            public bool PreviewApplyRootMotion = false;
            public float PlaybackSpeed = 1f;
            public bool Loop = true;
            public int PreviewFPS = 60;

            public bool BoundsApplied;
        }

        private static readonly ConditionalWeakTable<ActiveActionData, EditorState> _states = new();

        private static EditorState GetOrCreate(ActiveActionData a)
        {
            if (a == null) return null;
            return _states.GetValue(a, _ => new EditorState());
        }

    internal static EditorState GetState(ActiveActionData a) => _states.TryGetValue(a, out var s) ? s : null;

        public static bool HasActivePreviewPlayer(ActiveActionData a)
        {
            return ActiveActionPreviewBridge.HasActivePreviewPlayer(a);
        }

        public static int GetAnimationCurrentFrame(ActiveActionData a)
        {
            if (a == null || a.Animation == null) return -1;
            var bridgePlayer = ActiveActionPreviewBridge.GetPlayer(a);
            if (bridgePlayer == null) return -1;
            float fps = ResolvePreviewFPS(a, 60f);
            return bridgePlayer != null ? bridgePlayer.CurrentFrameAt(fps) : -1;
        }

        public static void SeekPreviewFrameEditor(ActiveActionData a, int frame)
        {
            if (a == null || a.Animation == null) return;
            ActiveActionPreviewBridge.SyncPlayerClip(a, resetTime: false);
            var bridgePlayer = ActiveActionPreviewBridge.GetPlayer(a);
            if (bridgePlayer == null) return;
            float fps = ResolvePreviewFPS(a, 60f);
            if (fps <= 0f) return;
            int totalFrames = Mathf.Max(1, Mathf.RoundToInt(a.Animation.length * fps));
            int clamped = Mathf.Clamp(frame, 0, totalFrames - 1);
            bridgePlayer.SeekFrame(clamped, fps);
        }

        public static bool EnsurePreviewInfrastructure(ActiveActionData a, bool resetTime)
        {
            return ActiveActionPreviewBridge.EnsurePreviewInfrastructure(a, resetTime);
        }

        public static void SyncPlayerClip(ActiveActionData a, bool resetTime)
        {
            ActiveActionPreviewBridge.SyncPlayerClip(a, resetTime);
        }

        public static void SetPlaying(ActiveActionData a, bool playing, bool restart)
        {
            ActiveActionPreviewBridge.SetPlaying(a, playing, restart);
            var s = GetOrCreate(a);
            s.PreviewPlaying = playing;
            if (playing)
            {
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.update += OnEditorUpdate;
            }
            else
            {
                EditorApplication.update -= OnEditorUpdate;
            }
        }

        public static void StopAndReset(ActiveActionData a)
        {
            ActiveActionPreviewBridge.StopAndReset(a);
            var s = GetOrCreate(a);
            s.PreviewPlaying = false;
            EditorApplication.update -= OnEditorUpdate;
        }

        public static void TeardownPreview(ActiveActionData a)
        {
            var s = GetOrCreate(a);
            s.PreviewPlaying = false;
            EditorApplication.update -= OnEditorUpdate;
            ActiveActionPreviewBridge.TeardownPreview(a);
            s.BoundsApplied = false;
            if (_activeSceneOverlayOwner == a) _activeSceneOverlayOwner = null;
            ReleaseSceneOverlayRenderingSubscription();
            ClearSceneTimelinePalette();
            // Close stage if needed — ActiveActionPreviewBridge handles stage closing
        }

        private static void OnEditorUpdate()
        {
            // noop; active instances will repaint as needed via bridge events
            InternalEditorUtility.RepaintAllViews();
        }

        // A subset of the Scene-timeline helpers migrated below
        private static readonly List<SceneTimelinePaletteEntry> _sceneTimelinePaletteEntries = new(8);
        private static readonly Dictionary<string, int> _sceneTimelinePaletteIndexLookup = new(StringComparer.Ordinal);
        private static ActiveActionData _activeSceneOverlayOwner;
        private static bool _sceneOverlayRenderingSubscribed;
        private static readonly List<SceneTrackRow> _sceneTrackRows = new(8);
        private static readonly List<SceneTrackRow> _activeSceneTrackRows = new(8);
        private static readonly Dictionary<Type, TimelineTrackDescriptor[]> _timelineTrackCache = new();
        private static bool _sceneTimelineDataReady;
        private static int _sceneTimelineRowCount;
        private static int _sceneTimelineCurrentFrame;
        private static int _sceneTimelineMaxFrameIndex;
        private static float _sceneTimelineCurrentTime;
        private static float _sceneTimelineCurrentFps;

        public readonly struct SceneTimelinePaletteEntry
        {
            public SceneTimelinePaletteEntry(int index, string label, Color fillColor, Color outlineColor, bool isActive)
            {
                Index = index; Label = label; FillColor = fillColor; OutlineColor = outlineColor; IsActive = isActive;
            }
            public int Index { get; }
            public string Label { get; }
            public Color FillColor { get; }
            public Color OutlineColor { get; }
            public bool IsActive { get; }
        }

        public static event Action<IReadOnlyList<SceneTimelinePaletteEntry>> SceneTimelinePaletteChanged;
        public readonly struct TrackPaletteEntry { public TrackPaletteEntry(int i, string l, Color f, Color o, bool a) { Index = i; Label = l; FillColor = f; OutlineColor = o; IsActive = a; } public int Index { get; } public string Label { get; } public Color FillColor { get; } public Color OutlineColor { get; } public bool IsActive { get; } public static TrackPaletteEntry From(SceneTimelinePaletteEntry e) => new TrackPaletteEntry(e.Index, e.Label, e.FillColor, e.OutlineColor, e.IsActive); }
        public static event Action<IReadOnlyList<TrackPaletteEntry>> TrackPaletteChanged;

        private static void NotifySceneTimelinePalette(IReadOnlyList<SceneTimelinePaletteEntry> entries)
        {
            if (entries == null) return;
            SceneTimelinePaletteChanged?.Invoke(entries);
            if (TrackPaletteChanged != null)
            {
                var tmp = new TrackPaletteEntry[entries.Count];
                for (int i = 0; i < entries.Count; i++) tmp[i] = TrackPaletteEntry.From(entries[i]);
                TrackPaletteChanged.Invoke(tmp);
            }
            try
            {
                var snapshot = new List<EditorPlus.AnimationPreview.PaletteEntry>(entries.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    snapshot.Add(new EditorPlus.AnimationPreview.PaletteEntry { Label = e.Label, Index = e.Index, FillColor = e.FillColor, OutlineColor = e.OutlineColor, IsActive = e.IsActive });
                }
                EditorPlus.AnimationPreview.PaletteBus.Publish(snapshot);
            }
            catch { }
        }

        private static void ClearSceneTimelinePalette()
        {
            _sceneTimelineDataReady = false;
            _sceneTimelineRowCount = 0;
            _sceneTimelinePaletteEntries.Clear();
            _sceneTimelinePaletteIndexLookup.Clear();
        }

        private static void EnsureSceneOverlayRenderingSubscription()
        {
            if (_sceneOverlayRenderingSubscribed) return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            _sceneOverlayRenderingSubscribed = true;
        }

        private static void ReleaseSceneOverlayRenderingSubscription()
        {
            if (!_sceneOverlayRenderingSubscribed) return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _sceneOverlayRenderingSubscribed = false;
        }

        private static void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera camera)
        {
            TryPrepareSceneTimelineDataForCamera(camera);
        }

        public static bool TryPrepareSceneTimelineDataForCamera(Camera camera)
        {
            if (_activeSceneOverlayOwner == null) return false;
            if (!IsSceneOverlayTargetCamera(camera)) return false;
            // Delegate to the internal preparation helper migrated from the original file
            return PrepareSceneTimelineData_Internal(_activeSceneOverlayOwner);
        }

        private static bool IsSceneOverlayTargetCamera(Camera camera)
        {
            if (camera == null) return false;
            if (camera.cameraType == CameraType.SceneView) return true;
            if (camera.cameraType == CameraType.Game)
            {
                if (EditorPlus.AnimationPreview.AnimationPreviewStageBridge.TryGetStageScene(out var stageScene) && camera.scene == stageScene) return true;
                var stage = StageUtility.GetStage(camera.gameObject);
                if (stage != null && stage != StageUtility.GetMainStage()) return true;
            }
            return false;
        }

        // Minimal timeline helpers retained from original file
        private struct TimelineTrackDescriptor { public string Label; public Color Color; public Color OutlineColor; public int Order; public Func<ActiveActionData, object> Getter; }
        private sealed class SceneTrackRow { public string Label; public Color Color; public Color OutlineColor; public readonly List<TrackSegment> Segments = new(4); public readonly List<int> Markers = new(4); public bool IsAvailable = true; public void Clear() { Segments.Clear(); Markers.Clear(); Label = string.Empty; Color = Color.white; OutlineColor = Color.white; } }
        private struct TrackSegment { public int Start; public int End; }

        private static TimelineTrackDescriptor[] GetTimelineTrackDescriptors(Type type)
        {
            if (type == null) return Array.Empty<TimelineTrackDescriptor>();
            if (_timelineTrackCache.TryGetValue(type, out var cached)) return cached;
            var descriptors = new List<TimelineTrackDescriptor>();
            var members = type.GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            for (int i = 0; i < members.Length; i++)
            {
                var member = members[i];
                var attribute = member.GetCustomAttribute<AnimationEventAttribute>();
                if (attribute == null) continue;
                var getter = CreateTimelineGetter(member);
                if (getter == null) continue;
                var baseColor = ParseTrackColor(attribute.ColorHex);
                var outlineColor = IntensifyOutlineColor(baseColor);
                descriptors.Add(new TimelineTrackDescriptor { Label = string.IsNullOrWhiteSpace(attribute.Label) ? member.Name : attribute.Label, Color = baseColor, OutlineColor = outlineColor, Order = attribute.Order, Getter = getter });
            }
            descriptors.Sort((a, b) => a.Order.CompareTo(b.Order));
            var result = descriptors.ToArray();
            _timelineTrackCache[type] = result;
            return result;
        }

        private static Func<ActiveActionData, object> CreateTimelineGetter(System.Reflection.MemberInfo member)
        {
            if (member is System.Reflection.FieldInfo f) return instance => f.GetValue(instance);
            if (member is System.Reflection.PropertyInfo p)
            {
                var getter = p.GetGetMethod(true);
                if (getter != null) return instance => getter.Invoke(instance, null);
            }
            return null;
        }

        private static Color ParseTrackColor(string hex) { if (!string.IsNullOrWhiteSpace(hex) && ColorUtility.TryParseHtmlString(hex, out var parsed)) return parsed; return new Color(0.4f, 0.75f, 0.95f); }
        private static Color IntensifyOutlineColor(Color source) { Color.RGBToHSV(source, out var h, out var s, out var v); v = Mathf.Clamp01(v * 1.15f + 0.1f); s = Mathf.Clamp01(s * 0.95f + 0.05f); var intensified = Color.HSVToRGB(h, s, v); intensified.a = Mathf.Clamp01(source.a + 0.25f); return intensified; }

        private static bool PrepareSceneTimelineData_Internal(ActiveActionData a)
        {
            _sceneTimelineDataReady = false;
            _sceneTimelineRowCount = 0;
            var bridgePlayer = ActiveActionPreviewBridge.GetPlayer(a);
            if (a.Animation == null || bridgePlayer == null) { ClearSceneTimelinePalette(); ReleaseActiveSceneTrackRows(); return false; }
            float fps = ResolvePreviewFPS(a, 60f);
            if (fps <= 0f) { ClearSceneTimelinePalette(); ReleaseActiveSceneTrackRows(); return false; }
            int totalFrames = Mathf.Max(1, Mathf.RoundToInt(a.Animation.length * fps));
            int maxFrameIndex = totalFrames - 1;
            if (maxFrameIndex < 0) { ClearSceneTimelinePalette(); ReleaseActiveSceneTrackRows(); return false; }
            var descriptors = GetTimelineTrackDescriptors(a.GetType());
            if (descriptors.Length == 0) { ClearSceneTimelinePalette(); ReleaseActiveSceneTrackRows(); return false; }
            ReleaseActiveSceneTrackRows();
            int rowCount = BuildSceneTrackRows(descriptors, maxFrameIndex);
            _sceneTimelineRowCount = rowCount;
            int currentFrame = Mathf.Clamp(GetAnimationCurrentFrame(a), 0, maxFrameIndex);
            _sceneTimelineCurrentFps = fps;
            _sceneTimelineCurrentFrame = currentFrame;
            _sceneTimelineMaxFrameIndex = maxFrameIndex;
            _sceneTimelineCurrentTime = fps > 0f ? currentFrame / fps : 0f;
            _sceneTimelinePaletteEntries.Clear(); _sceneTimelinePaletteIndexLookup.Clear();
            for (int i = 0; i < _activeSceneTrackRows.Count; i++)
            {
                var row = _activeSceneTrackRows[i];
                int paletteIndex = ResolveSceneTimelinePaletteIndex(row.Label);
                bool isActive = IsSceneTrackRowActive(row, currentFrame);
                var pe = new SceneTimelinePaletteEntry(paletteIndex, row.Label, row.Color, row.OutlineColor, isActive);
                StoreSceneTimelinePaletteEntry(pe);
            }
            if (_sceneTimelinePaletteEntries.Count > 0)
            {
                NotifySceneTimelinePalette(_sceneTimelinePaletteEntries);
                _sceneTimelineDataReady = true;
            }
            else
            {
                if (true) ClearSceneTimelinePalette();
                _sceneTimelineDataReady = rowCount > 0;
            }
            return _sceneTimelineDataReady;
        }

        private static void ReleaseActiveSceneTrackRows()
        {
            for (int i = 0; i < _activeSceneTrackRows.Count; i++)
            {
                var row = _activeSceneTrackRows[i];
                row.IsAvailable = true;
                row.Clear();
            }

            _activeSceneTrackRows.Clear();
        }

        private static int BuildSceneTrackRows(TimelineTrackDescriptor[] descriptors, int maxFrameIndex)
        {
            _activeSceneTrackRows.Clear();
            for (int i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                var value = descriptor.Getter?.Invoke(null);
                if (!TryPrepareTrackRow(value, maxFrameIndex, out var row)) continue;
                row.Label = descriptor.Label; row.Color = descriptor.Color; row.OutlineColor = descriptor.OutlineColor; _activeSceneTrackRows.Add(row);
            }
            return _activeSceneTrackRows.Count;
        }

        private static bool TryPrepareTrackRow(object value, int maxFrameIndex, out SceneTrackRow row)
        {
            row = null;
            if (!AcquireSceneTrackRow(out var candidate)) return false;
            candidate.Clear(); bool hasData = false;
            switch (value)
            {
                case int marker when marker >= 0: candidate.Markers.Add(Mathf.Clamp(marker, 0, maxFrameIndex)); hasData = true; break;
                case int[] array when array.Length >= 2:
                    for (int i = 0; i + 1 < array.Length; i += 2)
                    {
                        int start = array[i]; int end = array[i + 1]; if (start < 0 && end < 0) continue; if (start < 0) start = 0; if (end < 0) end = maxFrameIndex; start = Mathf.Clamp(start, 0, maxFrameIndex); end = Mathf.Clamp(end, 0, maxFrameIndex); if (end < start) { int swap = start; start = end; end = swap; } candidate.Segments.Add(new TrackSegment { Start = start, End = end }); hasData = true;
                    }
                    break;
                case IActionAffectWindow window when window != null:
                    int winStart = window.IntraActionStartFrame; int winEnd = window.IntraActionEndFrame; if (winStart < 0 && winEnd < 0) break; if (winStart < 0) winStart = 0; if (winEnd < 0) winEnd = maxFrameIndex; winStart = Mathf.Clamp(winStart, 0, maxFrameIndex); winEnd = Mathf.Clamp(winEnd, 0, maxFrameIndex); if (winEnd >= winStart) { candidate.Segments.Add(new TrackSegment { Start = winStart, End = winEnd }); hasData = true; } break;
            }
            if (!hasData) { candidate.IsAvailable = true; candidate.Clear(); return false; }
            row = candidate; return true;
        }

        private static bool AcquireSceneTrackRow(out SceneTrackRow row)
        {
            for (int i = 0; i < _sceneTrackRows.Count; i++) if (_sceneTrackRows[i].IsAvailable) { row = _sceneTrackRows[i]; row.IsAvailable = false; row.Clear(); return true; }
            var created = new SceneTrackRow { IsAvailable = false }; _sceneTrackRows.Add(created); row = created; row.Clear(); return true;
        }

        private static int ResolveSceneTimelinePaletteIndex(string label)
        {
            if (string.IsNullOrEmpty(label)) return -1;
            if (!_sceneTimelinePaletteIndexLookup.TryGetValue(label, out var index)) { index = _sceneTimelinePaletteIndexLookup.Count; _sceneTimelinePaletteIndexLookup[label] = index; }
            return index;
        }

        private static void StoreSceneTimelinePaletteEntry(SceneTimelinePaletteEntry entry)
        {
            int targetIndex = entry.Index; if (targetIndex < 0) return; while (_sceneTimelinePaletteEntries.Count <= targetIndex) _sceneTimelinePaletteEntries.Add(default); _sceneTimelinePaletteEntries[targetIndex] = entry;
        }

        private static bool IsSceneTrackRowActive(SceneTrackRow row, int currentFrame)
        {
            if (row == null) return false; for (int i = 0; i < row.Segments.Count; i++) { var seg = row.Segments[i]; if (currentFrame >= seg.Start && currentFrame <= seg.End) return true; } for (int i = 0; i < row.Markers.Count; i++) if (row.Markers[i] == currentFrame) return true; return false;
        }

        private static float ResolvePreviewFPS(ActiveActionData a, float fallback)
        {
            var state = GetState(a);
            if (state != null)
            {
                float v = state.PreviewFPS > 0 ? state.PreviewFPS : fallback;
                return Mathf.Max(1f, v);
            }
            if (a.Animation != null && a.Animation.frameRate > 0f) return Mathf.Max(1f, a.Animation.frameRate);
            return Mathf.Max(1f, fallback);
        }
    }
}
#endif
