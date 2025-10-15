#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Sirenix.Utilities.Editor;

namespace EditorPlus.AnimationPreview
{
    internal static class InputHandler
    {
        public static void HandleZoomAndClick(UnityEngine.Object parentTarget, Rect fullRect, Rect rulerRect, Rect tracksRect, TimelineState st, int totalFrames)
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
                        if (ActiveActionIntegration.TryEnsurePreviewInfrastructure(parentTarget, resetTime: false))
                        {
                            ActiveActionIntegration.TrySyncPlayerClip(parentTarget, resetTime: false);
                        }
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

        public static void SeekTimelineToMouse(UnityEngine.Object parentTarget, TimelineState st, Rect tracksRect, int totalFrames, float mouseX)
        {
            if (totalFrames <= 0) return;

            float localX = mouseX - (tracksRect.x + TimelineContext.TimelineLabelWidth);
            if (localX < 0f) localX = 0f;

            int frame = st.PixelToFrame(localX, totalFrames);

            bool hasPreview = ActiveActionIntegration.HasPreview(parentTarget);
            bool canSeek = hasPreview || ActiveActionIntegration.CanSeekPreview(parentTarget);

            if (st.CursorFrame != frame) st.CursorFrame = frame;

            if (canSeek)
            {
                ActiveActionIntegration.SeekPreviewFrame(parentTarget, frame);
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
            GUIHelper.RequestRepaint();
        }
    }
}
#endif
