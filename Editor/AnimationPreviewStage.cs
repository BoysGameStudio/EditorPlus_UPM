#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Experimental.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EditorPlus.AnimationPreview;
using EditorPlus.Preview;
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

    private readonly Transform _originalParentTransform;
    private readonly int _originalSiblingIndex = -1;
    private readonly Scene _originalScene;
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
    private UniversalAnimPlayer _playerInstance; // direct reference

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
                _playerInstance = Target ? Target.GetComponent<UniversalAnimPlayer>() : null;
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
                // Record original placement to restore later
                // Strict prefab-only: do not move original; preview will be skipped until a valid prefab is provided
                _movedOriginal = false;
                _previewRoot = null;
                Debug.LogWarning("[Preview] No valid character Prefab found on UniversalAnimPlayer.modelPrefab. Please assign a Prefab with a SkinnedMeshRenderer.");
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

    private static Vector4[] BuildUploadBuffer()
    {
        var buffer = new Vector4[MaxOutlineSlots];
        for (int i = 0; i < MaxOutlineSlots; i++)
        {
            buffer[i] = Vector4.zero;
        }

        return buffer;
    }

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
        try
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
                featureInstance.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
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
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to ensure outline feature: {ex.Message}");
        }
    }

    private static bool AddFeatureToRendererDataSerialized(ScriptableObject rendererData, ScriptableObject feature)
    {
        if (rendererData == null || feature == null)
        {
            return false;
        }

        try
        {
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
        catch
        {
            return false;
        }
    }

    private static bool TryInstantiatePlayerModelPrefab(GameObject source, Scene targetScene, out GameObject instance)
    {
        instance = null;
        if (source == null)
        {
            return false;
        }

        // Read the model prefab from UniversalAnimPlayer (if present) on the preview root
        var player = source.GetComponent<UniversalAnimPlayer>();
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
        var type = PrefabAssetType.NotAPrefab;
        try { type = PrefabUtility.GetPrefabAssetType(asset); } catch { }
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
        GameObject contents = null;
        try
        {
            contents = PrefabUtility.LoadPrefabContents(path);
            if (contents == null) return false;
            var smrs = contents.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            return smrs != null && smrs.Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (contents != null)
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
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
            try
            {
                var candidate = assembly.GetType(outlineTypeName, false);
                if (candidate != null)
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore load issues for dynamic assemblies.
            }
        }
        return null;
    }

    private static bool ConfigureOutlineFeatureInstance(ScriptableObject featureInstance, GameObject previewRoot)
    {
        bool dirty = false;
        if (featureInstance == null)
        {
            return false;
        }

        const HideFlags desiredHideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
        if (featureInstance.hideFlags != desiredHideFlags)
        {
            featureInstance.hideFlags = desiredHideFlags;
            dirty = true;
        }

        var featureType = featureInstance.GetType();
        var settingsProperty = featureType.GetProperty("FeatureSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object settings = settingsProperty?.GetValue(featureInstance);
        if (settings != null)
        {
            var settingsType = settings.GetType();

            FieldInfo FindField(string name)
            {
                return settingsType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            var shaderField = FindField("shader");
            if (shaderField != null)
            {
                if (shaderField.GetValue(settings) is not Shader shader || shader == null)
                {
                    // Try a few likely shader names to keep this feature generic across projects
                    string[] candidates = new[]
                    {
                        "Hidden/Quantum/SceneTimelineOutline",
                        "Hidden/SceneTimelineOutline",
                        "Hidden/BoysGameStudio/SceneTimelineOutline",
                    };
                    Shader locatedShader = null;
                    for (int i = 0; i < candidates.Length && locatedShader == null; i++)
                    {
                        var s = Shader.Find(candidates[i]);
                        if (s != null) locatedShader = s;
                    }
                    if (locatedShader != null)
                    {
                        shaderField.SetValue(settings, locatedShader);
                        dirty = true;
                    }
                }
            }

            var runSceneField = FindField("sceneView");
            if (runSceneField != null && runSceneField.GetValue(settings) is bool runScene && !runScene)
            {
                runSceneField.SetValue(settings, true);
                dirty = true;
            }

            var runGameField = FindField("gameView");
            if (runGameField != null)
            {
                bool desired = false; // restore strict: SceneView only by default
                if (runGameField.GetValue(settings) is bool current && current != desired)
                {
                    runGameField.SetValue(settings, desired);
                    dirty = true;
                }
            }

            var layerMaskField = FindField("layers");
            if (layerMaskField != null)
            {
                // Include all layers used by the preview root and its child renderers; fallback to Everything
                int maskValue = -1;
                if (previewRoot != null)
                {
                    maskValue = 0;
                    try
                    {
                        var rends = previewRoot.GetComponentsInChildren<Renderer>(true);
                        if (rends != null && rends.Length > 0)
                        {
                            for (int i = 0; i < rends.Length; i++)
                            {
                                var r = rends[i];
                                int l = r.gameObject.layer;
                                maskValue |= (1 << l);
                            }
                        }
                        int rootLayer = previewRoot.layer;
                        maskValue |= (1 << rootLayer);
                    }
                    catch { }
                    if (maskValue == 0)
                    {
                        maskValue = -1; // Everything
                    }
                }
                // Assign whichever field type is present
                var fieldType = layerMaskField.FieldType;
                if (fieldType == typeof(int))
                {
                    layerMaskField.SetValue(settings, maskValue);
                }
                else if (fieldType == typeof(UnityEngine.LayerMask))
                {
                    var unityMask = new UnityEngine.LayerMask { value = maskValue };
                    layerMaskField.SetValue(settings, unityMask);
                }
                else
                {
                    // Best-effort fallback
                    layerMaskField.SetValue(settings, maskValue);
                }
                dirty = true;
            }

            // Set Reversed Z Override to Auto (both outline variants are drawn anyway in feature)
            var reversedZField = FindField("z");
            if (reversedZField != null)
            {
                try
                {
                    var enumType = reversedZField.FieldType;
                    // The enum values are: Auto=0, ForceReversed=1, ForceForward=2
                    object desired = Enum.ToObject(enumType, 0);
                    object current = reversedZField.GetValue(settings);
                    if (!Equals(current, desired))
                    {
                        reversedZField.SetValue(settings, desired);
                        dirty = true;
                    }
                }
                catch { /* ignore reflection issues */ }
            }

            // Enforce Render Pass Event = AfterRenderingTransparents
            var renderPassEventField = FindField("passEvent");
            if (renderPassEventField != null)
            {
                try
                {
                    var enumType = renderPassEventField.FieldType;
                    var desired = Enum.Parse(enumType, "AfterRenderingTransparents", true);
                    var current = renderPassEventField.GetValue(settings);
                    if (!Equals(current, desired))
                    {
                        renderPassEventField.SetValue(settings, desired);
                        dirty = true;
                    }
                }
                catch { /* ignore */ }
            }

            // Use Palette Alpha = OFF
            var usePaletteAlphaField = FindField("useAlpha");
            if (usePaletteAlphaField != null)
            {
                if (usePaletteAlphaField.GetValue(settings) is bool current && current != false)
                {
                    usePaletteAlphaField.SetValue(settings, false);
                    dirty = true;
                }
            }

            // Thickness Space = Screen (1.0), Min Screen Thickness >= 3, Outline Width small but non-zero (use 3 px)
            var thicknessSpaceField = FindField("thicknessSpace");
            if (thicknessSpaceField != null)
            {
                if (!(thicknessSpaceField.GetValue(settings) is float ts) || Math.Abs(ts - 1.0f) > 1e-6f)
                {
                    thicknessSpaceField.SetValue(settings, 1.0f);
                    dirty = true;
                }
            }
            var minScreenThicknessField = FindField("minScreenPx");
            if (minScreenThicknessField != null)
            {
                float desiredMin = 6.0f;
                if (!(minScreenThicknessField.GetValue(settings) is float ms) || ms < desiredMin)
                {
                    minScreenThicknessField.SetValue(settings, desiredMin);
                    dirty = true;
                }
            }
            var outlineWidthField = FindField("width");
            if (outlineWidthField != null)
            {
                float desiredWidth = 6.0f; // pixels in screen thickness mode
                if (!(outlineWidthField.GetValue(settings) is float ow) || Math.Abs(ow - desiredWidth) > 1e-6f)
                {
                    outlineWidthField.SetValue(settings, desiredWidth);
                    dirty = true;
                }
            }

            // Silhouette Threshold ≈ 0.4, Feather ≈ 0.1
            var silhouetteThresholdField = FindField("silhouette");
            if (silhouetteThresholdField != null)
            {
                float desiredTh = 0.5f; // slightly stricter to reduce inner artifacts
                if (!(silhouetteThresholdField.GetValue(settings) is float th) || Math.Abs(th - desiredTh) > 1e-6f)
                {
                    silhouetteThresholdField.SetValue(settings, desiredTh);
                    dirty = true;
                }
            }
            var silhouetteFeatherField = FindField("feather");
            if (silhouetteFeatherField != null)
            {
                float desiredFeather = 0.1f;
                if (!(silhouetteFeatherField.GetValue(settings) is float sf) || Math.Abs(sf - desiredFeather) > 1e-6f)
                {
                    silhouetteFeatherField.SetValue(settings, desiredFeather);
                    dirty = true;
                }
            }
            // Disable debug overlay to use proper stencil-limited silhouette only
            var debugOverlayField = FindField("debug");
            if (debugOverlayField != null)
            {
                if (!(debugOverlayField.GetValue(settings) is bool dbg) || dbg != false)
                {
                    debugOverlayField.SetValue(settings, false);
                    dirty = true;
                }
            }
        }

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
            try
            {
                var originals = r.sharedMaterials;
                if (originals == null || originals.Length == 0) continue;
                _originalMaterials[r] = originals;
                // Assign depth-only to all slots
                var reps = new Material[originals.Length];
                for (int s = 0; s < reps.Length; s++) reps[s] = _depthOnlyMat;
                r.sharedMaterials = reps;
            }
            catch { /* ignore renderer issues */ }
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
            try { r.sharedMaterials = mats; } catch { }
        }
        _originalMaterials.Clear();
    }
}

// Timeline receiver contracts moved to EditorPlus.AnimationPreview namespace (see SceneTimelineTypes.cs)

// Reflection helpers removed – using typed UniversalAnimPlayer access.
#endif
