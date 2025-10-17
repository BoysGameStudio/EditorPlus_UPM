#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EditorPlus.AnimationPreview
{
    public static class AnimationPreviewStageBridge
    {
        public static IPreviewStageController Impl { get; set; }

    public static bool Open(GameObject target, UnityEngine.Object asset)
    {
        if (Impl == null || target == null) return false;
        Impl.Open(target, asset);
        return true;
    }        public static void Close()
        {
            Impl?.Close();
        }

        public static bool TryGetStageScene(out Scene scene)
        {
            if (Impl != null)
            {
                return Impl.TryGetStageScene(out scene);
            }

            scene = default;
            return false;
        }
    }
}
#endif
