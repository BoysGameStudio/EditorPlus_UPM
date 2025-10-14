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

    public void Close()
    {
        if (_stage == null)
        {
            return;
        }

        try { StageUtility.GoToMainStage(); } catch { }
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
