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
        // Simplified owner resolution: do not perform any host-type-specific checks here.
        var clipValue = this.ValueEntry?.WeakSmartValue as UnityEngine.Object;
        var candidates = new List<UnityEngine.Object>(4);
        var seen = new HashSet<int>();

        var prop = this.Property?.Parent;
        while (prop != null)
        {
            var entry = prop.ValueEntry;
            if (entry != null && entry.WeakSmartValue is UnityEngine.Object unityObj && unityObj != clipValue)
            {
                int id = unityObj.GetInstanceID();
                if (seen.Add(id)) candidates.Add(unityObj);
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
                    int id = unityObj.GetInstanceID();
                    if (seen.Add(id)) candidates.Add(unityObj);
                }
            }
        }

        // Return first available candidate if any. Providers / other subsystems own any type-specific behavior.
        return candidates.Count > 0 ? candidates[0] : null;
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

    // Toolbar, ruler, cursor and track iteration have been moved to dedicated renderers
    // (ToolbarRenderer / RulerRenderer / CursorRenderer / TrackRenderer). The local
    // implementations were left behind and are unused; they were removed to reduce
    // duplication and maintenance surface.

    private static float ComputeTimelineContentHeight(UnityEngine.Object parentTarget)
    {
        float tracksHeight = GetTrackMembers(parentTarget).Length * TimelineContext.TrackRowHeight;

        const float rulerHeight = 20f;

        return rulerHeight + tracksHeight;
    }

    // (BuildMarkerRect and ExpandRect were removed - unused helpers)

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

    // Default color selection moved into providers so each provider can tune defaults
    // for its specific value types.



    // Per-track drawing logic is handled by registered providers (via TrackRenderer).
    // This file no longer contains a local DrawSingleTrack fast-path.

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

    // ComputeControlSeed and CombineControlSeed are provided by TimelineContext; duplicates removed here.


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
            // Reflection-based setter for normal objects/structs
            var t = instance.GetType();
            // Field
            var fi = t.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (fi != null && fi.FieldType == typeof(int))
            {
                fi.SetValue(instance, value);
                return true;
            }
            // Property
            var pi = t.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (pi != null && pi.CanWrite && pi.PropertyType == typeof(int))
            {
                pi.SetValue(instance, value, null);
                return true;
            }
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
            // Reflection-based getter
            var t = instance.GetType();
            var fi = t.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (fi != null && fi.FieldType == typeof(int))
            {
                var v = fi.GetValue(instance);
                if (v is int iv) return iv;
                return fallback;
            }
            var pi = t.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (pi != null && pi.CanRead && pi.PropertyType == typeof(int))
            {
                var v = pi.GetValue(instance, null);
                if (v is int iv2) return iv2;
                return fallback;
            }
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

    // Seeking and cursor logic now lives in CursorRenderer / InputHandler; hitframe
    // preview drawing is provided directly by PreviewRenderer. Local wrappers were
    // removed.

    // Draw an approximate preview for a Shape3DConfig serialized property.
    // DrawShape3DConfigPreview removed - PreviewRenderer provides centralized implementations.

    internal static Color ParseHexOrDefault(string colorHex, Color color)
    {
        if (string.IsNullOrWhiteSpace(colorHex)) return color;
        if (ColorUtility.TryParseHtmlString(colorHex, out var c)) return c;
        return color;
    }

    // Localized helpers to avoid referencing TimelineUtils from this file.
    internal static int[] ReadFrameArrayLocal(UnityEngine.Object owner, MemberInfo member)
    {
        if (owner == null || member == null) return Array.Empty<int>();

        try
        {
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
                    if (elem == null) continue;
                    // Try reflection: look for 'frame' field or property
                    int ef = -1;
                    try
                    {
                        var et = elem.GetType();
                        var fiElem = et.GetField("frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                        if (fiElem != null)
                        {
                            var v = fiElem.GetValue(elem);
                            if (v is int iv) ef = iv;
                        }
                        else
                        {
                            var piElem = et.GetProperty("frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                            if (piElem != null && piElem.CanRead)
                            {
                                var v = piElem.GetValue(elem, null);
                                if (v is int iv2) ef = iv2;
                            }
                        }
                    }
                    catch { }

                    if (ef >= 0) list.Add(ef);
                }
                return list.ToArray();
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

    // Removed typed-only fast path helper. Reflection/SerializedProperty are used instead.
}
#endif