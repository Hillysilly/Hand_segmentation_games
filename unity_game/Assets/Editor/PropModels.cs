using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Repulsor.EditorTools
{
    /// <summary>
    /// Turns the CC0 prop models in Assets/Props (bows, crossbow, arrows, knight shield, rocks) into prefabs in
    /// Resources/Props, each turned and scaled to a fixed convention so the game can place them by hand:
    /// - Bow: limbs along +Y, arrows leave along +Z, origin at the grip.
    /// - Crossbow: stock along +Z (it shoots along +Z), origin at the grip, 0.5 m long.
    /// - Arrow / Bolt: along +Z with the tip at +Z, origin at the tail.
    /// - KnightShield: face toward +Z, 0.65 m tall, origin at its middle.
    /// - Rocks: origin at the middle of their base, 1 m across (scaled in game).
    /// Batch: -executeMethod Repulsor.EditorTools.PropModels.Build, then .Render (without -nographics).
    /// </summary>
    public static class PropModels
    {
        const string OutFolder = "Assets/Resources/Props";
        const string Weapons = "Assets/Props/Weapons/";
        const string Rocks = "Assets/Props/Rocks/";

        enum Anchor { Centre, Base, MinZ, MaxZ, MinX }

        sealed class Entry
        {
            public string Key;
            public string Model;
            public string Texture;
            /// <summary>Applied before measuring, to bring the model to the convention.</summary>
            public Vector3 Euler;
            /// <summary>The model's size along this axis (after turning) becomes <see cref="Size"/>.</summary>
            public int Axis;
            public float Size;
            public Anchor Anchor;
            /// <summary>Glowing tint over the model's colours (rocks get orange cracks in game instead).</summary>
            public Color? Tint;
            /// <summary>A flat model: turned so its thinnest side faces +Z.</summary>
            public bool FlatToZ;
            /// <summary>Drawn with this shader (unlit, tinted in game) instead of its own colours.</summary>
            public string Shader;
        }

        static readonly Entry[] Entries =
        {
            // Quaternius bows: limbs along Y, the string on +X and the grip bulging toward -X; yaw -90 puts the grip
            // in front (+Z) and the string toward the archer (checked in Logs/preview_props_side.png).
            new Entry { Key = "Bow", Model = Weapons + "Quaternius/Bow_Wooden.fbx", Euler = new Vector3(0f, -90f, 0f), Axis = 1, Size = 1.15f, Anchor = Anchor.MaxZ },
            new Entry { Key = "BowGolden", Model = Weapons + "Quaternius/Bow_Golden.fbx", Euler = new Vector3(0f, -90f, 0f), Axis = 1, Size = 1.15f, Anchor = Anchor.MaxZ },
            new Entry { Key = "Arrow", Model = Weapons + "Quaternius/Arrow.fbx", Axis = 2, Size = 0.75f, Anchor = Anchor.MinZ },
            new Entry { Key = "Crossbow", Model = Weapons + "KayKit/crossbow_1handed.fbx", Texture = Weapons + "KayKit/rogue_texture.png", Axis = 2, Size = 0.5f, Anchor = Anchor.Centre },
            new Entry { Key = "Bolt", Model = Weapons + "KayKit/arrow.fbx", Texture = Weapons + "KayKit/rogue_texture.png", Euler = new Vector3(-90f, 0f, 0f), Axis = 2, Size = 0.32f, Anchor = Anchor.MinZ },
            new Entry { Key = "KnightShield", Model = Weapons + "Quaternius/Shield_Heater.fbx", Axis = 1, Size = 0.65f, Anchor = Anchor.Centre },
            new Entry { Key = "RockA", Model = Rocks + "rock_largeA.fbx", Axis = 0, Size = 1f, Anchor = Anchor.Base },
            new Entry { Key = "RockB", Model = Rocks + "rock_largeB.fbx", Axis = 0, Size = 1f, Anchor = Anchor.Base },
            new Entry { Key = "RockC", Model = Rocks + "rock_largeC.fbx", Axis = 0, Size = 1f, Anchor = Anchor.Base },
            new Entry { Key = "RockSmallA", Model = Rocks + "rock_smallA.fbx", Axis = 0, Size = 1f, Anchor = Anchor.Centre },
            new Entry { Key = "RockSmallB", Model = Rocks + "rock_smallB.fbx", Axis = 0, Size = 1f, Anchor = Anchor.Centre },
            new Entry { Key = "RockSmallC", Model = Rocks + "rock_smallC.fbx", Axis = 0, Size = 1f, Anchor = Anchor.Centre },
            new Entry { Key = "SpikeA", Model = Rocks + "rock_tallA.fbx", Axis = 1, Size = 1f, Anchor = Anchor.Base },
            new Entry { Key = "SpikeB", Model = Rocks + "rock_tallB.fbx", Axis = 1, Size = 1f, Anchor = Anchor.Base },
            new Entry { Key = "SpikeC", Model = Rocks + "rock_tallC.fbx", Axis = 1, Size = 1f, Anchor = Anchor.Base },
            // Quaternius cobweb (CC0): flat, facing +Z, 1 m across, origin at its middle; tinted and faded in game.
            new Entry { Key = "Web", Model = "Assets/Props/Web/Cobweb.glb", FlatToZ = true, Axis = 0, Size = 1f, Anchor = Anchor.Centre, Shader = "Repulsor/WebModel" },
        };

        public static string[] Keys => Entries.Select(e => e.Key).ToArray();

        public static void Build()
        {
            Directory.CreateDirectory(OutFolder + "/Materials");
            AssetDatabase.Refresh();
            foreach (Entry entry in Entries)
                BuildOne(entry);
            // Stone with glowing cracks, for rock orbs, spikes, gauntlets and the rock aura.
            SaveRock("Rock", new Color(0.42f, 0.29f, 0.19f), 0.8f, 0f);
            SaveRock("RockHot", new Color(0.35f, 0.22f, 0.14f), 2.2f, 0f);
            SaveRock("RockGold", new Color(0.4f, 0.3f, 0.22f), 0.6f, 1f);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void BuildOne(Entry entry)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Model);
            if (source == null)
            {
                Debug.LogError($"[PropModels] Missing {entry.Model}");
                return;
            }
            var root = new GameObject("Prop " + entry.Key);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localRotation = Quaternion.Euler(entry.Euler) * model.transform.localRotation;

            Bounds bounds = MeshBounds(model);
            if (entry.FlatToZ)
            {
                Vector3 size = bounds.size;
                if (size.x < size.y && size.x < size.z)
                    model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * model.transform.localRotation;
                else if (size.y < size.x && size.y < size.z)
                    model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f) * model.transform.localRotation;
                bounds = MeshBounds(model);
                Debug.Log($"[PropModels] {entry.Key}: was {size.x:0.0000} x {size.y:0.0000} x {size.z:0.0000}, now {bounds.size.x:0.0000} x {bounds.size.y:0.0000} x {bounds.size.z:0.0000}");
            }
            float scale = entry.Size / Mathf.Max(1e-5f, bounds.size[entry.Axis]);
            model.transform.localScale *= scale;
            bounds = MeshBounds(model);
            Vector3 anchor = bounds.center;
            switch (entry.Anchor)
            {
                case Anchor.Base: anchor.y = bounds.min.y; break;
                case Anchor.MinZ: anchor.z = bounds.min.z; break;
                case Anchor.MaxZ: anchor.z = bounds.max.z; break;
                case Anchor.MinX: anchor.x = bounds.min.x; break;
            }
            model.transform.localPosition -= anchor;

            Material textured = entry.Texture != null ? TexturedMaterial(entry) : entry.Shader != null ? ShaderMaterial(entry) : null;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = textured != null ? textured : ColourMaterial(entry.Key, slots[i]);
                renderer.sharedMaterials = slots;
            }
            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
                Object.DestroyImmediate(animator);

            PrefabUtility.SaveAsPrefabAsset(root, $"{OutFolder}/{entry.Key}.prefab");
            Object.DestroyImmediate(root);
            bounds.center -= anchor;
            Debug.Log($"[PropModels] {entry.Key}: scale {scale:0.0000}, size {bounds.size.x:0.00} x {bounds.size.y:0.00} x {bounds.size.z:0.00}, centre {bounds.center}");
        }

        static void SaveRock(string name, Color stone, float glow, float sparkle)
        {
            string path = $"{OutFolder}/Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = material == null;
            if (fresh)
                material = new Material(Shader.Find("Repulsor/Rock"));
            material.shader = Shader.Find("Repulsor/Rock");
            material.SetColor("_BaseColor", stone);
            material.SetFloat("_Glow", glow);
            material.SetFloat("_Sparkle", sparkle);
            material.SetFloat("_Scale", 3.5f);
            if (fresh)
                AssetDatabase.CreateAsset(material, path);
            EditorUtility.SetDirty(material);
        }

        static Bounds MeshBounds(GameObject model)
        {
            Transform root = model.transform.parent;
            bool any = false;
            var bounds = new Bounds();
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 point = toRoot.MultiplyPoint3x4(vertex);
                    if (!any)
                        bounds = new Bounds(point, Vector3.zero);
                    else
                        bounds.Encapsulate(point);
                    any = true;
                }
            }
            foreach (SkinnedMeshRenderer skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                skinned.BakeMesh(baked, false);
                Matrix4x4 toRoot = root.worldToLocalMatrix * Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
                foreach (Vector3 vertex in baked.vertices)
                {
                    Vector3 point = toRoot.MultiplyPoint3x4(vertex);
                    if (!any)
                        bounds = new Bounds(point, Vector3.zero);
                    else
                        bounds.Encapsulate(point);
                    any = true;
                }
                Object.DestroyImmediate(baked);
            }
            return bounds;
        }

        static Material TexturedMaterial(Entry entry)
        {
            string path = $"{OutFolder}/Materials/{Path.GetFileNameWithoutExtension(entry.Texture)}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(entry.Texture));
            material.SetFloat("_Smoothness", 0.3f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Material ShaderMaterial(Entry entry)
        {
            string path = $"{OutFolder}/Materials/{entry.Key}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(entry.Shader));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find(entry.Shader);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material ColourMaterial(string key, Material original)
        {
            string path = $"{OutFolder}/Materials/{key}_{(original != null ? original.name : "Plain")}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color color = original == null ? Color.grey
                : original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor")
                : original.HasProperty("_Color") ? original.color : Color.grey;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.35f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Batch (with graphics): the web from the front (+Z), the side and at an angle, to Logs/preview_web_*.png.</summary>
        public static void RenderWeb()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = Object.Instantiate(Resources.Load<GameObject>("Props/Web"));
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", Color.white);
                renderer.SetPropertyBlock(block);
            }
            Marker(Vector3.zero, Vector3.right, Color.red);
            Marker(Vector3.zero, Vector3.up, Color.green);
            Marker(Vector3.zero, Vector3.forward, Color.blue);
            Snap(new Vector3(0f, 0f, 1.6f), Vector3.zero, 50f, "Logs/preview_web_front.png");
            Snap(new Vector3(1.6f, 0f, 0f), Vector3.zero, 50f, "Logs/preview_web_side.png");
            Snap(new Vector3(1f, 0.6f, 1.2f), Vector3.zero, 50f, "Logs/preview_web_angle.png");
        }

        /// <summary>Batch (with graphics): every prop, from the side (+X) and from above, to Logs/preview_props_*.png.</summary>
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);
            // Axis markers at each origin: red +X, green +Y, blue +Z.
            for (int i = 0; i < Entries.Length; i++)
            {
                var prefab = Resources.Load<GameObject>("Props/" + Entries[i].Key);
                if (prefab == null)
                    continue;
                var go = Object.Instantiate(prefab);
                go.transform.position = i < 5 ? new Vector3(i * 1.4f, 0f, 0f) : new Vector3((i % 5) * 1.4f, 0f, -(i / 5) * 1.6f - 2f);
                Marker(go.transform.position, Vector3.right, Color.red);
                Marker(go.transform.position, Vector3.up, Color.green);
                Marker(go.transform.position, Vector3.forward, Color.blue);
            }
            Snap(new Vector3(2.8f, 1.2f, 3.5f), new Vector3(2.8f, 0.2f, -1.6f), 60f, "Logs/preview_props.png");
            // Side close-ups of the weapons (looking along -X, so +Z points right).
            for (int i = 0; i < 5; i++)
            {
                Vector3 at = new Vector3(i * 1.4f, 0f, 0f);
                Snap(at + new Vector3(0.7f, 0.05f, 0f), at, 60f, $"Logs/preview_prop_{Entries[i].Key}.png");
            }
            Snap(new Vector3(9f, 0.4f, -1.6f), new Vector3(2.8f, 0.3f, -1.6f), 50f, "Logs/preview_props_side.png");
        }

        static void Marker(Vector3 at, Vector3 axis, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = at + axis * 0.15f;
            go.transform.localScale = Vector3.one * 0.02f + axis * 0.3f;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        static void Snap(Vector3 position, Vector3 target, float fov, string outputPath)
        {
            var cameraObject = new GameObject("Preview Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.3f, 0.29f, 0.27f);
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = fov;
            var texture = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.DestroyImmediate(cameraObject);
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
            Debug.Log($"[Preview] Wrote {outputPath}");
        }
    }
}
