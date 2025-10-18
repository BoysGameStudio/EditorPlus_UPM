#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Sirenix.Utilities.Editor;

namespace EditorPlus.AnimationPreview
{
    /// <summary>
    /// Provider that builds TrackMember entries for members whose type implements IActionAffectWindow.
    /// </summary>
    internal class IActionAffectWindowTrackProvider : TrackRenderer.ITrackProvider, EditorPlus.AnimationPreview.TrackRenderer.ICustomTrackDrawer
    {
        // Provider-wide default color for IActionAffectWindow-like members.
        private readonly Color DefaultColor = new Color(0.5f, 0.9f, 0.5f);
        // Provider will be auto-registered via TrackRenderer.AutoRegisterProviders

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

            UnityEngine.Debug.Log($"[IActionAffectWindowTrackProvider] Found attributed member: {member.DeclaringType?.Name}.{member.Name} (memberType={member.MemberType})");

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
            string previewName = null;
            ProviderUtils.ExtractAttributeData(animationEventAttributeInstance, ref label, ref colorHex, ref order, ref previewName);

            // Use provider-level default color. The provider targets IActionAffectWindow-like types
            // so the class DefaultColor is a reasonable default. Attribute hex may override it.
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
            // Affect window drawing (same as previous central logic)
            var windowInstance = tm.Getter?.Invoke(target);
            if (windowInstance != null)
            {
                var binding = CreateAffectWindowBinding(target, tm, windowInstance, totalFrames);
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

        // Provider-owned helpers for affect-window binding and apply logic. These replace the previous
        // animation-drawer-level reflection helpers so the provider can manage its own typed behavior.
        private static WindowBinding CreateAffectWindowBinding(UnityEngine.Object target, TrackMember tm, object window, int totalFrames)
        {
            // Prefer the strongly-typed interface when available
            int rawStart = 0;
            int rawEnd = 0;
            if (window is IActionAffectWindow iaw)
            {
                rawStart = iaw.IntraActionStartFrame;
                rawEnd = iaw.IntraActionEndFrame;
            }

            int start = Mathf.Clamp(rawStart, 0, totalFrames);
            int end = Mathf.Clamp(rawEnd, 0, totalFrames);

            return new WindowBinding(
                start,
                end,
                tm.Color,
                string.Empty,
                (owner, newStart, newEnd) => ApplyAffectWindowChanges(owner, tm, window, newStart, newEnd));
        }

        private static bool ApplyAffectWindowChanges(UnityEngine.Object target, TrackMember tm, object windowInstance, int newStart, int newEnd)
        {
            if (windowInstance == null)
            {
                return false;
            }

            bool anyUpdated = false;

            // SerializedProperty case (asset/serialized fallback)
            if (windowInstance is SerializedProperty sp)
            {
                var pStart = sp.FindPropertyRelative("IntraActionStartFrame");
                var pEnd = sp.FindPropertyRelative("IntraActionEndFrame");
                if (pStart != null && pStart.intValue != newStart) { pStart.intValue = newStart; anyUpdated = true; }
                if (pEnd != null && pEnd.intValue != newEnd) { pEnd.intValue = newEnd; anyUpdated = true; }
                if (anyUpdated) sp.serializedObject.ApplyModifiedProperties();
            }
            else
            {
                // Try common concrete types which expose writable fields using pattern matching.
                switch (windowInstance)
                {
                    case Quantum.ActiveActionAffectWindow aa:
                        if (aa.IntraActionStartFrame != newStart) { aa.IntraActionStartFrame = newStart; anyUpdated = true; }
                        if (aa.IntraActionEndFrame != newEnd) { aa.IntraActionEndFrame = newEnd; anyUpdated = true; }
                        break;

                    case Quantum.ActiveActionIFrames ai:
                        if (ai.IntraActionStartFrame != newStart) { ai.IntraActionStartFrame = newStart; anyUpdated = true; }
                        if (ai.IntraActionEndFrame != newEnd) { ai.IntraActionEndFrame = newEnd; anyUpdated = true; }
                        break;

                    default:
                        // If the object implements the interface but lacks writable members accessible here,
                        // we'll rely on reapplying via tm.Setter below (for value-types or custom hosts).
                        break;
                }
            }

            if (anyUpdated)
            {
                var type = windowInstance.GetType();
                if (type.IsValueType)
                {
                    if (tm.Setter == null)
                    {
                        return false;
                    }

                    tm.Setter(target, windowInstance);
                }
                return true;
            }

            if (tm.Setter != null)
            {
                // Reapply the existing instance via setter in case the host requires it
                tm.Setter(target, windowInstance);
                return true;
            }

            return false;
        }

        // TrySetIntMember/TryGetIntMember removed: providers should prefer interface access and
        // known concrete types; serialized fallback is still handled above where required.
    }
}

#endif
