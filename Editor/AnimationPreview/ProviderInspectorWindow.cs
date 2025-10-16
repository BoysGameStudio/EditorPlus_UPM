#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    public class ProviderInspectorWindow : EditorWindow
    {
        [MenuItem("Window/EditorPlus/Provider Inspector")]
        public static void ShowWindow()
        {
            GetWindow<ProviderInspectorWindow>("Provider Inspector");
        }

        private Vector2 _scroll;

        private void OnGUI()
        {
            GUILayout.Label("Track Providers", EditorStyles.boldLabel);
            if (GUILayout.Button("Refresh / Ensure Registration"))
            {
                TrackRenderer.EnsureProvidersRegistered();
            }

            var providers = TrackRenderer.GetRegisteredProviders();
            GUILayout.Label($"Registered: {providers.Length}");

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var p in providers)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(p.GetType().Name, GUILayout.Width(260));
                if (Selection.activeObject != null && GUILayout.Button("Test against selection", GUILayout.Width(160)))
                {
                    var t = Selection.activeObject.GetType();
                    bool ok = false;
                    try { ok = p.CanHandle(t); } catch { }
                    Debug.Log($"[ProviderInspector] {p.GetType().Name}.CanHandle({t.Name}) => {ok}");
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("Dump TrackMembers for Selection"))
            {
                var obj = Selection.activeObject;
                if (obj == null) Debug.Log("[ProviderInspector] No selection");
                else
                {
                    try
                    {
                        var members = TrackRenderer.GetTrackMembers(obj);
                        Debug.Log($"[ProviderInspector] GetTrackMembers({obj.GetType().Name}) => {members.Length} members");
                        foreach (var m in members)
                        {
                            Debug.Log($"  Member: {m.Member?.Name} ValueType={m.ValueType?.Name} Label={m.Label} Order={m.Order}");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }
    }
}

#endif
