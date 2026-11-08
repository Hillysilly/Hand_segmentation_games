using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates the materials the game loads at runtime from Resources (shields, the knocked-out grey, aim lines).
/// Menu: Repulsor > Build Game Assets. Batch: -executeMethod GameAssets.Build
/// </summary>
public static class GameAssets
{
    const string Folder = "Assets/Resources";

    [MenuItem("Repulsor/Build Game Assets")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        Save("Bubble", "Repulsor/Bubble", m => m.SetColor("_Color", new Color(0.3f, 0.75f, 1f)));
        Save("DeathView", "Repulsor/Room Dim", m =>
        {
            m.SetColor("_Tint", new Color(0.32f, 0.32f, 0.34f));
            m.SetFloat("_Amount", 0f);
        });
        SaveMesh("SphereMesh", PrimitiveType.Sphere);
        SaveMesh("QuadMesh", PrimitiveType.Quad);
        SaveMesh("CubeMesh", PrimitiveType.Cube);
        SaveMesh("CylinderMesh", PrimitiveType.Cylinder);
        EnableBoundingBoxes();
        PlayerSettings.Android.forceInternetPermission = true;   // LAN multiplayer
        Save("EnemyPlaceholder", "Repulsor/Orb", m => m.SetColor("_BaseColor", new Color(0.85f, 0.2f, 0.15f)));
        Save("EnemyBolt", "Repulsor/Orb", m => m.SetColor("_BaseColor", new Color(1f, 0.25f, 0.2f)));
        // Sprites/Default uses the line's vertex colour, so strands can fade and turn dark.
        Save("WebLine", "Sprites/Default", m => m.SetColor("_Color", Color.white));
        Save("AimLine", "Universal Render Pipeline/Unlit", m => m.SetColor("_BaseColor", new Color(0.35f, 0.75f, 1f)));
        BuildFx();
        AssetDatabase.SaveAssets();
        Debug.Log("[Setup] Game assets built");
    }

    /// <summary>
    /// Effect sprites drawn here from maths (soft glow, ring, flame puff, star, spark), saved as PNGs with the shape in
    /// alpha, each with an additive material in Resources/Fx. The energy ball looks are built from them.
    /// </summary>
    public static void BuildFx()
    {
        const string fx = Folder + "/Fx";
        Directory.CreateDirectory(fx);
        SaveSprite(fx, "FxGlow", (x, y, r, a) => Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));
        SaveSprite(fx, "FxRing", (x, y, r, a) =>
            Mathf.Exp(-Sq((r - 0.8f) / 0.06f)) + 0.25f * Mathf.Exp(-Sq((r - 0.8f) / 0.18f)));
        SaveSprite(fx, "FxFlame", (x, y, r, a) =>
        {
            // A soft puff torn up by noise, so overlapping flames flicker rather than reading as dots.
            float noise = Mathf.PerlinNoise(x * 3.1f + 7.3f, y * 3.1f + 2.9f) * 0.65f + Mathf.PerlinNoise(x * 7.3f + 1.1f, y * 7.3f + 5.5f) * 0.35f;
            float body = Mathf.Clamp01(1f - r * (0.75f + 0.6f * noise));
            return Mathf.SmoothStep(0f, 1f, body) * Mathf.Lerp(0.55f, 1f, noise);
        });
        SaveSprite(fx, "FxStar", (x, y, r, a) =>
        {
            float fade = Mathf.Pow(Mathf.Clamp01(1f - r), 1.4f);
            float big = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2f)), 40f);                 // four long spikes
            float small = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2f + Mathf.PI / 4f)), 60f) * 0.55f; // four short ones between
            float core = Mathf.Pow(Mathf.Clamp01(1f - r * 2.2f), 2f);
            return Mathf.Clamp01(core + big * fade + small * fade * fade);
        });
        SaveSprite(fx, "FxSpark", (x, y, r, a) => Mathf.Pow(Mathf.Clamp01(1f - r), 5f) + Mathf.Pow(Mathf.Clamp01(1f - r * 3f), 2f));
        // A column of fire, tiling top to bottom so it can scroll upward: a white-hot thread in the middle,
        // streaky flame either side whose edges wander, so the column never looks like a tube.
        SaveTexture(fx, "FxBeam", 128, 256, true, (u, v) =>
        {
            float x = u * 2f - 1f;
            float wobble = Periodic(v, 3, 0.31f) * 0.05f + Periodic(v, 7, 0.77f) * 0.025f;
            float edge = 0.62f + Periodic(v, 5, 0.13f) * 0.12f + Periodic(v, 11, 0.5f) * 0.06f;
            float body = Mathf.Clamp01(1f - Mathf.Abs(x - wobble) / edge);
            float streaks = 0.5f + 0.5f * Mathf.Sin((x * 9f + Periodic(v, 2, 0.4f) * 2.5f) * Mathf.PI);
            float flicker = 0.55f + 0.45f * (0.5f + 0.5f * Periodic(v, 9, x * 0.7f + 0.2f));
            float core = Mathf.Exp(-Sq((x - wobble * 0.5f) / 0.16f));
            return Mathf.Clamp01(Mathf.Pow(body, 1.6f) * Mathf.Lerp(0.55f, 1f, streaks) * flicker + core);
        });
        // A tech projectile seen side on, nose to the right (u = 1): a bright rounded nose, and behind it a body of
        // circuit-like panels and traces that thins out into streaks toward the back.
        var random = new System.Random(12);
        var cells = new float[24, 10];
        for (int i = 0; i < 24; i++)
            for (int j = 0; j < 10; j++)
                cells[i, j] = random.NextDouble() < 0.45 ? (float)random.NextDouble() : 0f;
        SaveTexture(fx, "FxTech", 256, 128, false, (u, v) =>
        {
            float y = v * 2f - 1f;
            // Rounded nose: the front half of an ellipse, with a bright rim.
            float d = Mathf.Sqrt(Sq(Mathf.Max(0f, u - 0.68f) / 0.3f) + Sq(y / 0.8f));
            float front = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.68f, u));
            float nose = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.45f, d)) * front;
            float rim = Mathf.Exp(-Sq((d - 0.93f) / 0.05f)) * Mathf.InverseLerp(0.6f, 0.75f, u);
            // The body tapers from the nose to a point at the back, with soft edges.
            float half = 0.78f * Mathf.Sqrt(Mathf.Clamp01(u / 0.68f));
            float inside = Mathf.Clamp01((half - Mathf.Abs(y)) * 10f) * (u < 0.72f ? 1f : 0f);
            int cx = Mathf.Min(23, (int)(u / 0.72f * 24f));
            int cy = Mathf.Min(9, (int)(v * 10f));
            float panel = cells[cx, cy];
            float fx01 = u / 0.72f * 24f - cx, fy01 = v * 10f - cy;
            float inset = panel > 0f && fx01 > 0.15f && fx01 < 0.85f && fy01 > 0.22f && fy01 < 0.78f ? 1f : 0f;
            float trace = Mathf.Exp(-Sq((fy01 - 0.5f) / 0.07f)) * (cells[cx, (cy + 3) % 10] > 0.4f ? 1f : 0f);
            float bodyGlow = 0.22f * inside * Mathf.Lerp(0.3f, 1f, u);
            float lit = (inset * Mathf.Lerp(0.5f, 1f, panel) + trace * 0.8f) * inside * Mathf.Lerp(0.45f, 1f, u / 0.72f);
            // Speed lines running back past the body's tail.
            float line = Mathf.Exp(-Sq((v * 14f % 1f - 0.5f) / 0.05f)) * Mathf.Clamp01(1f - Mathf.Abs(y) / 0.7f)
                * Mathf.Clamp01(u * 1.6f) * (u < 0.7f ? 1f : 0f) * (cells[(int)(v * 14f) % 24, 0] > 0f ? 1f : 0.3f);
            return Mathf.Clamp01(nose + rim + bodyGlow + lit + line * 0.7f);
        });
        foreach (string name in new[] { "FxGlow", "FxRing", "FxFlame", "FxStar", "FxSpark", "FxBeam", "FxTech" })
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{fx}/{name}.png");
            string path = $"{fx}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Repulsor/FxSprite"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find("Repulsor/FxSprite");
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_Hot", name == "FxRing" ? 0.3f : 0.7f);
            material.SetFloat("_Scroll", name == "FxBeam" ? 1.8f : 0f);
            material.SetFloat("_FadeEnds", name == "FxBeam" ? 1f : 0f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }
    }

    static float Sq(float v) => v * v;

    /// <summary>Smooth noise that repeats exactly every unit of <paramref name="v"/> (so textures tile vertically), -1 to 1.</summary>
    static float Periodic(float v, int frequency, float phase) =>
        0.6f * Mathf.Sin((v * frequency + phase) * Mathf.PI * 2f) + 0.4f * Mathf.Sin((v * frequency * 2 + phase * 3.1f) * Mathf.PI * 2f);

    /// <summary>Writes a white texture whose alpha is shape(u, v), with u and v from 0 to 1.</summary>
    static void SaveTexture(string folder, string name, int width, int height, bool repeat, System.Func<float, float, float> shape)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int j = 0; j < height; j++)
            for (int i = 0; i < width; i++)
            {
                float value = Mathf.Clamp01(shape((i + 0.5f) / width, (j + 0.5f) / height));
                pixels[j * width + i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(value * 255f));
            }
        texture.SetPixels32(pixels);
        string path = $"{folder}/{name}.png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.wrapModeU = TextureWrapMode.Clamp;
        importer.wrapModeV = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
    }

    /// <summary>Writes a white sprite whose alpha is shape(x, y, radius, angle), with x and y from -1 to 1.</summary>
    static void SaveSprite(string folder, string name, System.Func<float, float, float, float, float> shape)
    {
        const int Size = 128;
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        var pixels = new Color32[Size * Size];
        for (int j = 0; j < Size; j++)
        {
            for (int i = 0; i < Size; i++)
            {
                float x = (i + 0.5f) / Size * 2f - 1f;
                float y = (j + 0.5f) / Size * 2f - 1f;
                float r = Mathf.Sqrt(x * x + y * y);
                float value = r >= 1f ? 0f : Mathf.Clamp01(shape(x, y, r, Mathf.Atan2(y, x)));
                pixels[j * Size + i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(value * 255f));
            }
        }
        texture.SetPixels32(pixels);
        string path = $"{folder}/{name}.png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
    }

    /// <summary>
    /// Copies a built-in primitive's mesh, so the game can draw spheres and quads without CreatePrimitive, which
    /// wants physics colliders that this build strips out.
    /// </summary>
    static void SaveMesh(string name, PrimitiveType type)
    {
        GameObject primitive = GameObject.CreatePrimitive(type);
        Mesh mesh = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(primitive);
        string path = $"{Folder}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
            return;   // Keep the existing asset and its GUID.
        AssetDatabase.CreateAsset(mesh, path);
    }

    /// <summary>Turns on Space Setup furniture boxes for Android, without regenerating the scene.</summary>
    static void EnableBoundingBoxes()
    {
        var settings = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        var feature = settings.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.ARBoundingBoxFeature>();
        if (feature == null)
            throw new System.Exception("Meta bounding box feature not found");
        feature.enabled = true;
        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(settings);
        Debug.Log("[Setup] Bounding boxes on");
    }

    static void Save(string name, string shaderName, System.Action<Material> configure)
    {
        string path = $"{Folder}/{name}.mat";
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
            throw new System.Exception($"Shader {shaderName} not found");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        configure(material);
        EditorUtility.SetDirty(material);
    }
}
