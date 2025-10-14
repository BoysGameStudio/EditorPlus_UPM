#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;

using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;

using UnityEditor;

using UnityEngine;
using EditorPlus.SceneTimeline;

// ========================= Drawer Core =========================

/// <summary>
/// Field-level drawer that renders: 1) the AnimationClip field itself; 2) a timeline; 3) tracks discovered via [TimelineTrack].
/// </summary>
public sealed class ShowActionTimelineDrawer : OdinAttributeDrawer<ShowActionTimelineAttribute, AnimationClip>
{
    private const float TimelineLabelWidth = 180f;
    private const float TrackRowHeight = 24f;
    private const float MarkerWidth = 5f;

    // Per-parent target state (so multiple objects each have their own zoom/cursor, etc.)
    private static readonly Dictionary<UnityEngine.Object, TimelineState> StateByTarget = new();
    private static readonly Dictionary<int, WindowDragState> WindowBodyDragStates = new();
    private static readonly Dictionary<Type, TrackMember[]> TrackMembersCache = new();

    // Decoupled: we no longer reference ActiveActionData at compile time.
    private static readonly object[] SeekPreviewArgs = new object[1];
    private static readonly int TimelineSeekControlHint = "ShowActionTimelineSeekControl".GetHashCode();


    protected override void DrawPropertyLayout(GUIContent label)
    {
        // 1) Draw the clip field as usual
        this.CallNextDrawer(label);

        var clip = this.ValueEntry.SmartValue;
        if (clip == null)
        {
            SirenixEditorGUI.WarningMessageBox("No AnimationClip assigned.");
            return;
        }

        var parentTarget = ResolveOwningUnityObject(); // the object that owns the field (ScriptableObject/MonoBehaviour)
        if (parentTarget == null)
        {
            SirenixEditorGUI.ErrorMessageBox("Timeline needs a Unity object parent (ScriptableObject/MonoBehaviour).");
            return;
        }

        float? minHeightOverride = Attribute != null && Attribute.Height > 0f ? Attribute.Height : (float?)null;
        DrawTimeline(parentTarget, clip, minHeightOverride, includeFrameEventsInspector: true, addTopSpacing: true);
    }

    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterRuntimeAPI()
    {
        TimelineAPI.DrawTimelineHook = DrawTimeline;
    }
    public static void DrawTimeline(UnityEngine.Object parentTarget, AnimationClip clip, float? minHeightOverride = null, bool includeFrameEventsInspector = true, bool addTopSpacing = true)
    {
        if (clip == null || parentTarget == null)
        {
            return;
        }

        // Compute a robust end time from curves and events to avoid off-by-one due to floating errors
        var length = GetEffectiveClipEndTime(clip);
        var fps = ResolveClipFPS(clip);
        fps = ActiveActionIntegration.ResolvePreviewFPS(parentTarget, fps);
        var totalFrames = ComputeTotalFrames(length, fps);

        if (!StateByTarget.TryGetValue(parentTarget, out var state))
        {
            state = new TimelineState();
            StateByTarget[parentTarget] = state;
        }
        state.Ensure(totalFrames);

        if (addTopSpacing)
        {
            GUILayout.Space(4f);
        }

        GUILayout.BeginVertical(SirenixGUIStyles.BoxContainer);
        {
            DrawToolbar(parentTarget, state, clip, fps, totalFrames, length);

            var contentHeight = ComputeTimelineContentHeight(parentTarget);
            float baseMinHeight = minHeightOverride.HasValue ? Mathf.Max(0f, minHeightOverride.Value) : 80f;
            var areaH = Mathf.Max(baseMinHeight, contentHeight);
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(areaH));
            const float rulerH = 20f;
            var rulerRect = new Rect(rect.x, rect.y, rect.width, rulerH);
            var tracksRect = new Rect(rect.x, rect.y + rulerH, rect.width, rect.height - rulerH);

            DrawRuler(rulerRect, state, fps, totalFrames);
            DrawCursorLine(parentTarget, tracksRect, state, totalFrames);
            state.VisibleRect = tracksRect;

            DrawTracks(parentTarget, tracksRect, state, fps, totalFrames);
            HandleZoomAndClick(parentTarget, rect, rulerRect, tracksRect, state, totalFrames);
        }
        GUILayout.EndVertical();

        _ = includeFrameEventsInspector; // retained for signature compatibility
    }

    // Derive the effective end time using curves and events to match the true authored range
    private static float GetEffectiveClipEndTime(AnimationClip clip)
    {
        if (clip == null) return 0f;
        float end = Mathf.Max(0f, clip.length);
#if UNITY_EDITOR
        try
        {
            // Float curves
            var floatBindings = AnimationUtility.GetCurveBindings(clip);
            if (floatBindings != null)
            {
                for (int i = 0; i < floatBindings.Length; i++)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, floatBindings[i]);
                    if (curve == null || curve.length == 0) continue;
                    float t = curve.keys[curve.length - 1].time;
                    if (t > end) end = t;
                }
            }

            // Object reference curves
            var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (objBindings != null)
            {
                for (int i = 0; i < objBindings.Length; i++)
                {
                    var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, objBindings[i]);
                    if (keyframes == null || keyframes.Length == 0) continue;
                    float t = keyframes[keyframes.Length - 1].time;
                    if (t > end) end = t;
                }
            }

            // Events
            var events = clip.events;
            if (events != null && events.Length > 0)
            {
                float lastEvent = events[events.Length - 1].time;
                if (lastEvent > end) end = lastEvent;
            }
        }
        catch { }
#endif
        return end;
    }

    private static int ComputeTotalFrames(float endSeconds, float fps)
    {
        if (fps <= 0f || endSeconds <= 0f) return 0;
        // Use floor with a tiny epsilon to avoid spilling into the next frame due to float imprecision
        int lastIndex = Mathf.FloorToInt(endSeconds * fps + 1e-4f);
        return Mathf.Max(1, lastIndex + 1);
    }


    private UnityEngine.Object ResolveOwningUnityObject()
    {
        var clipValue = this.ValueEntry?.WeakSmartValue as UnityEngine.Object;

        // Collect candidate owners from the property chain and from the serialization root
        List<UnityEngine.Object> candidates = new List<UnityEngine.Object>(4);

        var prop = this.Property?.Parent;
        while (prop != null)
        {
            var entry = prop.ValueEntry;
            if (entry != null && entry.WeakSmartValue is UnityEngine.Object unityObj && unityObj != clipValue)
            {
                candidates.Add(unityObj);
            }
            prop = prop.Parent;
        }

        var root = this.Property?.SerializationRoot;
        var rootEntry = root?.ValueEntry;
        if (rootEntry != null)
        {
            foreach (var value in rootEntry.WeakValues)
            {
                if (value is UnityEngine.Object unityObj && unityObj != clipValue)
                {
                    candidates.Add(unityObj);
                }
            }
        }

        // Prefer a candidate that actually supports preview/seek
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (ActiveActionIntegration.HasPreview(c) || ActiveActionIntegration.CanSeekPreview(c))
            {
                return c;
            }
        }

        // Fallback to the first collected candidate
        if (candidates.Count > 0)
        {
            return candidates[0];
        }

        return null;
    }

    // ---------------- FPS helper ----------------
    private static float ResolveClipFPS(AnimationClip clip)
    {
#if UNITY_2021_2_OR_NEWER
        // Use frameRate; fallback to 60 if not set
        var est = (clip.frameRate > 0f) ? clip.frameRate : 60f;
        return Mathf.Clamp(est, 10f, 120f);
#else
        return 60f;
#endif
    }

    // ---------------- Toolbar ----------------
    private static void DrawToolbar(UnityEngine.Object parentTarget, TimelineState st, AnimationClip clip, float fps, int frames, float length)
    {
        using (new GUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label($"Clip: {clip.name}", GUILayout.Width(220));
            GUILayout.Label($"Len: {length:0.###}s", GUILayout.Width(90));
            GUILayout.Label($"FPS: {fps:0.##}", GUILayout.Width(80));
            GUILayout.FlexibleSpace();

            string frameInfo = "Frame: -";
            bool hasPreview = ActiveActionIntegration.HasPreview(parentTarget);
            if (frames > 0 && (hasPreview || st.IsSeeking))
            {
                // While scrubbing, prefer the local cursor for immediate feedback
                int playbackFrame = st.IsSeeking
                    ? st.CursorFrame
                    : (ActiveActionIntegration.TryGetPreviewFrame(parentTarget, out var previewFrame) && previewFrame >= 0 ? previewFrame : st.CursorFrame);
                int lastFrameIndex = Mathf.Max(0, frames - 1);
                playbackFrame = Mathf.Clamp(playbackFrame, 0, lastFrameIndex);
                float seconds = fps > 0f ? playbackFrame / Mathf.Max(1f, fps) : 0f;
                frameInfo = lastFrameIndex > 0
                    ? $"Frame: {playbackFrame} / {lastFrameIndex} ({seconds:0.###}s)"
                    : $"Frame: {playbackFrame} ({seconds:0.###}s)";
            }
            GUILayout.Label(frameInfo, GUILayout.Width(200));

            GUILayout.Label("Zoom", GUILayout.Width(40));
            st.Zoom = GUILayout.HorizontalSlider(st.Zoom, 0.25f, 6f, GUILayout.Width(120));
            st.Zoom = Mathf.Clamp(st.Zoom, 0.25f, 6f);

            float typedZoom = EditorGUILayout.DelayedFloatField(st.Zoom, GUILayout.Width(60));
            if (!Mathf.Approximately(typedZoom, st.Zoom))
            {
                st.Zoom = Mathf.Clamp(Mathf.Max(typedZoom, 0.001f), 0.25f, 6f);
                GUIHelper.RequestRepaint();
            }
        }
    }

    // ---------------- Ruler & Cursor ----------------
    private static void DrawRuler(Rect rect, TimelineState st, float fps, int totalFrames)
    {
        EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.15f));

        float rulerStartX = rect.x + TimelineLabelWidth; // Align with track content area
        float rulerWidth = rect.width - TimelineLabelWidth;

        float ppf = st.PixelsPerFrame;
        int step = 1;
        if (ppf < 3) step = 5;
        if (ppf < 1) step = 10;
        if (ppf < 0.5f) step = 20;
        if (ppf < 0.25f) step = 50;

        int start = Mathf.Max(0, st.PixelToFrame(0, totalFrames));
        int end = Mathf.Min(totalFrames, st.PixelToFrame(rulerWidth, totalFrames) + 1);

        Handles.BeginGUI();
        for (int f = AlignTo(start, step); f <= end; f += step)
        {
            float x = rulerStartX + st.FrameToPixelX(f);
            float h = (f % (step * 5) == 0) ? rect.height : rect.height * 0.6f;
            Handles.color = new Color(1, 1, 1, 0.2f);
            Handles.DrawLine(new Vector3(x, rect.yMax, 0), new Vector3(x, rect.yMax - h, 0));
            if (f % (step * 5) == 0)
            {
                var label = f.ToString();
                var size = EditorStyles.miniLabel.CalcSize(new GUIContent(label));
                GUI.Label(new Rect(x + 2, rect.yMax - size.y - 1, size.x, size.y), label, EditorStyles.miniLabel);
            }
        }
        Handles.EndGUI();
    }

    private static void DrawCursorLine(UnityEngine.Object parentTarget, Rect tracksRect, TimelineState st, int totalFrames)
    {
        if (totalFrames <= 0) return;
        bool hasPreview = ActiveActionIntegration.HasPreview(parentTarget);
        // If preview isn't active, only draw while scrubbing so the user gets feedback
        if (!hasPreview && !st.IsSeeking) return;

        // Always draw a cursor: prefer live preview when available and not scrubbing
        int frame = st.CursorFrame;
        int previewFrame;
        if (!st.IsSeeking && hasPreview && ActiveActionIntegration.TryGetPreviewFrame(parentTarget, out previewFrame))
        {
            frame = previewFrame;
        }

        frame = Mathf.Clamp(frame, 0, Mathf.Max(0, totalFrames - 1));

        float contentWidth = tracksRect.width - TimelineLabelWidth;
        if (contentWidth <= 0f)
        {
            return;
        }

        float x = tracksRect.x + TimelineLabelWidth + st.FrameToPixelX(frame);
        var lineRect = new Rect(x - 0.5f, tracksRect.y, 1.5f, tracksRect.height);
        EditorGUI.DrawRect(lineRect, new Color(1f, 0.85f, 0.2f, 0.9f));
    }

    private static void HandleZoomAndClick(UnityEngine.Object parentTarget, Rect fullRect, Rect rulerRect, Rect tracksRect, TimelineState st, int totalFrames)
    {
        var e = Event.current;
        if (e == null || e.type == EventType.Used)
        {
            return;
        }

        if (fullRect.Contains(e.mousePosition) && e.type == EventType.ScrollWheel)
        {
            float delta = -e.delta.y * 0.05f;
            st.Zoom = Mathf.Clamp(st.Zoom * (1f + delta), 0.25f, 6f);

            float contentWidth = Mathf.Max(1f, st.WidthInPixels(totalFrames));
            float mx = e.mousePosition.x - (tracksRect.x + TimelineLabelWidth);
            float norm = (mx + st.HScroll) / contentWidth;
            float visibleWidth = Mathf.Max(0f, tracksRect.width - TimelineLabelWidth);
            float maxScroll = Mathf.Max(0f, contentWidth - visibleWidth);
            st.HScroll = Mathf.Clamp(norm * contentWidth - mx, 0f, maxScroll);
            e.Use();
            GUIHelper.RequestRepaint();
        }

        // Use a control rect covering both ruler and tracks content horizontally (excluding left labels)
        float timelineStart = tracksRect.x + TimelineLabelWidth;
        float timelineEnd = tracksRect.xMax;
        var controlRect = new Rect(timelineStart, rulerRect.yMin, Mathf.Max(0f, timelineEnd - timelineStart), rulerRect.height + tracksRect.height);
        int controlId = GUIUtility.GetControlID(TimelineSeekControlHint, FocusType.Passive, controlRect);
        EventType typeForControl = e.GetTypeForControl(controlId);
        
        bool IsInTimelineContent(Vector2 mp)
        {
            if (mp.x < timelineStart || mp.x > timelineEnd) return false;
            return rulerRect.Contains(mp) || tracksRect.Contains(mp);
        }

        switch (typeForControl)
        {
            case EventType.MouseDown:
                if (e.button == 0 && GUIUtility.hotControl == 0 && IsInTimelineContent(e.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    GUIUtility.keyboardControl = controlId;
                    // Try to ensure preview infra exists so scrubbing can drive the live preview
                    if (ActiveActionIntegration.TryEnsurePreviewInfrastructure(parentTarget, resetTime: false))
                    {
                        // Bind the current clip if needed so seek has immediate effect
                        ActiveActionIntegration.TrySyncPlayerClip(parentTarget, resetTime: false);
                    }
                    // Capture play-state and pause once at drag start so scrubbing won't fight the player
                    st.WasPlayingBeforeSeek = ActiveActionIntegration.IsPreviewPlaying(parentTarget);
                    ActiveActionIntegration.PausePreviewIfPlaying(parentTarget);
                    st.IsSeeking = true;
                    SeekTimelineToMouse(parentTarget, st, tracksRect, totalFrames, e.mousePosition.x);
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlId)
                {
                    float clampedX = Mathf.Clamp(e.mousePosition.x, timelineStart, timelineEnd);
                    SeekTimelineToMouse(parentTarget, st, tracksRect, totalFrames, clampedX);
                    e.Use();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId)
                {
                    float clampedX = Mathf.Clamp(e.mousePosition.x, timelineStart, timelineEnd);
                    SeekTimelineToMouse(parentTarget, st, tracksRect, totalFrames, clampedX);
                    GUIUtility.hotControl = 0;
                    GUIUtility.keyboardControl = 0;
                    st.IsSeeking = false;
                    // Restore play-state if we were playing before scrubbing
                    if (st.WasPlayingBeforeSeek)
                    {
                        ActiveActionIntegration.SetPlaying(parentTarget, true);
                    }
                    st.WasPlayingBeforeSeek = false;
                    e.Use();
                }
                break;
        }
    }

    private static int AlignTo(int v, int step) => (v % step == 0) ? v : (v + (step - (v % step)));

    // ---------------- Tracks ----------------
    private struct TrackMember
    {
        public MemberInfo Member;
        public string Label;
        public Type ValueType;
        public Color Color;
        public Func<UnityEngine.Object, object> Getter;
        public Action<UnityEngine.Object, object> Setter;
        public int Order;
    }

    private readonly struct WindowBinding
    {
        public WindowBinding(int start, int end, Color color, string label, Func<UnityEngine.Object, int, int, bool> apply, int? rawStart = null, int? rawEnd = null)
        {
            if (apply == null) throw new ArgumentNullException(nameof(apply));

            if (end < start)
            {
                (start, end) = (end, start);
            }

            StartFrame = start;
            EndFrame = end;
            Color = color;
            Label = label ?? string.Empty;
            Apply = apply;
            RawStart = rawStart ?? start;
            RawEnd = rawEnd ?? end;
        }

        public int StartFrame { get; }
        public int EndFrame { get; }
        public int RawStart { get; }
        public int RawEnd { get; }
        public Color Color { get; }
        public string Label { get; }
        public Func<UnityEngine.Object, int, int, bool> Apply { get; }
    }

    private static void DrawTracks(UnityEngine.Object parentTarget, Rect tracksRect, TimelineState st, float fps, int totalFrames)
    {
        var members = GetTrackMembers(parentTarget);
        float rowH = TrackRowHeight;

        float currentY = tracksRect.y;

        for (int i = 0; i < members.Length; i++)
        {
            var tm = members[i];
            var row = new Rect(tracksRect.x, currentY, tracksRect.width, rowH);
            currentY += rowH;

            EditorGUI.DrawRect(row, new Color(0, 0, 0, 0.05f));

            var labelRect = new Rect(row.x + 6, row.y, TimelineLabelWidth - 6, row.height);
            GUI.Label(labelRect, tm.Label, SirenixGUIStyles.Label);

            var content = new Rect(tracksRect.x + TimelineLabelWidth, row.y + 4, tracksRect.width - TimelineLabelWidth - 8, row.height - 8);
            DrawSingleTrack(parentTarget, tm, content, st, totalFrames);
        }
    }

    private static float ComputeTimelineContentHeight(UnityEngine.Object parentTarget)
    {
        float tracksHeight = GetTrackMembers(parentTarget).Length * TrackRowHeight;

        const float rulerHeight = 20f;

        return rulerHeight + tracksHeight;
    }

    private static Rect BuildMarkerRect(Rect rect, TimelineState st, int frame, float width)
    {
        float px = rect.x + st.FrameToPixelX(frame);
        return new Rect(px - width * 0.5f, rect.y + 3f, width, rect.height - 6f);
    }

    private static Rect ExpandRect(Rect rect, float amount)
    {
        rect.xMin -= amount;
        rect.xMax += amount;
        rect.yMin -= amount;
        rect.yMax += amount;
        return rect;
    }

    private static TrackMember[] GetTrackMembers(UnityEngine.Object target)
    {
        if (target == null)
        {
            return Array.Empty<TrackMember>();
        }

        var type = target.GetType();
        if (!TrackMembersCache.TryGetValue(type, out var cached))
        {
            cached = BuildTrackMembersForType(type);
            TrackMembersCache[type] = cached;
        }

        return cached;
    }

    private static TrackMember[] BuildTrackMembersForType(Type type)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var members = type.GetMembers(flags);
        var entries = new List<(TrackMember Track, int DeclarationIndex)>(members.Length);
        int declarationIndex = 0;

        foreach (var member in members)
        {
            var attribute = member.GetCustomAttribute<TimelineTrackAttribute>();
            if (attribute == null)
            {
                continue;
            }

            Type valueType;
            Func<UnityEngine.Object, object> getter = null;
            Action<UnityEngine.Object, object> setter = null;

            if (member is FieldInfo field)
            {
                valueType = field.FieldType;
                getter = owner => field.GetValue(owner);
                if (!field.IsInitOnly)
                {
                    setter = (owner, value) => field.SetValue(owner, value);
                }
            }
            else if (member is PropertyInfo property)
            {
                valueType = property.PropertyType;
                if (property.CanRead)
                {
                    getter = owner => property.GetValue(owner, null);
                }
                if (property.CanWrite)
                {
                    setter = (owner, value) => property.SetValue(owner, value, null);
                }
            }
            else
            {
                continue;
            }

            if (getter == null)
            {
                continue;
            }

            var label = string.IsNullOrEmpty(attribute.Label) ? member.Name : attribute.Label;
            var color = ParseHexOrDefault(attribute.ColorHex, DefaultColorFor(valueType));

            var trackMember = new TrackMember
            {
                Member = member,
                Label = label,
                ValueType = valueType,
                Color = color,
                Getter = getter,
                Setter = setter,
                Order = attribute.Order
            };

            entries.Add((trackMember, declarationIndex));
            declarationIndex++;
        }

        if (entries.Count == 0)
        {
            return Array.Empty<TrackMember>();
        }

        entries.Sort((a, b) =>
        {
            int orderComparison = a.Track.Order.CompareTo(b.Track.Order);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            return a.DeclarationIndex.CompareTo(b.DeclarationIndex);
        });

        var result = new TrackMember[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            result[i] = entries[i].Track;
        }

        return result;
    }

    private static Color DefaultColorFor(Type type)
    {
        if (type == typeof(int)) return new Color(0.98f, 0.62f, 0.23f);
        if (type == typeof(int[])) return new Color(0.39f, 0.75f, 0.96f);
        if (HasAffectWindowPattern(type)) return new Color(0.5f, 0.9f, 0.5f);
        return new Color(0.8f, 0.8f, 0.8f);
    }

    private static Color ParseHexOrDefault(string hex, Color def)
    {
        if (string.IsNullOrWhiteSpace(hex)) return def;
        if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
        return def;
    }

    private static void DrawSingleTrack(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int totalFrames)
    {
        EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.04f));

        if (tm.ValueType == typeof(int))
        {
            int val = (int)(tm.Getter?.Invoke(target) ?? 0);
            DrawSingleMarker(target, tm, rect, st, val, tm.Color, MarkerWidth, ComputeControlSeed(target, tm), totalFrames, out _, out bool context, out int draggedFrame);

            // Handle dragging to change value
            if (draggedFrame != val && tm.Setter != null)
            {
                tm.Setter(target, draggedFrame);
                EditorUtility.SetDirty(target);
            }

            if (context)
            {
                ShowReadOnlyContextMenu();
            }
        }
        else if (tm.ValueType == typeof(int[]))
        {
            var arr = (int[])(tm.Getter?.Invoke(target) ?? Array.Empty<int>());
            if (arr == null) arr = Array.Empty<int>();

            var controlSeed = ComputeControlSeed(target, tm);

            if (arr.Length == 2)
            {
                var binding = CreateArrayWindowBinding(target, tm, arr, totalFrames);
                DrawWindowBinding(target, tm, rect, st, totalFrames, controlSeed, binding);
            }
            else
            {
                DrawMarkers(target, tm, rect, st, arr, tm.Color, MarkerWidth, controlSeed, totalFrames, out _, out bool context, out int draggedIndex, out int draggedFrame);

                // Handle dragging to change array element value
                if (draggedIndex >= 0 && draggedIndex < arr.Length && draggedFrame != arr[draggedIndex] && tm.Setter != null)
                {
                    var newArr = (int[])arr.Clone();
                    newArr[draggedIndex] = draggedFrame;
                    tm.Setter(target, newArr);
                    EditorUtility.SetDirty(target);
                }

                if (context)
                {
                    ShowReadOnlyContextMenu();
                }
            }
        }
        else if (HasAffectWindowPattern(tm.ValueType))
        {
            var windowInstance = tm.Getter?.Invoke(target);
            if (windowInstance != null)
            {
                var binding = CreateAffectWindowBinding(target, tm, windowInstance, totalFrames);
                DrawWindowBinding(target, tm, rect, st, totalFrames, ComputeControlSeed(target, tm), binding);

                var evt = Event.current;
                if (evt.type == EventType.ContextClick && rect.Contains(evt.mousePosition))
                {
                    ShowReadOnlyContextMenu();
                    evt.Use();
                }
            }
            else
            {
                EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.02f));
                var c = GUI.color; GUI.color = new Color(1, 1, 1, 0.5f);
                GUI.Label(rect, "〈No Window Data〉", SirenixGUIStyles.MiniLabelCentered);
                GUI.color = c;
            }
        }
    }

    // ---------------- Drawing helpers ----------------
    private static void DrawSingleMarker(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int frame, Color color, float width, int controlSeed, int totalFrames, out bool clicked, out bool context, out int draggedFrame)
    {
        clicked = false; context = false; draggedFrame = frame;
        float px = rect.x + st.FrameToPixelX(frame);
        var r = new Rect(px - width * 0.5f, rect.y + 2, width, rect.height - 4);
        int controlId = GUIUtility.GetControlID(controlSeed, FocusType.Passive, r);
        var e = Event.current;
        var type = e.GetTypeForControl(controlId);

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(r, color);
        }

        EditorGUIUtility.AddCursorRect(r, MouseCursor.SlideArrow);

        switch (type)
        {
            case EventType.MouseDown:
                if (e.button == 0 && r.Contains(e.mousePosition))
                {
                    if (target != null)
                    {
                        Undo.RecordObject(target, $"Move {tm.Label} Marker");
                    }
                    clicked = true;
                    GUIUtility.hotControl = controlId;
                    GUIUtility.keyboardControl = controlId;
                    e.Use();
                }
                else if (e.button == 1 && r.Contains(e.mousePosition))
                {
                    context = true;
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlId && e.button == 0)
                {
                    int newFrame = st.PixelToFrame(e.mousePosition.x - rect.x, totalFrames);
                    draggedFrame = Mathf.Clamp(newFrame, 0, totalFrames);
                    GUI.changed = true;
                    e.Use();
                    GUIHelper.RequestRepaint();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId && e.button == 0)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
                break;

            case EventType.ContextClick:
                if (r.Contains(e.mousePosition))
                {
                    context = true;
                    e.Use();
                }
                break;
        }
    }

    /// <summary>clickedIndex: >=0 hit index; -1 none; -2 clicked empty.</summary>
    private static void DrawMarkers(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int[] frames, Color color, float width, int controlSeedBase, int totalFrames, out int clickedIndex, out bool context, out int draggedIndex, out int draggedFrame)
    {
        clickedIndex = -1; context = false; draggedIndex = -1; draggedFrame = -1;

        for (int i = 0; i < frames.Length; i++)
        {
            float px = rect.x + st.FrameToPixelX(frames[i]);
            var r = new Rect(px - width * 0.5f, rect.y + 3, width, rect.height - 6);
            int controlId = GUIUtility.GetControlID(CombineControlSeed(controlSeedBase, i), FocusType.Passive, r);
            var e = Event.current;
            var type = e.GetTypeForControl(controlId);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, color);
            }

            EditorGUIUtility.AddCursorRect(r, MouseCursor.SlideArrow);

            switch (type)
            {
                case EventType.MouseDown:
                    if (r.Contains(e.mousePosition))
                    {
                        if (e.button == 0)
                        {
                            if (target != null)
                            {
                                Undo.RecordObject(target, $"Move {tm.Label} Marker");
                            }
                            clickedIndex = i;
                            GUIUtility.hotControl = controlId;
                            GUIUtility.keyboardControl = controlId;
                            e.Use();
                        }
                        else if (e.button == 1)
                        {
                            clickedIndex = i;
                            context = true;
                            e.Use();
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId && e.button == 0)
                    {
                        int newFrame = st.PixelToFrame(e.mousePosition.x - rect.x, totalFrames);
                        draggedIndex = i;
                        draggedFrame = Mathf.Clamp(newFrame, 0, totalFrames);
                        GUI.changed = true;
                        e.Use();
                        GUIHelper.RequestRepaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId && e.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;

                case EventType.ContextClick:
                    if (r.Contains(e.mousePosition))
                    {
                        clickedIndex = i;
                        context = true;
                        e.Use();
                    }
                    break;
            }
        }

        var ev = Event.current;
        if (rect.Contains(ev.mousePosition) && GUIUtility.hotControl == 0)
        {
            switch (ev.type)
            {
                case EventType.MouseDown when ev.button == 0:
                    clickedIndex = -2;
                    ev.Use();
                    break;

                case EventType.ContextClick:
                    context = true;
                    ev.Use();
                    break;
            }
        }
    }

    private static void DrawWindow(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int startFrame, int endFrame, Color color, string label, int controlSeedBase, int totalFrames, out int newStartFrame, out int newEndFrame, out bool dragged)
    {
        newStartFrame = startFrame;
        newEndFrame = endFrame;
        dragged = false;

        float x1 = rect.x + st.FrameToPixelX(startFrame);
        float x2 = rect.x + st.FrameToPixelX(endFrame);
        var r = new Rect(Mathf.Min(x1, x2), rect.y + 4, Mathf.Abs(x2 - x1), rect.height - 8);
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(r, color * new Color(1, 1, 1, 0.65f));
        }

        // Drag handles for start and end
        var handleW = 8f;
        var startHandle = new Rect(r.x - handleW * 0.5f, r.y, handleW, r.height);
        var endHandle = new Rect(r.xMax - handleW * 0.5f, r.y, handleW, r.height);

        // Draw drag handles
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(startHandle, new Color(0, 0, 0, 0.3f));
            EditorGUI.DrawRect(endHandle, new Color(0, 0, 0, 0.3f));
        }

        var e = Event.current;
        int startControlId = GUIUtility.GetControlID(CombineControlSeed(controlSeedBase, 101), FocusType.Passive, startHandle);
        int endControlId = GUIUtility.GetControlID(CombineControlSeed(controlSeedBase, 202), FocusType.Passive, endHandle);
        var startType = e.GetTypeForControl(startControlId);
        var endType = e.GetTypeForControl(endControlId);

        EditorGUIUtility.AddCursorRect(startHandle, MouseCursor.SlideArrow);
        EditorGUIUtility.AddCursorRect(endHandle, MouseCursor.SlideArrow);

        // Handle start frame dragging
        switch (startType)
        {
            case EventType.MouseDown:
                if (e.button == 0 && startHandle.Contains(e.mousePosition))
                {
                    if (target != null)
                    {
                        Undo.RecordObject(target, $"Adjust {tm.Label} Window");
                    }
                    GUIUtility.hotControl = startControlId;
                    GUIUtility.keyboardControl = startControlId;
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == startControlId && e.button == 0)
                {
                    int newFrame = st.PixelToFrame(e.mousePosition.x - rect.x, totalFrames);
                    newStartFrame = Mathf.Clamp(newFrame, 0, Mathf.Max(0, endFrame - 1));
                    dragged = true;
                    GUI.changed = true;
                    e.Use();
                    GUIHelper.RequestRepaint();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == startControlId && e.button == 0)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
                break;
        }

        // Handle end frame dragging
        switch (endType)
        {
            case EventType.MouseDown:
                if (e.button == 0 && endHandle.Contains(e.mousePosition))
                {
                    if (target != null)
                    {
                        Undo.RecordObject(target, $"Adjust {tm.Label} Window");
                    }
                    GUIUtility.hotControl = endControlId;
                    GUIUtility.keyboardControl = endControlId;
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == endControlId && e.button == 0)
                {
                    int newFrame = st.PixelToFrame(e.mousePosition.x - rect.x, totalFrames);
                    newEndFrame = Mathf.Clamp(newFrame, Mathf.Min(newStartFrame + 1, totalFrames), totalFrames);
                    dragged = true;
                    GUI.changed = true;
                    e.Use();
                    GUIHelper.RequestRepaint();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == endControlId && e.button == 0)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
                break;
        }

        // Body drag (move entire window)
        int bodyControlId = GUIUtility.GetControlID(CombineControlSeed(controlSeedBase, 303), FocusType.Passive, r);
        var bodyType = e.GetTypeForControl(bodyControlId);

        switch (bodyType)
        {
            case EventType.MouseDown:
                if (e.button == 0 && r.Contains(e.mousePosition) && !startHandle.Contains(e.mousePosition) && !endHandle.Contains(e.mousePosition))
                {
                    if (target != null)
                    {
                        Undo.RecordObject(target, $"Move {tm.Label} Window");
                    }
                    WindowBodyDragStates[bodyControlId] = new WindowDragState
                    {
                        StartFrame = startFrame,
                        EndFrame = endFrame,
                        MouseDownX = e.mousePosition.x
                    };
                    GUIUtility.hotControl = bodyControlId;
                    GUIUtility.keyboardControl = bodyControlId;
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == bodyControlId && e.button == 0 && WindowBodyDragStates.TryGetValue(bodyControlId, out var dragState))
                {
                    float pixelDelta = e.mousePosition.x - dragState.MouseDownX;
                    int frameDelta = Mathf.RoundToInt(pixelDelta / Mathf.Max(1e-3f, st.PixelsPerFrame));
                    int length = dragState.EndFrame - dragState.StartFrame;
                    length = Mathf.Max(0, length);
                    int maxStart = Mathf.Max(0, totalFrames - length);
                    int shiftedStart = Mathf.Clamp(dragState.StartFrame + frameDelta, 0, maxStart);
                    int shiftedEnd = Mathf.Clamp(shiftedStart + length, shiftedStart, totalFrames);

                    newStartFrame = shiftedStart;
                    newEndFrame = shiftedEnd;
                    dragged = true;
                    GUI.changed = true;
                    e.Use();
                    GUIHelper.RequestRepaint();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == bodyControlId && e.button == 0)
                {
                    GUIUtility.hotControl = 0;
                    WindowBodyDragStates.Remove(bodyControlId);
                    e.Use();
                }
                break;

            case EventType.Ignore:
            case EventType.Layout:
            case EventType.Repaint:
                break;
        }

        if (bodyType == EventType.MouseUp && WindowBodyDragStates.ContainsKey(bodyControlId))
        {
            WindowBodyDragStates.Remove(bodyControlId);
        }

        // border
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), new Color(0, 0, 0, 0.5f));
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), new Color(0, 0, 0, 0.5f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), new Color(0, 0, 0, 0.5f));
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), new Color(0, 0, 0, 0.5f));
        }

        // label
        if (!string.IsNullOrEmpty(label))
        {
            var gc = new GUIContent(label);
            var size = SirenixGUIStyles.MiniLabelCentered.CalcSize(gc);
            var lc = new Rect(Mathf.Clamp(r.center.x - size.x / 2f, rect.x + 2, rect.xMax - size.x - 2), r.center.y - size.y / 2f, size.x, size.y);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.Label(lc, gc, SirenixGUIStyles.MiniLabelCentered);
            }
        }
    }

    private static int ComputeControlSeed(UnityEngine.Object target, TrackMember tm, int index = -1)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (target != null ? target.GetInstanceID() : 0);
            hash = hash * 31 + (tm.Member?.MetadataToken ?? 0);
            hash = hash * 31 + index;
            return hash;
        }
    }

    private static int CombineControlSeed(int seed, int index)
    {
        unchecked
        {
            return seed * 397 ^ index;
        }
    }

    private static void ShowReadOnlyContextMenu()
    {
        var menu = new GenericMenu();
        menu.AddDisabledItem(new GUIContent("Timeline editing is disabled"));
        menu.ShowAsContext();
    }

    private static bool ApplyAffectWindowChanges(UnityEngine.Object target, TrackMember tm, object windowInstance, int newStart, int newEnd)
    {
        if (windowInstance == null)
        {
            return false;
        }

        var type = windowInstance.GetType();
        bool updatedStart = TrySetIntMember(windowInstance, "IntraActionStartFrame", newStart);
        bool updatedEnd = TrySetIntMember(windowInstance, "IntraActionEndFrame", newEnd);
        bool anyUpdated = updatedStart || updatedEnd;

        if (anyUpdated)
        {
            if (type.IsValueType)
            {
                if (tm.Setter == null)
                {
                    return false;
                }

                tm.Setter(target, windowInstance);
            }
            return true;
        }

        if (tm.Setter != null)
        {
            tm.Setter(target, windowInstance);
            return true;
        }

        return false;
    }

    private static void DrawWindowBinding(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int totalFrames, int controlSeed, WindowBinding binding)
    {
        int start = Mathf.Clamp(binding.StartFrame, 0, totalFrames);
        int end = Mathf.Clamp(binding.EndFrame, start, totalFrames);

        DrawWindow(target, tm, rect, st, start, end, binding.Color, binding.Label, controlSeed, totalFrames, out int newStart, out int newEnd, out bool dragged);

        bool needsApply = dragged
            || binding.RawStart != binding.StartFrame
            || binding.RawEnd != binding.EndFrame
            || newStart != binding.StartFrame
            || newEnd != binding.EndFrame;

        if (needsApply && binding.Apply(target, newStart, newEnd))
        {
            EditorUtility.SetDirty(target);
        }
    }

    private static WindowBinding CreateArrayWindowBinding(UnityEngine.Object target, TrackMember tm, int[] frames, int totalFrames)
    {
        int rawStart = frames.Length > 0 ? frames[0] : 0;
        int rawEnd = frames.Length > 1 ? frames[1] : rawStart;
        int start = Mathf.Clamp(rawStart, 0, totalFrames);
        int end = Mathf.Clamp(rawEnd, 0, totalFrames);

        return new WindowBinding(
            start,
            end,
            tm.Color,
            string.Empty,
            (owner, newStart, newEnd) =>
            {
                if (tm.Setter == null || frames.Length < 2) return false;
                if (frames[0] == newStart && frames[1] == newEnd) return false;

                var newArr = (int[])frames.Clone();
                newArr[0] = newStart;
                newArr[1] = newEnd;
                tm.Setter(owner, newArr);
                return true;
            },
            rawStart,
            rawEnd);
    }

    private static WindowBinding CreateAffectWindowBinding(UnityEngine.Object target, TrackMember tm, object window, int totalFrames)
    {
        int rawStart = TryGetIntMember(window, "IntraActionStartFrame", 0);
        int rawEnd = TryGetIntMember(window, "IntraActionEndFrame", rawStart);
        int start = Mathf.Clamp(rawStart, 0, totalFrames);
        int end = Mathf.Clamp(rawEnd, 0, totalFrames);

        return new WindowBinding(
            start,
            end,
            tm.Color,
            string.Empty,
            (owner, newStart, newEnd) => ApplyAffectWindowChanges(owner, tm, window, newStart, newEnd));
    }

    private static bool TrySetIntMember(object instance, string memberName, int value)
    {
        if (instance == null) return false;

        var type = instance.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        var prop = type.GetProperty(memberName, flags, null, typeof(int), Type.EmptyTypes, null);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(instance, value);
            return true;
        }

        var field = type.GetField(memberName, flags);
        if (field != null && field.FieldType == typeof(int))
        {
            field.SetValue(instance, value);
            return true;
        }

        // Try backing field patterns (_member or m_member)
        string camel = char.ToLowerInvariant(memberName[0]) + memberName.Substring(1);
        field = type.GetField(camel, flags);
        if (field != null && field.FieldType == typeof(int))
        {
            field.SetValue(instance, value);
            return true;
        }

        field = type.GetField("_" + camel, flags);
        if (field != null && field.FieldType == typeof(int))
        {
            field.SetValue(instance, value);
            return true;
        }

        field = type.GetField("m_" + memberName, flags);
        if (field != null && field.FieldType == typeof(int))
        {
            field.SetValue(instance, value);
            return true;
        }

        return false;
    }

    private static int TryGetIntMember(object instance, string memberName, int fallback)
    {
        if (instance == null) return fallback;

        var type = instance.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        var prop = type.GetProperty(memberName, flags, null, typeof(int), Type.EmptyTypes, null);
        if (prop != null && prop.CanRead)
        {
            try { return (int)prop.GetValue(instance, null); } catch { }
        }

        var field = type.GetField(memberName, flags);
        if (field != null && field.FieldType == typeof(int))
        {
            try { return (int)field.GetValue(instance); } catch { }
        }

        string camel = char.ToLowerInvariant(memberName[0]) + memberName.Substring(1);
        field = type.GetField(camel, flags);
        if (field != null && field.FieldType == typeof(int))
        {
            try { return (int)field.GetValue(instance); } catch { }
        }

        field = type.GetField("_" + camel, flags);
        if (field != null && field.FieldType == typeof(int))
        {
            try { return (int)field.GetValue(instance); } catch { }
        }

        field = type.GetField("m_" + memberName, flags);
        if (field != null && field.FieldType == typeof(int))
        {
            try { return (int)field.GetValue(instance); } catch { }
        }

        return fallback;
    }

    private static bool HasAffectWindowPattern(Type t)
    {
        if (t == null) return false;
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        bool hasStart = t.GetProperty("IntraActionStartFrame", flags, null, typeof(int), Type.EmptyTypes, null) != null
                        || t.GetField("IntraActionStartFrame", flags) != null
                        || t.GetField("_intraActionStartFrame", flags) != null
                        || t.GetField("m_IntraActionStartFrame", flags) != null;
        bool hasEnd = t.GetProperty("IntraActionEndFrame", flags, null, typeof(int), Type.EmptyTypes, null) != null
                        || t.GetField("IntraActionEndFrame", flags) != null
                        || t.GetField("_intraActionEndFrame", flags) != null
                        || t.GetField("m_IntraActionEndFrame", flags) != null;
        return hasStart && hasEnd;
    }

    private struct WindowDragState
    {
        public int StartFrame;
        public int EndFrame;
        public float MouseDownX;
    }

    private static class ActiveActionIntegration
    {
        public static bool HasPreview(UnityEngine.Object target)
        {
            return target is ITimelinePreviewHost host && host.HasActivePreview;
        }

        public static float ResolvePreviewFPS(UnityEngine.Object target, float fallback)
        {
            if (target is ITimelinePreviewHost host)
            {
                float v = host.ResolvePreviewFPS(fallback);
                if (v > 0f) return v;
            }
            return fallback;
        }

        public static bool TryGetPreviewFrame(UnityEngine.Object target, out int frame)
        {
            frame = -1;
            if (target is ITimelinePreviewHost host)
            {
                frame = host.GetPreviewFrame();
                return frame >= 0;
            }
            return false;
        }

        public static void SeekPreviewFrame(UnityEngine.Object target, int frame)
        {
            if (target is ITimelinePreviewHost host)
            {
                host.SeekPreviewFrame(frame);
            }
        }

        public static bool CanSeekPreview(UnityEngine.Object target)
        {
            return target is ITimelinePreviewHost;
        }

        public static void PausePreviewIfPlaying(UnityEngine.Object target)
        {
            if (target is ITimelinePreviewHost host && host.IsPreviewPlaying)
            {
                host.SetPreviewPlaying(false);
            }
        }

        public static bool IsPreviewPlaying(UnityEngine.Object target)
        {
            return target is ITimelinePreviewHost host && host.IsPreviewPlaying;
        }

        public static void SetPlaying(UnityEngine.Object target, bool playing)
        {
            if (target is ITimelinePreviewHost host)
            {
                host.SetPreviewPlaying(playing);
            }
        }

        public static bool TryEnsurePreviewInfrastructure(UnityEngine.Object target, bool resetTime)
        {
            return target is ITimelinePreviewHost host && host.EnsurePreviewInfrastructure(resetTime);
        }

        public static void TrySyncPlayerClip(UnityEngine.Object target, bool resetTime)
        {
            if (target is ITimelinePreviewHost host)
            {
                host.SyncPlayerClip(resetTime);
            }
        }
    }

    // ---------------- Timeline State ----------------
    private sealed class TimelineState
    {
        public float Zoom = 1f;
        public float HScroll = 0f;
        public int CursorFrame = 0;
        public Rect VisibleRect;
        public bool IsSeeking = false;
        public bool WasPlayingBeforeSeek = false;
        private int _totalFrames;

        public void Ensure(int totalFrames)
        {
            _totalFrames = totalFrames;
            CursorFrame = Mathf.Clamp(CursorFrame, 0, Mathf.Max(0, totalFrames - 1));
            HScroll = Mathf.Clamp(HScroll, 0, Mathf.Max(0, WidthInPixels(totalFrames) - VisibleRect.width));
            // Do not forcibly clear IsSeeking here; keep transient state across repaints during drag
        }

        public float PixelsPerFrame => 6f * Zoom;
        public float WidthInPixels(int frames) => frames * PixelsPerFrame;
        public float FrameToPixelX(int frame) => frame * PixelsPerFrame - HScroll;
        public int PixelToFrame(float localX, int totalFrames)
        {
            float pixelsPerFrame = Mathf.Max(0.0001f, PixelsPerFrame);
            int maxFrame = Mathf.Max(0, totalFrames - 1);
            int value = Mathf.RoundToInt((localX + HScroll) / pixelsPerFrame);
            return Mathf.Clamp(value, 0, maxFrame);
        }
    }

    private static void SeekTimelineToMouse(UnityEngine.Object parentTarget, TimelineState st, Rect tracksRect, int totalFrames, float mouseX)
    {
        if (totalFrames <= 0)
        {
            return;
        }

        float localX = mouseX - (tracksRect.x + TimelineLabelWidth);
        if (localX < 0f)
        {
            localX = 0f;
        }

        int frame = st.PixelToFrame(localX, totalFrames);

    // If a preview player exists or the object supports seeking, drive it; otherwise just keep the UI in sync
    bool hasPreview = ActiveActionIntegration.HasPreview(parentTarget);
    bool canSeek = hasPreview || ActiveActionIntegration.CanSeekPreview(parentTarget);

        // Update local cursor so the UI always reflects user input
        if (st.CursorFrame != frame) st.CursorFrame = frame;

        if (canSeek)
        {
            ActiveActionIntegration.SeekPreviewFrame(parentTarget, frame);
            // Force scene update for immediate visual feedback while scrubbing
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            // Also try repainting GameView to ensure overlay updates there too
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        GUIHelper.RequestRepaint();
    }

}
#endif