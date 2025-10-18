#if UNITY_EDITOR
using System;
using System.Collections.Generic;

using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    internal static class TimelineContext
    {
        public const float TimelineLabelWidth = 180f;
        public const float TrackRowHeight = 24f;
        public const float MarkerWidth = 5f;

        // Per-parent target state (so multiple objects each have their own zoom/cursor, etc.)
        internal static readonly Dictionary<UnityEngine.Object, TimelineState> StateByTarget = new();
        internal static readonly Dictionary<int, WindowDragState> WindowBodyDragStates = new();
        internal static readonly Dictionary<Type, TrackMember[]> TrackMembersCache = new();

        // When true, the timeline will draw scene gizmos for markers even when no runtime preview is active.
        public static bool AlwaysShowMarkers = true;

        // Decoupled: we no longer reference ActiveActionData at compile time.
        internal static readonly object[] SeekPreviewArgs = new object[1];
        internal static readonly int TimelineSeekControlHint = "ShowActionTimelineSeekControl".GetHashCode();

        public static int ComputeControlSeed(UnityEngine.Object target, TrackMember tm, int index = -1)
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

        // Simple stack-based preview name context so DrawTimeline callers can scope which
        // tracks should be shown for the currently-drawn clip. PushPreviewName should be
        // called before drawing and PopPreviewName after drawing to restore state.
        private static readonly Stack<string> s_PreviewNameStack = new Stack<string>(4);

        public static void PushPreviewName(string name) => s_PreviewNameStack.Push(name);
        public static void PopPreviewName()
        {
            if (s_PreviewNameStack.Count > 0) s_PreviewNameStack.Pop();
        }

        public static string GetPreviewNameForTarget(UnityEngine.Object target)
        {
            return s_PreviewNameStack.Count > 0 ? s_PreviewNameStack.Peek() : null;
        }

        public static int CombineControlSeed(int seed, int index)
        {
            unchecked
            {
                return seed * 397 ^ index;
            }
        }
    }
}
#endif