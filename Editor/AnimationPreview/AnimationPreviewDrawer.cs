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

    protected override void DrawPropertyLayout(GUIContent label)
    {
        this.CallNextDrawer(label);
    }

    // Initialization for editor runtime used to be here (hook registration).
    // TimelineAPI (host-facing hook) was removed; provider auto-registration
    // remains handled by TrackRenderer.AutoRegisterProviders via InitializeOnLoadMethod.
    public static void DrawTimeline(UnityEngine.Object parentTarget, AnimationClip clip, float? minHeightOverride = null, bool includeFrameEventsInspector = true, bool addTopSpacing = true, string previewName = null)
    {
        if (clip == null || parentTarget == null)
        {
            return;
        }


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

        if (!string.IsNullOrEmpty(previewName)) TimelineContext.PushPreviewName(previewName);
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
            // Emit diagnostics to the Console to help debug missing timeline/tracks
            // Diagnostics removed: avoid spamming the Console in normal editor usage.
            // Diagnostic: if no tracks were drawn, show a small hint so it's easier to
            // spot why the timeline appears empty. This prints provider count and
            // the current previewName scope.
            try
            {
                var members = GetTrackMembers(parentTarget);
                if (members == null || members.Length == 0)
                {
                    int provCount = 0;
                    try { provCount = TrackRenderer.GetRegisteredProviders()?.Length ?? 0; } catch { }

                    // Reflection fallback: scan the parentTarget type for any members
                    // bearing an attribute named "AnimationEventAttribute" so we can
                    // detect attribute presence and any PreviewName values.
                    string foundAttrsSummary = string.Empty;
                    try
                    {
                        var t = parentTarget.GetType();
                        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        var membersList = t.GetMembers(flags);
                        var found = new List<string>();
                        for (int mi = 0; mi < membersList.Length; mi++)
                        {
                            var m = membersList[mi];
                            var attrs = m.GetCustomAttributes(false);
                            if (attrs == null) continue;
                            for (int ai = 0; ai < attrs.Length; ai++)
                            {
                                var a = attrs[ai];
                                if (a == null) continue;
                                if (a.GetType().Name == "AnimationEventAttribute")
                                {
                                    string pm = "<none>";
                                    try
                                    {
                                        var prop = a.GetType().GetProperty("PreviewName");
                                        if (prop != null) pm = prop.GetValue(a) as string ?? "<none>";
                                    }
                                    catch { }
                                    found.Add($"{m.Name}(previewName={pm})");
                                }
                            }
                        }
                        if (found.Count > 0) foundAttrsSummary = string.Join(", ", found);
                    }
                    catch { }

                    var hint = $"No timeline tracks found (providers={provCount}, previewName={(string.IsNullOrEmpty(previewName)?"<none>":previewName)})" + (string.IsNullOrEmpty(foundAttrsSummary) ? "" : $" — attributes: {foundAttrsSummary}");
                    var hintR = new Rect(tracksRect.x + 8, tracksRect.y + 8, tracksRect.width - 16, 20 + (string.IsNullOrEmpty(foundAttrsSummary) ? 0 : 14));
                    GUIStyle s = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
                    EditorGUI.LabelField(hintR, hint, s);
                    // Reflection diagnostic removed to avoid console spam.
                }
            }
            catch { }
            InputHandler.HandleZoomAndClick(parentTarget, rect, rulerRect, tracksRect, state, totalFrames);
        }
        GUILayout.EndVertical();
        _ = includeFrameEventsInspector; // retained for signature compatibility
        if (!string.IsNullOrEmpty(previewName)) TimelineContext.PopPreviewName();
    }


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
        if (candidates.Count > 0) return candidates[0];

        // Fallback: sometimes Odin property resolution fails to enumerate the owning
        // object (domain reloads, nested serialization, etc.). Use the current
        // selection as a best-effort fallback so the timeline still appears in
        // the Inspector when the inspected object is selected.
        try
        {
            var sel = UnityEditor.Selection.activeObject;
            if (sel != null && sel != clipValue) return sel;
        }
        catch { }

        return null;
    }


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


    private static float ComputeTimelineContentHeight(UnityEngine.Object parentTarget)
    {
        float tracksHeight = GetTrackMembers(parentTarget).Length * TimelineContext.TrackRowHeight;

        const float rulerHeight = 20f;

        return rulerHeight + tracksHeight;
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


    internal static void DrawSingleMarker(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int frame, Color color, float width, int controlSeed, int totalFrames, out bool clicked, out bool context, out int draggedFrame)
    {
        clicked = false; context = false; draggedFrame = frame;
        float px = rect.x + st.FrameToPixelX(frame);
        var r = new Rect(px - width * 0.5f, rect.y + 2, width, rect.height - 4);
        int controlId = GUIUtility.GetControlID(controlSeed, FocusType.Passive, r);
        var e = Event.current;
        var type = e.GetTypeForControl(controlId);

        if (e.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(r, color);
        }

        EditorGUIUtility.AddCursorRect(r, MouseCursor.SlideArrow);

        switch (type)
        {
            case EventType.MouseDown:
                if (e.button == 0 && r.Contains(e.mousePosition))
                {
                    RecordUndo(target, $"Move {tm.Label} Marker");
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


    internal static void ShowReadOnlyContextMenu()
    {
        var menu = new GenericMenu();
        menu.AddDisabledItem(new GUIContent("Timeline editing is disabled"));
        menu.ShowAsContext();
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


    internal static Color ParseHexOrDefault(string colorHex, Color color)
    {
        if (string.IsNullOrWhiteSpace(colorHex)) return color;
        if (ColorUtility.TryParseHtmlString(colorHex, out var c)) return c;
        return color;
    }

    // Helper: record an undo if target is available
    private static void RecordUndo(UnityEngine.Object target, string label)
    {
        if (target == null) return;
        Undo.RecordObject(target, label);
    }

    // Helper: draw simple 1px borders around a rect
    private static void DrawRectBorders(Rect r)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), new Color(0, 0, 0, 0.5f));
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), new Color(0, 0, 0, 0.5f));
        EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), new Color(0, 0, 0, 0.5f));
        EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), new Color(0, 0, 0, 0.5f));
    }


}
#endif