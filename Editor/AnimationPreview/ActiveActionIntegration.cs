#if UNITY_EDITOR
using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    using Quantum;

    internal static class ActiveActionIntegration
    {
        public static bool HasPreview(UnityEngine.Object target)
        {
            // Prefer host interface
            if (target is IAnimationPreviewHost host) return host.HasActivePreview;
            // Special-case ActiveActionData which uses editor bridge
            if (target is ActiveActionData aad) return ActiveActionDataEditorBridge.HasActivePreviewPlayer(aad);
            return InlinePreviewManager.HasPreview(target);
        }

        public static float ResolvePreviewFPS(UnityEngine.Object target, float fallback)
        {
            if (target is IAnimationPreviewHost host)
            {
                float v = host.ResolvePreviewFPS(fallback);
                if (v > 0f) return v;
            }
            if (target is ActiveActionData aad)
            {
                float v = ActiveActionDataEditorBridge.GetState(aad)?.PreviewFPS ?? 0f;
                if (v > 0f) return v;
            }
            return fallback;
        }

        public static bool TryGetPreviewFrame(UnityEngine.Object target, out int frame)
        {
            frame = -1;
            if (target is IAnimationPreviewHost host)
            {
                frame = host.GetPreviewFrame();
                return frame >= 0;
            }
            if (target is ActiveActionData aad)
            {
                frame = ActiveActionDataEditorBridge.GetAnimationCurrentFrame(aad);
                return frame >= 0;
            }
            // Fallback to inline previews
            return InlinePreviewManager.TryGetPreviewFrame(target, out frame, 60f);
        }

        public static void SeekPreviewFrame(UnityEngine.Object target, int frame)
        {
            if (target is IAnimationPreviewHost host)
            {
                host.SeekPreviewFrame(frame);
                return;
            }
            if (target is ActiveActionData aad)
            {
                ActiveActionDataEditorBridge.SeekPreviewFrameEditor(aad, frame);
                return;
            }
            InlinePreviewManager.SeekPreviewFrame(target, frame, 60f);
        }

        public static bool CanSeekPreview(UnityEngine.Object target)
        {
            return target is IAnimationPreviewHost || InlinePreviewManager.HasPreview(target);
        }

        public static void PausePreviewIfPlaying(UnityEngine.Object target)
        {
            if (target is IAnimationPreviewHost host && host.IsPreviewPlaying)
            {
                host.SetPreviewPlaying(false);
                return;
            }
            if (target is ActiveActionData aad && ActiveActionDataEditorBridge.HasActivePreviewPlayer(aad) && ActiveActionDataEditorBridge.GetState(aad)?.PreviewPlaying == true)
            {
                ActiveActionDataEditorBridge.SetPlaying(aad, false, restart: false);
                return;
            }
            if (InlinePreviewManager.HasPreview(target) && InlinePreviewManager.IsPlaying(target))
            {
                InlinePreviewManager.SetPlaying(target, false);
            }
        }

        public static bool IsPreviewPlaying(UnityEngine.Object target)
        {
            if (target is IAnimationPreviewHost host) return host.IsPreviewPlaying;
            if (target is ActiveActionData aad) return ActiveActionDataEditorBridge.GetState(aad)?.PreviewPlaying == true;
            return InlinePreviewManager.HasPreview(target) && InlinePreviewManager.IsPlaying(target);
        }

        public static void SetPlaying(UnityEngine.Object target, bool playing)
        {
            if (target is IAnimationPreviewHost host)
            {
                host.SetPreviewPlaying(playing);
                return;
            }
            if (target is ActiveActionData aad)
            {
                ActiveActionDataEditorBridge.SetPlaying(aad, playing, restart: !playing);
                return;
            }
            if (InlinePreviewManager.HasPreview(target)) InlinePreviewManager.SetPlaying(target, playing);
        }

        public static bool TryEnsurePreviewInfrastructure(UnityEngine.Object target, bool resetTime)
        {
            if (target is IAnimationPreviewHost host) return host.EnsurePreviewInfrastructure(resetTime);
            if (target is ActiveActionData aad) return ActiveActionDataEditorBridge.EnsurePreviewInfrastructure(aad, resetTime);
            // Inline preview: nothing to ensure here without a clip; caller should call InlinePreviewManager.EnsurePreview
            return InlinePreviewManager.HasPreview(target);
        }

        public static void TrySyncPlayerClip(UnityEngine.Object target, bool resetTime)
        {
            if (target is IAnimationPreviewHost host)
            {
                host.SyncPlayerClip(resetTime);
                return;
            }
            if (target is ActiveActionData aad)
            {
                ActiveActionDataEditorBridge.SyncPlayerClip(aad, resetTime);
                return;
            }
            // No-op for inline previews; the InlinePreviewManager manages its own clip.
        }
    }
}
#endif