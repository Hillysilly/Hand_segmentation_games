using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Repulsor.EditorTools
{
    /// <summary>
    /// The character select screen's assets: the Quaternius mannequin (CC0, Universal Animation Library) as a prefab in Resources/Showcase with the clips the moves use, and the game-icons.net symbols in
    /// Resources/Icons imported as sprites. Batch: -executeMethod Repulsor.EditorTools.ShowcaseModels.Build
    /// </summary>
    public static class ShowcaseModels
    {
        const string Library1 = "Assets/Characters/Mannequin/UAL1.fbx";
        const string OutFolder = "Assets/Resources/Showcase";
        const string IconFolder = "Assets/Resources/Icons";
        const float Height = 1.8f;

        /// <summary>The clips the moves use (see CharacterShowcase), by their names in the libraries.</summary>
        public static readonly string[] Clips =
        {
            "Idle_Loop", "Punch_Jab", "Punch_Cross", "Punch_Enter", "Spell_Simple_Shoot", "Spell_Simple_Enter",
            "Spell_Simple_Idle_Loop", "Push_Loop", "Pistol_Shoot", "Pistol_Aim_Neutral", "Pistol_Idle_Loop",
            "Sword_Attack", "Sword_Idle",
        };

        public static void Build()
        {
            ImportIcons();
            BuildHands();
            MakeLegacy(Library1);
            Directory.CreateDirectory(OutFolder);

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Library1);
            var clips = new Dictionary<string, AnimationClip>();
            foreach (string path in new[] { Library1 })
            {
                foreach (AnimationClip clip in AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<AnimationClip>())
                {
                    if (clip.name.StartsWith("__preview"))
                        continue;
                    string name = clip.name.Contains("|") ? clip.name.Substring(clip.name.LastIndexOf('|') + 1) : clip.name;
                    if (!clips.ContainsKey(name))
                        clips[name] = clip;
                }
            }

            var root = new GameObject("Mannequin");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            if (clips.TryGetValue("Idle_Loop", out AnimationClip idle))
                idle.SampleAnimation(model, 0f);

            // Turn it to face +Z: forward is from the ankles toward the toes.
            Transform foot = model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "DEF-foot.L");
            Transform toe = model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "DEF-toe.L");
            if (foot != null && toe != null)
            {
                Vector3 facing = Vector3.ProjectOnPlane(toe.position - foot.position, Vector3.up);
                float yaw = Vector3.SignedAngle(facing, Vector3.forward, Vector3.up);
                model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * model.transform.localRotation;
                Debug.Log($"[ShowcaseModels] faced {facing.normalized}, turned {yaw:0} degrees");
            }

            Bounds bounds = PoseBounds(model);
            float scale = Height / Mathf.Max(0.01f, bounds.size.y);
            model.transform.localScale *= scale;
            bounds = PoseBounds(model);
            model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            // One material per original slot, tinted at runtime: the body takes the player's colour.
            var log = new StringBuilder();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    string slotName = slots[i] != null ? slots[i].name : "Slot" + i;
                    log.Append($" material {i}: {slotName} {(slots[i] != null && slots[i].HasProperty("_Color") ? slots[i].color.ToString() : "")};");
                    slots[i] = SlotMaterial(slotName, slots[i]);
                }
                renderer.sharedMaterials = slots;
                if (renderer is SkinnedMeshRenderer skinned)
                    skinned.updateWhenOffscreen = true;
            }

            if (!model.TryGetComponent(out Animation animation))
                animation = model.AddComponent<Animation>();
            // Saved as a plain list (an Animation keeps clips, not the names they're added under; the game
            // names them by what follows the "|").
            var chosen = new List<AnimationClip>();
            foreach (string name in Clips)
            {
                if (clips.TryGetValue(name, out AnimationClip clip))
                    chosen.Add(clip);
                else
                    Debug.LogWarning($"[ShowcaseModels] No clip {name}");
            }
            AnimationUtility.SetAnimationClips(animation, chosen.ToArray());
            int added = chosen.Count;
            // Check each clip drives this model: its curves' paths must exist under the model.
            foreach (string name in Clips)
            {
                if (!clips.TryGetValue(name, out AnimationClip check))
                    continue;
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(check);
                int found = bindings.Count(b => model.transform.Find(b.path) != null || b.path == "");
                Debug.Log($"[ShowcaseModels] {name}: legacy {check.legacy}, length {check.length:0.00}, {found}/{bindings.Length} curves find their bone, first path '{(bindings.Length > 0 ? bindings[0].path : "-")}'");
            }
            animation.clip = idle;
            animation.playAutomatically = true;
            animation.wrapMode = WrapMode.Loop;
            animation.cullingType = AnimationCullingType.AlwaysAnimate;

            PrefabUtility.SaveAsPrefabAsset(root, $"{OutFolder}/Mannequin.prefab");
            Object.DestroyImmediate(root);
            Debug.Log($"[ShowcaseModels] Mannequin: scale {scale:0.0000}, {added}/{Clips.Length} clips, {clips.Count} in the libraries;{log}");
            Debug.Log("[ShowcaseModels] Library clips: " + string.Join(", ", clips.Keys.OrderBy(k => k)));
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The hand sign's hands: the XR Hands package's sample hands (Unity Package Distribution License), copied
        /// into Assets/Characters/Hands, as prefabs with one lit material the game tints.
        /// </summary>
        static void BuildHands()
        {
            Directory.CreateDirectory(OutFolder);
            string materialPath = $"{OutFolder}/HandSign.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Repulsor/Enemy Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_BaseColor", new Color(0.9f, 0.9f, 0.92f));
            material.SetFloat("_Smoothness", 0.45f);
            EditorUtility.SetDirty(material);
            foreach (string side in new[] { "Right", "Left" })
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Characters/Hands/{side}Hand.fbx");
                if (source == null)
                {
                    Debug.LogWarning($"[ShowcaseModels] No {side}Hand.fbx");
                    continue;
                }
                var hand = (GameObject)PrefabUtility.InstantiatePrefab(source);
                PrefabUtility.UnpackPrefabInstance(hand, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                hand.name = side + "Hand";
                foreach (Renderer renderer in hand.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    if (renderer is SkinnedMeshRenderer skinned)
                        skinned.updateWhenOffscreen = true;
                }
                PrefabUtility.SaveAsPrefabAsset(hand, $"{OutFolder}/{side}Hand.prefab");
                Object.DestroyImmediate(hand);
            }
            Debug.Log("[ShowcaseModels] Hand sign hands built");
        }

        static Material SlotMaterial(string slotName, Material original)
        {
            string key = slotName.ToLowerInvariant().Contains("joint") || slotName.ToLowerInvariant().Contains("purple") ? "MannequinJoints" : "MannequinBody";
            string path = $"{OutFolder}/{key}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Repulsor/Enemy Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            Color color = key == "MannequinJoints" ? new Color(0.12f, 0.13f, 0.16f) : new Color(0.85f, 0.87f, 0.9f);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.55f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void ImportIcons()
        {
            foreach (string path in Directory.GetFiles(IconFolder, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                if (importer == null || importer.textureType == TextureImporterType.Sprite)
                    continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 256;
                importer.SaveAndReimport();
            }
        }

        static void MakeLegacy(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer.animationType == ModelImporterAnimationType.Legacy)
                return;
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.SaveAndReimport();
        }

        static Bounds PoseBounds(GameObject model)
        {
            bool any = false;
            var bounds = new Bounds();
            var baked = new Mesh();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Vector3[] vertices;
                Matrix4x4 toWorld;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.BakeMesh(baked, true);
                    vertices = baked.vertices;
                    toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
                }
                else if (renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                {
                    vertices = filter.sharedMesh.vertices;
                    toWorld = renderer.transform.localToWorldMatrix;
                }
                else
                {
                    continue;
                }
                Matrix4x4 toRoot = model.transform.parent.worldToLocalMatrix * toWorld;
                foreach (Vector3 v in vertices)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(v);
                    if (!any)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }
            Object.DestroyImmediate(baked);
            return bounds;
        }
    }
}
