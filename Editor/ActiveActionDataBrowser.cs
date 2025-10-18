#if UNITY_EDITOR && ODIN_INSPECTOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace Quantum.Editor {
    public class ActiveActionDataBrowser : OdinMenuEditorWindow {

        [MenuItem("Quantum/Active Action Data Browser", priority = 100)]
        private static void Open() {
            var window = GetWindow<ActiveActionDataBrowser>();
            window.titleContent = new GUIContent("ActiveActionData");
            window.Show();
        }

        private Type _createType;
        private List<Type> _concreteTypes;

        protected override void OnEnable() {
            base.OnEnable();
            RefreshTypeCache();
        }

        private void RefreshTypeCache() {
            _concreteTypes = TypeCache.GetTypesDerivedFrom<Quantum.ActiveActionData>()
                .Where(t => !t.IsAbstract && typeof(ScriptableObject).IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToList();
            _createType = _concreteTypes.FirstOrDefault(t => t == _createType) ?? _concreteTypes.FirstOrDefault();
        }

        protected override OdinMenuTree BuildMenuTree() {
            var tree = new OdinMenuTree(supportsMultiSelect: true) {
                Config = { DrawSearchToolbar = true, AutoFocusSearchBar = true }
            };

            var instances = AssetDatabase.FindAssets("t:ScriptableObject")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ScriptableObject>)
                .Where(obj => obj is Quantum.ActiveActionData && !obj.GetType().IsAbstract)
                .GroupBy(i => i.GetType())
                .OrderBy(g => g.Key.Name);

            foreach (var group in instances) {
                foreach (var asset in group.OrderBy(a => a.name)) {
                    tree.Add($"{group.Key.Name}/{asset.name}", asset);
                }
            }

            tree.Add("─ Utilities/Refresh Type Cache", new ActionRunner(RefreshTypeCache));
            tree.Add("─ Utilities/Rescan Assets", new ActionRunner(() => ForceMenuTreeRebuild()));

            return tree;
        }

        protected override void OnBeginDrawEditors() {
            if (MenuTree == null) return;

            SirenixEditorGUI.BeginHorizontalToolbar();
            GUILayout.Label("ActiveActionData Browser", SirenixGUIStyles.BoldLabel);

            if (_concreteTypes?.Count > 0) {
                var names = _concreteTypes.Select(t => t.Name).ToArray();
                int index = Mathf.Max(0, _concreteTypes.IndexOf(_createType));
                int newIndex = EditorGUILayout.Popup(index, names, GUILayout.Width(180));
                if (newIndex != index) _createType = _concreteTypes[newIndex];
            }

            if (GUILayout.Button("Create", GUILayout.Width(70))) CreateAsset(_createType);
            if (GUILayout.Button("Ping", GUILayout.Width(50))) {
                var so = MenuTree.Selection.FirstOrDefault()?.Value as UnityEngine.Object;
                if (so) EditorGUIUtility.PingObject(so);
            }
            if (GUILayout.Button("Rescan", GUILayout.Width(60))) ForceMenuTreeRebuild();

            SirenixEditorGUI.EndHorizontalToolbar();
            base.OnBeginDrawEditors();
        }

        private void CreateAsset(Type type) {
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type)) return;

            var instance = ScriptableObject.CreateInstance(type);
            var folder = GetSelectedFolder() ?? "Assets";
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, $"New_{type.Name}.asset"));
            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ForceMenuTreeRebuild();

            EditorGUIUtility.PingObject(instance);
            Selection.activeObject = instance;
        }

        private string GetSelectedFolder() {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return string.IsNullOrEmpty(path) ? null : Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        }

        // Helper node that executes an action
        private class ActionRunner {
            private readonly Action _action;
            public ActionRunner(Action action) { _action = action; }
            [Button(ButtonSizes.Medium)]
            public void Run() => _action?.Invoke();
        }
    }
}
#endif