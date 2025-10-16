#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;

using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;

using UnityEditor;

using UnityEngine;
using EditorPlus.AnimationPreview;

// ========================= Drawer Core =========================

/// <summary>
/// Field-level drawer that renders: 1) the AnimationClip field itself; 2) a timeline; 3) tracks discovered via [TimelineTrack].
/// </summary>
public sealed partial class AnimationPreviewDrawer : OdinAttributeDrawer<AnimationPreviewAttribute, AnimationClip>
{
    // Shared constants and caches have been moved to TimelineContext for clarity.


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

        if (!TimelineContext.StateByTarget.TryGetValue(parentTarget, out var state))
        {
            state = new TimelineState();
            TimelineContext.StateByTarget[parentTarget] = state;
        }
        state.Ensure(totalFrames);

        if (addTopSpacing)
        {
            GUILayout.Space(4f);
        }

        GUILayout.BeginVertical(SirenixGUIStyles.BoxContainer);
        {
            ToolbarRenderer.DrawToolbar(parentTarget, state, clip, fps, totalFrames, length);

            var contentHeight = ComputeTimelineContentHeight(parentTarget);
            float baseMinHeight = minHeightOverride.HasValue ? Mathf.Max(0f, minHeightOverride.Value) : 80f;
            var areaH = Mathf.Max(baseMinHeight, contentHeight);
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(areaH));
            const float rulerH = 20f;
            var rulerRect = new Rect(rect.x, rect.y, rect.width, rulerH);
            var tracksRect = new Rect(rect.x, rect.y + rulerH, rect.width, rect.height - rulerH);

            RulerRenderer.DrawRuler(rulerRect, state, fps, totalFrames);
            CursorRenderer.DrawCursorLine(parentTarget, tracksRect, state, totalFrames);
            state.VisibleRect = tracksRect;

            TrackRenderer.DrawTracks(parentTarget, tracksRect, state, fps, totalFrames);
            InputHandler.HandleZoomAndClick(parentTarget, rect, rulerRect, tracksRect, state, totalFrames);
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

        // Prefer a candidate that declares a Timeline track member (AnimationEventAttribute)
        foreach (var c in candidates)
        {
            try
            {
                // Typed-first: known Quantum asset types
                try
                {
                    if (c is Quantum.AttackActionData || c is Quantum.ActiveActionData)
                    {
                        return c;
                    }
                }
                catch { }

                // Use TrackRenderer which has typed-first discovery for known types and a reflection fallback for unknowns.
                var tracks = TrackRenderer.GetTrackMembers(c);
                if (tracks != null && tracks.Length > 0) return c;
            }
            catch { }
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

            // DEBUG: show resolved owner type and discovered track count
            try
            {
                var ownerType = parentTarget != null ? parentTarget.GetType().Name : "(null)";
                var tracks = parentTarget != null ? GetTrackMembers(parentTarget).Length : 0;
                GUILayout.Label($"Owner: {ownerType}  Tracks: {tracks}", GUILayout.Width(260));
            }
            catch { }

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

    float rulerStartX = rect.x + TimelineContext.TimelineLabelWidth; // Align with track content area
    float rulerWidth = rect.width - TimelineContext.TimelineLabelWidth;

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

    float contentWidth = tracksRect.width - TimelineContext.TimelineLabelWidth;
        if (contentWidth <= 0f)
        {
            return;
        }

    float x = tracksRect.x + TimelineContext.TimelineLabelWidth + st.FrameToPixelX(frame);
        var lineRect = new Rect(x - 0.5f, tracksRect.y, 1.5f, tracksRect.height);
        EditorGUI.DrawRect(lineRect, new Color(1f, 0.85f, 0.2f, 0.9f));
        // Render HitFrame preview visuals (if any) for the current frame
        DrawHitFramesPreview(parentTarget, frame);
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
            float mx = e.mousePosition.x - (tracksRect.x + TimelineContext.TimelineLabelWidth);
            float norm = (mx + st.HScroll) / contentWidth;
            float visibleWidth = Mathf.Max(0f, tracksRect.width - TimelineContext.TimelineLabelWidth);
            float maxScroll = Mathf.Max(0f, contentWidth - visibleWidth);
            st.HScroll = Mathf.Clamp(norm * contentWidth - mx, 0f, maxScroll);
            e.Use();
            GUIHelper.RequestRepaint();
        }

        // Use a control rect covering both ruler and tracks content horizontally (excluding left labels)
    float timelineStart = tracksRect.x + TimelineContext.TimelineLabelWidth;
        float timelineEnd = tracksRect.xMax;
        var controlRect = new Rect(timelineStart, rulerRect.yMin, Mathf.Max(0f, timelineEnd - timelineStart), rulerRect.height + tracksRect.height);
    int controlId = GUIUtility.GetControlID(TimelineContext.TimelineSeekControlHint, FocusType.Passive, controlRect);
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

    internal static int AlignTo(int v, int step) => (v % step == 0) ? v : (v + (step - (v % step)));

    // ---------------- Tracks ----------------

    private static void DrawTracks(UnityEngine.Object parentTarget, Rect tracksRect, TimelineState st, float fps, int totalFrames)
    {
        var members = GetTrackMembers(parentTarget);
    float rowH = TimelineContext.TrackRowHeight;

        float currentY = tracksRect.y;

        for (int i = 0; i < members.Length; i++)
        {
            var tm = members[i];
            var row = new Rect(tracksRect.x, currentY, tracksRect.width, rowH);
            currentY += rowH;

            EditorGUI.DrawRect(row, new Color(0, 0, 0, 0.05f));

            var labelRect = new Rect(row.x + 6, row.y, TimelineContext.TimelineLabelWidth - 6, row.height);
            GUI.Label(labelRect, tm.Label, SirenixGUIStyles.Label);

            var content = new Rect(tracksRect.x + TimelineContext.TimelineLabelWidth, row.y + 4, tracksRect.width - TimelineContext.TimelineLabelWidth - 8, row.height - 8);
            DrawSingleTrack(parentTarget, tm, content, st, totalFrames);
        }
    }

    private static float ComputeTimelineContentHeight(UnityEngine.Object parentTarget)
    {
    float tracksHeight = GetTrackMembers(parentTarget).Length * TimelineContext.TrackRowHeight;

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

        // Delegate to TrackRenderer which handles typed-first discovery and caching.
        return TrackRenderer.GetTrackMembers(target);
    }

    // Track discovery and caching is delegated to TrackRenderer.

    internal static Color DefaultColorFor(Type type)
    {
        if (type == typeof(int)) return new Color(0.98f, 0.62f, 0.23f);
        if (type == typeof(int[])) return new Color(0.39f, 0.75f, 0.96f);
        if (HasAffectWindowPattern(type)) return new Color(0.5f, 0.9f, 0.5f);
        return new Color(0.8f, 0.8f, 0.8f);
    }

    

    private static void DrawSingleTrack(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int totalFrames)
    {
        EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.04f));

        if (tm.ValueType == typeof(int))
        {
            int val = (int)(tm.Getter?.Invoke(target) ?? 0);
            DrawSingleMarker(target, tm, rect, st, val, tm.Color, TimelineContext.MarkerWidth, TimelineContext.ComputeControlSeed(target, tm), totalFrames, out _, out bool context, out int draggedFrame);

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
        else if (tm.ValueType != null && tm.ValueType.IsArray)
        {
            // Support arrays of POCO/frame structs like HitFrame[] where each element has a 'frame' int member.
            var arrObj = tm.Getter?.Invoke(target) as Array;
            int[] frames = Array.Empty<int>();
            object[] elements = null;
            if (arrObj != null)
            {
                var list = new List<int>(arrObj.Length);
                elements = new object[arrObj.Length];
                for (int i = 0; i < arrObj.Length; i++)
                {
                    var elem = arrObj.GetValue(i);
                    elements[i] = elem;
                    if (elem == null) continue;
                    int elemFrame = -1;
                    try
                    {
                        // Typed direct access to known Quantum frame types (no string/reflection)
                        if (elem is Quantum.HitFrame hf) elemFrame = hf.frame;
                        else if (elem is Quantum.ProjectileFrame pf) elemFrame = pf.frame;
                        else if (elem is Quantum.ChildActorFrame caf) elemFrame = caf.Frame;
                        else elemFrame = -1;
                    }
                    catch { elemFrame = -1; }
                    if (elemFrame >= 0) list.Add(elemFrame);
                    else list.Add(-1);
                }

                // Build frames array for drawing: use -1 entries as skipped
                var tmp = new List<int>(list.Count);
                for (int i = 0; i < list.Count; i++) if (list[i] >= 0) tmp.Add(list[i]);
                frames = tmp.ToArray();
            }

            if (frames != null && frames.Length > 0)
            {
                // DEBUG: show count of detected elements in the track (helps debug why markers may be missing)
                if (Event.current.type == EventType.Repaint)
                {
                    try
                    {
                        var dbgRect = new Rect(rect.x + 4, rect.y + 2, 200, 16);
                        GUIStyle s = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white } };
                        GUI.Label(dbgRect, $"Elements: { (arrObj != null ? arrObj.Length : 0) }  FramesDrawn: {frames.Length}", s);
                    }
                    catch { }
                }
                DrawMarkers(target, tm, rect, st, frames, tm.Color, TimelineContext.MarkerWidth, TimelineContext.ComputeControlSeed(target, tm), totalFrames, out int clickedIndex, out bool context, out int draggedIndex, out int draggedFrame);

                // Removed verbose diagnostic logging; keep drawing only.

                // If a drag changed a frame and we have a setter, try to update the backing element's 'frame' member and apply.
                if (draggedIndex >= 0 && draggedFrame >= 0 && arrObj != null && tm.Setter != null)
                {
                    // Need to map draggedIndex in compacted frames[] back to original element index
                        int mapped = -1; int seen = 0;
                    for (int i = 0; i < arrObj.Length; i++)
                    {
                        var elem = arrObj.GetValue(i);
                        if (elem == null) continue;
                        int ef = -1;
                        try
                        {
                            if (elem is Quantum.HitFrame hf2) ef = hf2.frame;
                            else if (elem is Quantum.ProjectileFrame pf2) ef = pf2.frame;
                            else if (elem is Quantum.ChildActorFrame caf2) ef = caf2.Frame;
                            else ef = -1;
                        }
                        catch { ef = -1; }
                        if (ef >= 0)
                        {
                            if (seen == draggedIndex) { mapped = i; break; }
                            seen++;
                        }
                    }

                    if (mapped >= 0)
                    {
                        var elem = arrObj.GetValue(mapped);
                            if (elem != null)
                            {
                                bool applied = false;
                                try
                                {
                                    if (elem is Quantum.HitFrame hh)
                                    {
                                        hh.frame = draggedFrame;
                                        try { arrObj.SetValue(hh, mapped); } catch { }
                                        applied = true;
                                    }
                                    else if (elem is Quantum.ProjectileFrame pp)
                                    {
                                        pp.frame = draggedFrame;
                                        try { arrObj.SetValue(pp, mapped); } catch { }
                                        applied = true;
                                    }
                                        else if (elem is Quantum.ChildActorFrame cc)
                                        {
                                            // ChildActorFrame.Frame is read-only in the simulation types; do not attempt to mutate it here.
                                            // Editing read-only simulation-frame structs would require reflection into Quantum types which is forbidden.
                                            applied = false;
                                        }
                                }
                                catch { applied = false; }

                                if (applied)
                                {
                                    try
                                    {
                                        tm.Setter(target, arrObj);
                                        EditorUtility.SetDirty(target);
                                    }
                                    catch { }
                                }
                            }
                    }
                }

                if (context)
                {
                    ShowReadOnlyContextMenu();
                }
            }
            else
            {
                    // Fallback: try reading via SerializedProperty in case the getter doesn't expose the runtime array
                    bool drewFromSerialized = false;
                    try
                    {
                        // Local helper: typed-first read of frame arrays with SerializedProperty fallback
                        frames = ReadFrameArrayLocal(target, tm.Member);
                        if (frames != null && frames.Length > 0)
                        {
                            DrawMarkers(target, tm, rect, st, frames, tm.Color, TimelineContext.MarkerWidth, TimelineContext.ComputeControlSeed(target, tm), totalFrames, out int clickedIndex2, out bool context2, out int draggedIndex2, out int draggedFrame2);
                            if (context2) ShowReadOnlyContextMenu();
                            drewFromSerialized = true;
                        }
                    }
                    catch { }

                    if (!drewFromSerialized)
                    {
                        // Diagnostic: log that an array exists but no valid frame values were found
                        // No valid frame values discovered; show empty indicator.

                        // No frame-like elements: show empty indicator
                        EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.02f));
                        var c = GUI.color; GUI.color = new Color(1, 1, 1, 0.5f);
                        GUI.Label(rect, "〈No Frame Data〉", SirenixGUIStyles.MiniLabelCentered);
                        GUI.color = c;
                    }
            }
        }
        else if (tm.ValueType == typeof(int[]))
        {
            var arr = (int[])(tm.Getter?.Invoke(target) ?? Array.Empty<int>());
            if (arr == null) arr = Array.Empty<int>();

            var controlSeed = TimelineContext.ComputeControlSeed(target, tm);

            if (arr.Length == 2)
            {
                var binding = CreateArrayWindowBinding(target, tm, arr, totalFrames);
                DrawWindowBinding(target, tm, rect, st, totalFrames, controlSeed, binding);
            }
            else
            {
                DrawMarkers(target, tm, rect, st, arr, tm.Color, TimelineContext.MarkerWidth, controlSeed, totalFrames, out _, out bool context, out int draggedIndex, out int draggedFrame);

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
                DrawWindowBinding(target, tm, rect, st, totalFrames, TimelineContext.ComputeControlSeed(target, tm), binding);

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
    internal static void DrawSingleMarker(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int frame, Color color, float width, int controlSeed, int totalFrames, out bool clicked, out bool context, out int draggedFrame)
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
    internal static void DrawMarkers(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int[] frames, Color color, float width, int controlSeedBase, int totalFrames, out int clickedIndex, out bool context, out int draggedIndex, out int draggedFrame)
    {
        clickedIndex = -1; context = false; draggedIndex = -1; draggedFrame = -1;

        for (int i = 0; i < frames.Length; i++)
        {
            float px = rect.x + st.FrameToPixelX(frames[i]);
            var r = new Rect(px - width * 0.5f, rect.y + 3, width, rect.height - 6);
            int controlId = GUIUtility.GetControlID(TimelineContext.CombineControlSeed(controlSeedBase, i), FocusType.Passive, r);
            var e = Event.current;
            var type = e.GetTypeForControl(controlId);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, color);
            }

            // Optionally draw scene gizmos for all markers when configured (helps visual debugging and always-on previews)
            if (TimelineContext.AlwaysShowMarkers && Event.current.type == EventType.Repaint)
            {
                try
                {
                    foreach (var f in frames)
                    {
                        PreviewRenderer.DrawHitFramesPreview(target, f);
                    }
                }
                catch { }
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

    internal static void DrawWindow(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int startFrame, int endFrame, Color color, string label, int controlSeedBase, int totalFrames, out int newStartFrame, out int newEndFrame, out bool dragged)
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
    int startControlId = GUIUtility.GetControlID(TimelineContext.CombineControlSeed(controlSeedBase, 101), FocusType.Passive, startHandle);
    int endControlId = GUIUtility.GetControlID(TimelineContext.CombineControlSeed(controlSeedBase, 202), FocusType.Passive, endHandle);
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
    int bodyControlId = GUIUtility.GetControlID(TimelineContext.CombineControlSeed(controlSeedBase, 303), FocusType.Passive, r);
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
                    TimelineContext.WindowBodyDragStates[bodyControlId] = new WindowDragState
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
                if (GUIUtility.hotControl == bodyControlId && e.button == 0 && TimelineContext.WindowBodyDragStates.TryGetValue(bodyControlId, out var dragState))
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
                    TimelineContext.WindowBodyDragStates.Remove(bodyControlId);
                    e.Use();
                }
                break;

            case EventType.Ignore:
            case EventType.Layout:
            case EventType.Repaint:
                break;
        }

        if (bodyType == EventType.MouseUp && TimelineContext.WindowBodyDragStates.ContainsKey(bodyControlId))
        {
            TimelineContext.WindowBodyDragStates.Remove(bodyControlId);
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


    internal static void ShowReadOnlyContextMenu()
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

    internal static void DrawWindowBinding(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int totalFrames, int controlSeed, WindowBinding binding)
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

    internal static WindowBinding CreateArrayWindowBinding(UnityEngine.Object target, TrackMember tm, int[] frames, int totalFrames)
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

    internal static WindowBinding CreateAffectWindowBinding(UnityEngine.Object target, TrackMember tm, object window, int totalFrames)
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
        if (instance == null || string.IsNullOrEmpty(memberName)) return false;
        try
        {
            // SerializedProperty path (editor asset case)
            if (instance is SerializedProperty sp)
            {
                var p = sp.FindPropertyRelative(memberName);
                if (p != null)
                {
                    p.intValue = value;
                    sp.serializedObject.ApplyModifiedProperties();
                    return true;
                }
                return false;
            }

            // Typed Quantum structures
            if (instance is Quantum.ActiveActionIFrames iFrames)
            {
                if (memberName.Equals("IntraActionStartFrame", StringComparison.OrdinalIgnoreCase)) { iFrames.IntraActionStartFrame = value; return true; }
                if (memberName.Equals("IntraActionEndFrame", StringComparison.OrdinalIgnoreCase)) { iFrames.IntraActionEndFrame = value; return true; }
            }

            if (instance is Quantum.ActiveActionAffectWindow aw)
            {
                if (memberName.Equals("IntraActionStartFrame", StringComparison.OrdinalIgnoreCase)) { aw.IntraActionStartFrame = value; return true; }
                if (memberName.Equals("IntraActionEndFrame", StringComparison.OrdinalIgnoreCase)) { aw.IntraActionEndFrame = value; return true; }
            }

            // Frame elements (boxed structs) - set only for mutable types
            if (instance is Quantum.HitFrame hf)
            {
                hf.frame = value;
                return true;
            }
            if (instance is Quantum.ProjectileFrame pf)
            {
                pf.frame = value;
                return true;
            }

            // ChildActorFrame.Frame is read-only; do not attempt to set
        }
        catch { }
        return false;
    }

    private static int TryGetIntMember(object instance, string memberName, int fallback)
    {
        if (instance == null || string.IsNullOrEmpty(memberName)) return fallback;
        try
        {
            if (instance is SerializedProperty sp)
            {
                var p = sp.FindPropertyRelative(memberName);
                if (p != null) return p.intValue;
                return fallback;
            }

            if (instance is Quantum.ActiveActionIFrames iFrames)
            {
                if (memberName.Equals("IntraActionStartFrame", StringComparison.OrdinalIgnoreCase)) return iFrames.IntraActionStartFrame;
                if (memberName.Equals("IntraActionEndFrame", StringComparison.OrdinalIgnoreCase)) return iFrames.IntraActionEndFrame;
                return fallback;
            }

            if (instance is Quantum.ActiveActionAffectWindow aw)
            {
                if (memberName.Equals("IntraActionStartFrame", StringComparison.OrdinalIgnoreCase)) return aw.IntraActionStartFrame;
                if (memberName.Equals("IntraActionEndFrame", StringComparison.OrdinalIgnoreCase)) return aw.IntraActionEndFrame;
                return fallback;
            }

            if (instance is Quantum.HitFrame hf) return hf.frame;
            if (instance is Quantum.ProjectileFrame pf) return pf.frame;
            if (instance is Quantum.ChildActorFrame caf) return caf.Frame;
        }
        catch { }
        return fallback;
    }

    internal static bool HasAffectWindowPattern(Type t)
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

    // Window drag state, ActiveActionIntegration and TimelineState moved to partial files.

    private static void SeekTimelineToMouse(UnityEngine.Object parentTarget, TimelineState st, Rect tracksRect, int totalFrames, float mouseX)
    {
        if (totalFrames <= 0)
        {
            return;
        }

    float localX = mouseX - (tracksRect.x + TimelineContext.TimelineLabelWidth);
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

    // ---------------- HitFrame preview rendering ----------------
    // Delegates to PreviewRenderer to centralize preview drawing logic.
    private static void DrawHitFramesPreview(UnityEngine.Object parentTarget, int frame)
    {
        if (parentTarget == null) return;
        if (Event.current?.type != EventType.Repaint) return;
        try
        {
            PreviewRenderer.DrawHitFramesPreview(parentTarget, frame);
        }
        catch { }
    }

    // Draw an approximate preview for a Shape3DConfig serialized property.
    private static void DrawShape3DConfigPreview(SerializedProperty shapeProp, GameObject context)
    {
        try
        {
            // Expect fields: ShapeType (enum), PositionOffset (Vector3-like), RotationOffset (float or Quaternion-like), SphereRadius, BoxExtents, CapsuleRadius, CapsuleHeight
            var shapeTypeProp = shapeProp.FindPropertyRelative("ShapeType");
            if (shapeTypeProp == null) return;

            // Determine shape type by enum index
            int shapeType = shapeTypeProp.enumValueIndex;

            // Position offset
            Vector3 pos = Vector3.zero;
            var posProp = shapeProp.FindPropertyRelative("PositionOffset");
            if (posProp != null && posProp.propertyType == SerializedPropertyType.Vector3)
            {
                pos = posProp.vector3Value;
            }

            // Rotation offset (if present as Quaternion or Euler float)
            Quaternion rot = Quaternion.identity;
            var rotProp = shapeProp.FindPropertyRelative("RotationOffset");
            if (rotProp != null)
            {
                if (rotProp.propertyType == SerializedPropertyType.Vector3)
                {
                    rot = Quaternion.Euler(rotProp.vector3Value);
                }
                else if (rotProp.propertyType == SerializedPropertyType.Quaternion)
                {
                    try { rot = rotProp.quaternionValue; } catch { rot = Quaternion.identity; }
                }
            }

            // Transform local position to world using context if available
            Vector3 worldPos = pos;
            Quaternion worldRot = rot;
            if (context != null)
            {
                var t = context.transform;
                worldPos = t.TransformPoint(pos);
                worldRot = t.rotation * rot;
            }

            Handles.color = new Color(1f, 0.25f, 0.25f, 0.6f);

            // 0=Unknown/None, 1=Sphere, 2=Box, 3=Capsule (approx mapping)
            switch (shapeType)
            {
                case 1: // Sphere
                {
                    var radiusProp = shapeProp.FindPropertyRelative("SphereRadius");
                    float r = 0.5f;
                    if (radiusProp != null) r = Mathf.Max(0.001f, radiusProp.floatValue);
                    Handles.DrawWireDisc(worldPos, worldRot * Vector3.up, r);
                    Handles.DrawWireDisc(worldPos, worldRot * Vector3.right, r);
                    Handles.DrawWireDisc(worldPos, worldRot * Vector3.forward, r);
                    Handles.Label(worldPos + Vector3.up * (r + 0.1f), "HitFrame(Sphere)");
                }
                break;

                case 2: // Box
                {
                    var extentsProp = shapeProp.FindPropertyRelative("BoxExtents");
                    Vector3 ext = Vector3.one * 0.5f;
                    if (extentsProp != null && extentsProp.propertyType == SerializedPropertyType.Vector3) ext = extentsProp.vector3Value;
                    var verts = new Vector3[8];
                    var half = ext;
                    verts[0] = worldPos + worldRot * new Vector3(-half.x, -half.y, -half.z);
                    verts[1] = worldPos + worldRot * new Vector3(half.x, -half.y, -half.z);
                    verts[2] = worldPos + worldRot * new Vector3(half.x, -half.y, half.z);
                    verts[3] = worldPos + worldRot * new Vector3(-half.x, -half.y, half.z);
                    verts[4] = worldPos + worldRot * new Vector3(-half.x, half.y, -half.z);
                    verts[5] = worldPos + worldRot * new Vector3(half.x, half.y, -half.z);
                    verts[6] = worldPos + worldRot * new Vector3(half.x, half.y, half.z);
                    verts[7] = worldPos + worldRot * new Vector3(-half.x, half.y, half.z);

                    Handles.DrawLine(verts[0], verts[1]); Handles.DrawLine(verts[1], verts[2]); Handles.DrawLine(verts[2], verts[3]); Handles.DrawLine(verts[3], verts[0]);
                    Handles.DrawLine(verts[4], verts[5]); Handles.DrawLine(verts[5], verts[6]); Handles.DrawLine(verts[6], verts[7]); Handles.DrawLine(verts[7], verts[4]);
                    Handles.DrawLine(verts[0], verts[4]); Handles.DrawLine(verts[1], verts[5]); Handles.DrawLine(verts[2], verts[6]); Handles.DrawLine(verts[3], verts[7]);
                    Handles.Label(worldPos + worldRot * Vector3.up * (half.y + 0.1f), "HitFrame(Box)");
                }
                break;

                case 3: // Capsule (approx as two spheres + cylinder)
                {
                    var radiusProp = shapeProp.FindPropertyRelative("CapsuleRadius");
                    var heightProp = shapeProp.FindPropertyRelative("CapsuleHeight");
                    float radius = 0.25f; float height = 1f;
                    if (radiusProp != null) radius = Mathf.Max(0.001f, radiusProp.floatValue);
                    if (heightProp != null) height = Mathf.Max(0f, heightProp.floatValue);
                    float half = Mathf.Max(0f, (height - 2f * radius) * 0.5f);
                    Vector3 up = worldRot * Vector3.up;
                    var top = worldPos + up * half;
                    var bot = worldPos - up * half;
                    Handles.DrawWireDisc(top, worldRot * Vector3.up, radius);
                    Handles.DrawWireDisc(bot, worldRot * Vector3.up, radius);
                    // draw simple connecting lines (approx cylinder)
                    Handles.DrawLine(top + worldRot * Vector3.right * radius, bot + worldRot * Vector3.right * radius);
                    Handles.DrawLine(top - worldRot * Vector3.right * radius, bot - worldRot * Vector3.right * radius);
                    Handles.DrawLine(top + worldRot * Vector3.forward * radius, bot + worldRot * Vector3.forward * radius);
                    Handles.DrawLine(top - worldRot * Vector3.forward * radius, bot - worldRot * Vector3.forward * radius);
                    Handles.Label(worldPos + up * (half + radius + 0.05f), "HitFrame(Capsule)");
                }
                break;

                default:
                {
                    // Unknown shape type — draw simple indicator
                    Handles.DrawWireDisc(worldPos, Vector3.up, 0.5f);
                    Handles.Label(worldPos + Vector3.up * 0.6f, "HitFrame");
                }
                break;
            }
        }
        catch { }
    }

    internal static Color ParseHexOrDefault(string colorHex, Color color)
    {
        if (string.IsNullOrWhiteSpace(colorHex)) return color;
        if (ColorUtility.TryParseHtmlString(colorHex, out var c)) return c;
        return color;
    }

    // Localized helpers to avoid referencing TimelineUtils from this file.
    private static int[] ReadFrameArrayLocal(UnityEngine.Object owner, MemberInfo member)
    {
        if (owner == null || member == null) return Array.Empty<int>();

        try
        {
            // Typed-first: known Quantum asset arrays
            try
            {
                if (owner is Quantum.AttackActionData attack)
                {
                    if (attack.hitFrames != null)
                    {
                        var tmp = new System.Collections.Generic.List<int>(attack.hitFrames.Length);
                        foreach (var hf in attack.hitFrames) if (hf != null) tmp.Add(hf.frame);
                        return tmp.ToArray();
                    }

                    if (attack.projectileFrames != null)
                    {
                        var tmp = new System.Collections.Generic.List<int>(attack.projectileFrames.Length);
                        foreach (var pf in attack.projectileFrames) if (pf != null) tmp.Add(pf.frame);
                        return tmp.ToArray();
                    }
                }
            }
            catch { }

            Func<object> getter = null;
            if (member is FieldInfo fi) getter = () => fi.GetValue(owner);
            else if (member is PropertyInfo pi && pi.CanRead) getter = () => pi.GetValue(owner, null);

            var arrObj = getter != null ? getter() as Array : null;
            if (arrObj != null)
            {
                var list = new System.Collections.Generic.List<int>(arrObj.Length);
                for (int i = 0; i < arrObj.Length; i++)
                {
                    var elem = arrObj.GetValue(i);
                    if (elem == null) { list.Add(-1); continue; }
                    int ef = -1;
                    if (TryGetIntFieldOrPropLocal(elem, "frame", out ef)) list.Add(ef); else list.Add(-1);
                }
                var tmp = new System.Collections.Generic.List<int>();
                for (int i = 0; i < list.Count; i++) if (list[i] >= 0) tmp.Add(list[i]);
                return tmp.ToArray();
            }
        }
        catch { }

        // SerializedProperty fallback
        try
        {
            var so = new SerializedObject(owner);
            var prop = so.FindProperty(member.Name);
            if (prop != null && prop.isArray && prop.arraySize > 0)
            {
                var tmp = new System.Collections.Generic.List<int>();
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var elem = prop.GetArrayElementAtIndex(i);
                    if (elem == null) continue;
                    var frameProp = elem.FindPropertyRelative("frame");
                    if (frameProp != null) tmp.Add(frameProp.intValue);
                }
                return tmp.ToArray();
            }
        }
        catch { }

        return Array.Empty<int>();
    }

    // Local typed-only fast path to read 'frame' from known Quantum frame types.
    // This intentionally avoids reflection and only supports the in-repo known frame structs.
    private static bool TryGetIntFieldOrPropLocal(object instance, string name, out int value)
    {
        value = -1;
        if (instance == null) return false;
        try
        {
            if (instance is Quantum.HitFrame hf && (name.Equals("frame", StringComparison.OrdinalIgnoreCase) || name.Equals("Frame", StringComparison.OrdinalIgnoreCase))) { value = hf.frame; return true; }
            if (instance is Quantum.ProjectileFrame pf && (name.Equals("frame", StringComparison.OrdinalIgnoreCase) || name.Equals("Frame", StringComparison.OrdinalIgnoreCase))) { value = pf.frame; return true; }
            if (instance is Quantum.ChildActorFrame caf && (name.Equals("frame", StringComparison.OrdinalIgnoreCase) || name.Equals("Frame", StringComparison.OrdinalIgnoreCase))) { value = caf.Frame; return true; }
        }
        catch { }

        return false;
    }
}
#endif