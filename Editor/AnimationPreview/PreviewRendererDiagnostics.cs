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

            var root = typeof(PreviewRenderer).GetMethod("ResolvePreviewRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.Invoke(null, new object[] { obj }) as GameObject;
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

            // Try reflection fallback
            var fi = obj.GetType().GetField("hitFrames", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (fi != null)
            {
                var arr = fi.GetValue(obj) as System.Array;
                if (arr != null)
                {
                    Debug.Log($"Reflected hitFrames array length: {arr.Length}");
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var elem = arr.GetValue(i);
                        if (elem == null) continue;
                        var f = elem.GetType().GetField("frame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (f != null)
                        {
                            var fv = f.GetValue(elem);
                            Debug.Log($"  hitFrames[{i}] frame={fv}");
                        }
                    }
                    return;
                }
            }

            Debug.Log("No hitFrames discovered (serialized or reflected).");
        }
    }
}
#endif
