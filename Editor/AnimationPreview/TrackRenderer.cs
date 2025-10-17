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
            string previewName = TimelineContext.GetPreviewNameForTarget(parentTarget);
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

            if (s_Providers.Count == 0)
            {
                AutoRegisterProviders();
            }

            if (!TimelineContext.TrackMembersCache.TryGetValue(type, out var cached))
            {
                cached = BuildTrackMembersForType(type) ?? Array.Empty<TrackMember>();
                TimelineContext.TrackMembersCache[type] = cached;
            }

            if (!string.IsNullOrEmpty(previewName))
            {
                var list = new List<TrackMember>();
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

        private static TrackMember[] BuildTrackMembersForType(Type type)
        {
            if (type == null) return null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var result = new List<TrackMember>();

            foreach (var member in type.GetMembers(flags))
            {
                var attrs = member.GetCustomAttributes(false);
                var animAttr = attrs.FirstOrDefault(a => a.GetType().Name == "AnimationEventAttribute");
                if (animAttr == null) continue;

                foreach (var p in s_Providers)
                {
                    if (!p.CanHandle(type)) continue;
                    var tmOpt = p.Build(member, animAttr);
                    if (tmOpt.HasValue)
                    {
                        var tm = tmOpt.Value;
                        result.Add(tm);
                        s_ProviderByMember[member] = p;
                        break;
                    }
                }
            }

            if (result.Count == 0) return null;
            result.Sort((a, b) => a.Order.CompareTo(b.Order));
            return result.ToArray();
        }

        public interface ITrackProvider
        {
            bool CanHandle(Type t);
            TrackMember? Build(MemberInfo member, object animationEventAttributeInstance);
        }

        public interface ICustomTrackDrawer
        {
            void Draw(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int totalFrames);
        }

        private static readonly List<ITrackProvider> s_Providers = new List<ITrackProvider>();
        private static readonly Dictionary<MemberInfo, ITrackProvider> s_ProviderByMember = new Dictionary<MemberInfo, ITrackProvider>();

        public static void RegisterTrackProvider(ITrackProvider provider)
        {
            if (provider == null) return;
            var pt = provider.GetType();
            if (s_Providers.Any(p => p.GetType() == pt)) return;
            s_Providers.Insert(0, provider);
            TimelineContext.TrackMembersCache.Clear();
            s_ProviderByMember.Clear();
        }

        public static ITrackProvider[] GetRegisteredProviders()
        {
            return s_Providers.ToArray();
        }

        public static void EnsureProvidersRegistered()
        {
            if (s_Providers.Count == 0) AutoRegisterProviders();
        }

        [InitializeOnLoadMethod]
        private static void AutoRegisterProviders()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t.IsAbstract || t.IsInterface || t == typeof(ITrackProvider) || t.Name.StartsWith("<")) continue;
                    if (!typeof(ITrackProvider).IsAssignableFrom(t)) continue;

                    var inst = Activator.CreateInstance(t) as ITrackProvider;
                    if (inst != null) RegisterTrackProvider(inst);
                }
            }
        }

        internal static void DrawSingleTrack(UnityEngine.Object target, TrackMember tm, Rect rect, TimelineState st, int totalFrames)
        {
            if (tm.Member != null && s_ProviderByMember.TryGetValue(tm.Member, out var prov) && prov is ICustomTrackDrawer drawer)
            {
                drawer.Draw(target, tm, rect, st, totalFrames);
                return;
            }

            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.01f));
        }
    }
}
#endif
