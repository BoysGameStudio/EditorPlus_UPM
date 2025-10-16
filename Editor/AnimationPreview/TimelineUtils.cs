#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    internal static class TimelineUtils
    {
        public static bool TryGetIntMember(object instance, string memberName, out int value)
        {
            value = 0;
            if (instance == null || string.IsNullOrEmpty(memberName)) return false;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            try
            {
                var prop = instance.GetType().GetProperty(memberName, flags, null, typeof(int), Type.EmptyTypes, null);
                if (prop != null && prop.CanRead)
                {
                    var v = prop.GetValue(instance, null);
                    if (v is int vi) { value = vi; return true; }
                    if (v != null) { value = Convert.ToInt32(v); return true; }
                }

                var field = instance.GetType().GetField(memberName, flags);
                if (field != null)
                {
                    var v = field.GetValue(instance);
                    if (v is int fi) { value = fi; return true; }
                    if (v != null) { value = Convert.ToInt32(v); return true; }
                }

                // try common backing field patterns
                string camel = char.ToLowerInvariant(memberName[0]) + memberName.Substring(1);
                field = instance.GetType().GetField(camel, flags);
                if (field != null && field.FieldType == typeof(int)) { value = (int)field.GetValue(instance); return true; }
                field = instance.GetType().GetField("_" + camel, flags);
                if (field != null && field.FieldType == typeof(int)) { value = (int)field.GetValue(instance); return true; }
                field = instance.GetType().GetField("m_" + memberName, flags);
                if (field != null && field.FieldType == typeof(int)) { value = (int)field.GetValue(instance); return true; }
            }
            catch { }
            return false;
        }

        public static bool TrySetIntMember(object instance, string memberName, int newValue)
        {
            if (instance == null || string.IsNullOrEmpty(memberName)) return false;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            try
            {
                var prop = instance.GetType().GetProperty(memberName, flags, null, typeof(int), Type.EmptyTypes, null);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(instance, newValue, null);
                    return true;
                }

                var field = instance.GetType().GetField(memberName, flags);
                if (field != null && field.FieldType == typeof(int))
                {
                    field.SetValue(instance, newValue);
                    return true;
                }

                string camel = char.ToLowerInvariant(memberName[0]) + memberName.Substring(1);
                field = instance.GetType().GetField(camel, flags);
                if (field != null && field.FieldType == typeof(int)) { field.SetValue(instance, newValue); return true; }
                field = instance.GetType().GetField("_" + camel, flags);
                if (field != null && field.FieldType == typeof(int)) { field.SetValue(instance, newValue); return true; }
                field = instance.GetType().GetField("m_" + memberName, flags);
                if (field != null && field.FieldType == typeof(int)) { field.SetValue(instance, newValue); return true; }
            }
            catch { }
            return false;
        }

        // Read an int[] of frame indices from an arbitrary member (via getter or SerializedProperty fallback)
        public static int[] ReadFrameArray(UnityEngine.Object owner, MemberInfo member)
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
                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    for (int i = 0; i < arrObj.Length; i++)
                    {
                        var elem = arrObj.GetValue(i);
                        if (elem == null) { list.Add(-1); continue; }
                        int ef = -1;
                        if (TryGetIntFieldOrProp(elem, "frame", out ef)) list.Add(ef); else list.Add(-1);
                    }
                    var tmp = new System.Collections.Generic.List<int>();
                    for (int i = 0; i < list.Count; i++) if (list[i] >= 0) tmp.Add(list[i]);
                    return tmp.ToArray();
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

        private static bool TryGetIntFieldOrProp(object instance, string name, out int value)
        {
            value = -1;
            if (instance == null) return false;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            try
            {
                var f = instance.GetType().GetField(name, flags);
                if (f != null)
                {
                    var v = f.GetValue(instance);
                    if (v == null) return false;
                    value = Convert.ToInt32(v);
                    return true;
                }
                var p = instance.GetType().GetProperty(name, flags);
                if (p != null)
                {
                    var v = p.GetValue(instance, null);
                    if (v == null) return false;
                    value = Convert.ToInt32(v);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}

#endif
