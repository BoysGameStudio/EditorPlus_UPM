#if UNITY_EDITOR
using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    public interface IAnimationPreviewHost
    {
        /// <summary>True if a preview player is available and can be driven.</summary>
        bool HasActivePreview { get; }

        /// <summary>True if the preview player is currently playing.</summary>
        bool IsPreviewPlaying { get; }

        /// <summary>Get the current preview frame index (clamped to [0..last]).</summary>
        int GetPreviewFrame();

        /// <summary>Seek the preview player to the given frame index.</summary>
        void SeekPreviewFrame(int frame);

        /// <summary>Set the preview play state. Implementations may ignore when infrastructure is missing.</summary>
        void SetPreviewPlaying(bool playing);

        /// <summary>Ensure any required temporary infrastructure exists (preview stage, player, etc.).</summary>
        /// <returns>True if ready.</returns>
        bool EnsurePreviewInfrastructure(bool resetTime);

        /// <summary>Sync/bind the active clip to the preview player.</summary>
        void SyncPlayerClip(bool resetTime);

        /// <summary>Resolve the FPS used by the timeline when drawing/stepping. Return a positive value to override; otherwise return the fallback.</summary>
        float ResolvePreviewFPS(float fallbackFps);
    }
}
#endif
