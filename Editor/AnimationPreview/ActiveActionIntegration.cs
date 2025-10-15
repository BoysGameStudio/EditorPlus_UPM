#if UNITY_EDITOR
using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    internal static class ActiveActionIntegration
    {
        public static bool HasPreview(UnityEngine.Object target)
        {
            return target is IAnimationPreviewHost host && host.HasActivePreview;
        }

        public static float ResolvePreviewFPS(UnityEngine.Object target, float fallback)
        {
            if (target is IAnimationPreviewHost host)
            {
                float v = host.ResolvePreviewFPS(fallback);
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
            return false;
        }

        public static void SeekPreviewFrame(UnityEngine.Object target, int frame)
        {
            if (target is IAnimationPreviewHost host)
            {
                host.SeekPreviewFrame(frame);
            }
        }

        public static bool CanSeekPreview(UnityEngine.Object target)
        {
            return target is IAnimationPreviewHost;
        }

        public static void PausePreviewIfPlaying(UnityEngine.Object target)
        {
            if (target is IAnimationPreviewHost host && host.IsPreviewPlaying)
            {
                host.SetPreviewPlaying(false);
            }
        }

        public static bool IsPreviewPlaying(UnityEngine.Object target)
        {
            return target is IAnimationPreviewHost host && host.IsPreviewPlaying;
        }

        public static void SetPlaying(UnityEngine.Object target, bool playing)
        {
            if (target is IAnimationPreviewHost host)
            {
                host.SetPreviewPlaying(playing);
            }
        }

        public static bool TryEnsurePreviewInfrastructure(UnityEngine.Object target, bool resetTime)
        {
            return target is IAnimationPreviewHost host && host.EnsurePreviewInfrastructure(resetTime);
        }

        public static void TrySyncPlayerClip(UnityEngine.Object target, bool resetTime)
        {
            if (target is IAnimationPreviewHost host)
            {
                host.SyncPlayerClip(resetTime);
            }
        }
    }
}
#endif