using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Repulsor;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

/// <summary>
/// Configures the project for Meta Quest 3 and regenerates the scene, prefabs and materials.
/// Safe to run again, but it rebuilds Main.unity and the prefabs, so hand edits to those are lost.
/// Menu: Repulsor > Set Up Project. Batch: -executeMethod ProjectSetup.Run
/// </summary>
public static class ProjectSetup
{
    const string ApplicationId = "com.alberthan.repulsortest";
    const string ProductName = "Repulsor Test";
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string MetaFeatureSet = "com.unity.openxr.featureset.meta";
    const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
    const string MetaLifeCycleFeature = "OpenXRLifeCycleFeature";
    const string AuraPrefabPath = "Assets/Prefabs/Aura.prefab";
    const AndroidSdkVersions MinSdk = (AndroidSdkVersions)32;      // Android 12L, required for passthrough
    const AndroidSdkVersions TargetSdk = (AndroidSdkVersions)34;

    static readonly Color RepulsorBlue = new Color(0.47f, 0.88f, 1f);
    static readonly Color TargetOrange = new Color(1f, 0.42f, 0.1f);
    static readonly Color LaserCoreBlue = new Color(0.5f, 0.88f, 1f);
    static readonly Color LaserOuterBlue = new Color(0.08f, 0.36f, 1f);

    [MenuItem("Repulsor/Set Up Project")]
    public static void Run()
    {
        ConfigurePlayer();
        ConfigureRenderPipeline();
        ConfigureXr();
        BuildScene();
        AssetDatabase.SaveAssets();
        Validate();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[Setup] Done");
    }

    /// <summary>Rebuilds only the aura prefab, leaving the scene and everything else alone.</summary>
    [MenuItem("Repulsor/Rebuild Aura")]
    public static void RebuildAura()
    {
        CreateAuraPrefab();
        AssetDatabase.SaveAssets();
        Debug.Log("[Setup] Aura rebuilt");
    }

    /// <summary>Adds the person detection test to the scene, and turns on camera images for it.</summary>
    [MenuItem("Repulsor/Add Person Detector")]
    public static void AddPersonDetector()
    {
        OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        settings.GetFeature<ARCameraFeature>().cameraImageSupportEnabled = true;
        EditorUtility.SetDirty(settings.GetFeature<ARCameraFeature>());

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        foreach (PersonDetector old in Object.FindObjectsByType<PersonDetector>(FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);

        var detectorObject = new GameObject("Person Detector", typeof(PersonDetector));
        var detector = new SerializedObject(detectorObject.GetComponent<PersonDetector>());
        detector.FindProperty("model").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>("Assets/Models/pose_detection.onnx");
        detector.FindProperty("anchorsCsv").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Models/pose_anchors.csv");
        detector.FindProperty("cameraManager").objectReferenceValue = Object.FindAnyObjectByType<ARCameraManager>();
        detector.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Setup] Person detector added; camera manager {detector.FindProperty("cameraManager").objectReferenceValue}, "
            + $"model {detector.FindProperty("model").objectReferenceValue}");
    }

    /// <summary>Updates only the orb material, leaving the scene and everything else alone.</summary>
    [MenuItem("Repulsor/Rebuild Orb")]
    public static void RebuildOrb()
    {
        CreateOrbMaterial();
        AssetDatabase.SaveAssets();
        Debug.Log("[Setup] Orb rebuilt");
    }

    static void ConfigurePlayer()
    {
        PlayerSettings.companyName = "Albert Han";
        PlayerSettings.productName = ProductName;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = MinSdk;
        PlayerSettings.Android.targetSdkVersion = TargetSdk;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
    }

    static void ConfigureRenderPipeline()
    {
        Directory.CreateDirectory("Assets/Settings");
        const string rendererPath = "Assets/Settings/QuestRenderer.asset";
        const string pipelinePath = "Assets/Settings/QuestURP.asset";

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, rendererPath);
        }
        // Passthrough needs: no post-processing, Auto intermediate texture (Meta OpenXR graphics docs).
        renderer.postProcessData = null;
        renderer.intermediateTextureMode = IntermediateTextureMode.Auto;
        EditorUtility.SetDirty(renderer);

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, pipelinePath);
        }
        pipeline.supportsHDR = false;
        pipeline.msaaSampleCount = 4;
        pipeline.supportsCameraDepthTexture = false;
        pipeline.supportsCameraOpaqueTexture = false;

        var serialized = new SerializedObject(pipeline);
        serialized.FindProperty("m_SupportsTerrainHoles").boolValue = false;
        serialized.FindProperty("m_MainLightShadowsSupported").boolValue = false;
        serialized.FindProperty("m_SoftShadowsSupported").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pipeline);

        GraphicsSettings.defaultRenderPipeline = pipeline;
        int quality = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }
        QualitySettings.SetQualityLevel(quality, false);
    }

    static void ConfigureXr()
    {
        const BuildTargetGroup group = BuildTargetGroup.Android;
        Directory.CreateDirectory("Assets/XR");

        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget))
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
        }
        if (!perTarget.HasSettingsForBuildTarget(group))
            perTarget.CreateDefaultSettingsForBuildTarget(group);
        if (!perTarget.HasManagerSettingsForBuildTarget(group))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

        perTarget.SettingsForBuildTarget(group).InitManagerOnStart = true;
        if (!XRPackageMetadataStore.AssignLoader(perTarget.ManagerSettingsForBuildTarget(group), OpenXRLoaderType, group))
            Debug.Log("[Setup] OpenXR loader was already assigned");

        FeatureHelpers.RefreshFeatures(group);
        OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
        if (settings == null)
            throw new BuildFailedException("OpenXR settings for Android were not created.");

        OpenXRFeatureSetManager.InitializeFeatureSets();
        OpenXRFeatureSetManager.FeatureSet metaSet = OpenXRFeatureSetManager.GetFeatureSetWithId(group, MetaFeatureSet);
        if (metaSet == null)
            throw new BuildFailedException("Meta Quest OpenXR feature set is missing.");
        metaSet.isEnabled = true;
        OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(group);

        // Only what this game uses. Each extra feature adds Android permissions and startup work.
        // Raycasts and occlusion use live depth to find walls and furniture and to hide things behind
        // them. Planes and meshing would add a saved room model, which needs Space Setup.
        var wanted = new HashSet<System.Type>
        {
            typeof(MetaQuestFeature),
            typeof(ARSessionFeature),
            typeof(ARCameraFeature),
            typeof(ARRaycastFeature),
            typeof(AROcclusionFeature),
            typeof(DisplayUtilitiesFeature),
            typeof(HandTracking),
            typeof(OculusTouchControllerProfile),
            typeof(UnityEngine.XR.OpenXR.Features.CompositionLayers.OpenXRCompositionLayersFeature),
        };
        foreach (OpenXRFeature feature in settings.GetFeatures())
        {
            // Hidden features are plumbing that packages need, not options. Meta's life cycle feature must
            // stay on: the build leaves out Meta's native library when it's off, and nothing flags that.
            System.Type type = feature.GetType();
            bool hidden = type.GetCustomAttribute<OpenXRFeatureAttribute>(true)?.Hidden ?? false;
            feature.enabled = hidden ? type.Name == MetaLifeCycleFeature : wanted.Contains(type);
            EditorUtility.SetDirty(feature);
        }
        settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
        EditorUtility.SetDirty(settings);
    }

    static void BuildScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/Prefabs");
        Directory.CreateDirectory("Assets/Materials");

        Material blue = CreateMaterial("Repulsor", RepulsorBlue);
        Material orange = CreateOrbMaterial();

        Target targetPrefab = CreateSpherePrefab<Target>("Target", orange);
        Blast blastPrefab = CreateSpherePrefab<Blast>("Blast", blue);
        Shockwave shockwavePrefab = CreateShockwavePrefab(blue);
        ParticleSystem explosionPrefab = CreateExplosionPrefab();
        LaserBeamView laserPrefab = CreateLaserPrefab();
        AuraView auraPrefab = CreateAuraPrefab();

        new GameObject("AR Session", typeof(ARSession), typeof(ARInputManager));
        XROrigin origin = CreateRig();
        RoomSense room = CreateRoomSense(origin);
        RoomDim roomDim = CreateRoomDim(origin.Camera);

        var gameObject = new GameObject("Game", typeof(RepulsorGame), typeof(AudioSource));
        HandRepulsor left = CreateHand("Left Hand", Handedness.Left, blue, laserPrefab, gameObject.transform);
        HandRepulsor right = CreateHand("Right Hand", Handedness.Right, blue, laserPrefab, gameObject.transform);

        var audioSource = gameObject.GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 1.5f;

        Transform hud = CreateHud(out Text hudText);

        var game = new SerializedObject(gameObject.GetComponent<RepulsorGame>());
        game.FindProperty("origin").objectReferenceValue = origin;
        game.FindProperty("room").objectReferenceValue = room;
        game.FindProperty("roomDim").objectReferenceValue = roomDim;
        game.FindProperty("aura").objectReferenceValue = PrefabUtility.InstantiatePrefab(auraPrefab);
        game.FindProperty("leftHand").objectReferenceValue = left;
        game.FindProperty("rightHand").objectReferenceValue = right;
        game.FindProperty("targetPrefab").objectReferenceValue = targetPrefab;
        game.FindProperty("blastPrefab").objectReferenceValue = blastPrefab;
        game.FindProperty("shockwavePrefab").objectReferenceValue = shockwavePrefab;
        game.FindProperty("explosionPrefab").objectReferenceValue = explosionPrefab;
        game.FindProperty("hud").objectReferenceValue = hud;
        game.FindProperty("hudText").objectReferenceValue = hudText;
        game.FindProperty("audioSource").objectReferenceValue = audioSource;
        game.ApplyModifiedPropertiesWithoutUndo();

        var demoObject = new GameObject("Laser Demo", typeof(LaserDemo));
        LaserBeamView demoBeam = InstantiateLaser(laserPrefab, demoObject.transform);
        var demo = new SerializedObject(demoObject.GetComponent<LaserDemo>());
        demo.FindProperty("beam").objectReferenceValue = demoBeam;
        demo.FindProperty("room").objectReferenceValue = room;
        demo.FindProperty("roomDim").objectReferenceValue = roomDim;
        demo.FindProperty("head").objectReferenceValue = origin.Camera.transform;
        demo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    static XROrigin CreateRig()
    {
        var originObject = new GameObject("XR Origin", typeof(XROrigin));
        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(originObject.transform, false);

        var cameraObject = new GameObject("Main Camera",
            typeof(Camera), typeof(AudioListener), typeof(ARCameraManager), typeof(TrackedPoseDriver))
        {
            tag = "MainCamera",
        };
        cameraObject.transform.SetParent(offset.transform, false);

        // Passthrough is drawn behind the app, so the camera must clear to fully transparent.
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 50f;

        UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = false;
        cameraData.renderShadows = false;

        var pose = cameraObject.GetComponent<TrackedPoseDriver>();
        pose.positionInput = new InputActionProperty(
            new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        pose.rotationInput = new InputActionProperty(
            new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        pose.trackingStateInput = new InputActionProperty(
            new InputAction("Tracking State", binding: "<XRHMD>/trackingState", expectedControlType: "Integer"));

        var origin = originObject.GetComponent<XROrigin>();
        origin.Camera = camera;
        origin.CameraFloorOffsetObject = offset;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        return origin;
    }

    static RoomSense CreateRoomSense(XROrigin origin)
    {
        // Off until the room permission is granted: without it the raycast subsystem logs errors.
        var raycaster = origin.gameObject.AddComponent<ARRaycastManager>();
        raycaster.enabled = false;

        // Depth for hiding things behind real objects, read by the aura shader. Also off until then.
        var occlusion = origin.Camera.gameObject.AddComponent<AROcclusionManager>();
        var shaderOcclusion = origin.Camera.gameObject.AddComponent<ARShaderOcclusion>();
        shaderOcclusion.occlusionShaderMode = AROcclusionShaderMode.HardOcclusion;
        occlusion.enabled = false;
        shaderOcclusion.enabled = false;

        var roomObject = new GameObject("Room Sense", typeof(RoomSense));
        var room = new SerializedObject(roomObject.GetComponent<RoomSense>());
        room.FindProperty("raycaster").objectReferenceValue = raycaster;
        room.FindProperty("occlusion").objectReferenceValue = occlusion;
        room.FindProperty("shaderOcclusion").objectReferenceValue = shaderOcclusion;
        room.ApplyModifiedPropertiesWithoutUndo();
        return roomObject.GetComponent<RoomSense>();
    }

    static HandRepulsor CreateHand(string name, Handedness handedness, Material material, LaserBeamView laserPrefab, Transform parent)
    {
        var handObject = new GameObject(name, typeof(HandRepulsor), typeof(LineRenderer), typeof(AudioSource));
        handObject.transform.SetParent(parent, false);

        LineRenderer aimLine = handObject.GetComponent<LineRenderer>();
        ConfigureLine(aimLine, 0.006f, 0.001f, material);

        LaserBeamView beam = InstantiateLaser(laserPrefab, handObject.transform);

        GameObject orb = CreateSphere("Charge Orb", material);
        orb.transform.SetParent(handObject.transform, false);
        orb.SetActive(false);

        var hum = handObject.GetComponent<AudioSource>();
        hum.playOnAwake = false;
        hum.loop = true;
        hum.spatialBlend = 1f;
        hum.minDistance = 1.5f;

        var hand = new SerializedObject(handObject.GetComponent<HandRepulsor>());
        hand.FindProperty("handedness").intValue = (int)handedness;
        hand.FindProperty("chargeOrb").objectReferenceValue = orb.transform;
        hand.FindProperty("aimLine").objectReferenceValue = aimLine;
        hand.FindProperty("laserBeam").objectReferenceValue = beam;
        hand.FindProperty("laserHum").objectReferenceValue = hum;
        hand.ApplyModifiedPropertiesWithoutUndo();
        return handObject.GetComponent<HandRepulsor>();
    }

    static LaserBeamView InstantiateLaser(LaserBeamView prefab, Transform parent)
    {
        var beam = (LaserBeamView)PrefabUtility.InstantiatePrefab(prefab);
        beam.transform.SetParent(parent, false);
        return beam;
    }

    /// <summary>
    /// A ribbon of light, a glow in the palm, and a burst on the surface it hits. Everything is painted
    /// in shaders on flat quads, so no solid shape shows.
    /// </summary>
    static LaserBeamView CreateLaserPrefab()
    {
        Directory.CreateDirectory("Assets/Meshes");
        Directory.CreateDirectory("Assets/Textures");
        Mesh ribbon = CreateRibbonMesh("Assets/Meshes/BeamRibbon.asset");
        Texture2D noise = CreateNoiseTexture("Assets/Textures/BeamNoise.asset");

        Material beamMaterial = CreateShaderMaterial("LaserBeam", "Repulsor/Laser Beam");
        beamMaterial.SetTexture("_Noise", noise);
        beamMaterial.SetColor("_CoreColor", LaserCoreBlue);
        beamMaterial.SetColor("_OuterColor", LaserOuterBlue);
        beamMaterial.SetFloat("_NoiseScale", 0.3f);
        beamMaterial.SetFloat("_FlowSpeed", 1.1f);

        // Muzzle glow, splash on the surface, flash on top of it.
        Material muzzle = CreateBurstMaterial("LaserMuzzle", noise, true, 5f, 0.5f, 7f, 0.65f);
        Material splash = CreateBurstMaterial("LaserSplash", noise, false, 7f, 0.95f, 5f, 1f);
        Material flash = CreateBurstMaterial("LaserFlash", noise, true, 5f, 0.85f, 8f, 0.8f);

        var root = new GameObject("LaserBeam", typeof(LaserBeamView));
        var visuals = new GameObject("Visuals");
        visuals.transform.SetParent(root.transform, false);

        var beam = new GameObject("Beam", typeof(MeshFilter), typeof(MeshRenderer));
        beam.transform.SetParent(visuals.transform, false);
        beam.GetComponent<MeshFilter>().sharedMesh = ribbon;
        ConfigureRenderer(beam.GetComponent<MeshRenderer>(), beamMaterial);

        GameObject muzzleGlow = CreateQuad("Muzzle Glow", muzzle);
        muzzleGlow.transform.SetParent(visuals.transform, false);

        var impact = new GameObject("Impact");
        impact.transform.SetParent(visuals.transform, false);
        GameObject impactSplash = CreateQuad("Splash", splash);
        impactSplash.transform.SetParent(impact.transform, false);
        GameObject impactFlash = CreateQuad("Flash", flash);
        impactFlash.transform.SetParent(impact.transform, false);

        ParticleSystem sparks = CreateLaserSparks();
        sparks.transform.SetParent(root.transform, false);

        var sizzle = sparks.gameObject.AddComponent<AudioSource>();
        sizzle.playOnAwake = false;
        sizzle.loop = true;
        sizzle.spatialBlend = 1f;
        sizzle.minDistance = 1.5f;

        var view = new SerializedObject(root.GetComponent<LaserBeamView>());
        view.FindProperty("visuals").objectReferenceValue = visuals;
        view.FindProperty("beam").objectReferenceValue = beam.transform;
        view.FindProperty("muzzleGlow").objectReferenceValue = muzzleGlow.transform;
        view.FindProperty("impact").objectReferenceValue = impact;
        view.FindProperty("impactSplash").objectReferenceValue = impactSplash.transform;
        view.FindProperty("impactFlash").objectReferenceValue = impactFlash.transform;
        view.FindProperty("sparks").objectReferenceValue = sparks;
        view.FindProperty("sizzle").objectReferenceValue = sizzle;
        view.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/LaserBeam.prefab");
        Object.DestroyImmediate(root);
        return prefab.GetComponent<LaserBeamView>();
    }

    static AuraView CreateAuraPrefab()
    {
        var root = new GameObject("Aura", typeof(AudioSource), typeof(AuraView));

        Mesh haloMesh = CreateHaloMesh("Assets/Meshes/AuraHalo.asset");
        GameObject[] styles =
        {
            CreateFlipbookStyle("Real Fire", "Flame02.tga", true, 40f, 1f, 0.5f, 0.75f, 0.25f, 0.45f),
            CreateFlipbookStyle("Wild Fire", "Flame03Hollow.tga", false, 20f, 1f, 0.6f, 0.85f, 0.25f, 0.25f),
            CreateShellStyle(),
            CreateHaloStyle("Light Halo", "AuraLightHalo", haloMesh, 0f, 1.9f),
            CreateHaloStyle("Sun Halo", "AuraSunHalo", haloMesh, 1f, 0.9f),
            CreateHaloStyle("Rune Circle", "AuraRuneCircle", haloMesh, 2f, 0.45f),
        };
        foreach (GameObject style in styles)
            style.transform.SetParent(root.transform, false);

        var wheelMaterial = CreateShaderMaterial("ColorWheel", "Repulsor/ColorWheel");
        GameObject wheel = CreateQuad("Color Wheel", wheelMaterial);
        wheel.transform.SetParent(root.transform, false);
        var wheelPicker = new SerializedObject(wheel.AddComponent<ColorPicker>());
        wheelPicker.FindProperty("wheel").objectReferenceValue = wheel.GetComponent<Renderer>();
        wheelPicker.ApplyModifiedPropertiesWithoutUndo();

        ParticleSystem embers = CreateEmbers();
        embers.transform.SetParent(root.transform, false);

        var roar = root.GetComponent<AudioSource>();
        roar.playOnAwake = false;
        roar.loop = true;
        roar.spatialBlend = 1f;
        roar.minDistance = 1.5f;

        var view = new SerializedObject(root.GetComponent<AuraView>());
        SerializedProperty styleList = view.FindProperty("styles");
        styleList.arraySize = styles.Length;
        for (int i = 0; i < styles.Length; i++)
            styleList.GetArrayElementAtIndex(i).objectReferenceValue = styles[i];
        view.FindProperty("picker").objectReferenceValue = wheel.GetComponent<ColorPicker>();
        view.FindProperty("embers").objectReferenceValue = embers;
        view.FindProperty("roar").objectReferenceValue = roar;
        view.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, AuraPrefabPath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<AuraView>();
    }

    /// <summary>A style that plays a 16x4 flipbook on flames spread around a standing body. Sizes are widths.</summary>
    static GameObject CreateFlipbookStyle(string name, string file, bool hasAlpha, float rate, float lifetime,
        float minWidth, float maxWidth, float lift, float baseFade)
    {
        Texture2D texture = ImportAuraTexture(file);
        Material material = CreateSpriteMaterial($"AuraStyle{name.Replace(" ", "")}", texture, hasAlpha);
        material.SetVector("_Tiles", new Vector4(16f, 4f, 0f, 0f));
        material.SetFloat("_BaseFade", baseFade);
        ParticleSystem particles = CreateFlameParticles(name, material, rate, lifetime, minWidth, maxWidth, 2f, lift);

        ParticleSystem.TextureSheetAnimationModule sheet = particles.textureSheetAnimation;
        sheet.enabled = true;
        sheet.numTilesX = 16;
        sheet.numTilesY = 4;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
        sheet.cycleCount = 1;
        return particles.gameObject;
    }

    /// <summary>
    /// Fire on real curved surfaces around the body, a ring on the floor, and thin streaks of light rising
    /// at real places, which is how mesh effects get their depth.
    /// </summary>
    static GameObject CreateShellStyle()
    {
        Material material = CreateShaderMaterial("AuraShell", "Repulsor/AuraShell");
        material.SetTexture("_Noise", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/BeamNoise.asset"));

        var style = new GameObject("Fire Shells", typeof(MeshFilter), typeof(MeshRenderer));
        style.GetComponent<MeshFilter>().sharedMesh = CreateShellMesh("Assets/Meshes/AuraShells.asset");
        ConfigureRenderer(style.GetComponent<MeshRenderer>(), material);

        CreateStreaks(style.transform, 26f, 0.75f);
        return style;
    }

    /// <summary>A floor pattern, a curtain of beams and a ring over the head.</summary>
    static GameObject CreateHaloStyle(string name, string materialName, Mesh mesh, float pattern, float curtainHeight)
    {
        Material material = CreateShaderMaterial(materialName, "Repulsor/AuraHalo");
        material.SetTexture("_Noise", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/BeamNoise.asset"));
        material.SetFloat("_Pattern", pattern);
        material.SetFloat("_CurtainHeight", curtainHeight);

        var style = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        style.GetComponent<MeshFilter>().sharedMesh = mesh;
        ConfigureRenderer(style.GetComponent<MeshRenderer>(), material);
        CreateStreaks(style.transform, 34f, 0.66f);
        return style;
    }

    /// <summary>Thin streaks of light rising at real places around the body.</summary>
    static void CreateStreaks(Transform parent, float rate, float radius)
    {
        var streakObject = new GameObject("Streaks", typeof(ParticleSystem));
        streakObject.transform.SetParent(parent, false);
        streakObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var streaks = streakObject.GetComponent<ParticleSystem>();
        streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = streaks.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;
        main.maxParticles = 60;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.024f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        ParticleSystem.EmissionModule emission = streaks.emission;
        emission.rateOverTime = rate;

        ParticleSystem.ShapeModule shape = streaks.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 3f;
        shape.radius = radius;
        shape.radiusThickness = 0.5f;

        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        ParticleSystem.ColorOverLifetimeModule color = streaks.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(fade);

        var renderer = streaks.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.14f;
        renderer.lengthScale = 2f;
        ConfigureRenderer(renderer, AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/AuraEmber.mat"));
    }

    /// <summary>Three nested shells shaped around a standing body, then a flat ring on the floor.</summary>
    static Mesh CreateShellMesh(string path)
    {
        float[] scales = { 0.82f, 1f, 1.2f };
        var mesh = new RevolvedMesh(40);

        for (int shell = 0; shell < scales.Length; shell++)
        {
            float scale = scales[shell];
            mesh.Add(22, v =>
            {
                // Out from the feet to the shoulders, then in to a point well above the head.
                float height = v * 2.5f;
                float shoulders = (height - 1.35f) / 0.65f;
                float width = (0.34f + 0.26f * Mathf.Exp(-shoulders * shoulders)) * Mathf.Sqrt(Mathf.Clamp01(1f - v * v * v));
                return new Vector2(width * scale + 0.08f, height);
            }, shell * 0.37f, 0f);
        }

        mesh.Add(3, v => new Vector2(Mathf.Lerp(0.35f, 1.05f, v), 0.02f), 0f, 1f);
        return mesh.Save(path, "AuraShells");
    }

    /// <summary>A curtain of height 1 for the shader to stretch, a floor pattern, and a ring over the head.</summary>
    static Mesh CreateHaloMesh(string path)
    {
        var mesh = new RevolvedMesh(64);
        mesh.Add(1, v => new Vector2(0.64f, v), 0f, 0f);
        mesh.Add(2, v => new Vector2(Mathf.Lerp(0.04f, 1.02f, v), 0.015f), 0f, 1f);
        mesh.Add(1, v => new Vector2(Mathf.Lerp(0.2f, 0.34f, v), 2.02f), 0f, 2f);
        return mesh.Save(path, "AuraHalo");
    }

    /// <summary>Builds surfaces by spinning a profile (radius, height) around the body's axis.</summary>
    sealed class RevolvedMesh
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector4> details = new List<Vector4>();
        readonly List<int> triangles = new List<int>();
        readonly int around;

        public RevolvedMesh(int around) => this.around = around;

        /// <summary>Adds a surface. Its uv holds (around, along, seed, part).</summary>
        public void Add(int rows, System.Func<float, Vector2> profile, float seed, float part)
        {
            int start = vertices.Count;
            for (int row = 0; row <= rows; row++)
            {
                float v = row / (float)rows;
                Vector2 point = profile(v);
                Vector2 slope = (profile(Mathf.Min(v + 0.01f, 1f)) - profile(Mathf.Max(v - 0.01f, 0f))).normalized;

                for (int column = 0; column <= around; column++)
                {
                    float u = column / (float)around;
                    float angle = u * Mathf.PI * 2f;
                    var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    vertices.Add(outward * point.x + Vector3.up * point.y);
                    normals.Add(part > 0.5f ? Vector3.up : (outward * slope.y - Vector3.up * slope.x).normalized);
                    details.Add(new Vector4(u, v, seed, part));
                }
            }

            int stride = around + 1;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < around; column++)
                {
                    int a = start + row * stride + column;
                    int b = a + stride;
                    triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            }
        }

        public Mesh Save(string path, string name)
        {
            AssetDatabase.DeleteAsset(path);
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, details);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(new Vector3(0f, 1.4f, 0f), new Vector3(3.2f, 3.2f, 3.2f));
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }

    static Texture2D ImportAuraTexture(string file)
    {
        string path = $"Assets/Textures/Aura/{file}";
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material CreateSpriteMaterial(string name, Texture2D texture, bool hasAlpha)
    {
        Material material = CreateShaderMaterial(name, "Repulsor/AuraSprite");
        material.SetTexture("_MainTex", texture);
        material.SetFloat("_UseAlpha", hasAlpha ? 1f : 0f);
        return material;
    }

    /// <summary>Upright flames that start on the surface of a body-sized cylinder and rise slowly.</summary>
    static ParticleSystem CreateFlameParticles(string name, Material material, float rate, float lifetime,
        float minWidth, float maxWidth, float heightOverWidth, float lift)
    {
        var flameObject = new GameObject(name, typeof(ParticleSystem));
        flameObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        flameObject.transform.localPosition = new Vector3(0f, lift, 0f);
        var particles = flameObject.GetComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;
        main.maxParticles = 150;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.75f, lifetime * 1.25f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(minWidth, maxWidth);
        main.startSizeY = new ParticleSystem.MinMaxCurve(minWidth * heightOverWidth, maxWidth * heightOverWidth);
        main.startSizeZ = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = rate;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.ConeVolume;
        shape.angle = 0f;
        shape.radius = 0.28f;
        shape.radiusThickness = 0f;
        shape.length = 1.4f;

        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(fade);

        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.VerticalBillboard;
        renderer.sortMode = ParticleSystemSortMode.None;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.Center,
        });
        ConfigureRenderer(renderer, material);
        return particles;
    }

    /// <summary>Sparks rising from a body-sized ring, cooler than the laser's and hidden by real things.</summary>
    static ParticleSystem CreateEmbers()
    {
        var emberObject = new GameObject("Embers", typeof(ParticleSystem));
        emberObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        emberObject.transform.localPosition = new Vector3(0f, 0.1f, 0f);
        var particles = emberObject.GetComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.maxParticles = 60;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.018f);
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 18f;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 6f;
        shape.radius = 0.45f;
        shape.radiusThickness = 0.3f;

        ConfigureSparks(particles);

        var cooling = new Gradient();
        cooling.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.7f, 0.3f), 0f), new GradientColorKey(new Color(1f, 0.25f, 0.05f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.color = new ParticleSystem.MinMaxGradient(cooling);

        Material material = CreateShaderMaterial("AuraEmber", "Repulsor/Spark");
        material.SetFloat("_DepthTest", 1f);
        ConfigureRenderer(particles.GetComponent<ParticleSystemRenderer>(), material);
        return particles;
    }

    static void ConfigureRenderer(Renderer renderer, Material material)
    {
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static GameObject CreateQuad(string name, Material material)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        ConfigureRenderer(quad.GetComponent<MeshRenderer>(), material);
        return quad;
    }

    /// <summary>A flat strip, x = -1..1 across and z = 0..1 along. The shader turns it to face the eye.</summary>
    static Mesh CreateRibbonMesh(string path)
    {
        const int segments = 12;
        var vertices = new Vector3[(segments + 1) * 2];
        var triangles = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float z = i / (float)segments;
            vertices[i * 2] = new Vector3(-1f, 0f, z);
            vertices[i * 2 + 1] = new Vector3(1f, 0f, z);
        }

        for (int i = 0; i < segments; i++)
        {
            int t = i * 6, near = i * 2;
            triangles[t] = near;
            triangles[t + 1] = near + 2;
            triangles[t + 2] = near + 1;
            triangles[t + 3] = near + 1;
            triangles[t + 4] = near + 2;
            triangles[t + 5] = near + 3;
        }

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = "BeamRibbon" };
            AssetDatabase.CreateAsset(mesh, path);
        }
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        // The shader turns the strip in any direction around its axis, so the bounds must allow for that.
        mesh.bounds = new Bounds(new Vector3(0f, 0f, 0.5f), new Vector3(2f, 2f, 1f));
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>Smooth noise that tiles, for the shaders to scroll. Sampling a texture is cheap on a Quest.</summary>
    static Texture2D CreateNoiseTexture(string path)
    {
        const int size = 128;
        var random = new System.Random(7);
        int[] cells = { 4, 8, 16, 32 };
        float[] weights = { 0.5f, 0.27f, 0.15f, 0.08f };
        var values = new float[size * size];

        for (int octave = 0; octave < cells.Length; octave++)
        {
            int count = cells[octave];
            var lattice = new float[count * count];
            for (int i = 0; i < lattice.Length; i++)
                lattice[i] = (float)random.NextDouble();

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float gridX = x * count / (float)size;
                    float gridY = y * count / (float)size;
                    int left = (int)gridX, bottom = (int)gridY;
                    float blendX = Mathf.SmoothStep(0f, 1f, gridX - left);
                    float blendY = Mathf.SmoothStep(0f, 1f, gridY - bottom);
                    int right = (left + 1) % count, top = (bottom + 1) % count;

                    float lower = Mathf.Lerp(lattice[bottom * count + left], lattice[bottom * count + right], blendX);
                    float upper = Mathf.Lerp(lattice[top * count + left], lattice[top * count + right], blendX);
                    values[y * size + x] += Mathf.Lerp(lower, upper, blendY) * weights[octave];
                }
            }
        }

        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte value = (byte)Mathf.Clamp(values[i] * 255f, 0f, 255f);
            pixels[i] = new Color32(value, value, value, 255);
        }

        AssetDatabase.DeleteAsset(path);
        var texture = new Texture2D(size, size, TextureFormat.R8, true)
        {
            name = "BeamNoise",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
        };
        texture.SetPixels32(pixels);
        texture.Apply(true);
        AssetDatabase.CreateAsset(texture, path);
        return texture;
    }

    static Material CreateOrbMaterial()
    {
        Material material = CreateShaderMaterial("Target", "Repulsor/Orb");
        material.shader = Shader.Find("Repulsor/Orb");
        material.SetColor("_BaseColor", TargetOrange);
        return material;
    }

    static Material CreateShaderMaterial(string name, string shaderName)
    {
        string path = $"Assets/Materials/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find(shaderName));
            AssetDatabase.CreateAsset(material, path);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material CreateBurstMaterial(string name, Texture2D noise, bool billboard, float rayCount,
        float rayStrength, float softness, float brightness)
    {
        Material material = CreateShaderMaterial(name, "Repulsor/Laser Burst");
        material.SetTexture("_Noise", noise);
        material.SetColor("_CoreColor", LaserCoreBlue);
        material.SetColor("_OuterColor", LaserOuterBlue);
        material.SetFloat("_Billboard", billboard ? 1f : 0f);
        material.SetFloat("_RayCount", rayCount);
        material.SetFloat("_RayStrength", rayStrength);
        material.SetFloat("_Softness", softness);
        material.SetFloat("_Brightness", brightness);
        return material;
    }

    static ParticleSystem CreateLaserSparks()
    {
        var sparkObject = new GameObject("Sparks", typeof(ParticleSystem));
        var particles = sparkObject.GetComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.maxParticles = 220;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 5.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.024f);
        main.gravityModifier = 0.7f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 190f;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 68f;
        shape.radius = 0.03f;

        ConfigureSparks(particles);
        return particles;
    }

    static RoomDim CreateRoomDim(Camera camera)
    {
        GameObject quad = CreateQuad("Room Dim", CreateShaderMaterial("RoomDim", "Repulsor/Room Dim"));
        quad.transform.SetParent(camera.transform, false);
        quad.transform.localPosition = new Vector3(0f, 0f, 0.12f);
        quad.transform.localScale = Vector3.one * 2f;
        return quad.AddComponent<RoomDim>();
    }

    static void ConfigureLine(LineRenderer line, float startWidth, float endWidth, Material material)
    {
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = startWidth;
        line.endWidth = endWidth;
        line.sharedMaterial = material;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
    }

    static Shockwave CreateShockwavePrefab(Material material)
    {
        var waveObject = new GameObject("Shockwave", typeof(LineRenderer), typeof(Shockwave));

        // Positions are a unit circle, set in Shockwave.Awake; the transform scales it to size.
        LineRenderer ring = waveObject.GetComponent<LineRenderer>();
        ring.positionCount = 48;
        ring.loop = true;
        ring.useWorldSpace = false;
        ring.startWidth = 1f;
        ring.endWidth = 1f;
        ring.sharedMaterial = material;
        ring.shadowCastingMode = ShadowCastingMode.Off;
        ring.receiveShadows = false;

        var wave = new SerializedObject(waveObject.GetComponent<Shockwave>());
        wave.FindProperty("ring").objectReferenceValue = ring;
        wave.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(waveObject, "Assets/Prefabs/Shockwave.prefab");
        Object.DestroyImmediate(waveObject);
        return prefab.GetComponent<Shockwave>();
    }

    static Transform CreateHud(out Text text)
    {
        var hudObject = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
        var canvas = hudObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        hudObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;

        var rect = hudObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1250f, 240f);
        rect.localScale = Vector3.one * 0.001f;
        rect.position = new Vector3(0f, 1.8f, 1.6f);

        var background = new GameObject("Background", typeof(Image));
        background.transform.SetParent(hudObject.transform, false);
        background.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.1f, 0.75f);
        Stretch(background.GetComponent<RectTransform>());

        var textObject = new GameObject("Text", typeof(Text));
        textObject.transform.SetParent(hudObject.transform, false);
        Stretch(textObject.GetComponent<RectTransform>());

        text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 46;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.supportRichText = true;
        text.color = Color.white;
        text.text = "SCORE 0";
        return hudObject.transform;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static Material CreateMaterial(string name, Color color)
    {
        string path = $"Assets/Materials/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(material);
        return material;
    }

    static GameObject CreateSphere(string name, Material material)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        Object.DestroyImmediate(sphere.GetComponent<Collider>());
        var renderer = sphere.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return sphere;
    }

    static T CreateSpherePrefab<T>(string name, Material material) where T : Component
    {
        GameObject sphere = CreateSphere(name, material);
        sphere.AddComponent<T>();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(sphere, $"Assets/Prefabs/{name}.prefab");
        Object.DestroyImmediate(sphere);
        return prefab.GetComponent<T>();
    }

    /// <summary>A burst of welding sparks, flying out in every direction.</summary>
    static ParticleSystem CreateExplosionPrefab()
    {
        var explosion = new GameObject("Explosion", typeof(ParticleSystem));
        var particles = explosion.GetComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.3f;
        main.loop = false;
        main.playOnAwake = true;
        main.maxParticles = 120;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 6.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        main.gravityModifier = 0.8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;

        ConfigureSparks(particles);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(explosion, "Assets/Prefabs/Explosion.prefab");
        Object.DestroyImmediate(explosion);
        return prefab.GetComponent<ParticleSystem>();
    }

    /// <summary>Draws particles as streaks along their flight, white hot and cooling through orange.</summary>
    static void ConfigureSparks(ParticleSystem particles)
    {
        var cooling = new Gradient();
        cooling.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.75f, 0.35f), 0.35f),
                new GradientColorKey(new Color(1f, 0.25f, 0.05f), 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(cooling);

        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.05f;
        renderer.lengthScale = 1f;
        ConfigureRenderer(renderer, CreateShaderMaterial("LaserSpark", "Repulsor/Spark"));
    }

    /// <summary>Runs the Project Validation rules, applies automatic fixes, and fails on any error left.</summary>
    static void Validate()
    {
        const BuildTargetGroup group = BuildTargetGroup.Android;
        var issues = new List<OpenXRFeature.ValidationRule>();

        OpenXRProjectValidation.GetCurrentValidationIssues(issues, group);
        foreach (OpenXRFeature.ValidationRule issue in issues)
        {
            if (issue.fixIt == null || !issue.fixItAutomatic)
                continue;
            Debug.Log($"[Setup] Auto-fixing: {issue.message}");
            issue.fixIt();
        }

        OpenXRProjectValidation.GetCurrentValidationIssues(issues, group);
        bool failed = false;
        foreach (OpenXRFeature.ValidationRule issue in issues)
        {
            failed |= issue.error;
            Debug.Log($"[Setup] Validation {(issue.error ? "ERROR" : "warning")}: {issue.message}");
        }
        Debug.Log($"[Setup] Validation finished with {issues.Count} remaining issue(s)");
        if (failed)
            throw new BuildFailedException("OpenXR project validation failed; see the [Setup] lines above.");
    }
}
