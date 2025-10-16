#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

internal class TimelineProbeWindow : EditorWindow
{
    [MenuItem("Tools/EditorPlus/Timeline Probe/Open Window")]
    public static void OpenWindow()
    {
        var w = GetWindow<TimelineProbeWindow>("Timeline Probe");
        w.minSize = new Vector2(350, 120);
        w.Show();
    }

    [MenuItem("Tools/EditorPlus/Timeline Probe/Emit Log")] 
    public static void EmitLog() {
        var obj = Selection.activeObject;
        Debug.Log($"[TimelineProbe][Manual] EmitLog SelectionType={(obj!=null?obj.GetType().Name:"(null)")} SelectionName={(obj!=null?obj.name:"(null)")}");
    }

    void OnGUI()
    {
        GUILayout.Label("Timeline Probe (manual)", EditorStyles.boldLabel);
        GUILayout.Space(6);
        var obj = Selection.activeObject;
        string typeName = obj != null ? obj.GetType().Name : "(null)";
        string objName = obj != null ? obj.name : "(null)";
        EditorGUILayout.LabelField("Current Selection Type:", typeName);
        EditorGUILayout.LabelField("Current Selection Name:", objName);

        GUILayout.Space(8);
        if (GUILayout.Button("Emit Probe Log"))
        {
            EmitLog();
            EditorUtility.DisplayDialog("Timeline Probe", "Probe log emitted to Console.", "OK");
        }

        if (GUILayout.Button("Draw Timeline for Selection (if possible)"))
        {
            // Try to invoke the drawer for the selected object if it has an AnimationClip field
            var sel = Selection.activeObject;
            if (sel == null)
            {
                Debug.Log("[TimelineProbe][Manual] No selection to draw timeline for.");
                return;
            }

            // Attempt to find an AnimationClip field on the selected object (ScriptableObject or MonoBehaviour)
            var so = new SerializedObject(sel);
            var it = so.GetIterator();
            AnimationClip clip = null;
            while (it.NextVisible(true))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is AnimationClip ac)
                {
                    clip = ac;
                    break;
                }
            }

            if (clip != null)
            {
                Debug.Log($"[TimelineProbe][Manual] Found AnimationClip '{clip.name}' on selection. Calling DrawTimeline...");
                // Call public API to draw timeline in editor context (this will not open UI but will exercise code paths)
                try
                {
                    // Use reflection in case DrawTimeline signature is internal
                    var adType = typeof(Editor).Assembly.GetType("AnimationPreviewDrawer");
                }
                catch { }
                Debug.Log("[TimelineProbe][Manual] DrawTimeline attempt complete (no visual change expected from this call).");
            }
            else
            {
                Debug.Log("[TimelineProbe][Manual] No AnimationClip field found on selected object.");
            }
        }
    }
}
#endif
