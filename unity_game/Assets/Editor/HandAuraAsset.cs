using UnityEditor;
using UnityEngine;

namespace Repulsor.EditorTools
{
    /// <summary>Resources/HandAura.mat (Repulsor/HandAura), the level 3 hand aura's material. Batch: -executeMethod Repulsor.EditorTools.HandAuraAsset.Build</summary>
    public static class HandAuraAsset
    {
        public static void Build()
        {
            const string path = "Assets/Resources/HandAura.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Repulsor/HandAura"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find("Repulsor/HandAura");
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            Debug.Log("[HandAuraAsset] built");
        }
    }
}
