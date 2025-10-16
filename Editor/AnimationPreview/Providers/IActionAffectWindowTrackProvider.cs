#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    /// <summary>
    /// Provider that builds TrackMember entries for members whose type implements IActionAffectWindow.
    /// </summary>
    internal class IActionAffectWindowTrackProvider : TrackRenderer.ITrackProvider
    {
        static IActionAffectWindowTrackProvider()
        {
            // Register so TrackRenderer will pick up this provider automatically
            TrackRenderer.RegisterTrackProvider(new IActionAffectWindowTrackProvider());
        }

        public bool CanHandle(Type t)
        {
            if (t == null) return false;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var m in t.GetMembers(flags))
            {
                var attrs = m.GetCustomAttributes(false);
                object attrObj = null;
                foreach (var a in attrs) if (a != null && a.GetType().Name == "AnimationEventAttribute") { attrObj = a; break; }
                if (attrObj == null) continue;
                Type vt = null;
                if (m is FieldInfo f) vt = f.FieldType;
                else if (m is PropertyInfo p) vt = p.PropertyType;
                if (vt == null) continue;
                // Accept members that implement IActionAffectWindow
                if (typeof(IActionAffectWindow).IsAssignableFrom(vt)) return true;
            }
            return false;
        }

        public TrackMember? Build(MemberInfo member, object animationEventAttributeInstance)
        {
            if (member == null) return null;

            try { UnityEngine.Debug.Log($"[IActionAffectWindowTrackProvider] Found attributed member: {member.DeclaringType?.Name}.{member.Name} (memberType={member.MemberType})"); } catch { }

            Type valueType = null;
            Func<UnityEngine.Object, object> getter = null;
            Action<UnityEngine.Object, object> setter = null;

            if (member is FieldInfo field && typeof(IActionAffectWindow).IsAssignableFrom(field.FieldType))
            {
                valueType = field.FieldType;
                getter = owner => field.GetValue(owner);
                if (!field.IsInitOnly) setter = (owner, value) => field.SetValue(owner, value);
            }
            else if (member is PropertyInfo property && typeof(IActionAffectWindow).IsAssignableFrom(property.PropertyType))
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

            var color = AnimationPreviewDrawer.ParseHexOrDefault(colorHex, AnimationPreviewDrawer.DefaultColorFor(valueType));
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
    }
}

#endif
