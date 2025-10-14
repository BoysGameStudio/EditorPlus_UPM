#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EditorPlus.Preview
{
    public interface IPreviewStageController
    {
        void Open(GameObject target);
        void Close();
        bool TryGetStageScene(out Scene scene);
    }

    public static class PreviewStageAPI
    {
        public static IPreviewStageController Impl { get; set; }

        public static bool Open(GameObject target)
        {
            if (Impl == null || target == null) return false;
            Impl.Open(target);
            return true;
        }

        public static void Close()
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
