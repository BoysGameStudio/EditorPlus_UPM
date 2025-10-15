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