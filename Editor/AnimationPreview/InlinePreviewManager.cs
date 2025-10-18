#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    internal static class InlinePreviewManager
    {
        // Map inspected parent targets to hidden preview GameObjects
        private static readonly Dictionary<Object, GameObject> _previewRoots = new();

        public static bool HasPreview(Object parent)
        {
            if (parent == null) return false;
            return _previewRoots.ContainsKey(parent) && _previewRoots[parent] != null;
        }

        public static bool IsPlaying(Object parent)
        {
            if (!HasPreview(parent)) return false;
            var go = _previewRoots[parent];
            var player = go.GetComponent<AnimationPreviewPlayer>();
            return player != null && player.IsPlaying;
        }

        public static void EnsurePreview(Object parent, AnimationClip clip, bool resetTime, float fps)
        {
            if (parent == null || clip == null) return;
            if (!HasPreview(parent) || _previewRoots[parent] == null)
            {
                var go = new GameObject("__InlinePreviewRoot__");
                go.hideFlags = HideFlags.HideAndDontSave;
                var player = go.AddComponent<AnimationPreviewPlayer>();
                player.SetClip(clip, resetTime);
                player.previewFPS = fps > 0f ? fps : player.previewFPS;
                _previewRoots[parent] = go;
                return;
            }

            var root = _previewRoots[parent];
            var p = root.GetComponent<AnimationPreviewPlayer>();
            if (p != null)
            {
                p.SetClip(clip, resetTime);
                p.previewFPS = fps > 0f ? fps : p.previewFPS;
            }
        }

        public static void SetPlaying(Object parent, bool playing)
        {
            if (!HasPreview(parent)) return;
            var go = _previewRoots[parent];
            var player = go.GetComponent<AnimationPreviewPlayer>();
            if (player == null) return;
            if (playing) player.Play(restart: false); else player.Pause();
        }

        public static void Stop(Object parent)
        {
            if (!HasPreview(parent)) return;
            var go = _previewRoots[parent];
            var player = go.GetComponent<AnimationPreviewPlayer>();
            if (player != null)
            {
                player.StopAndReset(true);
            }
        }

        public static bool TryGetPreviewFrame(Object parent, out int frame, float fps)
        {
            frame = -1;
            if (!HasPreview(parent)) return false;
            var go = _previewRoots[parent];
            var player = go.GetComponent<AnimationPreviewPlayer>();
            if (player == null) return false;
            frame = player.CurrentFrameAt(fps > 0f ? fps : player.previewFPS);
            return frame >= 0;
        }

        public static void SeekPreviewFrame(Object parent, int frame, float fps)
        {
            if (!HasPreview(parent)) return;
            var go = _previewRoots[parent];
            var player = go.GetComponent<AnimationPreviewPlayer>();
            if (player == null) return;
            player.SeekFrame(frame, fps > 0f ? fps : player.previewFPS);
        }

        public static void DisposePreview(Object parent)
        {
            if (!HasPreview(parent)) return;
            var go = _previewRoots[parent];
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
            _previewRoots.Remove(parent);
        }
    }
}
#endif
