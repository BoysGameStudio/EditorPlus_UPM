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
        // Provider will be auto-registered via TrackRenderer.AutoRegisterProviders
        // Provider-wide default color for frame-array members.
        private readonly Color DefaultColor = new Color(0.8f, 0.8f, 0.8f);

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
            ProviderUtils.ExtractAttributeData(animationEventAttributeInstance, ref label, ref colorHex, ref order);

            // Use provider-level default color (frame-array provider default is a neutral gray).
            var color = AnimationPreviewDrawer.ParseHexOrDefault(colorHex, DefaultColor);

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
                    var frames2 = ReadFrameArrayLocal(target, tm.Member);
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

            // Localized copy of ReadFrameArrayLocal moved from AnimationPreviewDrawer so the
            // frame-array provider owns its frame-extraction logic and doesn't depend on the drawer.
            private static int[] ReadFrameArrayLocal(UnityEngine.Object owner, MemberInfo member)
            {
                if (owner == null || member == null) return Array.Empty<int>();

                try
                {
                    Func<object> getter = null;
                    if (member is FieldInfo fi) getter = () => fi.GetValue(owner);
                    else if (member is PropertyInfo pi && pi.CanRead) getter = () => pi.GetValue(owner, null);

                    var arrObj = getter != null ? getter() as Array : null;
                    if (arrObj != null)
                    {
                        var list = new System.Collections.Generic.List<int>(arrObj.Length);
                        for (int i = 0; i < arrObj.Length; i++)
                        {
                            var elem = arrObj.GetValue(i);
                            if (elem == null) continue;
                            // Try reflection: look for 'frame' field or property
                            int ef = -1;
                            try
                            {
                                var et = elem.GetType();
                                var fiElem = et.GetField("frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                                if (fiElem != null)
                                {
                                    var v = fiElem.GetValue(elem);
                                    if (v is int iv) ef = iv;
                                }
                                else
                                {
                                    var piElem = et.GetProperty("frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                                    if (piElem != null && piElem.CanRead)
                                    {
                                        var v = piElem.GetValue(elem, null);
                                        if (v is int iv2) ef = iv2;
                                    }
                                }
                            }
                            catch { }

                            if (ef >= 0) list.Add(ef);
                        }
                        return list.ToArray();
                    }
                }
                catch { }

                // SerializedProperty fallback
                try
                {
                    var so = new SerializedObject(owner);
                    var prop = so.FindProperty(member.Name);
                    if (prop != null && prop.isArray && prop.arraySize > 0)
                    {
                        var tmp = new System.Collections.Generic.List<int>();
                        for (int i = 0; i < prop.arraySize; i++)
                        {
                            var elem = prop.GetArrayElementAtIndex(i);
                            if (elem == null) continue;
                            var frameProp = elem.FindPropertyRelative("frame");
                            if (frameProp != null) tmp.Add(frameProp.intValue);
                        }
                        return tmp.ToArray();
                    }
                }
                catch { }

                return Array.Empty<int>();
            }
    }
}

#endif
