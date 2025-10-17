#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    // Builds TrackMember for int[] fields/properties marked with [AnimationEvent]
    internal class IntArrayAttributeTrackProvider : TrackRenderer.ITrackProvider, EditorPlus.AnimationPreview.TrackRenderer.ICustomTrackDrawer
    {
        // Provider will be auto-registered via TrackRenderer.AutoRegisterProviders
        // Provider-wide default color for int-array members.
        private readonly Color DefaultColor = new Color(0.39f, 0.75f, 0.96f);

        public bool CanHandle(Type t)
        {
            if (t == null) return false;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var m in t.GetMembers(flags))
            {
                var attrs = m.GetCustomAttributes(false);
                object found = null;
                foreach (var a in attrs) if (a != null && a.GetType().Name == "AnimationEventAttribute") { found = a; break; }
                if (found == null) continue;
                Type vt = null;
                if (m is FieldInfo f) vt = f.FieldType;
                else if (m is PropertyInfo p) vt = p.PropertyType;
                if (vt == typeof(int[])) return true;
            }
            return false;
        }

        public TrackMember? Build(MemberInfo member, object animationEventAttributeInstance)
        {
            if (member == null) return null;

            Type valueType = null;
            Func<UnityEngine.Object, object> getter = null;
            Action<UnityEngine.Object, object> setter = null;

            if (member is FieldInfo field && field.FieldType == typeof(int[]))
            {
                valueType = field.FieldType;
                getter = owner => field.GetValue(owner);
                if (!field.IsInitOnly) setter = (owner, value) => field.SetValue(owner, value);
            }
            else if (member is PropertyInfo property && property.PropertyType == typeof(int[]))
            {
                valueType = property.PropertyType;
                if (property.CanRead) getter = owner => property.GetValue(owner, null);
                if (property.CanWrite) setter = (owner, value) => property.SetValue(owner, value, null);
            }
            else
            {
                return null;
            }

            string label = member.Name;
            string colorHex = null;
            int order = 0;
            string previewName = null;
            ProviderUtils.ExtractAttributeData(animationEventAttributeInstance, ref label, ref colorHex, ref order, ref previewName);

            // Use provider-level default color (int-array provider targets int[] members).
            var color = AnimationPreviewDrawer.ParseHexOrDefault(colorHex, DefaultColor);
            var trackMember = new TrackMember
            {
                Member = member,
                Label = label,
                PreviewName = previewName,
                ValueType = valueType,
                Color = color,
                Getter = getter,
                Setter = setter,
                Order = order
            };

            return trackMember;
        }

        public void Draw(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int totalFrames)
        {
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.04f));

            var arr = (int[])(tm.Getter?.Invoke(target) ?? Array.Empty<int>());
            if (arr == null) arr = Array.Empty<int>();

            var controlSeed = TimelineContext.ComputeControlSeed(target, tm);

            if (arr.Length == 2)
            {
                var binding = CreateArrayWindowBinding(target, tm, arr, totalFrames);
                AnimationPreviewDrawer.DrawWindowBinding(target, tm, rect, st, totalFrames, controlSeed, binding);
            }
            else
            {
                AnimationPreviewDrawer.DrawMarkers(target, tm, rect, st, arr, tm.Color, TimelineContext.MarkerWidth, controlSeed, totalFrames, out _, out bool context, out int draggedIndex, out int draggedFrame);

                if (draggedIndex >= 0 && draggedIndex < arr.Length && draggedFrame != arr[draggedIndex] && tm.Setter != null)
                {
                    var newArr = (int[])arr.Clone();
                    newArr[draggedIndex] = draggedFrame;
                    tm.Setter(target, newArr);
                    EditorUtility.SetDirty(target);
                }

                if (context) AnimationPreviewDrawer.ShowReadOnlyContextMenu();
            }
        }
        // Provider-owned window binding creation for int[] members (moved from drawer)
        private static WindowBinding CreateArrayWindowBinding(UnityEngine.Object target, TrackMember tm, int[] frames, int totalFrames)
        {
            int rawStart = frames.Length > 0 ? frames[0] : 0;
            int rawEnd = frames.Length > 1 ? frames[1] : rawStart;
            int start = Mathf.Clamp(rawStart, 0, totalFrames);
            int end = Mathf.Clamp(rawEnd, 0, totalFrames);

            return new WindowBinding(
                start,
                end,
                tm.Color,
                string.Empty,
                (owner, newStart, newEnd) =>
                {
                    if (tm.Setter == null || frames.Length < 2) return false;
                    if (frames[0] == newStart && frames[1] == newEnd) return false;

                    var newArr = (int[])frames.Clone();
                    newArr[0] = newStart;
                    newArr[1] = newEnd;
                    tm.Setter(owner, newArr);
                    return true;
                },
                rawStart,
                rawEnd);
        }
    }
}
#endif
