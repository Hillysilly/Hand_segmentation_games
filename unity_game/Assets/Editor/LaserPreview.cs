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
