#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
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
            // If no providers were registered yet (possible during domain reload ordering), ensure registration
            if (s_Providers.Count == 0)
            {
                try { AutoRegisterProviders(); } catch { }
            }

            if (!TimelineContext.TrackMembersCache.TryGetValue(type, out var cached))
            {
                // Use registered providers to build TrackMembers. Providers are responsible for
                // attribute-driven reflection or typed logic. If no provider handles the type, return empty.
                var beforeProviders = s_Providers.Count;
                try { UnityEngine.Debug.Log($"[TrackRenderer] Building TrackMembers for type {type.Name}, providers={beforeProviders}"); } catch { }
                cached = BuildTrackMembersForType_TypedFirst(type) ?? Array.Empty<TrackMember>();
                try { UnityEngine.Debug.Log($"[TrackRenderer] Built {cached.Length} TrackMembers for {type.Name}"); } catch { }
                TimelineContext.TrackMembersCache[type] = cached;
            }
            return cached;
        }

        // Try to build track members using registered providers.
        // For each member that has an AnimationEventAttribute, call providers in order and accept the
        // first TrackMember produced. Returns null if no members were produced.
        private static TrackMember[] BuildTrackMembersForType_TypedFirst(Type type)
        {
            if (type == null) return null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var result = new List<TrackMember>();

            foreach (var member in type.GetMembers(flags))
            {
                // find any attribute instance named AnimationEventAttribute (assembly mismatches may exist)
                var attrs = member.GetCustomAttributes(false);
                object animAttr = null;
                foreach (var a in attrs) if (a != null && a.GetType().Name == "AnimationEventAttribute") { animAttr = a; break; }
                if (animAttr == null) continue;

                // For this attributed member, ask each provider to build a single TrackMember
                foreach (var p in s_Providers)
                {
                    try
                    {
                        if (!p.CanHandle(type)) continue;
                        var tmOpt = p.Build(member, animAttr);
                        if (tmOpt.HasValue)
                        {
                            result.Add(tmOpt.Value);
                            break; // move to next member once one provider handled it
                        }
                    }
                    catch { }
                }
            }

            if (result.Count == 0) return null;
            result.Sort((a, b) => a.Order.CompareTo(b.Order));
            return result.ToArray();
        }


        // Public provider interface so external editor extensions can register track providers.
        public interface ITrackProvider
        {
            bool CanHandle(Type t);
            // Build a TrackMember for a single member (or return null if this provider doesn't handle it)
            TrackMember? Build(System.Reflection.MemberInfo member, object animationEventAttributeInstance);
        }

        // Provider registry for open-closed extensibility. Providers should register themselves
        // via TrackRenderer.RegisterTrackProvider (e.g. Providers/*.cs files).
        private static readonly List<ITrackProvider> s_Providers = new List<ITrackProvider>();

        /// <summary>
        /// Register a custom track provider. Newly registered providers take precedence over built-in providers.
        /// </summary>
        public static void RegisterTrackProvider(ITrackProvider provider)
        {
            if (provider == null) return;
            // Avoid duplicate provider registration by concrete type
            var pt = provider.GetType();
            if (s_Providers.Any(p => p.GetType() == pt)) return;
            s_Providers.Insert(0, provider);
            // Invalidate cached TrackMember lists so newly-registered providers can take effect
            try { TimelineContext.TrackMembersCache.Clear(); } catch { }
        }

        /// <summary>
        /// Returns a snapshot of currently-registered providers (for diagnostics).
        /// </summary>
        public static ITrackProvider[] GetRegisteredProviders()
        {
            return s_Providers.ToArray();
        }

        /// <summary>
        /// Ensure providers are registered now (invokes auto-registration path).
        /// </summary>
        public static void EnsureProvidersRegistered()
        {
            if (s_Providers.Count == 0) AutoRegisterProviders();
        }

        // Ensure providers are registered at editor load (static constructors in provider classes may not run).
        [InitializeOnLoadMethod]
        private static void AutoRegisterProviders()
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assemblies)
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }
                    foreach (var t in types)
                    {
                        if (t.IsAbstract || t.IsInterface) continue;
                        if (!typeof(ITrackProvider).IsAssignableFrom(t)) continue;
                        // Skip the nested interface type itself
                        if (t == typeof(ITrackProvider)) continue;
                        // Skip anonymous / compiler-generated types
                        if (t.Name.StartsWith("<")) continue;

                        try
                        {
                            var inst = Activator.CreateInstance(t) as ITrackProvider;
                            if (inst != null) RegisterTrackProvider(inst);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // Log provider count after a short delay so Domain Reload messages don't swallow it
            try
            {
                UnityEditor.EditorApplication.delayCall += () => {
                    try { UnityEngine.Debug.Log($"[TrackRenderer] Providers registered: {s_Providers.Count}"); } catch { }
                };
            }
            catch { }
        }

        // NOTE: The reflection-based BuildTrackMembersForType was removed so that attribute-driven
        // track creation is implemented by per-type providers in the Providers/ folder. This
        // keeps TrackRenderer free of reflection and allows each data type to own its Track logic.

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
