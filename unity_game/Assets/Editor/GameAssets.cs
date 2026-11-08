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
        Save("AimLine", "Universal Render Pipeline/Unlit", m => m.SetColor("_BaseColor", new Color(0.35f, 0.75f, 1f)));
        AssetDatabase.SaveAssets();
        Debug.Log("[Setup] Game assets built");
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
