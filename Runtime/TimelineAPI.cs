#if UNITY_EDITOR
using UnityEngine;

namespace EditorPlus.SceneTimeline
{
    /// <summary>
    /// Editor-only hook surface so hosts can draw the ShowActionTimeline UI at a custom spot
    /// without taking a hard compile-time dependency on the Editor assembly.
    /// </summary>
    public static class TimelineAPI
    {
        public delegate void DrawTimelineDelegate(Object parentTarget, AnimationClip clip, float? minHeightOverride, bool includeFrameEventsInspector, bool addTopSpacing);

        /// <summary>Set by the Editor assembly at load time.</summary>
        public static DrawTimelineDelegate DrawTimelineHook;

        /// <summary>Try to draw the timeline using the registered hook (if any).</summary>
        public static bool TryDrawTimeline(Object parentTarget, AnimationClip clip, float? minHeightOverride = null, bool includeFrameEventsInspector = true, bool addTopSpacing = true)
        {
            var d = DrawTimelineHook;
            if (d == null || clip == null || parentTarget == null)
            {
                return false;
            }
            d(parentTarget, clip, minHeightOverride, includeFrameEventsInspector, addTopSpacing);
            return true;
        }
    }
}
#endif
