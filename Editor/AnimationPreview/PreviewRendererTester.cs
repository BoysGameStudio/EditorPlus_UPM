#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace EditorPlus.AnimationPreview
{
    internal class PreviewRendererTester : EditorWindow
    {
        private Object selectedAsset;
        private GameObject overridePreviewRoot;
        private int frameIndex = 0;
        private bool autoResolveRoot = true;

        [MenuItem("Tools/AnimationPreview/Preview Renderer Tester", false, 2001)]
        public static void ShowWindow()
        {
            var w = GetWindow<PreviewRendererTester>("PreviewRenderer Tester");
            w.minSize = new Vector2(320, 120);
        }

        private void OnGUI()
        {
            GUILayout.Label("Preview Renderer Tester", EditorStyles.boldLabel);
            selectedAsset = EditorGUILayout.ObjectField("ActionData Asset", selectedAsset, typeof(Object), false);
            autoResolveRoot = EditorGUILayout.Toggle("Auto-resolve preview root", autoResolveRoot);
            overridePreviewRoot = (GameObject)EditorGUILayout.ObjectField("Override Preview Root", overridePreviewRoot, typeof(GameObject), true);
            frameIndex = EditorGUILayout.IntField("Frame Index", frameIndex);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Draw Once in Scene"))
            {
                DrawPreviewOnce();
            }
            if (GUILayout.Button("Ping Resolved Root"))
            {
                PingResolvedRoot();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void PingResolvedRoot()
        {
            if (selectedAsset == null)
            {
                Debug.Log("No asset selected to resolve preview root from.");
                return;
            }

            GameObject root = null;
            if (!autoResolveRoot && overridePreviewRoot != null) root = overridePreviewRoot;
            else
            {
                root = PreviewRenderer.ResolvePreviewRoot(selectedAsset);
            }

            Debug.Log($"Resolved preview root: {(root != null ? root.name : "<null>")}");
            if (root != null) EditorGUIUtility.PingObject(root);
        }

        private void DrawPreviewOnce()
        {
            if (selectedAsset == null)
            {
                Debug.Log("No asset selected to preview.");
                return;
            }

            GameObject root = null;
            if (!autoResolveRoot && overridePreviewRoot != null) root = overridePreviewRoot;
            else
            {
                root = PreviewRenderer.ResolvePreviewRoot(selectedAsset);
            }

            // Call the same entrypoint used by the real preview
            PreviewRenderer.DrawHitFramesPreview(selectedAsset, frameIndex);
            // Force a SceneView repaint after invoking drawing — the preview renderer uses Handles which draw during OnGUI of SceneView.
            SceneView.RepaintAll();
            SceneView.RepaintAll();
        }
    }
}
#endif
