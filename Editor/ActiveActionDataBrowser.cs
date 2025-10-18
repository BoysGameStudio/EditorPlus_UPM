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

namespace Quantum.Editor
{
    public class ActiveActionDataBrowser : OdinMenuEditorWindow
    {

        [MenuItem("Quantum/Active Action Data Browser")]
        private static void Open()
        {
            var window = GetWindow<ActiveActionDataBrowser>();
            window.titleContent = new GUIContent("ActiveActionData");
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
        }

        protected override OdinMenuTree BuildMenuTree()
        {
            var tree = new OdinMenuTree(supportsMultiSelect: true)
            {
                Config = { DrawSearchToolbar = true, AutoFocusSearchBar = true }
            };

            var instances = AssetDatabase.FindAssets("t:ScriptableObject")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ScriptableObject>)
                .Where(obj => obj is Quantum.ActiveActionData && !obj.GetType().IsAbstract)
                .GroupBy(i => i.GetType())
                .OrderBy(g => g.Key.Name);

            foreach (var group in instances)
            {
                foreach (var asset in group.OrderBy(a => a.name))
                {
                    tree.Add($"{group.Key.Name}/{asset.name}", asset);
                }
            }

            return tree;
        }

        private void CreateAsset(Type type)
        {
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

        private string GetSelectedFolder()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return string.IsNullOrEmpty(path) ? null : Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        }
    }
}
#endif