#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Quantum;
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
                // Prefer typed descriptors for known Quantum types to avoid reflection into simulation types.
                cached = BuildTrackMembersForType_TypedFirst(type) ?? BuildTrackMembersForType(type);
                TimelineContext.TrackMembersCache[type] = cached;
            }
            return cached;
        }

        // Try to build track members using explicit typed descriptors for known Quantum/Simulation types.
        // Returns null if no typed descriptor is available for the requested type.
        private static TrackMember[] BuildTrackMembersForType_TypedFirst(Type type)
        {
            if (type == null) return null;

            // ActiveActionData explicit mapping
            if (type == typeof(ActiveActionData) || typeof(ActiveActionData).IsAssignableFrom(type))
            {
                // Build explicit TrackMember array matching the AnimationEventAttribute annotations in source.
                var list = new List<TrackMember>();

                // MoveInterruptionLockEndFrame -> int MoveInterruptionLockEndFrame
                list.Add(new TrackMember
                {
                    Member = null,
                    Label = "Move Lock End",
                    ValueType = typeof(int),
                    Color = AnimationPreviewDrawer.ParseHexOrDefault("#4BA5E8", AnimationPreviewDrawer.DefaultColorFor(typeof(int))),
                    Getter = owner => {
                        var a = owner as UnityEngine.Object;
                        // Owner is expected to be a UnityEngine.Object wrapper for ActiveActionData serialized asset
                        // Use serialized property fallback in the editor drawer if instance access fails.
                        var asObj = owner as UnityEngine.Object;
                        if (asObj == null) return 0;
                        var so = new SerializedObject(asObj);
                        var prop = so.FindProperty("MoveInterruptionLockEndFrame");
                        if (prop != null) return prop.intValue;
                        return 0;
                    },
                    Setter = (owner, value) => {
                        var asObj = owner as UnityEngine.Object;
                        if (asObj == null) return;
                        var so = new SerializedObject(asObj);
                        var prop = so.FindProperty("MoveInterruptionLockEndFrame");
                        if (prop != null) { prop.intValue = (int)value; so.ApplyModifiedProperties(); }
                    },
                    Order = -2
                });

                // ActionMovementStartFrame
                list.Add(new TrackMember
                {
                    Member = null,
                    Label = "Action Move Start Frame",
                    ValueType = typeof(int),
                    Color = AnimationPreviewDrawer.ParseHexOrDefault("#4BA5E8", AnimationPreviewDrawer.DefaultColorFor(typeof(int))),
                    Getter = owner => {
                        var asObj = owner as UnityEngine.Object;
                        if (asObj == null) return 0;
                        var so = new SerializedObject(asObj);
                        var prop = so.FindProperty("ActionMovementStartFrame");
                        if (prop != null) return prop.intValue;
                        return 0;
                    },
                    Setter = (owner, value) => {
                        var asObj = owner as UnityEngine.Object;
                        if (asObj == null) return;
                        var so = new SerializedObject(asObj);
                        var prop = so.FindProperty("ActionMovementStartFrame");
                        if (prop != null) { prop.intValue = (int)value; so.ApplyModifiedProperties(); }
                    },
                    Order = 0
                });

                // NonHitLockableFrames (int[])
                list.Add(new TrackMember
                {
                    Member = null,
                    Label = "Non-Hit Lock Window",
                    ValueType = typeof(int[]),
                    Color = AnimationPreviewDrawer.ParseHexOrDefault("#CB7AF3", AnimationPreviewDrawer.DefaultColorFor(typeof(int[]))),
                    Getter = owner => {
                        var asObj = owner as UnityEngine.Object;
                        if (asObj == null) return Array.Empty<int>();
                        var so = new SerializedObject(asObj);
                        var prop = so.FindProperty("NonHitLockableFrames");
                        if (prop != null && prop.isArray)
                        {
                            var arr = new List<int>();
                            for (int i = 0; i < prop.arraySize; i++) arr.Add(prop.GetArrayElementAtIndex(i).intValue);
                            return arr.ToArray();
                        }
                        return Array.Empty<int>();
                    },
                    Setter = (owner, value) => {
                        var asObj = owner as UnityEngine.Object;
                        if (asObj == null) return;
                        var so = new SerializedObject(asObj);
                        var prop = so.FindProperty("NonHitLockableFrames");
                        if (prop != null && prop.isArray)
                        {
                            var arr = value as int[] ?? Array.Empty<int>();
                            prop.arraySize = arr.Length;
                            for (int i = 0; i < arr.Length; i++) prop.GetArrayElementAtIndex(i).intValue = arr[i];
                            so.ApplyModifiedProperties();
                        }
                    },
                    Order = 1
                });

                // IFrames (ActiveActionIFrames) -> treat as affect window
                list.Add(new TrackMember
                {
                    Member = null,
                    Label = "I-Frames",
                    ValueType = typeof(ActiveActionIFrames),
                    Color = AnimationPreviewDrawer.ParseHexOrDefault("#82E0AA", AnimationPreviewDrawer.DefaultColorFor(typeof(ActiveActionIFrames))),
                    Getter = owner => {
                        var asObj = owner as UnityEngine.Object;
                        if (asObj == null) return null;
                        var so = new SerializedObject(asObj);
                        var prop = so.FindProperty("IFrames");
                        if (prop != null) return prop; // pass SerializedProperty for drawer's CreateAffectWindowBinding support
                        return null;
                    },
                    Setter = null,
                    Order = 2
                });

                // AffectWindow
                list.Add(new TrackMember
                {
                    Member = null,
                    Label = "Affect Window",
                    ValueType = typeof(ActiveActionAffectWindow),
                    Color = AnimationPreviewDrawer.ParseHexOrDefault("#F5B041", AnimationPreviewDrawer.DefaultColorFor(typeof(ActiveActionAffectWindow))),
                    Getter = owner => {
                        var asObj = owner as UnityEngine.Object;
                        if (asObj == null) return null;
                        var so = new SerializedObject(asObj);
                        var prop = so.FindProperty("AffectWindow");
                        if (prop != null) return prop;
                        return null;
                    },
                    Setter = null,
                    Order = 3
                });

                return list.ToArray();
            }

            // PlayerDashActionData: Dash End Frame
            if (type == typeof(PlayerDashActionData) || typeof(PlayerDashActionData).IsAssignableFrom(type))
            {
                var member = new TrackMember
                {
                    Member = null,
                    Label = "Dash End Frame",
                    ValueType = typeof(int),
                    Color = AnimationPreviewDrawer.ParseHexOrDefault("#FF8C5A", AnimationPreviewDrawer.DefaultColorFor(typeof(int))),
                    Getter = owner => {
                        var asObj = owner as UnityEngine.Object;
                        if (asObj == null) return 0;
                        var so = new SerializedObject(asObj);
                        var prop = so.FindProperty("DashEndFrame") ?? so.FindProperty("DashEndFrame");
                        if (prop != null) return prop.intValue;
                        // fallback to common name 'DashEndFrame' or 'Dash End Frame' is handled by attribute label
                        return 0;
                    },
                    Setter = (owner, value) => {
                        var asObj = owner as UnityEngine.Object;
                        if (asObj == null) return;
                        var so = new SerializedObject(asObj);
                        var prop = so.FindProperty("DashEndFrame");
                        if (prop != null) { prop.intValue = (int)value; so.ApplyModifiedProperties(); }
                    },
                    Order = -1
                };
                return new[] { member };
            }

            // AttackActionData typed mapping (no string-based type/member checks)
            if (type == typeof(AttackActionData) || typeof(AttackActionData).IsAssignableFrom(type))
            {
                var list = new List<TrackMember>();

                list.Add(new TrackMember
                {
                    Member = null,
                    Label = "Hit Frames",
                    ValueType = typeof(HitFrame[]),
                    Color = AnimationPreviewDrawer.ParseHexOrDefault("#FF5555", AnimationPreviewDrawer.DefaultColorFor(typeof(HitFrame[]))),
                    Getter = owner => {
                        if (owner is AttackActionData attack) return attack.hitFrames;
                        // also accept UnityEngine.Object that wraps a ScriptableObject instance
                        if (owner is UnityEngine.Object uo)
                        {
                            var inst = uo as AttackActionData;
                            if (inst != null) return inst.hitFrames;
                        }
                        return null;
                    },
                    Setter = null,
                    Order = 100
                });

                list.Add(new TrackMember
                {
                    Member = null,
                    Label = "Projectile Frames",
                    ValueType = typeof(ProjectileFrame[]),
                    Color = AnimationPreviewDrawer.ParseHexOrDefault("#FFAA55", AnimationPreviewDrawer.DefaultColorFor(typeof(ProjectileFrame[]))),
                    Getter = owner => {
                        if (owner is AttackActionData attack) return attack.projectileFrames;
                        if (owner is UnityEngine.Object uo)
                        {
                            var inst = uo as AttackActionData;
                            if (inst != null) return inst.projectileFrames;
                        }
                        return null;
                    },
                    Setter = null,
                    Order = 101
                });

                list.Add(new TrackMember
                {
                    Member = null,
                    Label = "Child Actor Frames",
                    ValueType = typeof(ChildActorFrame[]),
                    Color = AnimationPreviewDrawer.ParseHexOrDefault("#FF77CC", AnimationPreviewDrawer.DefaultColorFor(typeof(ChildActorFrame[]))),
                    Getter = owner => {
                        if (owner is AttackActionData attack) return attack.childActorFrames;
                        if (owner is UnityEngine.Object uo)
                        {
                            var inst = uo as AttackActionData;
                            if (inst != null) return inst.childActorFrames;
                        }
                        return null;
                    },
                    Setter = null,
                    Order = 102
                });

                return list.ToArray();
            }

            return null;
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
