#if UNITY_EDITOR
using System;
using System.Reflection;

using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    internal struct TrackMember
    {
        public MemberInfo Member;
        public string Label;
        // Optional preview name this track belongs to (matches AnimationEventAttribute.PreviewName)
        public string PreviewName;
        public Type ValueType;
        public Color Color;
        public Func<UnityEngine.Object, object> Getter;
        public Action<UnityEngine.Object, object> Setter;
        public int Order;
    }

    internal readonly struct WindowBinding
    {
        public WindowBinding(int start, int end, Color color, string label, Func<UnityEngine.Object, int, int, bool> apply, int? rawStart = null, int? rawEnd = null)
        {
            if (apply == null) throw new ArgumentNullException(nameof(apply));

            if (end < start)
            {
                (start, end) = (end, start);
            }

            StartFrame = start;
            EndFrame = end;
            Color = color;
            Label = label ?? string.Empty;
            Apply = apply;
            RawStart = rawStart ?? start;
            RawEnd = rawEnd ?? end;
        }

        public int StartFrame { get; }
        public int EndFrame { get; }
        public int RawStart { get; }
        public int RawEnd { get; }
        public Color Color { get; }
        public string Label { get; }
        public Func<UnityEngine.Object, int, int, bool> Apply { get; }
    }

    internal struct WindowDragState
    {
        public int StartFrame;
        public int EndFrame;
        public float MouseDownX;
    }
}
#endif