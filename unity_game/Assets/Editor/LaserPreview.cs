using System.IO;
using Repulsor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders the laser against a room-coloured background to Logs/laser_preview.png, to judge its look
/// without building. Only one eye, with no passthrough. Menu: Repulsor > Preview Laser.
/// Batch: -executeMethod LaserPreview.Render (without -nographics)
/// </summary>
public static class LaserPreview
{
    const string OutputPath = "Logs/laser_preview.png";
    static readonly Color RoomColor = new Color(0.46f, 0.42f, 0.37f);

    [MenuItem("Repulsor/Preview Laser")]
    public static void Render()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var beamPrefab = AssetDatabase.LoadAssetAtPath<LaserBeamView>("Assets/Prefabs/LaserBeam.prefab");
        LaserBeamView beam = Object.Instantiate(beamPrefab);
        var end = new Vector3(-0.55f, 0.28f, 2.6f);
        beam.Aim(new Vector3(0.22f, -0.2f, 0.45f), end, true, new Vector3(0.35f, -0.1f, -0.93f).normalized);
        beam.Apply(1f);

        var targetPrefab = AssetDatabase.LoadAssetAtPath<Target>("Assets/Prefabs/Target.prefab");
        Target target = Object.Instantiate(targetPrefab);
        target.transform.position = end + new Vector3(-0.08f, 0.04f, 0.12f);
        target.transform.localScale = Vector3.one * (target.Radius * 2f);

        SaveView(Vector3.zero, Quaternion.identity, OutputPath);
    }

    /// <summary>
    /// The aura around a person-shaped stand-in. There's no live depth here, so flames behind the body
    /// aren't hidden as they are on the headset. This shows the fire, not the depth.
    /// </summary>
    [MenuItem("Repulsor/Preview Aura")]
    public static void RenderAura()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var person = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        person.transform.position = new Vector3(0f, 0.87f, 2.2f);
        person.transform.localScale = new Vector3(0.5f, 0.87f, 0.3f);

        var auraPrefab = AssetDatabase.LoadAssetAtPath<AuraView>("Assets/Prefabs/Aura.prefab");
        AuraView aura = Object.Instantiate(auraPrefab, new Vector3(0f, 0f, 2.2f), Quaternion.identity);
        Shader.SetGlobalVector("_AuraFoot", aura.transform.position);
        aura.Picker.GetComponent<Renderer>().enabled = false;   // In the game it's hidden until called.
        Shader.SetGlobalFloat("_AuraPower", 1f);
        var orange = new Vector4(1f, 0.38f, 0.03f, 1f);
        var blue = new Vector4(0.05f, 0.35f, 1f, 1f);

        var side = new Vector3(1.6f, 1.3f, 1.1f);
        for (int i = 0; i < aura.StyleCount; i++)
        {
            aura.ShowStyle(i);
            Shader.SetGlobalFloat("_AuraPower", 1f);
            foreach (ParticleSystem particles in aura.GetComponentsInChildren<ParticleSystem>())
                particles.Simulate(2f, false, true);

            Shader.SetGlobalVector("_AuraTint", orange);
            SaveView(new Vector3(0f, 1.3f, 0f), Quaternion.identity, $"Logs/aura_style_{i + 1}.png");
            SaveView(side, Quaternion.LookRotation(new Vector3(0f, 1.3f, 2.2f) - side), $"Logs/aura_style_{i + 1}_side.png");
            Shader.SetGlobalVector("_AuraTint", blue);
            SaveView(new Vector3(0f, 1.3f, 0f), Quaternion.identity, $"Logs/aura_style_{i + 1}_blue.png");
        }
    }

    /// <summary>
    /// The ball, the comet and the shockwave, each from the side and from where the shooter stands. Embers are
    /// left out, because they're only shed by a shot that's really moving, and arcs are one random moment.
    /// </summary>
    [MenuItem("Repulsor/Preview Weapons")]
    public static void RenderWeapons()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var blastPrefab = AssetDatabase.LoadAssetAtPath<Blast>("Assets/Prefabs/Blast.prefab");
        Blast blast = Object.Instantiate(blastPrefab);
        ElectricBall ball = blast.GetComponentInChildren<ElectricBall>();

        blast.Launch(null, new Vector3(0f, 0f, 0.5f), Vector3.right, 1f);
        for (int i = 0; i < 6; i++)
            ball.Crackle(0.06f);
        SaveView(Vector3.zero, Quaternion.identity, "Logs/weapon_ball.png");

        blast.Launch(null, new Vector3(0.75f, 0.12f, 2.2f), new Vector3(1f, 0.08f, 0.25f), 1f);
        blast.ShowTail(1.6f);
        ball.Crackle(0.06f);
        SaveView(Vector3.zero, Quaternion.identity, "Logs/weapon_comet_side.png");

        // From beside the head, aimed at something ahead of the eyes, as a real shot is.
        var hand = new Vector3(0.2f, -0.25f, 0.3f);
        Vector3 aim = (new Vector3(0f, 0.05f, 3.5f) - hand).normalized;
        blast.Launch(null, hand + aim * 1.8f, aim, 1f);
        blast.ShowTail(1.6f);
        ball.Crackle(0.06f);
        SaveView(Vector3.zero, Quaternion.identity, "Logs/weapon_comet_shooter.png");
        Object.DestroyImmediate(blast.gameObject);

        var wavePrefab = AssetDatabase.LoadAssetAtPath<Shockwave>("Assets/Prefabs/Shockwave.prefab");
        Shockwave wave = Object.Instantiate(wavePrefab);
        wave.Launch(null, new Vector3(-1.1f, 0f, 2.6f), Vector3.right, 1f);
        wave.ShowAt(1.3f);
        ThrowSparks(wave);
        SaveView(Vector3.zero, Quaternion.identity, "Logs/weapon_wave_side.png");

        wave.Launch(null, new Vector3(0.15f, -0.2f, 0.3f), new Vector3(0f, 0.08f, 1f), 1f);
        wave.ShowAt(1.2f);
        ThrowSparks(wave);
        SaveView(Vector3.zero, Quaternion.identity, "Logs/weapon_wave_shooter.png");
    }

    /// <summary>
    /// A hand-shaped cut through a glowing surface, from a made-up depth map with as many depth pixels per
    /// screen pixel as the headset has, for judging the cut's edge. OCCLUSION_TAG names the picture.
    /// </summary>
    public static void RenderOcclusion()
    {
        const int DepthSize = 96;
        const int PictureSize = 960;

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);

        var depth = new Texture2DArray(DepthSize, DepthSize, 1, TextureFormat.RFloat, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        var values = new Color[DepthSize * DepthSize];
        for (int y = 0; y < DepthSize; y++)
        {
            for (int x = 0; x < DepthSize; x++)
            {
                var uv = new Vector2((x + 0.5f) / DepthSize, (y + 0.5f) / DepthSize);
                values[y * DepthSize + x] = new Color(IsHand(uv) ? 0.9f : 0.1f, 0f, 0f);
            }
        }
        depth.SetPixels(values, 0);
        depth.Apply(false);

        // Each depth is 1 / (2 * value) metres, which puts the surface, at 1 m, between the hand and the room.
        var fromWorld = new Matrix4x4();
        fromWorld.SetRow(0, new Vector4(2f, 0f, 0f, 0f));
        fromWorld.SetRow(1, new Vector4(0f, 2f, 0f, 0f));
        fromWorld.SetRow(2, Vector4.zero);
        fromWorld.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
        Shader.SetGlobalTexture("_EnvironmentDepthTexture", depth);
        Shader.SetGlobalMatrixArray("_EnvironmentDepthProjectionMatrices", new[] { fromWorld, fromWorld });
        Shader.SetGlobalVector("_NdcLinearConversionParameters", new Vector4(1f, 1f, 0f, 0f));
        Shader.SetGlobalInt("_IsOcclusionOn", 1);

        var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
        surface.GetComponent<MeshRenderer>().sharedMaterial =
            AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/EnergyBall.mat");

        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -1f);
        camera.orthographic = true;
        camera.orthographicSize = 0.5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = RoomColor;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 3f;

        var texture = new RenderTexture(PictureSize, PictureSize, 24);
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture.active = texture;
        var image = new Texture2D(PictureSize, PictureSize, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, PictureSize, PictureSize), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;

        string tag = System.Environment.GetEnvironmentVariable("OCCLUSION_TAG") ?? "new";
        File.WriteAllBytes($"Logs/occlusion_{tag}.png", image.EncodeToPNG());
        Debug.Log($"[Preview] Wrote occlusion_{tag}.png");
    }

    static bool IsHand(Vector2 uv)
    {
        var palm = uv - new Vector2(0.5f, 0.3f);
        if (palm.magnitude < 0.2f)
            return true;

        for (int i = 0; i < 4; i++)
        {
            float angle = (-24f + i * 16f) * Mathf.Deg2Rad;
            var along = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            Vector2 fromPalm = palm;
            float reach = Vector2.Dot(fromPalm, along);
            float across = Mathf.Abs(fromPalm.x * along.y - fromPalm.y * along.x);
            if (reach > 0f && reach < 0.5f && across < 0.035f)
                return true;
        }
        return false;
    }

    static void ThrowSparks(Shockwave wave)
    {
        ParticleSystem sparks = wave.GetComponentInChildren<ParticleSystem>();
        Transform root = wave.transform;
        Vector3 reached = root.position;
        root.position = reached - root.forward * 0.8f;
        sparks.Simulate(0.15f, true, true);
        root.position = reached;
    }

    static void SaveView(Vector3 position, Quaternion rotation, string outputPath)
    {
        var cameraObject = new GameObject("Preview Camera", typeof(Camera));
        cameraObject.transform.SetPositionAndRotation(position, rotation);
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = RoomColor;
        camera.nearClipPlane = 0.05f;
        camera.gameObject.tag = "Untagged";
        camera.fieldOfView = 70f;

        var texture = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };
        camera.targetTexture = texture;
        camera.Render();

        RenderTexture.active = texture;
        var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;

        File.WriteAllBytes(outputPath, image.EncodeToPNG());
        Debug.Log($"[Preview] Wrote {outputPath}");
    }
}
