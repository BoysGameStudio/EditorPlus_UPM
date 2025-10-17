#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Sirenix.Utilities.Editor;

namespace EditorPlus.AnimationPreview
{
    internal static class ToolbarRenderer
    {
        public static void DrawToolbar(UnityEngine.Object parentTarget, TimelineState st, AnimationClip clip, float fps, int frames, float length)
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

                // Play / Pause / Stop controls (works for hosts implementing IAnimationPreviewHost via
                // ActiveActionIntegration and for arbitrary objects via InlinePreviewManager).
                bool hasHostPreview = ActiveActionIntegration.HasPreview(parentTarget);
                bool hasInlinePreview = InlinePreviewManager.HasPreview(parentTarget);
                bool isPlaying = false;
                if (hasHostPreview)
                {
                    isPlaying = ActiveActionIntegration.IsPreviewPlaying(parentTarget);
                }
                else if (hasInlinePreview)
                {
                    isPlaying = InlinePreviewManager.IsPlaying(parentTarget);
                }

                var playLabel = isPlaying ? "Pause" : "Play";
                if (GUILayout.Button(playLabel, EditorStyles.toolbarButton, GUILayout.Width(64)))
                {
                    if (hasHostPreview)
                    {
                        ActiveActionIntegration.SetPlaying(parentTarget, !isPlaying);
                    }
                    else
                    {
                        // Ensure inline preview exists and toggle play state
                        InlinePreviewManager.EnsurePreview(parentTarget, clip, resetTime: false, fps: fps);
                        InlinePreviewManager.SetPlaying(parentTarget, !isPlaying);
                    }
                }

                if (GUILayout.Button("Stop", EditorStyles.toolbarButton, GUILayout.Width(48)))
                {
                    if (hasHostPreview)
                    {
                        ActiveActionIntegration.SetPlaying(parentTarget, false);
                        ActiveActionIntegration.TrySyncPlayerClip(parentTarget, resetTime: true);
                    }
                    else
                    {
                        InlinePreviewManager.Stop(parentTarget);
                    }
                }

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
    }
}
#endif
