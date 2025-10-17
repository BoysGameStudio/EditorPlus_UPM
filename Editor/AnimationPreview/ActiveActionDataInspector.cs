#if UNITY_EDITOR && ODIN_INSPECTOR
using System;
using UnityEngine;
using UnityEditor;
using EditorPlus.AnimationPreview;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;

namespace Quantum
{
    [CustomEditor(typeof(ActiveActionData), true)]
    internal class ActiveActionDataInspector : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var a = base.target as ActiveActionData;
            if (a == null) return;

            var foldoutKey = $"EditorPlus.ActiveActionData.PreviewFoldout.{a.GetInstanceID()}";
            bool expanded = EditorPrefs.GetBool(foldoutKey, true);
            expanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, "Animation Preview");
            if (expanded)
            {
                SirenixEditorGUI.BeginBox();

                bool showOpenWarning = false;
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                bool isPlaying = (ActiveActionDataEditorBridge.GetState(a)?.PreviewPlaying ?? false);
                string toggleLabel = isPlaying ? "Close Preview" : "Open Preview";
                if (SirenixEditorGUI.ToolbarButton(toggleLabel))
                {
                    if (isPlaying)
                    {
                        ActiveActionDataEditorBridge.TeardownPreview(a);
                    }
                    else
                    {
                        bool opened = ActiveActionDataEditorBridge.EnsurePreviewInfrastructure(a, resetTime: true);
                        if (!opened)
                        {
                            showOpenWarning = true;
                        }
                        else
                        {
                            ActiveActionDataEditorBridge.SyncPlayerClip(a, resetTime: true);
                            ActiveActionDataEditorBridge.SetPlaying(a, true, restart: true);
                        }
                    }
                }
                GUILayout.EndHorizontal();

                if (showOpenWarning)
                {
                    EditorGUILayout.HelpBox("Preview could not be opened. Ensure an AnimationClip is assigned and (optionally) a valid character prefab with SkinnedMeshRenderer is set in the preview settings.", MessageType.Warning);
                }

                if (a.Animation == null)
                {
                    EditorGUILayout.HelpBox("No AnimationClip assigned. Assign an Animation to preview in the 'Animation' field above.", MessageType.Warning);
                    if (GUILayout.Button("Create Empty Preview Player"))
                    {
                        ActiveActionDataEditorBridge.EnsurePreviewInfrastructure(a, resetTime: true);
                    }
                }

                SirenixEditorGUI.EndBox();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorPrefs.SetBool(foldoutKey, expanded);
        }
    }
}
#endif