// AnimationPreviewPlayer.cs (Editor-only minimal backend)
// Purpose:
//   Lightweight animation preview driver used by editor tooling.
//   - Wraps a PlayableGraph (AnimationClipPlayable + optional crossfade mixer)
//   - Provides programmatic control (Play/Pause/Stop/Seek by frame or normalized)
//   - Optional auto model (Prefab) assignment from clip asset path
//   - Applies simple visibility fixes (expand SMR bounds, offscreen update)
//   - Raises events: OnClipChanged / OnPlayStateChanged / OnSeekNormalized
// Thread / Determinism: Editor preview only (non-deterministic), NOT used in runtime simulation.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[ExecuteAlways]
[DisallowMultipleComponent]
public class AnimationPreviewPlayer : MonoBehaviour
{
    public enum RetargetMode { Auto, Humanoid, Generic }

    private GameObject _modelPrefab;
    public GameObject modelPrefab
    {
        get => _modelPrefab;
        set
        {
            if (_modelPrefab == value) return;
            if (value && !IsValidPreviewPrefab(value)) return;
            _modelPrefab = value;
            DespawnModelIfTemp();
            EnsureModelAndAnimator();
        }
    }

    private bool IsValidPreviewPrefab(GameObject go)
    {
        if (!go) return true;
        var type = PrefabUtility.GetPrefabAssetType(go);
        if (type != PrefabAssetType.Regular && type != PrefabAssetType.Variant)
            return false;
        return go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
    }

    private Animator _animator;
    public Animator animator
    {
        get => _animator;
        set
        {
            if (_animator == value) return;
            _animator = value;
            if (_graph.IsValid())
            {
                if (_output.IsOutputValid())
                {
                    _output.SetTarget(_animator);
                }
                else if (_animator)
                {
                    _output = AnimationPlayableOutput.Create(_graph, "Out", _animator);
                }
                RebindClip(false);
                SeekByNormalizedTime();
            }
            else if (_animator)
            {
                BuildGraphIfNeeded();
            }
        }
    }

    private readonly List<AnimationClip> _clips = new List<AnimationClip>();
    public List<AnimationClip> clips => _clips;
    [HideInInspector] public int clipIndex;

    private bool play;
    [HideInInspector][SerializeField] private float _speed = 1.0f; public float speed { get => _speed; set => _speed = Mathf.Max(0f, value); }
    [HideInInspector][SerializeField] private bool _loop = true; public bool loop { get => _loop; set => _loop = value; }
    [HideInInspector][SerializeField] private float _crossFade = 0.12f; public float crossFade { get => _crossFade; set => _crossFade = Mathf.Max(0f, value); }
    [HideInInspector][SerializeField] private bool _applyRootMotion = false; public bool applyRootMotion { get => _applyRootMotion; set { _applyRootMotion = value; ApplyAnimatorFlags(); } }
    [HideInInspector][SerializeField] private bool _alwaysAnimate = true; public bool alwaysAnimate { get => _alwaysAnimate; set { _alwaysAnimate = value; ApplyAnimatorFlags(); } }
    [HideInInspector][SerializeField] private RetargetMode _retarget = RetargetMode.Auto; public RetargetMode retarget { get => _retarget; set => _retarget = value; }
    [HideInInspector][SerializeField] private bool _autoAssignModelFromClip = true; public bool autoAssignModelFromClip { get => _autoAssignModelFromClip; set => _autoAssignModelFromClip = value; }
    [HideInInspector][SerializeField] private bool _autoAssignScanFolder = true; public bool autoAssignScanFolder { get => _autoAssignScanFolder; set => _autoAssignScanFolder = value; }
    [HideInInspector][SerializeField] private bool _autoAssignVerbose = false; public bool autoAssignVerbose { get => _autoAssignVerbose; set => _autoAssignVerbose = value; }
    [NonSerialized] private string _lastAutoAssignStatus = "<idle>";

    [HideInInspector][SerializeField] private bool _autoFixSkinnedBounds = true; public bool autoFixSkinnedBounds { get => _autoFixSkinnedBounds; set { _autoFixSkinnedBounds = value; ApplyVisibilityFix(); } }
    [HideInInspector][SerializeField] private float _boundsExtent = 5f; public float boundsExtent { get => _boundsExtent; set { if (Mathf.Approximately(_boundsExtent, value)) return; _boundsExtent = Mathf.Max(0.01f, value); _boundsApplied = false; ApplyVisibilityFix(); } }
    [HideInInspector][SerializeField] private bool _updateSMRWhenOffscreen = true; public bool updateSMRWhenOffscreen { get => _updateSMRWhenOffscreen; set { _updateSMRWhenOffscreen = value; _boundsApplied = false; ApplyVisibilityFix(); } }

    [NonSerialized] private float normalizedTime = 0f;
    [HideInInspector][SerializeField] private bool _sampleFirstOnStop = true; public bool sampleFirstOnStop { get => _sampleFirstOnStop; set => _sampleFirstOnStop = value; }
    [HideInInspector][SerializeField] private float _previewFPS = 60f; public float previewFPS { get => _previewFPS; set => _previewFPS = Mathf.Clamp(value, 1f, 480f); }
    [HideInInspector][SerializeField] private int _targetUpdateFPS = 120; public int targetUpdateFPS { get => _targetUpdateFPS; set => _targetUpdateFPS = Mathf.Clamp(value, 15, 480); }
    [HideInInspector][SerializeField] private int _maxSubsteps = 8; public int maxSubsteps { get => _maxSubsteps; set => _maxSubsteps = Mathf.Clamp(value, 1, 64); }
    [HideInInspector][SerializeField] private bool _forceHighFreqRepaint = true; public bool forceHighFreqRepaint { get => _forceHighFreqRepaint; set => _forceHighFreqRepaint = value; }
    [HideInInspector][SerializeField] private bool _autoResumeAfterScrub = true; public bool autoResumeAfterScrub { get => _autoResumeAfterScrub; set => _autoResumeAfterScrub = value; }

    private PlayableGraph _graph;
    private AnimationPlayableOutput _output;
    private AnimationClipPlayable _currentPlayable;
    private AnimationClip _currentClip;
    private GameObject _spawnedModel;
    private bool _isCrossfading;
    private float _crossTimer;
    private AnimationMixerPlayable _activeMixer;
    private AnimationClipPlayable _previousPlayable;
    // The clip currently fed to the playable graph. In preview we may use a sanitized clone.
    private bool _boundsApplied;
    private float _lastBoundsExtent;
    private bool _lastUpdateOffscreen;
    private float _lastClipLength = -1f;
    [HideInInspector][SerializeField] private bool _resetOnClipChange = false; public bool resetOnClipChange { get => _resetOnClipChange; set => _resetOnClipChange = value; }
    private double _lastEditorTime;
    private bool _isScrubbing;
    private double _lastScrubEditorTime;
    private const double _scrubResumeDelay = 0.25;

    // Original clip (as selected) and a sanitized preview clone used to suppress invalid AnimationEvents.
    private AnimationClip _currentClipOrig;
    [HideInInspector][SerializeField] private bool _ignoreAnimationEvents = true; public bool ignoreAnimationEvents { get => _ignoreAnimationEvents; set => _ignoreAnimationEvents = value; }
    private readonly Dictionary<AnimationClip, AnimationClip> _sanitizedCache = new Dictionary<AnimationClip, AnimationClip>();

    public event Action<AnimationClip> OnClipChanged;
    public event Action<bool> OnPlayStateChanged;
    public event Action<float> OnSeekNormalized;

    public int TotalFrames => _currentClip ? Mathf.Max(1, Mathf.RoundToInt(_currentClip.length * Mathf.Max(1f, previewFPS))) : 1;
    public int CurrentFrame => _currentClip ? Mathf.Clamp(Mathf.RoundToInt(CurrentSec * Mathf.Max(1f, previewFPS)), 0, TotalFrames) : 0;

    private void OnEnable()
    {
        EnsureModelAndAnimator();
        BuildGraphIfNeeded();
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
        _lastEditorTime = EditorApplication.timeSinceStartup;
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorTick;
        DestroyGraph();
        DespawnModelIfTemp();
        ClearSanitizedCache();
    }

    private void OnValidate()
    {
        EnsureModelAndAnimator();
        ApplyAnimatorFlags();
        ApplyVisibilityFix();
        RebindClipIfChanged();
        if (!play) SeekByNormalizedTime();
    }

    public void PrevClip()
    {
        if (clips.Count > 0) { clipIndex = (clipIndex - 1 + clips.Count) % clips.Count; RebindClip(true); }
    }

    private void TogglePlay()
    {
        play = !play;
    }

    public void Stop()
    {
        play = false;
        if (_isCrossfading)
        {
            if (_activeMixer.IsValid()) _activeMixer.Destroy();
            if (_previousPlayable.IsValid()) _previousPlayable.Destroy();
            _activeMixer = default;
            _previousPlayable = default;
            _isCrossfading = false;
        }
        if (sampleFirstOnStop) { normalizedTime = 0f; SeekByNormalizedTime(); }
    }

    public void NextClip()
    {
        if (clips.Count > 0) { clipIndex = (clipIndex + 1) % clips.Count; RebindClip(true); }
    }

    public void StepFrames(int frames)
    {
        if (_currentClip == null) return;
        var len = Mathf.Max(_currentClip.length, 0.0001f);
        var dt = frames / Mathf.Max(1f, previewFPS);
        var target = Mathf.Clamp(CurrentSec + dt, 0f, len);
        play = false;
        normalizedTime = Mathf.Clamp01(target / len);
        SeekByNormalizedTime();
    }

    public float DurationSec => _currentClip ? _currentClip.length : 0f;
    public float CurrentSec
    {
        get
        {
            if (_currentClip == null) return 0f;
            if (_currentPlayable.IsValid())
                return Mathf.Clamp((float)_currentPlayable.GetTime(), 0f, DurationSec);
            return Mathf.Clamp(normalizedTime * DurationSec, 0f, DurationSec);
        }
    }

    private void EnsureModelAndAnimator()
    {
        // Spawn assigned prefab if present
        if (!_spawnedModel && modelPrefab != null)
        {
            _spawnedModel = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, transform);
            _spawnedModel.name = modelPrefab.name;
        }

        // If no animator found in children, ensure there is a fallback Animator on the player root
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                // Create or reuse an Animator on this GameObject so the PlayableGraph has a valid target
                var existing = GetComponent<Animator>();
                if (existing == null)
                {
                    existing = gameObject.AddComponent<Animator>();
                    // Keep the animator disabled by default; it is only used as a target for sampling
                    existing.enabled = false;
                }
                animator = existing;
            }
        }
        if (_graph.IsValid() && _animator && !_output.IsOutputValid())
        {
            _output = AnimationPlayableOutput.Create(_graph, "Out", _animator);
        }
        ApplyAnimatorFlags();
        ApplyVisibilityFix();
    }

    private void ApplyAnimatorFlags()
    {
        if (!animator) return;
        animator.applyRootMotion = applyRootMotion;
        if (alwaysAnimate) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    private void ApplyVisibilityFix()
    {
        if (!autoFixSkinnedBounds || !_spawnedModel) return;
        if (_boundsApplied && Mathf.Approximately(_lastBoundsExtent, boundsExtent) && _lastUpdateOffscreen == updateSMRWhenOffscreen)
            return;
        var smrs = _spawnedModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in smrs)
        {
            smr.localBounds = new Bounds(Vector3.zero, Vector3.one * boundsExtent);
            smr.updateWhenOffscreen = updateSMRWhenOffscreen;
        }
        _boundsApplied = true;
        _lastBoundsExtent = boundsExtent;
        _lastUpdateOffscreen = updateSMRWhenOffscreen;
    }

    private void BuildGraphIfNeeded()
    {
        if (!animator || _graph.IsValid()) return;
        _graph = PlayableGraph.Create("UniversalAnimPlayer");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _output = AnimationPlayableOutput.Create(_graph, "Out", animator);
        _graph.Play();
        RebindClip(false);
    }

    private AnimationClip GetClipAtIndex()
    {
        if (clips == null || clips.Count == 0) return null;
        int idx = Mathf.Clamp(clipIndex, 0, clips.Count - 1);
        return clips[idx];
    }

    private bool IsHumanoid(AnimationClip c) => c && c.humanMotion;

    private void RebindClipIfChanged()
    {
        var c = GetClipAtIndex();
        if (c != _currentClipOrig)
        {
            if (resetOnClipChange) normalizedTime = 0f;
            RebindClip(true);
            return;
        }
        if (c && _currentClip && !Mathf.Approximately(_lastClipLength, _currentClip.length))
        {
            float prevLength = Mathf.Max(_lastClipLength, 0.0001f);
            float absT = normalizedTime * prevLength;
            _lastClipLength = _currentClip.length;
            normalizedTime = Mathf.Clamp01(absT / Mathf.Max(_currentClip.length, 0.0001f));
            RebindClip(false);
        }
    }

    private void RebindClip(bool doCrossfade)
    {
        if (!_graph.IsValid() || _output.Equals(default(AnimationPlayableOutput))) return;
        if (_isCrossfading)
        {
            if (_activeMixer.IsValid()) _activeMixer.Destroy();
            if (_previousPlayable.IsValid()) _previousPlayable.Destroy();
            _isCrossfading = false;
        }
        var c = GetClipAtIndex();
        bool changed = c != _currentClipOrig;
        _currentClipOrig = c;
        _currentClip = SelectPlayableClip(c);
        _lastClipLength = _currentClip ? _currentClip.length : -1f;
        if (resetOnClipChange && doCrossfade == false) normalizedTime = Mathf.Clamp01(normalizedTime);
        if (!_currentClip || !animator)
        {
            if (_currentPlayable.IsValid())
            {
                _currentPlayable.Destroy();
                _output.SetSourcePlayable(Playable.Null);
            }
            if (changed) OnClipChanged?.Invoke(_currentClipOrig);
            return;
        }
        if (retarget != RetargetMode.Generic && IsHumanoid(_currentClip))
        {
            // ensure humanoid avatar exists on animator
        }
        var newPlayable = AnimationClipPlayable.Create(_graph, _currentClip);
        newPlayable.SetApplyFootIK(true);
        newPlayable.SetApplyPlayableIK(true);
        newPlayable.SetSpeed(0);
        if (doCrossfade && _currentPlayable.IsValid() && crossFade > 0f)
        {
            var mixer = AnimationMixerPlayable.Create(_graph, 2);
            _graph.Connect(_currentPlayable, 0, mixer, 0);
            _graph.Connect(newPlayable, 0, mixer, 1);
            mixer.SetInputWeight(0, 1f);
            mixer.SetInputWeight(1, 0f);
            _output.SetSourcePlayable(mixer);
            StartCrossfade(mixer, _currentPlayable, newPlayable);
        }
        else
        {
            if (_currentPlayable.IsValid()) _currentPlayable.Destroy();
            _currentPlayable = newPlayable;
            _output.SetSourcePlayable(_currentPlayable);
            SeekByNormalizedTime();
        }
        if (changed)
        {
            if (autoAssignModelFromClip && modelPrefab == null && _currentClipOrig != null)
            {
                TryAutoAssignModelFromClip(_currentClipOrig, false);
            }
            OnClipChanged?.Invoke(_currentClipOrig);
        }
    }

    private void StartCrossfade(AnimationMixerPlayable mixer, AnimationClipPlayable fadingOut, AnimationClipPlayable fadingIn)
    {
        _previousPlayable = fadingOut;
        _currentPlayable = fadingIn;
        _crossTimer = 0f;
        _activeMixer = mixer;
        _isCrossfading = true;
    }

    private void Advance(float dt)
    {
        if (!animator || !_currentPlayable.IsValid() || _currentClip == null) return;
        float len = Mathf.Max(_currentClip.length, 0.0001f);
        if (!loop && !_isCrossfading)
        {
            double curEnd = _currentPlayable.GetTime();
            if (curEnd >= len)
            {
                if (play && !_isScrubbing)
                    normalizedTime = 1f;
                return;
            }
        }
        double t = _currentPlayable.GetTime();
        t += dt * Mathf.Max(0f, speed);
        if (loop) t = t % len; else t = Math.Min(t, len);
        _currentPlayable.SetTime(t);
        if (_isCrossfading && _activeMixer.IsValid())
        {
            _crossTimer += dt;
            float w = Mathf.Clamp01(_crossTimer / Mathf.Max(0.0001f, crossFade));
            _activeMixer.SetInputWeight(0, 1f - w);
            _activeMixer.SetInputWeight(1, w);
            if (w >= 1f)
            {
                _output.SetSourcePlayable(_currentPlayable);
                if (_previousPlayable.IsValid())
                {
                    _previousPlayable.Destroy();
                    _previousPlayable = default;
                }
                if (_activeMixer.IsValid()) _activeMixer.Destroy();
                _isCrossfading = false;
            }
        }
        _graph.Evaluate(0f);
        if (play && !_isScrubbing && _currentClip != null)
        {
            double cur = _currentPlayable.GetTime();
            if (loop) cur = cur % len; else cur = Math.Min(cur, len);
            normalizedTime = Mathf.Clamp01((float)(cur / len));
        }
    }

    private void SeekByNormalizedTime()
    {
        if (!_currentPlayable.IsValid() || _currentClip == null) return;
        float len = Mathf.Max(_currentClip.length, 0.0001f);
        double t = normalizedTime * len;
        _currentPlayable.SetTime(t);
        _graph.Evaluate(0f);
    }

    private void DestroyGraph()
    {
        if (_graph.IsValid()) _graph.Destroy();
        _currentPlayable = default;
        _previousPlayable = default;
        _activeMixer = default;
        _isCrossfading = false;
    }

    public void DespawnModelIfTemp()
    {
        if (_spawnedModel)
        {
            DestroyImmediate(_spawnedModel);
            _spawnedModel = null;
        }
    }

    private void EditorTick()
    {
        if (!this || !gameObject) return;
        EnsureModelAndAnimator();
        BuildGraphIfNeeded();
        ApplyAnimatorFlags();
        RebindClipIfChanged();
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)Math.Max(0.0, now - _lastEditorTime);
        _lastEditorTime = now;
        if (play)
        {
            double step = 1.0 / Math.Max(15, targetUpdateFPS);
            double remaining = dt;
            int steps = 0;
            while (remaining > 0.000001 && steps < maxSubsteps)
            {
                float adv = (float)Math.Min(step, remaining);
                Advance(adv);
                remaining -= adv;
                steps++;
            }
        }
        else
        {
            SeekByNormalizedTime();
        }
        if (_isScrubbing)
        {
            double idle = now - _lastScrubEditorTime;
            if (idle > _scrubResumeDelay)
            {
                _isScrubbing = false;
                play = false;
            }
        }
        EditorApplication.QueuePlayerLoopUpdate();
        if (forceHighFreqRepaint)
        {
            SceneView.RepaintAll();
            InternalEditorUtility.RepaintAllViews();
        }
    }

    public bool IsPlaying => play;
    public float Normalized => normalizedTime;
    public float CurrentSeconds => CurrentSec;
    public int TotalFramesAt(float fps) => _currentClip ? Mathf.Max(1, Mathf.RoundToInt(_currentClip.length * Mathf.Max(1f, fps))) : 1;
    public int CurrentFrameAt(float fps)
    {
        if (_currentClip == null) return 0;
        int total = TotalFramesAt(fps);
        int cur = Mathf.Clamp(Mathf.RoundToInt(CurrentSec * Mathf.Max(1f, fps)), 0, total);
        return cur;
    }
    public void SetLoop(bool v) { loop = v; }
    public void SetSpeed(float v) { speed = Mathf.Max(0f, v); }
    public void Play(bool restart = false)
    {
        if (restart)
        {
            normalizedTime = 0f;
            SeekByNormalizedTime();
        }
        bool prev = play;
        play = true;
        if (play != prev) OnPlayStateChanged?.Invoke(play);
    }
    public void Pause() { play = false; }
    public void StopAndReset(bool resetFirstFrame = true)
    {
        Stop();
        if (resetFirstFrame)
        {
            normalizedTime = 0f;
            SeekByNormalizedTime();
        }
    }
    public void SeekNormalized(float n)
    {
        float clamped = Mathf.Clamp01(n);
        // Mark scrubbing and record time so EditorTick can resume correctly
        _isScrubbing = true;
        _lastScrubEditorTime = EditorApplication.timeSinceStartup;
        if (!Mathf.Approximately(clamped, normalizedTime))
        {
            normalizedTime = clamped;
            SeekByNormalizedTime();
            OnSeekNormalized?.Invoke(normalizedTime);
        }
        else
        {
            SeekByNormalizedTime();
        }
    }
    public void SeekFrame(int frame, float fps)
    {
        if (_currentClip == null) return;
        fps = Mathf.Max(1f, fps);
        int total = TotalFramesAt(fps);
        frame = Mathf.Clamp(frame, 0, total);
        float len = Mathf.Max(_currentClip.length, 0.0001f);
        float sec = frame / fps;
        float n = Mathf.Clamp01(sec / len);
        // Mark scrubbing and record time so EditorTick can resume correctly
        _isScrubbing = true;
        _lastScrubEditorTime = EditorApplication.timeSinceStartup;
        if (!Mathf.Approximately(n, normalizedTime))
        {
            normalizedTime = n;
            SeekByNormalizedTime();
            OnSeekNormalized?.Invoke(normalizedTime);
        }
        else
        {
            SeekByNormalizedTime();
        }
    }
    public void SetClip(AnimationClip clip, bool resetTime)
    {
        if (clip == null)
        {
            clips.Clear();
            clipIndex = 0;
            RebindClip(false);
            return;
        }
        if (clips.Count == 1 && clips[0] == clip && !resetTime)
        {
            return;
        }
        TryAutoAssignModelFromClip(clip, false);
        clips.Clear();
        clips.Add(clip);
        clipIndex = 0;
        if (resetTime) normalizedTime = 0f;
        RebindClip(false);
        SeekByNormalizedTime();
    }

    private AnimationClip SelectPlayableClip(AnimationClip original)
    {
        if (original == null) return null;
        if (!ignoreAnimationEvents) return original;
        // Return cached sanitized clone, or create one.
        if (_sanitizedCache.TryGetValue(original, out var cached) && cached)
            return cached;
        var clone = Instantiate(original);
        clone.name = original.name + " (Preview)";
        clone.hideFlags = HideFlags.HideAndDontSave;
        var events = AnimationUtility.GetAnimationEvents(clone);
        if (events != null && events.Length > 0)
        {
            // Keep only events with a valid function name to avoid Unity errors during sampling.
            List<AnimationEvent> filtered = new List<AnimationEvent>(events.Length);
            for (int i = 0; i < events.Length; i++)
            {
                var ev = events[i];
                if (!string.IsNullOrEmpty(ev.functionName))
                    filtered.Add(ev);
            }
            if (filtered.Count != events.Length)
            {
                AnimationUtility.SetAnimationEvents(clone, filtered.ToArray());
            }
        }
        _sanitizedCache[original] = clone;
        return clone;
    }

    private void ClearSanitizedCache()
    {
        if (_sanitizedCache.Count == 0) return;
        foreach (var kv in _sanitizedCache)
        {
            if (kv.Value)
            {
                DestroyImmediate(kv.Value);
            }
        }
        _sanitizedCache.Clear();
    }

    private void TryAutoAssignModelFromClip(AnimationClip clip, bool forced)
    {
        if (!autoAssignModelFromClip || clip == null) return;
        if (modelPrefab != null) return;
        string path = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(path)) return;
        bool assigned = false;
        int scanned = 0;
        if (autoAssignScanFolder)
        {
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { dir });
                foreach (var g in guids)
                {
                    string fpath = AssetDatabase.GUIDToAssetPath(g);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(fpath);
                    if (!go) continue;
                    scanned++;
                    var type = PrefabUtility.GetPrefabAssetType(go);
                    if (type != PrefabAssetType.Regular && type != PrefabAssetType.Variant) continue;
                    if (go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
                    modelPrefab = go;
                    assigned = true;
                    if (autoAssignVerbose) Debug.Log($"[UniversalAnimPlayer] Auto-assigned model by prefab scan: {fpath} (clip:{path})", this);
                    _lastAutoAssignStatus = $"Prefab scan assigned: {go.name} (scanned {scanned})";
                    break;
                }
                if (!assigned)
                {
                    _lastAutoAssignStatus = $"Prefab scan failed (scanned {scanned})";
                }
            }
        }
        if (!assigned)
        {
            if (string.IsNullOrEmpty(_lastAutoAssignStatus) || _lastAutoAssignStatus == "<idle>")
                _lastAutoAssignStatus = "No suitable Prefab found";
        }
        if (!assigned && forced && autoAssignVerbose)
        {
            Debug.LogWarning($"[UniversalAnimPlayer] Auto model assignment failed for clip: {path}. Provide a Prefab manually.", this);
        }
        if (assigned == false && forced)
        {
            InternalEditorUtility.RepaintAllViews();
        }
    }
}
#endif
