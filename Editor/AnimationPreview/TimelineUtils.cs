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

            // Typed-first fast paths (no reflection): handle known Quantum types directly
            try
            {
                // AttackActionData / ActiveActionIFrames / AffectWindow common int members
                if (instance is Quantum.ActiveActionIFrames iFrames)
                {
                    if (memberName.Equals("IntraActionStartFrame", StringComparison.OrdinalIgnoreCase)) { value = iFrames.IntraActionStartFrame; return true; }
                    if (memberName.Equals("IntraActionEndFrame", StringComparison.OrdinalIgnoreCase)) { value = iFrames.IntraActionEndFrame; return true; }
                }

                if (instance is Quantum.ActiveActionAffectWindow affectWindow)
                {
                    if (memberName.Equals("IntraActionStartFrame", StringComparison.OrdinalIgnoreCase)) { value = affectWindow.IntraActionStartFrame; return true; }
                    if (memberName.Equals("IntraActionEndFrame", StringComparison.OrdinalIgnoreCase)) { value = affectWindow.IntraActionEndFrame; return true; }
                }

                // If instance is a frame element or implements ICastFrame, try Frame/Frame property
                if (instance is Quantum.HitFrame hf)
                {
                    if (memberName.Equals("frame", StringComparison.OrdinalIgnoreCase) || memberName.Equals("Frame", StringComparison.OrdinalIgnoreCase)) { value = hf.frame; return true; }
                }
                if (instance is Quantum.ProjectileFrame pf)
                {
                    if (memberName.Equals("frame", StringComparison.OrdinalIgnoreCase) || memberName.Equals("Frame", StringComparison.OrdinalIgnoreCase)) { value = pf.frame; return true; }
                }
                if (instance is Quantum.ChildActorFrame caf)
                {
                    if (memberName.Equals("frame", StringComparison.OrdinalIgnoreCase) || memberName.Equals("Frame", StringComparison.OrdinalIgnoreCase)) { value = caf.Frame; return true; }
                }

                // If instance is AttackActionData or similar, handle common members
                if (instance is Quantum.ActiveActionData aad)
                {
                    if (memberName.Equals("MoveInterruptionLockEndFrame", StringComparison.OrdinalIgnoreCase)) { value = aad.MoveInterruptionLockEndFrame; return true; }
                    if (memberName.Equals("ActionMovementStartFrame", StringComparison.OrdinalIgnoreCase)) { value = aad.ActionMovementStartFrame; return true; }
                }
            }
            catch { /* fallthrough to reflection fallback */ }

            // Reflection fallback (existing logic)
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

            // Typed-first: handle some known writable members
            try
            {
                if (instance is Quantum.ActiveActionIFrames iFrames)
                {
                    if (memberName.Equals("IntraActionStartFrame", StringComparison.OrdinalIgnoreCase)) { iFrames.IntraActionStartFrame = newValue; return true; }
                    if (memberName.Equals("IntraActionEndFrame", StringComparison.OrdinalIgnoreCase)) { iFrames.IntraActionEndFrame = newValue; return true; }
                }

                if (instance is Quantum.ActiveActionAffectWindow affectWindow)
                {
                    if (memberName.Equals("IntraActionStartFrame", StringComparison.OrdinalIgnoreCase)) { affectWindow.IntraActionStartFrame = newValue; return true; }
                    if (memberName.Equals("IntraActionEndFrame", StringComparison.OrdinalIgnoreCase)) { affectWindow.IntraActionEndFrame = newValue; return true; }
                }
            }
            catch { /* fallthrough to reflection fallback */ }

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

            // Typed-first: if owner is a known Quantum asset with direct arrays, read them without reflection
            try
            {
                if (owner is Quantum.AttackActionData attack)
                {
                    if (attack.hitFrames != null)
                    {
                        var tmp = new System.Collections.Generic.List<int>(attack.hitFrames.Length);
                        foreach (var hf in attack.hitFrames) if (hf != null) tmp.Add(hf.frame);
                        return tmp.ToArray();
                    }

                    if (attack.projectileFrames != null)
                    {
                        var tmp = new System.Collections.Generic.List<int>(attack.projectileFrames.Length);
                        foreach (var pf in attack.projectileFrames) if (pf != null) tmp.Add(pf.frame);
                        return tmp.ToArray();
                    }
                }
            }
            catch { /* fallthrough to reflection/serialized fallback */ }

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

            // Typed-first: common frame container types
            try
            {
                if (instance is Quantum.HitFrame hf && (name.Equals("frame", StringComparison.OrdinalIgnoreCase) || name.Equals("Frame", StringComparison.OrdinalIgnoreCase))) { value = hf.frame; return true; }
                if (instance is Quantum.ProjectileFrame pf && (name.Equals("frame", StringComparison.OrdinalIgnoreCase) || name.Equals("Frame", StringComparison.OrdinalIgnoreCase))) { value = pf.frame; return true; }
                if (instance is Quantum.ChildActorFrame caf && (name.Equals("frame", StringComparison.OrdinalIgnoreCase) || name.Equals("Frame", StringComparison.OrdinalIgnoreCase))) { value = caf.Frame; return true; }
            }
            catch { /* fallthrough to reflection below */ }

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
