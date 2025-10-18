#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

internal static class AnimationPreviewInspectorHelper
{
    // Draws timelines for any field/property on the provided target that has AnimationPreviewAttribute.
    public static void DrawPreviewsFor(UnityEngine.Object target)
    {
        if (target == null) return;

        var t = target.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var fields = t.GetFields(flags).Where(f => f.GetCustomAttributes(false).Any(a => a.GetType().Name == "AnimationPreviewAttribute"));
        var props = t.GetProperties(flags).Where(p => p.GetCustomAttributes(false).Any(a => a.GetType().Name == "AnimationPreviewAttribute"));

        foreach (var fi in fields)
        {
            var clip = fi.GetValue(target) as AnimationClip;
            if (clip != null)
            {
                AnimationPreviewDrawer.DrawTimeline(target, clip, minHeightOverride: null, includeFrameEventsInspector: true, addTopSpacing: true, previewName: fi.Name);
            }
            else
            {
                // Help the user understand why no timeline was drawn for this annotated member
                EditorGUILayout.HelpBox($"No AnimationClip assigned to '{fi.Name}'.", MessageType.Warning);
            }
        }

        foreach (var pi in props)
        {
            if (!pi.CanRead) continue;
            var clip = pi.GetValue(target, null) as AnimationClip;
            if (clip != null)
            {
                AnimationPreviewDrawer.DrawTimeline(target, clip, minHeightOverride: null, includeFrameEventsInspector: true, addTopSpacing: true, previewName: pi.Name);
            }
            else
            {
                EditorGUILayout.HelpBox($"No AnimationClip assigned to '{pi.Name}'.", MessageType.Warning);
            }
        }
    }
}
#endif
