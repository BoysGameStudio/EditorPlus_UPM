#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Sirenix.Utilities.Editor;

namespace EditorPlus.AnimationPreview
{
    internal static class TrackRenderer
    {
        public static void DrawTracks(UnityEngine.Object parentTarget, Rect tracksRect, TimelineState st, float fps, int totalFrames)
        {
            var members = GetTrackMembers(parentTarget);
            float rowH = TimelineContext.TrackRowHeight;

            float currentY = tracksRect.y;

            for (int i = 0; i < members.Length; i++)
            {
                var tm = members[i];
                var row = new Rect(tracksRect.x, currentY, tracksRect.width, rowH);
                currentY += rowH;

                EditorGUI.DrawRect(row, new Color(0, 0, 0, 0.05f));

                var labelRect = new Rect(row.x + 6, row.y, TimelineContext.TimelineLabelWidth - 6, row.height);
                GUI.Label(labelRect, tm.Label, SirenixGUIStyles.Label);

                var content = new Rect(tracksRect.x + TimelineContext.TimelineLabelWidth, row.y + 4, tracksRect.width - TimelineContext.TimelineLabelWidth - 8, row.height - 8);
                DrawSingleTrack(parentTarget, tm, content, st, totalFrames);
            }
        }

        public static TrackMember[] GetTrackMembers(UnityEngine.Object target)
        {
            if (target == null) return Array.Empty<TrackMember>();
            var type = target.GetType();
            if (!TimelineContext.TrackMembersCache.TryGetValue(type, out var cached))
            {
                cached = BuildTrackMembersForType(type);
                TimelineContext.TrackMembersCache[type] = cached;
            }
            return cached;
        }

        public static TrackMember[] BuildTrackMembersForType(Type type)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var members = type.GetMembers(flags);
            var entries = new List<(TrackMember Track, int DeclarationIndex)>(members.Length);
            int declarationIndex = 0;

            foreach (var member in members)
            {
                var attribute = member.GetCustomAttribute<AnimationEventAttribute>();
                if (attribute == null) continue;

                Type valueType;
                Func<UnityEngine.Object, object> getter = null;
                Action<UnityEngine.Object, object> setter = null;

                if (member is FieldInfo field)
                {
                    valueType = field.FieldType;
                    getter = owner => field.GetValue(owner);
                    if (!field.IsInitOnly) setter = (owner, value) => field.SetValue(owner, value);
                }
                else if (member is PropertyInfo property)
                {
                    valueType = property.PropertyType;
                    if (property.CanRead) getter = owner => property.GetValue(owner, null);
                    if (property.CanWrite) setter = (owner, value) => property.SetValue(owner, value, null);
                }
                else
                {
                    continue;
                }

                if (getter == null) continue;

                var label = string.IsNullOrEmpty(attribute.Label) ? member.Name : attribute.Label;
                var color = AnimationPreviewDrawer.ParseHexOrDefault(attribute.ColorHex, AnimationPreviewDrawer.DefaultColorFor(valueType));

                var trackMember = new TrackMember
                {
                    Member = member,
                    Label = label,
                    ValueType = valueType,
                    Color = color,
                    Getter = getter,
                    Setter = setter,
                    Order = attribute.Order
                };

                entries.Add((trackMember, declarationIndex));
                declarationIndex++;
            }

            if (entries.Count == 0) return Array.Empty<TrackMember>();

            entries.Sort((a, b) =>
            {
                int orderComparison = a.Track.Order.CompareTo(b.Track.Order);
                if (orderComparison != 0) return orderComparison;
                return a.DeclarationIndex.CompareTo(b.DeclarationIndex);
            });

            var result = new TrackMember[entries.Count];
            for (int i = 0; i < entries.Count; i++) result[i] = entries[i].Track;
            return result;
        }

        private static void DrawSingleTrack(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int totalFrames)
        {
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.04f));

            if (tm.ValueType == typeof(int))
            {
                int val = (int)(tm.Getter?.Invoke(target) ?? 0);
                AnimationPreviewDrawer.DrawSingleMarker(target, tm, rect, st, val, tm.Color, TimelineContext.MarkerWidth, TimelineContext.ComputeControlSeed(target, tm), totalFrames, out _, out bool context, out int draggedFrame);

                if (draggedFrame != val && tm.Setter != null)
                {
                    tm.Setter(target, draggedFrame);
                    EditorUtility.SetDirty(target);
                }

                if (context) AnimationPreviewDrawer.ShowReadOnlyContextMenu();
            }
            else if (tm.ValueType == typeof(int[]))
            {
                var arr = (int[])(tm.Getter?.Invoke(target) ?? Array.Empty<int>());
                if (arr == null) arr = Array.Empty<int>();

                var controlSeed = TimelineContext.ComputeControlSeed(target, tm);

                if (arr.Length == 2)
                {
                    var binding = AnimationPreviewDrawer.CreateArrayWindowBinding(target, tm, arr, totalFrames);
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
            else if (AnimationPreviewDrawer.HasAffectWindowPattern(tm.ValueType))
            {
                var windowInstance = tm.Getter?.Invoke(target);
                if (windowInstance != null)
                {
                    var binding = AnimationPreviewDrawer.CreateAffectWindowBinding(target, tm, windowInstance, totalFrames);
                    AnimationPreviewDrawer.DrawWindowBinding(target, tm, rect, st, totalFrames, TimelineContext.ComputeControlSeed(target, tm), binding);

                    var evt = Event.current;
                    if (evt.type == EventType.ContextClick && rect.Contains(evt.mousePosition))
                    {
                        AnimationPreviewDrawer.ShowReadOnlyContextMenu();
                        evt.Use();
                    }
                }
                else
                {
                    EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.02f));
                    var c = GUI.color; GUI.color = new Color(1, 1, 1, 0.5f);
                    GUI.Label(rect, "〈No Window Data〉", SirenixGUIStyles.MiniLabelCentered);
                    GUI.color = c;
                }
            }
        }
    }
}
#endif
