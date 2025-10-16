#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Sirenix.Utilities.Editor;

namespace EditorPlus.AnimationPreview
{
    // Provider for members that are arrays of frame-like POCOs (HitFrame[], ProjectileFrame[], ChildActorFrame[], etc.)
    internal class FrameArrayTrackProvider : TrackRenderer.ITrackProvider, TrackRenderer.ICustomTrackDrawer
    {
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
                if (vt == null) continue;
                if (vt.IsArray) return true;
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
            try
            {
                var atype = animationEventAttributeInstance.GetType();
                var pLabel = atype.GetProperty("Label"); if (pLabel != null) label = (pLabel.GetValue(animationEventAttributeInstance) as string) ?? label;
                var pColor = atype.GetProperty("ColorHex"); if (pColor != null) colorHex = pColor.GetValue(animationEventAttributeInstance) as string;
                var pOrder = atype.GetProperty("Order"); if (pOrder != null) order = (int)(pOrder.GetValue(animationEventAttributeInstance) ?? 0);
            }
            catch { }

            // Compute provider-local default color (moved from AnimationPreviewDrawer.DefaultColorFor)
            Color defaultColor;
            if (valueType == typeof(int)) defaultColor = new Color(0.98f, 0.62f, 0.23f);
            else if (valueType == typeof(int[])) defaultColor = new Color(0.39f, 0.75f, 0.96f);
            else if (AnimationPreviewDrawer.HasAffectWindowPattern(valueType)) defaultColor = new Color(0.5f, 0.9f, 0.5f);
            else defaultColor = new Color(0.8f, 0.8f, 0.8f);

            var color = AnimationPreviewDrawer.ParseHexOrDefault(colorHex, defaultColor);

            var trackMember = new TrackMember
            {
                Member = member,
                Label = label,
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
            // Reuse AnimationPreviewDrawer helpers to read frames and draw markers for POCO arrays
            var arrObj = tm.Getter?.Invoke(target) as Array;
            int[] frames = Array.Empty<int>();
            object[] elements = null;
            if (arrObj != null)
            {
                var list = new List<int>(arrObj.Length);
                elements = new object[arrObj.Length];
                for (int i = 0; i < arrObj.Length; i++)
                {
                    var elem = arrObj.GetValue(i);
                    elements[i] = elem;
                    if (elem == null) { list.Add(-1); continue; }
                    int elemFrame = -1;
                    try
                    {
                        if (elem is Quantum.HitFrame hf) elemFrame = hf.frame;
                        else if (elem is Quantum.ProjectileFrame pf) elemFrame = pf.frame;
                        else if (elem is Quantum.ChildActorFrame caf) elemFrame = caf.Frame;
                        else elemFrame = -1;
                    }
                    catch { elemFrame = -1; }
                    if (elemFrame >= 0) list.Add(elemFrame); else list.Add(-1);
                }

                var tmp = new List<int>(list.Count);
                for (int i = 0; i < list.Count; i++) if (list[i] >= 0) tmp.Add(list[i]);
                frames = tmp.ToArray();
            }

            if (frames != null && frames.Length > 0)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    try
                    {
                        var dbgRect = new Rect(rect.x + 4, rect.y + 2, 200, 16);
                        GUIStyle s = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white } };
                        GUI.Label(dbgRect, $"Elements: { (arrObj != null ? arrObj.Length : 0) }  FramesDrawn: {frames.Length}", s);
                    }
                    catch { }
                }

                AnimationPreviewDrawer.DrawMarkers(target, tm, rect, st, frames, tm.Color, TimelineContext.MarkerWidth, TimelineContext.ComputeControlSeed(target, tm), totalFrames, out int clickedIndex, out bool context, out int draggedIndex, out int draggedFrame);

                if (draggedIndex >= 0 && draggedFrame >= 0 && arrObj != null && tm.Setter != null)
                {
                    int mapped = -1; int seen = 0;
                    for (int i = 0; i < arrObj.Length; i++)
                    {
                        var elem = arrObj.GetValue(i);
                        if (elem == null) continue;
                        int ef = -1;
                        try
                        {
                            if (elem is Quantum.HitFrame hf2) ef = hf2.frame;
                            else if (elem is Quantum.ProjectileFrame pf2) ef = pf2.frame;
                            else if (elem is Quantum.ChildActorFrame caf2) ef = caf2.Frame;
                            else ef = -1;
                        }
                        catch { ef = -1; }
                        if (ef >= 0)
                        {
                            if (seen == draggedIndex) { mapped = i; break; }
                            seen++;
                        }
                    }

                    if (mapped >= 0)
                    {
                        var elem = arrObj.GetValue(mapped);
                        if (elem != null)
                        {
                            bool applied = false;
                            try
                            {
                                if (elem is Quantum.HitFrame hh)
                                {
                                    hh.frame = draggedFrame;
                                    try { arrObj.SetValue(hh, mapped); } catch { }
                                    applied = true;
                                }
                                else if (elem is Quantum.ProjectileFrame pp)
                                {
                                    pp.frame = draggedFrame;
                                    try { arrObj.SetValue(pp, mapped); } catch { }
                                    applied = true;
                                }
                                else if (elem is Quantum.ChildActorFrame)
                                {
                                    applied = false;
                                }
                            }
                            catch { applied = false; }

                            if (applied)
                            {
                                try { tm.Setter(target, arrObj); EditorUtility.SetDirty(target); } catch { }
                            }
                        }
                    }
                }

                if (context)
                {
                    AnimationPreviewDrawer.ShowReadOnlyContextMenu();
                }
            }
            else
            {
                // Try serialized fallback
                bool drewFromSerialized = false;
                try
                {
                    var frames2 = AnimationPreviewDrawer.ReadFrameArrayLocal(target, tm.Member);
                    if (frames2 != null && frames2.Length > 0)
                    {
                        AnimationPreviewDrawer.DrawMarkers(target, tm, rect, st, frames2, tm.Color, TimelineContext.MarkerWidth, TimelineContext.ComputeControlSeed(target, tm), totalFrames, out int clickedIndex2, out bool context2, out int draggedIndex2, out int draggedFrame2);
                        if (context2) AnimationPreviewDrawer.ShowReadOnlyContextMenu();
                        drewFromSerialized = true;
                    }
                }
                catch { }

                if (!drewFromSerialized)
                {
                    EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.02f));
                    var c = GUI.color; GUI.color = new Color(1, 1, 1, 0.5f);
                    GUI.Label(rect, "〈No Frame Data〉", SirenixGUIStyles.MiniLabelCentered);
                    GUI.color = c;
                }
            }
        }
    }
}

#endif
