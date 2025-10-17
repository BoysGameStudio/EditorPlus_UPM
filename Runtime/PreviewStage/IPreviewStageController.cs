#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EditorPlus.AnimationPreview
{
    /// <summary>
    /// Interface for editor-side preview stage controllers.
    /// Implementations manage opening/closing an isolation preview stage and
    /// may provide the Scene instance used by the stage.
    /// Editor-only API (guarded by UNITY_EDITOR).
    /// </summary>
    public interface IPreviewStageController
    {
        void Open(GameObject target);
        void Open(GameObject target, UnityEngine.Object asset);
        void Close();
        bool TryGetStageScene(out Scene scene);
    }
}
#endif
