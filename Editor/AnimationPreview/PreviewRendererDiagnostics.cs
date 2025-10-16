#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    internal static class PreviewRendererDiagnostics
    {
        [MenuItem("Tools/AnimationPreview/PreviewRenderer Diagnostics", false, 2000)]
        private static void RunDiagnostics()
        {
            var obj = Selection.activeObject;
            if (obj == null)
            {
                Debug.Log("PreviewRenderer Diagnostics: No object selected.");
                return;
            }

            Debug.Log($"PreviewRenderer Diagnostics: Selected object type={obj.GetType().FullName}, name={obj.name}");

            var root = PreviewRenderer.ResolvePreviewRoot(obj);
            Debug.Log($"Resolved preview root: {(root != null ? root.name : "<null>")}");

            try
            {
                var so = new SerializedObject(obj);
                var prop = so.FindProperty("hitFrames");
                if (prop != null && prop.isArray)
                {
                    Debug.Log($"Serialized hitFrames array size: {prop.arraySize}");
                    for (int i = 0; i < prop.arraySize; i++)
                    {
                        var elem = prop.GetArrayElementAtIndex(i);
                        if (elem == null) continue;
                        var frameProp = elem.FindPropertyRelative("frame");
                        var frame = frameProp != null ? frameProp.intValue : int.MinValue;
                        Debug.Log($"  hitFrames[{i}] frame={frame}");
                    }
                    return;
                }
            }
            catch { }

            // If the asset is a known Quantum type, access typed members directly (no reflection)
            try
            {
                if (obj is Quantum.AttackActionData attack && attack.hitFrames != null)
                {
                    Debug.Log($"Typed AttackActionData.hitFrames length: {attack.hitFrames.Length}");
                    for (int i = 0; i < attack.hitFrames.Length; i++)
                    {
                        var hf = attack.hitFrames[i];
                        if (hf == null) continue;
                        Debug.Log($"  hitFrames[{i}] frame={hf.frame}");
                    }
                    return;
                }
            }
            catch { }

            Debug.Log("No hitFrames discovered (serialized or reflected).");
        }
    }
}
#endif
