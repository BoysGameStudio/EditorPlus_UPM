#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EditorPlus.AnimationPreview;
using UnityEngine.Rendering;

public class AnimationPreviewStage : PreviewSceneStage
{
    private const int MaxOutlineSlots = 16;
    private const string OutlineColorsProperty = "_OutlineColors";
    private const string OutlineColorCountProperty = "_OutlineColorCount";
    private static readonly int SceneTimelineOutlineColorsId = Shader.PropertyToID(OutlineColorsProperty);
    private static readonly int SceneTimelineOutlineColorCountId = Shader.PropertyToID(OutlineColorCountProperty);
    private static readonly Vector4[] OutlineUpload = BuildUploadBuffer();
    private static readonly PaletteEntry[] EmptyPalette = Array.Empty<PaletteEntry>();

    private Transform _originalParentTransform;
    private int _originalSiblingIndex = -1;
    private Scene _originalScene;
    public GameObject Target;
    private GameObject _stageInstance; // preview instance created in the stage (when using Prefab)
    private bool _movedOriginal;       // whether we moved the original object into the stage
    private GameObject _previewRoot;   // the actual root used for preview (instance or original)
    private readonly List<ITimelineTrackColorReceiver> _colorReceivers = new List<ITimelineTrackColorReceiver>(8);
    private readonly Dictionary<string, PaletteEntry> _paletteByLabel = new Dictionary<string, PaletteEntry>(StringComparer.Ordinal);

    // Bind UniversalAnimPlayer (if present) to preview instance animator and suppress player's own local spawn during isolation
    private Animator _originalPlayerAnimator;
    private GameObject _originalPlayerModelPrefab;
    private bool _suppressedLocalSpawn;
    private AnimationPreviewPlayer _playerInstance; // direct reference

    // Material swapping to hide fill in preview (show outline only)
    private readonly Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>(64);
    private Material _depthOnlyMat;

    protected override GUIContent CreateHeaderContent()
    {
        return new GUIContent(Target ? $"Preview: {Target.name}" : "Preview Object");
    }

    protected override bool OnOpenStage()
    {
        base.OnOpenStage();
        if (Target)
        {
            // Attempt to instantiate a Prefab for preview; fallback to moving the original
            if (TryInstantiatePlayerModelPrefab(Target, scene, out _stageInstance))
            {
                _movedOriginal = false;
                _previewRoot = _stageInstance;
                Selection.activeObject = _stageInstance;

                // Ensure a binder exists to publish outline indices and receive palette updates
                EnsurePreviewBinder(_previewRoot);

                // Swap renderers to DepthOnly so only the outline feature draws color
                ApplyDepthOnlyToPreviewRenderers(_previewRoot);

                // Re-route UniversalAnimPlayer to drive the animator on the preview instance
                _playerInstance = Target ? Target.GetComponent<AnimationPreviewPlayer>() : null;
                if (_playerInstance != null)
                {
                    _originalPlayerAnimator = _playerInstance.animator;
                    _originalPlayerModelPrefab = _playerInstance.modelPrefab;

                    var instAnimator = _stageInstance.GetComponentInChildren<Animator>(true);
                    if (instAnimator != null)
                    {
                        // Suppress player's own local spawned model to avoid duplicate visuals in original scene
                        if (_originalPlayerModelPrefab != null)
                        {
                            _suppressedLocalSpawn = true;
                            _playerInstance.modelPrefab = null; // setter also despawns via EnsureModelAndAnimator
                            _playerInstance.DespawnModelIfTemp();
                        }
                        _playerInstance.animator = instAnimator; // bind output to stage instance
                    }
                    else
                    {
                        Debug.LogWarning("[Preview] The preview Prefab has no Animator; animation may not play.");
                    }
                }
            }
            else
            {
                // If prefab instantiation failed, fall back to moving the provided target into the stage
                // so the preview scene contains the preview root and the player can be inspected.
                    _originalScene = Target.scene;
                    _originalParentTransform = Target.transform.parent;
                    _originalSiblingIndex = Target.transform.GetSiblingIndex();
                    SceneManager.MoveGameObjectToScene(Target, scene);
                    _movedOriginal = true;
                    _previewRoot = Target;
                    Selection.activeObject = Target;

                    // Ensure binder and depth-only materials are applied to the moved original
                    EnsurePreviewBinder(_previewRoot);
                    ApplyDepthOnlyToPreviewRenderers(_previewRoot);

                    // Re-route player's animator to any animator on the moved original
                    _playerInstance = Target ? Target.GetComponent<AnimationPreviewPlayer>() : null;
                    if (_playerInstance != null)
                    {
                        var instAnimator = _previewRoot.GetComponentInChildren<Animator>(true);
                        if (instAnimator != null)
                        {
                            _playerInstance.animator = instAnimator;
                        }
                        else
                        {
                            Debug.LogWarning("[Preview] The preview object has no Animator; animation may not play.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[Preview] No AnimationPreviewPlayer found on Target. Preview may be limited.");
                    }
            }
        }

        EnsureSceneTimelineOutlineFeature(_previewRoot);
        CacheColorReceivers();
        PaletteBus.Changed -= OnSceneTimelinePaletteChanged;
        PaletteBus.Changed += OnSceneTimelinePaletteChanged;
        // Ensure shaders and receivers start from a clean state.
        PushOutlineColors(EmptyPalette);
        DistributePalette(EmptyPalette);
        return true;
    }

    protected override void OnCloseStage()
    {
        base.OnCloseStage();
        PaletteBus.Changed -= OnSceneTimelinePaletteChanged;
        PushOutlineColors(EmptyPalette);
        DistributePalette(EmptyPalette);
        _colorReceivers.Clear();
        _paletteByLabel.Clear();

        if (_movedOriginal && Target)
        {
            // Restore back to original scene if still valid
            if (_originalScene.IsValid() && _originalParentTransform != null)
            {
                SceneManager.MoveGameObjectToScene(Target, _originalScene);
                if (_originalParentTransform)
                {
                    Target.transform.SetParent(_originalParentTransform, false);
                }
                if (_originalSiblingIndex >= 0)
                {
                    Target.transform.SetSiblingIndex(Mathf.Clamp(_originalSiblingIndex, 0, Target.transform.parent ? Target.transform.parent.childCount - 1 : _originalSiblingIndex));
                }
            }
        }

        if (_stageInstance)
        {
            UnityEngine.Object.DestroyImmediate(_stageInstance);
            _stageInstance = null;
        }
        // Restore original materials on preview renderers
        RestorePreviewRendererMaterials();
        if (_depthOnlyMat != null)
        {
            UnityEngine.Object.DestroyImmediate(_depthOnlyMat);
            _depthOnlyMat = null;
        }
        // Restore UniversalAnimPlayer bindings/state
        if (Target && _playerInstance != null)
        {
            // Restore animator target
            var currentAnim = _playerInstance.animator;
            if (_originalPlayerAnimator != null && currentAnim != _originalPlayerAnimator)
            {
                _playerInstance.animator = _originalPlayerAnimator;
            }
            _originalPlayerAnimator = null;

            // Restore original model prefab (and allow local spawn again)
            if (_suppressedLocalSpawn)
            {
                _playerInstance.modelPrefab = _originalPlayerModelPrefab; // setter will respawn as needed
                _suppressedLocalSpawn = false;
            }
            _originalPlayerModelPrefab = null;
            _playerInstance = null;
        }
        _previewRoot = null;
        _movedOriginal = false;
    }

    private void OnSceneTimelinePaletteChanged(IReadOnlyList<PaletteEntry> entries)
    {
        PushOutlineColors(entries ?? EmptyPalette);
        DistributePalette(entries ?? EmptyPalette);
    }

    private static void PushOutlineColors(IReadOnlyList<PaletteEntry> entries)
    {
        int requestedCount = entries?.Count ?? 0;
        int count = Mathf.Clamp(requestedCount, 0, MaxOutlineSlots);

        for (int i = 0; i < MaxOutlineSlots; i++)
        {
            if (i < count)
            {
                Color c = entries[i].OutlineColor;
                OutlineUpload[i] = new Vector4(c.r, c.g, c.b, c.a);
            }
            else
            {
                OutlineUpload[i] = Vector4.zero;
            }
        }

        Shader.SetGlobalFloat(SceneTimelineOutlineColorCountId, count);
        Shader.SetGlobalVectorArray(SceneTimelineOutlineColorsId, OutlineUpload);
    }

    private static Vector4[] BuildUploadBuffer() => new Vector4[MaxOutlineSlots];

    private void CacheColorReceivers()
    {
        _colorReceivers.Clear();

        if (_previewRoot == null)
        {
            return;
        }

        var behaviours = _previewRoot.GetComponentsInChildren<MonoBehaviour>(true);
        if (behaviours == null || behaviours.Length == 0)
        {
            return;
        }

        for (int i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            if (behaviour is ITimelineTrackColorReceiver receiver && !_colorReceivers.Contains(receiver))
            {
                _colorReceivers.Add(receiver);
            }
        }
    }

    private void DistributePalette(IReadOnlyList<PaletteEntry> entries)
    {
        _paletteByLabel.Clear();

        if (entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrEmpty(entry.Label))
                {
                    continue;
                }

                _paletteByLabel[entry.Label] = entry;
            }
        }

        if (_colorReceivers.Count == 0)
        {
            CacheColorReceivers();
        }

        if (_colorReceivers.Count == 0)
        {
            return;
        }

        for (int i = _colorReceivers.Count - 1; i >= 0; i--)
        {
            var receiver = _colorReceivers[i];
            if (receiver == null)
            {
                _colorReceivers.RemoveAt(i);
                continue;
            }

            bool layeredApplied = receiver is ITimelineTrackLayerPaletteReceiver layeredReceiver
                ? layeredReceiver.ApplyLayeredTimelinePalette(_paletteByLabel)
                : false;

            string label = receiver.TrackLabel;
            if (!string.IsNullOrEmpty(label) && _paletteByLabel.TryGetValue(label, out var entry))
            {
                receiver.ApplyTimelinePalette(entry);
            }
            else if (!layeredApplied)
            {
                receiver.ClearTimelinePalette();
            }
        }
    }

    private static void EnsureSceneTimelineOutlineFeature(GameObject previewRoot)
    {
            RenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (pipelineAsset == null)
            {
                pipelineAsset = GraphicsSettings.defaultRenderPipeline;
            }
            if (pipelineAsset == null)
            {
                pipelineAsset = QualitySettings.renderPipeline;
            }
            if (pipelineAsset == null)
            {
                return;
            }

            var pipelineTypeName = pipelineAsset.GetType().FullName;
            if (string.IsNullOrEmpty(pipelineTypeName) || pipelineTypeName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            var rendererData = GetPrimaryRendererData(pipelineAsset);
            if (rendererData == null)
            {
                return;
            }

            bool rendererEditable = AssetDatabase.IsOpenForEdit(rendererData);

            var outlineFeatureType = FindOutlineFeatureType();
            if (outlineFeatureType == null)
            {
                return;
            }

            var featuresField = rendererData.GetType().GetField("m_RendererFeatures", BindingFlags.Instance | BindingFlags.NonPublic);
            if (featuresField == null)
            {
                return;
            }

            if (featuresField.GetValue(rendererData) is not IList featuresList)
            {
                return;
            }

            ScriptableObject featureInstance = null;
            for (int i = 0; i < featuresList.Count; i++)
            {
                if (featuresList[i] is ScriptableObject candidate && candidate != null && outlineFeatureType.IsInstanceOfType(candidate))
                {
                    featureInstance = candidate;
                    break;
                }
            }

            bool featureAdded = false;
            if (featureInstance == null)
            {
                if (!rendererEditable)
                {
                    Debug.LogWarning("[Preview] RendererData is not editable and outline feature is missing; cannot add feature. Will attempt to configure if it appears later.");
                    return;
                }
                featureInstance = ScriptableObject.CreateInstance(outlineFeatureType) as ScriptableObject;
                if (featureInstance == null)
                {
                    return;
                }
                featureInstance.name = "OutlineFeature";
                // featureInstance.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                AssetDatabase.AddObjectToAsset(featureInstance, rendererData);
                // Use SerializedObject to append to m_RendererFeatures to avoid YAML block assertion issues
                if (!AddFeatureToRendererDataSerialized(rendererData, featureInstance))
                {
                    // Fallback to list add if serialized path failed
                    featuresList.Add(featureInstance);
                }
                featureAdded = true;

            }

            bool settingsDirty = ConfigureOutlineFeatureInstance(featureInstance, previewRoot);
            if (rendererEditable && (featureAdded || settingsDirty))
            {
                EditorUtility.SetDirty(featureInstance);
                EditorUtility.SetDirty(rendererData);
                var path = AssetDatabase.GetAssetPath(rendererData);
                if (!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.ImportAsset(path);
                }
                AssetDatabase.SaveAssets();
            }
    }

    private static bool AddFeatureToRendererDataSerialized(ScriptableObject rendererData, ScriptableObject feature)
    {
        if (rendererData == null || feature == null)
        {
            return false;
        }

            var so = new SerializedObject(rendererData);
            var featuresProp = so.FindProperty("m_RendererFeatures");
            if (featuresProp == null || !featuresProp.isArray)
            {
                return false;
            }

            int newIndex = featuresProp.arraySize;
            featuresProp.InsertArrayElementAtIndex(newIndex);
            var element = featuresProp.GetArrayElementAtIndex(newIndex);
            element.objectReferenceValue = feature;

            // Optionally clear the feature map so Unity rebuilds it (if present)
            var mapProp = so.FindProperty("m_RendererFeatureMap");
            if (mapProp != null && mapProp.isArray)
            {
                mapProp.ClearArray();
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
    }

    private static bool TryInstantiatePlayerModelPrefab(GameObject source, Scene targetScene, out GameObject instance)
    {
        instance = null;
        if (source == null)
        {
            return false;
        }

        // Read the model prefab from UniversalAnimPlayer (if present) on the preview root
        var player = source.GetComponent<AnimationPreviewPlayer>();
        if (player == null)
        {
            return false;
        }
        var asset = player.modelPrefab;
        if (asset == null)
        {
            return false;
        }
        var prefabType = PrefabUtility.GetPrefabAssetType(asset);
        if (prefabType != PrefabAssetType.Regular && prefabType != PrefabAssetType.Variant)
        {
            Debug.LogWarning($"[Preview] Assigned modelPrefab '{asset.name}' is not a Prefab asset. Please assign a Prefab.");
            return false;
        }

        // Ensure it contains a SkinnedMeshRenderer
        if (!PrefabAssetHasSkinnedMesh(asset))
        {
            Debug.LogWarning($"[Preview] Prefab '{asset.name}' has no SkinnedMeshRenderer. Please assign a character Prefab with SMR.");
            return false;
        }

        return InstantiateAsset(asset, source, targetScene, out instance);
    }

    private static bool InstantiateAsset(GameObject asset, GameObject source, Scene targetScene, out GameObject instance)
    {
        instance = null;
        if (asset == null) return false;
        var type = PrefabUtility.GetPrefabAssetType(asset);
        if (type != PrefabAssetType.Regular && type != PrefabAssetType.Variant)
        {
            return false;
        }

        var obj = PrefabUtility.InstantiatePrefab(asset) as GameObject;
        if (obj == null)
        {
            return false;
        }

        SceneManager.MoveGameObjectToScene(obj, targetScene);
        // Match transform to source
        obj.transform.position = source.transform.position;
        obj.transform.rotation = source.transform.rotation;
        obj.transform.localScale = source.transform.localScale;
        instance = obj;
        return true;
    }

    private static bool PrefabAssetHasSkinnedMesh(GameObject asset)
    {
        if (asset == null) return false;
        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path)) return false;
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        if (contents == null) return false;
        var smrs = contents.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        bool has = smrs != null && smrs.Length > 0;
        PrefabUtility.UnloadPrefabContents(contents);
        return has;
    }

    // Note: Prefab must contain at least one SkinnedMeshRenderer for preview; FBX origin not required.

    private static ScriptableObject GetPrimaryRendererData(RenderPipelineAsset pipelineAsset)
    {
        var pipelineType = pipelineAsset.GetType();

        var rendererDataProperty = pipelineType.GetProperty("scriptableRendererData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (rendererDataProperty != null)
        {
            var value = rendererDataProperty.GetValue(pipelineAsset);
            if (value is ScriptableObject rendererDataFromProperty && rendererDataFromProperty != null)
            {
                return rendererDataFromProperty;
            }
            if (value is IEnumerable enumerable)
            {
                foreach (object element in enumerable)
                {
                    if (element is ScriptableObject rendererData && rendererData != null)
                    {
                        return rendererData;
                    }
                }
            }
        }

        var rendererListField = pipelineType.GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
        if (rendererListField != null && rendererListField.GetValue(pipelineAsset) is IList list && list.Count > 0)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is ScriptableObject rendererData && rendererData != null)
                {
                    return rendererData;
                }
            }

            var defaultIndexField = pipelineType.GetField("m_DefaultRendererIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            if (defaultIndexField != null)
            {
                int defaultIndex = Convert.ToInt32(defaultIndexField.GetValue(pipelineAsset));
                if (defaultIndex >= 0 && defaultIndex < list.Count && list[defaultIndex] is ScriptableObject defaultRenderer && defaultRenderer != null)
                {
                    return defaultRenderer;
                }
            }
        }

        return null;
    }

    private static Type FindOutlineFeatureType()
    {
        // Only support the concise standalone OutlineFeature in ShaderLab package.
        const string outlineTypeName = "BoysGameStudio.ShaderLab.Renderer.OutlineFeature";
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            var assembly = assemblies[i];
            var candidate = assembly.GetType(outlineTypeName, false);
            if (candidate != null)
            {
                return candidate;
            }
        }
        return null;
    }

    private static bool ConfigureOutlineFeatureInstance(ScriptableObject featureInstance, GameObject previewRoot)
    {
        bool dirty = false;
        if (featureInstance == null) return false;

        var featureType = featureInstance.GetType();
        var settingsProperty = featureType.GetProperty("FeatureSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object settings = settingsProperty?.GetValue(featureInstance);
        if (settings == null) return false;

        var settingsType = settings.GetType();

        FieldInfo FindField(string name) => settingsType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        // Helper to set field if different
        bool SetIfDifferent(FieldInfo field, object value)
        {
            if (field == null) return false;
            var current = field.GetValue(settings);
            if (!Equals(current, value))
            {
                field.SetValue(settings, value);
                return true;
            }
            return false;
        }

        // Shader
        var shaderField = FindField("shader");
        if (shaderField != null && shaderField.GetValue(settings) is not Shader)
        {
            Shader locatedShader = null;
            string[] candidates = { "Hidden/Quantum/SceneTimelineOutline", "Hidden/SceneTimelineOutline", "Hidden/BoysGameStudio/SceneTimelineOutline" };
            foreach (var name in candidates)
            {
                locatedShader = Shader.Find(name);
                if (locatedShader != null) break;
            }
            if (locatedShader != null) dirty |= SetIfDifferent(shaderField, locatedShader);
        }

        // Scene View
        dirty |= SetIfDifferent(FindField("sceneView"), true);

        // Game View
        dirty |= SetIfDifferent(FindField("gameView"), false);

        // Layers
        var layerMaskField = FindField("layers");
        if (layerMaskField != null)
        {
            int maskValue = -1;
            if (previewRoot != null)
            {
                maskValue = 0;
                var rends = previewRoot.GetComponentsInChildren<Renderer>(true);
                if (rends != null)
                {
                    foreach (var r in rends)
                    {
                        if (r != null) maskValue |= (1 << r.gameObject.layer);
                    }
                }
                maskValue |= (1 << previewRoot.layer);
                if (maskValue == 0) maskValue = -1;
            }
            var fieldType = layerMaskField.FieldType;
            object value = fieldType == typeof(int) ? maskValue : new UnityEngine.LayerMask { value = maskValue };
            dirty |= SetIfDifferent(layerMaskField, value);
        }

        // Reversed Z
        var reversedZField = FindField("z");
        if (reversedZField != null && reversedZField.FieldType.IsEnum)
        {
            var desired = Enum.ToObject(reversedZField.FieldType, 0);
            dirty |= SetIfDifferent(reversedZField, desired);
        }

        // Render Pass Event
        var renderPassEventField = FindField("passEvent");
        if (renderPassEventField != null && renderPassEventField.FieldType.IsEnum)
        {
            var names = Enum.GetNames(renderPassEventField.FieldType);
            string match = Array.Find(names, n => string.Equals(n, "AfterRenderingTransparents", StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                var desired = Enum.Parse(renderPassEventField.FieldType, match, true);
                dirty |= SetIfDifferent(renderPassEventField, desired);
            }
        }

        // Use Palette Alpha
        dirty |= SetIfDifferent(FindField("useAlpha"), false);

        // Thickness Space
        var thicknessSpaceField = FindField("thicknessSpace");
        if (thicknessSpaceField != null && thicknessSpaceField.GetValue(settings) is float ts && Math.Abs(ts - 1.0f) > 1e-6f)
        {
            dirty |= SetIfDifferent(thicknessSpaceField, 1.0f);
        }

        // Min Screen Thickness
        var minScreenThicknessField = FindField("minScreenPx");
        if (minScreenThicknessField != null && minScreenThicknessField.GetValue(settings) is float ms && ms < 6.0f)
        {
            dirty |= SetIfDifferent(minScreenThicknessField, 6.0f);
        }

        // Outline Width
        var outlineWidthField = FindField("width");
        if (outlineWidthField != null && outlineWidthField.GetValue(settings) is float ow && Math.Abs(ow - 6.0f) > 1e-6f)
        {
            dirty |= SetIfDifferent(outlineWidthField, 6.0f);
        }

        // Silhouette Threshold
        var silhouetteThresholdField = FindField("silhouette");
        if (silhouetteThresholdField != null && silhouetteThresholdField.GetValue(settings) is float th && Math.Abs(th - 0.5f) > 1e-6f)
        {
            dirty |= SetIfDifferent(silhouetteThresholdField, 0.5f);
        }

        // Feather
        var silhouetteFeatherField = FindField("feather");
        if (silhouetteFeatherField != null && silhouetteFeatherField.GetValue(settings) is float sf && Math.Abs(sf - 0.1f) > 1e-6f)
        {
            dirty |= SetIfDifferent(silhouetteFeatherField, 0.1f);
        }

        // Debug
        dirty |= SetIfDifferent(FindField("debug"), false);

        // Activate and create
        var setActiveMethod = featureType.GetMethod("SetActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        setActiveMethod?.Invoke(featureInstance, new object[] { true });

        var createMethod = featureType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        createMethod?.Invoke(featureInstance, null);

        return dirty;
    }

    // Removed legacy mask helpers.

    private static void EnsurePreviewBinder(GameObject root)
    {
        if (!root) return;
        // If any receiver already exists, skip
        var existingReceiver = root.GetComponentInChildren<EditorPlus.AnimationPreview.ITimelineTrackColorReceiver>(true);
        if (existingReceiver != null) return;

        // Add a default TimelineTrackPreviewBinder on the root so it can publish outline indices to globals
        root.AddComponent<EditorPlus.AnimationPreview.TimelinePaletteBinder>();
    }

    private void ApplyDepthOnlyToPreviewRenderers(GameObject root)
    {
        if (!root) return;
        if (_depthOnlyMat == null)
        {
            var depthShader = Shader.Find("Hidden/Universal Render Pipeline/DepthOnly");
            if (depthShader != null)
            {
                _depthOnlyMat = new Material(depthShader) { name = "__Preview_DepthOnly__" };
            }
        }
        if (_depthOnlyMat == null)
        {
            return; // fallback: do nothing if depth shader missing
        }

        _originalMaterials.Clear();
        var rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0) return;
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (!r) continue;
            var originals = r.sharedMaterials;
            if (originals == null || originals.Length == 0) continue;
            _originalMaterials[r] = originals;
            // Assign depth-only to all slots
            var reps = new Material[originals.Length];
            for (int s = 0; s < reps.Length; s++) reps[s] = _depthOnlyMat;
            r.sharedMaterials = reps;
        }
    }

    private void RestorePreviewRendererMaterials()
    {
        if (_originalMaterials.Count == 0) return;
        foreach (var kv in _originalMaterials)
        {
            var r = kv.Key;
            var mats = kv.Value;
            if (!r) continue;
            r.sharedMaterials = mats;
        }
        _originalMaterials.Clear();
    }
}

// Timeline receiver contracts moved to EditorPlus.AnimationPreview namespace (see SceneTimelineTypes.cs)

// Reflection helpers removed – using typed UniversalAnimPlayer access.
#endif
