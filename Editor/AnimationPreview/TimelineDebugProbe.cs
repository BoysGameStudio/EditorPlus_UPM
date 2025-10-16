#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Small debug probe to verify Editor logging and selection notifications.
/// Remove this file when no longer needed.
/// </summary>
[InitializeOnLoad]
internal static class TimelineDebugProbe
{
    static TimelineDebugProbe()
    {
        // Log once at initialization so we know editor scripts are running
        Debug.Log("[TimelineProbe] Initialized Editor probe — logging is active.");

        // Subscribe to selection changes to report what the inspector is showing
        Selection.selectionChanged += OnSelectionChanged;
    }

    private static void OnSelectionChanged()
    {
        var obj = Selection.activeObject;
        string tname = obj != null ? obj.GetType().Name : "(null)";
        string oname = obj != null ? obj.name : "(null)";
        Debug.Log($"[TimelineProbe] Selection changed => Type={tname} Name={oname}");
    }
}

#endif
