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
            // Determine preview scope for this draw (if available)
            string previewName = null;
            previewName = TimelineContext.GetPreviewNameForTarget(parentTarget);

            var members = GetTrackMembers(parentTarget, previewName);
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

        public static TrackMember[] GetTrackMembers(UnityEngine.Object target, string previewName = null)
        {
            if (target == null) return Array.Empty<TrackMember>();
            var type = target.GetType();
            // If no providers were registered yet (possible during domain reload ordering), ensure registration
            if (s_Providers.Count == 0)
            {
                AutoRegisterProviders();
            }

            if (!TimelineContext.TrackMembersCache.TryGetValue(type, out var cached))
            {
                // Use registered providers to build TrackMembers. Providers are responsible for
                // attribute-driven reflection or typed logic. If no provider handles the type, return empty.
                cached = BuildTrackMembersForType_TypedFirst(type) ?? Array.Empty<TrackMember>();
                TimelineContext.TrackMembersCache[type] = cached;
            }
            // If previewName filtering requested, return subset matching PreviewName or unscoped members
            if (!string.IsNullOrEmpty(previewName))
            {
                var list = new System.Collections.Generic.List<TrackMember>();
                foreach (var tm in cached)
                {
                    if (string.IsNullOrEmpty(tm.PreviewName) || string.Equals(tm.PreviewName, previewName, StringComparison.Ordinal))
                    {
                        list.Add(tm);
                    }
                }
                return list.ToArray();
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
                    if (!p.CanHandle(type)) continue;
                    var tmOpt = p.Build(member, animAttr);
                    if (tmOpt.HasValue)
                    {
                        var tm = tmOpt.Value;
                        result.Add(tm);
                        s_ProviderByMember[member] = p;
                        break; // move to next member once one provider handled it
                    }
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

            // Optional interface providers can implement to handle custom drawing for their TrackMembers
            public interface ICustomTrackDrawer
            {
                // Draw the given TrackMember into rect for the target object
                void Draw(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int totalFrames);
            }

        // Provider registry for open-closed extensibility. Providers should register themselves
        // via TrackRenderer.RegisterTrackProvider (e.g. Providers/*.cs files).
        private static readonly List<ITrackProvider> s_Providers = new List<ITrackProvider>();
    // Mapping from MemberInfo to the provider that created its TrackMember (populated during Build)
    private static readonly Dictionary<MemberInfo, ITrackProvider> s_ProviderByMember = new Dictionary<MemberInfo, ITrackProvider>();

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
            TimelineContext.TrackMembersCache.Clear();
            s_ProviderByMember.Clear();
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
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type[] types = asm.GetTypes();
                foreach (var t in types)
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(ITrackProvider).IsAssignableFrom(t)) continue;
                    // Skip the nested interface type itself
                    if (t == typeof(ITrackProvider)) continue;
                    // Skip anonymous / compiler-generated types
                    if (t.Name.StartsWith("<")) continue;

                    // Try create instance; if it fails, skip the provider (do not swallow general exceptions silently)
                    object instObj = Activator.CreateInstance(t);
                    if (instObj is ITrackProvider inst)
                    {
                        RegisterTrackProvider(inst);
                    }
                }
            }
        }

        // NOTE: The reflection-based BuildTrackMembersForType was removed so that attribute-driven
        // track creation is implemented by per-type providers in the Providers/ folder. This
        // keeps TrackRenderer free of reflection and allows each data type to own its Track logic.

    internal static void DrawSingleTrack(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int totalFrames)
        {
            // If a provider supplied this member, delegate drawing to the provider if it implements Draw
            if (tm.Member != null && s_ProviderByMember.TryGetValue(tm.Member, out var prov))
            {
                if (prov is ICustomTrackDrawer drawer)
                {
                    drawer.Draw(target, tm, rect, st, totalFrames);
                    return;
                }
            }

            // Minimal placeholder when no provider exists for this member: draw a subtle background
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.01f));
        }
    }
}
#endif
