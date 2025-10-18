#if UNITY_EDITOR
using EditorPlus.AnimationPreview;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal sealed class AnimationPreviewController : IPreviewStageController
{
    private AnimationPreviewStage _stage;

    static AnimationPreviewController()
    {
        // Register this controller on load
        AnimationPreviewStageBridge.Impl = new AnimationPreviewController();
    }

    public void Open(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        // Close any existing stage we own to ensure a clean open
        Close();

        _stage = ScriptableObject.CreateInstance<AnimationPreviewStage>();
        _stage.Target = target;
        StageUtility.GoToStage(_stage, true);
    }

    public void Open(GameObject target, UnityEngine.Object asset)
    {
        if (target == null)
        {
            return;
        }

        // Close any existing stage we own to ensure a clean open
        Close();

        _stage = ScriptableObject.CreateInstance<AnimationPreviewStage>();
        _stage.Target = target;
        StageUtility.GoToStage(_stage, true);

        // Set the asset as the active selection so Inspector shows it instead of the temporary GameObject
        // This must be done after GoToStage to override any Selection changes made in OnOpenStage
        if (asset != null)
        {
            EditorApplication.delayCall += () => Selection.activeObject = asset;
        }
    }

    public void Close()
    {
        if (_stage == null)
        {
            return;
        }

        StageUtility.GoToMainStage();
        Object.DestroyImmediate(_stage);
        _stage = null;
    }

    public bool TryGetStageScene(out Scene scene)
    {
        if (_stage != null)
        {
            scene = _stage.scene;
            return true;
        }

        scene = default;
        return false;
    }
}
#endif
