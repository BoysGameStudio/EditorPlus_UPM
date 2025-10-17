#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Sirenix.Utilities.Editor;

namespace EditorPlus.AnimationPreview
{
    internal class FrameArrayTrackProvider : TrackRenderer.ITrackProvider, TrackRenderer.ICustomTrackDrawer
    {
        private readonly Color DefaultColor = new Color(0.8f, 0.8f, 0.8f);

        public bool CanHandle(Type t)
        {
            if (t == null) return false;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var m in t.GetMembers(flags))
            {
                var attrs = m.GetCustomAttributes(false);
                if (Array.Exists(attrs, a => a.GetType().Name == "AnimationEventAttribute"))
                {
                    Type vt = m is FieldInfo f ? f.FieldType : m is PropertyInfo p ? p.PropertyType : null;
                    if (vt?.IsArray == true) return true;
                }
            }
            return false;
        }

        public TrackMember? Build(MemberInfo member, object animationEventAttributeInstance)
        {
            if (member == null) return null;

            Type valueType = null;
            Func<UnityEngine.Object, object> getter = null;
            Action<UnityEngine.Object, object> setter = null;

            if (member is FieldInfo field && field.FieldType.IsArray)
            {
                valueType = field.FieldType;
                getter = owner => field.GetValue(owner);
                if (!field.IsInitOnly) setter = (owner, value) => field.SetValue(owner, value);
            }
            else if (member is PropertyInfo property && property.PropertyType.IsArray)
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

            var color = AnimationPreviewDrawer.ParseHexOrDefault(colorHex, DefaultColor);

            return new TrackMember
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
        }

        public void Draw(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int totalFrames)
        {
            var arrObj = tm.Getter?.Invoke(target) as Array;
            var frames = ExtractFrames(arrObj);

            if (frames.Length > 0)
            {
                AnimationPreviewDrawer.DrawMarkers(target, tm, rect, st, frames, tm.Color, TimelineContext.MarkerWidth, TimelineContext.ComputeControlSeed(target, tm), totalFrames, out int clickedIndex, out bool context, out int draggedIndex, out int draggedFrame);

                if (draggedIndex >= 0 && draggedFrame >= 0 && arrObj != null && tm.Setter != null)
                {
                    ApplyDraggedFrame(arrObj, draggedIndex, draggedFrame, tm, target);
                }

                if (context)
                {
                    AnimationPreviewDrawer.ShowReadOnlyContextMenu();
                }
            }
            else
            {
                var frames2 = ReadFrameArrayLocal(target, tm.Member);
                if (frames2.Length > 0)
                {
                    AnimationPreviewDrawer.DrawMarkers(target, tm, rect, st, frames2, tm.Color, TimelineContext.MarkerWidth, TimelineContext.ComputeControlSeed(target, tm), totalFrames, out int _, out bool context2, out int _, out int _);
                    if (context2) AnimationPreviewDrawer.ShowReadOnlyContextMenu();
                }
                else
                {
                    EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.02f));
                    var c = GUI.color; GUI.color = new Color(1, 1, 1, 0.5f);
                    GUI.Label(rect, "〈No Frame Data〉", SirenixGUIStyles.MiniLabelCentered);
                    GUI.color = c;
                }
            }
        }

        private static int[] ExtractFrames(Array arrObj)
        {
            if (arrObj == null) return Array.Empty<int>();
            var list = new List<int>();
            for (int i = 0; i < arrObj.Length; i++)
            {
                var element = arrObj.GetValue(i);
                if (element == null) continue;
                int frame = GetFrameFromElement(element);
                if (frame >= 0) list.Add(frame);
            }
            return list.ToArray();
        }

        private static int GetFrameFromElement(object elem)
        {
            return elem switch
            {
                Quantum.HitFrame hf => hf.frame,
                Quantum.ProjectileFrame pf => pf.frame,
                Quantum.ChildActorFrame caf => caf.Frame,
                _ => -1
            };
        }

        private static void ApplyDraggedFrame(Array arrObj, int draggedIndex, int draggedFrame, TrackMember tm, UnityEngine.Object target)
        {
            int mapped = -1, seen = 0;
            for (int i = 0; i < arrObj.Length; i++)
            {
                var element = arrObj.GetValue(i);
                if (element == null) continue;
                int ef = GetFrameFromElement(element);
                if (ef >= 0)
                {
                    if (seen == draggedIndex) { mapped = i; break; }
                    seen++;
                }
            }

            if (mapped < 0) return;
            var elem = arrObj.GetValue(mapped);
            if (elem == null) return;

            bool applied = false;
            if (elem is Quantum.HitFrame hh)
            {
                hh.frame = draggedFrame;
                arrObj.SetValue(hh, mapped);
                applied = true;
            }
            else if (elem is Quantum.ProjectileFrame pp)
            {
                pp.frame = draggedFrame;
                arrObj.SetValue(pp, mapped);
                applied = true;
            }

            if (applied)
            {
                tm.Setter(target, arrObj);
                EditorUtility.SetDirty(target);
            }
        }

        private static int[] ReadFrameArrayLocal(UnityEngine.Object owner, MemberInfo member)
        {
            if (owner == null || member == null) return Array.Empty<int>();

            Func<object> getter = member is FieldInfo fi ? () => fi.GetValue(owner) : member is PropertyInfo pi && pi.CanRead ? () => pi.GetValue(owner, null) : null;
            var arrObj = getter?.Invoke() as Array;
            if (arrObj == null) return Array.Empty<int>();

            var list = new List<int>();
            for (int i = 0; i < arrObj.Length; i++)
            {
                var elem = arrObj.GetValue(i);
                if (elem == null) continue;
                var et = elem.GetType();
                var fiElem = et.GetField("frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                int ef = fiElem != null ? (int)fiElem.GetValue(elem) : -1;
                if (ef >= 0) list.Add(ef);
            }
            return list.ToArray();
        }
    }
}
#endif
