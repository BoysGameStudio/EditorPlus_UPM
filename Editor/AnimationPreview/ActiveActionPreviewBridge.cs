#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using EditorPlus.AnimationPreview;
using Quantum;

namespace EditorPlus.AnimationPreview
{
    // Editor-side preview bridge moved from the host assembly. This class manages
    // per-instance preview GameObjects and the AnimationPreviewPlayer. It no
    // longer reads host internals directly; instead it queries the editor-side
    // state via ActiveActionDataEditorBridge.GetState(host).
    internal static class ActiveActionPreviewBridge
    {
        private sealed class PreviewState
        {
            public GameObject Root;
            public AnimationPreviewPlayer Player;
        }

        private static readonly Dictionary<ActiveActionData, PreviewState> _states = new();

        static ActiveActionPreviewBridge()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= StaticTeardown;
            AssemblyReloadEvents.beforeAssemblyReload += StaticTeardown;
            EditorApplication.quitting -= StaticTeardown;
            EditorApplication.quitting += StaticTeardown;
        }

        private static void StaticTeardown()
        {
            foreach (var kv in new List<ActiveActionData>(_states.Keys))
            {
                TeardownPreview(kv);
            }
            _states.Clear();
        }

        public static bool EnsurePreviewInfrastructure(ActiveActionData host, bool resetTime)
        {
            if (host == null) return false;
            var clip = host.Animation;
            if (clip == null) return false;

            if (_states.TryGetValue(host, out var st) && st != null && st.Player != null)
            {
                st.Player.SetClip(clip, resetTime);
                return true;
            }

            var root = new GameObject("__DashTempPreview__");
            // root.hideFlags = HideFlags.HideAndDontSave;
            var player = root.AddComponent<AnimationPreviewPlayer>();
            player.SetClip(clip, resetTime);

            // Prefer editor-state values (migrated into ActiveActionDataEditorBridge)
            var state = ActiveActionDataEditorBridge.GetState(host);
            if (state != null)
            {
                player.SetSpeed(state.PlaybackSpeed);
                player.SetLoop(state.Loop);
                player.applyRootMotion = state.PreviewApplyRootMotion;
                if (state.PreviewModelPrefab != null)
                {
                    // Validate prefab contains a SkinnedMeshRenderer (root or children).
                    var comps = state.PreviewModelPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    bool hasSkinned = comps != null && comps.Length > 0;

                    if (hasSkinned)
                    {
                        player.modelPrefab = state.PreviewModelPrefab;
                    }
                    else
                    {
                        // Invalid prefab for character preview — don't assign and avoid opening stage which expects a skinned model.
                        UnityEngine.Debug.LogWarning($"[AnimationPreview] Skipping model prefab for preview: the assigned prefab '{state.PreviewModelPrefab?.name}' contains no SkinnedMeshRenderer. Assign a character prefab with SkinnedMeshRenderer to preview models.");
                        player.modelPrefab = null;
                    }
                }
                player.previewFPS = state.PreviewFPS > 0 ? state.PreviewFPS : player.previewFPS;
            }
            else
            {
                // Fallback to host-provided interface for FPS if available
                if (host is IAnimationPreviewHost hostIface)
                {
                    float fps = hostIface.ResolvePreviewFPS(60f);
                    player.previewFPS = fps > 0f ? fps : player.previewFPS;
                }
            }

            if (player.modelPrefab == null && host is UnityEngine.Object uhost)
            {
                var so = new UnityEditor.SerializedObject(uhost);
                var prop = so.FindProperty("_previewModelPrefab");
                if (prop != null && prop.propertyType == UnityEditor.SerializedPropertyType.ObjectReference)
                {
                    var prefab = prop.objectReferenceValue as GameObject;
                    if (prefab != null)
                    {
                        // Validate contains SkinnedMeshRenderer
                        var comps = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        bool hasSkinned = comps != null && comps.Length > 0;

                        if (hasSkinned)
                        {
                            player.modelPrefab = prefab;
                        }
                        else
                        {
                            // keep null and let stage warn; avoid spamming console
                        }
                    }
                }
            }

            player.autoFixSkinnedBounds = true;
            player.boundsExtent = 5f;

            AnimationPreviewStageBridge.Open(root, host);

            st = new PreviewState { Root = root, Player = player };
            _states[host] = st;

            return true;
        }

        public static void SyncPlayerClip(ActiveActionData host, bool resetTime)
        {
            if (host == null) return;
            if (_states.TryGetValue(host, out var st) && st != null && st.Player != null)
            {
                st.Player.SetClip(host.Animation, resetTime);
            }
            else
            {
                EnsurePreviewInfrastructure(host, resetTime);
            }
        }

        public static AnimationPreviewPlayer GetPlayer(ActiveActionData host)
        {
            if (host == null) return null;
            if (_states.TryGetValue(host, out var st) && st != null) return st.Player;
            return null;
        }

        public static GameObject GetRoot(ActiveActionData host)
        {
            if (host == null) return null;
            if (_states.TryGetValue(host, out var st) && st != null) return st.Root;
            return null;
        }

        public static void SyncPlayerRuntimeSettings(ActiveActionData host)
        {
            if (host == null) return;
            if (!_states.TryGetValue(host, out var st) || st == null || st.Player == null) return;
            var player = st.Player;
            var state = ActiveActionDataEditorBridge.GetState(host);
            if (state != null)
            {
                player.SetSpeed(state.PlaybackSpeed);
                player.SetLoop(state.Loop);
                player.previewFPS = state.PreviewFPS > 0 ? state.PreviewFPS : player.previewFPS;
                player.applyRootMotion = state.PreviewApplyRootMotion;
                if (state.PreviewModelPrefab != null) player.modelPrefab = state.PreviewModelPrefab;
            }
        }

        public static void SetPlaying(ActiveActionData host, bool playing, bool restart = false)
        {
            if (host == null) return;
            if (!_states.TryGetValue(host, out var st) || st == null || st.Player == null)
            {
                if (!EnsurePreviewInfrastructure(host, resetTime: !playing)) return;
                st = _states[host];
            }

            if (playing) st.Player.Play(restart); else st.Player.Pause();
        }

        public static void StopAndReset(ActiveActionData host)
        {
            if (host == null) return;
            if (_states.TryGetValue(host, out var st) && st != null && st.Player != null)
            {
                st.Player.StopAndReset(true);
            }
        }

        public static bool HasActivePreviewPlayer(ActiveActionData host)
        {
            return host != null && _states.TryGetValue(host, out var st) && st != null && st.Player != null;
        }

        public static int GetPreviewFrame(ActiveActionData host)
        {
            if (host == null) return -1;
            if (_states.TryGetValue(host, out var st) && st != null && st.Player != null)
            {
                return st.Player.CurrentFrameAt(st.Player.previewFPS);
            }
            return -1;
        }

        public static void SeekPreviewFrame(ActiveActionData host, int frame)
        {
            if (host == null) return;
            if (_states.TryGetValue(host, out var st) && st != null && st.Player != null)
            {
                st.Player.SeekFrame(frame, st.Player.previewFPS);
            }
        }

        // Samples the player's current frame onto the preview root GameObject in the Scene view.
        // This is a debugging helper so you can inspect the sampled pose even when no model prefab is assigned.
        public static void SampleCurrentFrameToScene(ActiveActionData host)
        {
            if (host == null) return;
            var clip = host.Animation;
            if (clip == null) return;
            if (!_states.TryGetValue(host, out var st) || st == null || st.Player == null) return;
            var player = st.Player;
            var root = st.Root;
            if (root == null) return;
                float fps = Mathf.Max(1f, player.previewFPS);
                int frame = player.CurrentFrameAt(fps);
                float time = frame / fps;
                // Use AnimationMode to sample the clip onto the root GameObject in editor
                UnityEditor.AnimationMode.StartAnimationMode();
                UnityEditor.AnimationMode.SampleAnimationClip(root, clip, time);
                UnityEditor.SceneView.RepaintAll();
        }

        public static void TeardownPreview(ActiveActionData host)
        {
            if (host == null) return;
            if (_states.TryGetValue(host, out var st) && st != null)
            {
                if (st.Player != null)
                {
                    st.Player.StopAndReset(true);
                }
                if (st.Root != null)
                {
                    GameObject.DestroyImmediate(st.Root);
                }
            }
            _states.Remove(host);
                AnimationPreviewStageBridge.Close();
        }

        public static bool HasAnyActivePreview()
        {
            return _states.Count > 0;
        }
    }
}
#endif
