#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Small editor utility to print timeline track diagnostics for the currently selected object.
/// Use Tools -> EditorPlus -> Timeline Verbose Log to emit logs.
/// This is temporary debug tooling and can be removed after diagnosis.
/// </summary>
public static class TimelineVerboseLogger
{
    [MenuItem("Tools/EditorPlus/Timeline Verbose Log")]
    public static void LogSelectedTracks()
    {
        var obj = Selection.activeObject;
        Debug.Log($"[TimelineVerbose] Selection => Type={(obj!=null?obj.GetType().Name:"(null)")} Name={(obj!=null?obj.name:"(null)")}");
        if (obj == null)
        {
            return;
        }

        var type = obj.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        var members = type.GetMembers(flags);
        int found = 0;

        foreach (var member in members)
        {
            try
            {
                var attr = member.GetCustomAttribute(typeof(AnimationEventAttribute)) as AnimationEventAttribute;
                if (attr == null) continue;
                found++;

                string label = string.IsNullOrEmpty(attr.Label) ? member.Name : attr.Label;
                string memberTypeName = "?";
                if (member is FieldInfo f) memberTypeName = f.FieldType.Name;
                else if (member is PropertyInfo p) memberTypeName = p.PropertyType.Name;

                Debug.Log($"[TimelineVerbose] Track: {label} Member={member.Name} Type={memberTypeName}");

                Func<object> getter = null;
                if (member is FieldInfo fi) getter = () => fi.GetValue(obj);
                else if (member is PropertyInfo pi && pi.CanRead) getter = () => pi.GetValue(obj, null);

                object value = null;
                try { value = getter != null ? getter() : null; } catch (Exception ex) { Debug.LogWarning($"[TimelineVerbose] Getter threw: {ex.Message}"); }

                if (value == null)
                {
                    Debug.Log("[TimelineVerbose] Value is null");
                    // Try SerializedProperty fallback
                    try
                    {
                        var so = new SerializedObject(obj);
                        var prop = so.FindProperty(member.Name);
                        if (prop != null && prop.isArray && prop.arraySize > 0)
                        {
                            Debug.Log($"[TimelineVerbose] Serialized array detected for {member.Name} size={prop.arraySize}");
                            var list = new List<int>();
                            for (int i = 0; i < prop.arraySize; i++)
                            {
                                var elem = prop.GetArrayElementAtIndex(i);
                                var frameProp = elem.FindPropertyRelative("frame");
                                if (frameProp != null) list.Add(frameProp.intValue);
                            }
                            Debug.Log($"[TimelineVerbose] FramesFound={list.Count} Samples={(list.Count>0?list[0].ToString():"-")}");
                        }
                    }
                    catch { }

                    continue;
                }

                if (value is Array arr)
                {
                    Debug.Log($"[TimelineVerbose] Elements: {arr.Length}");
                    var list = new List<int>();
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var elem = arr.GetValue(i);
                        if (elem == null)
                        {
                            Debug.Log($"[TimelineVerbose] Elem[{i}] is null");
                            list.Add(-1);
                            continue;
                        }

                        int ef = -1;
                        var fField = elem.GetType().GetField("frame", flags);
                        if (fField != null)
                        {
                            try { var v = fField.GetValue(elem); ef = Convert.ToInt32(v); } catch { ef = -1; }
                        }
                        else
                        {
                            var pInfo = elem.GetType().GetProperty("frame", flags);
                            if (pInfo != null)
                            {
                                try { var v = pInfo.GetValue(elem, null); ef = Convert.ToInt32(v); } catch { ef = -1; }
                            }
                        }

                        Debug.Log($"[TimelineVerbose] Elem[{i}] frame={ef} type={elem.GetType().Name}");
                        list.Add(ef);
                    }
                    var valid = list.FindAll(x => x >= 0);
                    Debug.Log($"[TimelineVerbose] FramesFound={valid.Count} Samples={(valid.Count>0?valid[0].ToString():"-")}");
                }
                else if (value is int vint)
                {
                    Debug.Log($"[TimelineVerbose] int marker={vint}");
                }
                else if (value is int[] iarr)
                {
                    Debug.Log($"[TimelineVerbose] int[] length={iarr.Length} values={string.Join(",", iarr)}");
                }
                else
                {
                    Debug.Log($"[TimelineVerbose] Value type {value.GetType().Name} not an array");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TimelineVerbose] Inspect error: {ex.Message}");
            }
        }

        if (found == 0) Debug.Log("[TimelineVerbose] No tracks found (no AnimationEventAttribute members)");
    }
}

#endif
