#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace EditorPlus.AnimationPreview
{
    internal static class CursorRenderer
    {
        public static void DrawCursorLine(UnityEngine.Object parentTarget, Rect tracksRect, TimelineState st, int totalFrames)
        {
            if (totalFrames <= 0) return;
            bool hasPreview = ActiveActionIntegration.HasPreview(parentTarget);
            // Draw preview when scrubbing or when a preview player is active. Previously this returned early
            // and suppressed hit-frame Scene gizmos when no runtime preview existed. Allow drawing during seek
            // so assets show their hit frames in SceneView while editing.
            if (!hasPreview && !st.IsSeeking)
            {
                // still draw cursor line but skip trying to get live preview frame
            }

            int frame = st.CursorFrame;
            int previewFrame;
            if (!st.IsSeeking && hasPreview && ActiveActionIntegration.TryGetPreviewFrame(parentTarget, out previewFrame))
            {
                frame = previewFrame;
            }

            frame = Mathf.Clamp(frame, 0, Mathf.Max(0, totalFrames - 1));

            float contentWidth = tracksRect.width - TimelineContext.TimelineLabelWidth;
            if (contentWidth <= 0f) return;

            float x = tracksRect.x + TimelineContext.TimelineLabelWidth + st.FrameToPixelX(frame);
            var lineRect = new Rect(x - 0.5f, tracksRect.y, 1.5f, tracksRect.height);
            EditorGUI.DrawRect(lineRect, new Color(1f, 0.85f, 0.2f, 0.9f));
            // Delegate hit frame preview rendering
            PreviewRenderer.DrawHitFramesPreview(parentTarget, frame);
        }
    }
}
#endif
