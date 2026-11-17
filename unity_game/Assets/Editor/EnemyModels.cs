using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Repulsor.EditorTools
{
    /// <summary>
    /// Turns the CC0 enemy models in Assets/Enemies into ready-to-spawn prefabs in Resources/Enemies (see
    /// <see cref="Repulsor.EnemyLook"/>). Each model gets URP Lit materials from its own textures, is scaled to the
    /// enemy's size with its feet (robots) or middle (drones) at the prefab's origin, faces +Z, carries its walk or
    /// idle clip on a legacy Animation, and has eye markers where the game puts red glows.
    /// Batch: -executeMethod Repulsor.EditorTools.EnemyModels.Build, then .Render (without -nographics).
    /// </summary>
    public static class EnemyModels
    {
        const string OutFolder = "Assets/Resources/Enemies";
        const string Mechs = "Assets/Enemies/Mechs/";
        const string Kit = "Assets/Enemies/SciFiKit/";

        sealed class Entry
        {
            public string Key;
            public string Model;
            public bool Drone;
            /// <summary>Base colour texture (null keeps the model's own material colours).</summary>
            public string Texture;
            public string Emissive;
            /// <summary>Turns the model to face +Z.</summary>
            public float Yaw;
            /// <summary>Where the glowing eyes go, in metres in the finished prefab (it faces +Z, feet or middle at 0).</summary>
            public Vector3[] Eyes = new Vector3[0];
            public float EyeSize = 0.07f;
            /// <summary>Robots: how tall it stands. Taken from the enemy kind that uses this model.</summary>
            public float Height => EnemySpecFor(Key)?.Height ?? 1.3f;
            /// <summary>The height the eye positions were measured at; they scale with the real height.</summary>
            public float EyeHeight = 1.3f;
            /// <summary>Keep the model's own textures (glTF models), moved onto the game's lit shader.</summary>
            public bool OwnTextures;
        }

        static EnemySpec EnemySpecFor(string key) => System.Array.Find(EnemySpec.All, spec => spec.Model == key);

        static readonly Entry[] Entries =
        {
            // Eye positions read off the gridded close-ups (Logs/preview_enemy_*_front/side).
            new Entry { Key = "George", Model = Mechs + "George.fbx", Texture = Mechs + "Textures/George_Texture.png",
                Eyes = new[] { new Vector3(0f, 1.085f, 0.035f) }, EyeSize = 0.07f },
            new Entry { Key = "Leela", Model = Mechs + "Leela.fbx", Texture = Mechs + "Textures/Leela_Texture.png",
                Eyes = new[] { new Vector3(0.01f, 0.987f, 0.16f) }, EyeSize = 0.16f },
            new Entry { Key = "Mike", Model = Mechs + "Mike.fbx", Texture = Mechs + "Textures/Mike_Texture.png",
                Eyes = new[] { new Vector3(0.067f, 1.21f, 0.15f), new Vector3(-0.05f, 1.21f, 0.15f) }, EyeSize = 0.085f },
            new Entry { Key = "Stan", Model = Mechs + "Stan.fbx", Texture = Mechs + "Textures/Stan_Texture.png",
                Eyes = new[] { new Vector3(0.047f, 1.223f, 0.085f), new Vector3(-0.067f, 1.223f, 0.085f) }, EyeSize = 0.09f },
            new Entry { Key = "Classic", Model = "Assets/Enemies/ClassicRobot/Robot.fbx",
                Eyes = new[] { new Vector3(0.215f, 1.047f, 0.36f), new Vector3(-0.203f, 1.04f, 0.36f) }, EyeSize = 0.26f },
            new Entry { Key = "QuadShell", Model = Kit + "Enemy_QuadShell.fbx", EyeHeight = 0.75f, Texture = Kit + "Textures/T_Enemies_Large_BaseColor.png",
                Emissive = Kit + "Textures/T_Enemies_Large_Emissive.png",
                Eyes = new[] { new Vector3(0.087f, 0.533f, 0.17f), new Vector3(-0.2f, 0.533f, 0.17f) }, EyeSize = 0.11f },
            new Entry { Key = "EyeDrone", Model = Kit + "Enemy_EyeDrone.fbx", Drone = true, Texture = Kit + "Textures/T_Enemies_BaseColor.png",
                Emissive = Kit + "Textures/T_Enemies_Emissive.png",
                Eyes = new[] { new Vector3(0f, 0f, 0.2f) }, EyeSize = 0.17f, EyeHeight = 0.4f },
            // The boss: the sci-fi kit's four-legged walker with twin cannons, scaled up to 2.3 m.
            new Entry { Key = "Trilobite", Model = Kit + "Enemy_Trilobite.fbx", Texture = Kit + "Textures/T_Enemies_Large_BaseColor.png",
                Emissive = Kit + "Textures/T_Enemies_Large_Emissive.png", EyeHeight = 2.3f },
            // GOLIATH, the mech boss: "The Morser 16" by anonDutch (CC BY 4.0), scaled to 2.7 m. Its swing and gun
            // aim are animated in code (MechBrain); the model brings walking and running.
            new Entry { Key = "MechBoss", Model = "Assets/Enemies/Morser/Morser16.glb", OwnTextures = true },
        };

        /// <summary>Drones are this wide.</summary>
        const float DroneWidth = 0.4f;
        static readonly Color EyeRed = new Color(1f, 0.08f, 0.04f);

        /// <summary>Batch: builds every prefab in Resources/Enemies.</summary>
        public static void Build()
        {
            Directory.CreateDirectory(OutFolder + "/Materials");
            AssetDatabase.Refresh();
            foreach (Entry entry in Entries)
                BuildOne(entry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>Batch (with graphics): every enemy from the front and from the side, to Logs/preview_enemies*.png.</summary>
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
            var eye = new GameObject("Viewer").transform;
            FxSpin.Eye = eye;

            float spacing = 1.6f;
            for (int i = 0; i < Entries.Length; i++)
            {
                Entry entry = Entries[i];
                var look = EnemyLook.Create(entry.Key, null);
                if (look == null)
                    continue;
                look.transform.position = new Vector3((i - (Entries.Length - 1) * 0.5f) * spacing, entry.Drone ? 0.9f : 0f, 0f);
                look.Pose(0.25f);
            }
            int count = Entries.Length;
            foreach (FxSpin spin in Object.FindObjectsByType<FxSpin>())
                spin.Tick(0f);
            // From the front left and above, as you'd see them walking at you.
            eye.position = new Vector3(-3f, 2.2f, 5f);
            foreach (FxSpin spin in Object.FindObjectsByType<FxSpin>())
                spin.Tick(0f);
            Snap(eye.position, new Vector3(0f, 0.6f, 0f), count * spacing * 0.75f, "Logs/preview_enemies.png");
            // Straight-on close-ups of each head, front and side, orthographic and 0.6 m tall, for placing the eyes:
            // a pixel is 0.6/900 m, the middle pixel is the point named in the file.
            for (int i = 0; i < count; i++)
            {
                float x = (i - (count - 1) * 0.5f) * spacing;
                float y = Entries[i].Drone ? 0.9f : Entries[i].Height - 0.25f * Entries[i].Height / 1.3f;
                Vector3 at = new Vector3(x, y, 0f);
                foreach (FxSpin spin in Object.FindObjectsByType<FxSpin>())
                    spin.Tick(0f);
                Ortho(at + Vector3.forward * 3f, at, $"Logs/preview_enemy_{Entries[i].Key}_front_y{y:0.00}.png");
                Ortho(at + Vector3.right * 0.6f + Vector3.forward * 0.0f, at, $"Logs/preview_enemy_{Entries[i].Key}_side_y{y:0.00}.png");
            }
        }

        static void Ortho(Vector3 position, Vector3 target, string outputPath, float halfHeight = 0.3f, bool grid = true)
        {
            var cameraObject = new GameObject("Preview Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.3f, 0.29f, 0.27f);
            camera.orthographic = true;
            camera.orthographicSize = halfHeight;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 10f;
            var texture = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            // Grid lines every 5 cm, brighter every 10 cm, through the middle.
            for (int px = 0; grid && px < 1600; px++)
                for (int py = 0; py < 900; py++)
                {
                    float mx = (px - 800) * 0.6f / 900f, my = (py - 450) * 0.6f / 900f;
                    bool lineX = Mathf.Abs(mx / 0.05f - Mathf.Round(mx / 0.05f)) * 0.05f < 0.0004f;
                    bool lineY = Mathf.Abs(my / 0.05f - Mathf.Round(my / 0.05f)) * 0.05f < 0.0004f;
                    if (lineX || lineY)
                        image.SetPixel(px, py, Color.Lerp(image.GetPixel(px, py), Color.cyan, 0.35f));
                }
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.DestroyImmediate(cameraObject);
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
        }

        /// <summary>Renders from <paramref name="position"/> toward <paramref name="target"/>, fitting this much height there.</summary>
        static void Snap(Vector3 position, Vector3 target, float height, string outputPath)
        {
            Quaternion rotation = Quaternion.LookRotation(target - position);
            var cameraObject = new GameObject("Preview Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(position, rotation);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.3f, 0.29f, 0.27f);
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = 2f * Mathf.Atan2(height * 0.5f, Vector3.Distance(position, target)) * Mathf.Rad2Deg;
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

        static void BuildOne(Entry entry)
        {
            MakeLegacy(entry.Model);
            ShrinkTexture(entry.Texture);
            ShrinkTexture(entry.Emissive);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Model);
            AnimationClip[] clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(entry.Model).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview")).ToArray();
            AnimationClip walk = Clip(clips, "Walk", "Robot_Walking", "Walking");
            AnimationClip idle = Clip(clips, "Idle", "Robot_Idle");
            AnimationClip shoot = Clip(clips, "Shoot", "Robot_Punch", "Attack");
            AnimationClip run = Clip(clips, "Run", "Robot_Running", "Running");
            AnimationClip attack = Clip(clips, "AttackAuto", "Kick");
            AnimationClip hit = Clip(clips, "Hit", "HitRecieve_1");

            var root = new GameObject("Enemy " + entry.Key);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, entry.Yaw, 0f) * model.transform.localRotation;
            (walk != null ? walk : idle)?.SampleAnimation(model, 0f);

            Bounds bounds = PoseBounds(model);
            float scale = entry.Drone ? DroneWidth / Mathf.Max(bounds.size.x, bounds.size.z) : entry.Height / bounds.size.y;
            model.transform.localScale *= scale;
            bounds = PoseBounds(model);
            Vector3 anchor = entry.Drone ? bounds.center : new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            model.transform.localPosition -= anchor;
            bounds.center -= anchor;

            Material material = entry.Texture != null ? LitMaterial(entry) : null;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = material != null ? material : entry.OwnTextures ? TexturedMaterial(entry.Key, slots[i]) : ColourMaterial(entry.Key, slots[i]);
                renderer.sharedMaterials = slots;
                if (renderer is SkinnedMeshRenderer skinned)
                    skinned.updateWhenOffscreen = true;
            }

            if (walk != null || idle != null)
            {
                // Clips go in under the names the game asks for: walk, idle and shoot.
                // Legacy import already puts an Animation (holding every clip) on the model.
                if (!model.TryGetComponent(out Animation animation))
                    animation = model.AddComponent<Animation>();
                if (walk != null)
                    animation.AddClip(walk, "walk");
                if (idle != null)
                    animation.AddClip(idle, "idle");
                if (shoot != null)
                    animation.AddClip(shoot, "shoot");
                if (run != null)
                    animation.AddClip(run, "run");
                if (attack != null)
                    animation.AddClip(attack, "attack");
                if (hit != null)
                    animation.AddClip(hit, "hit");
                animation.clip = walk != null ? walk : idle;
                animation.playAutomatically = true;
                animation.wrapMode = WrapMode.Loop;
                animation.cullingType = AnimationCullingType.AlwaysAnimate;
            }

            // Eye markers, on the head bone when there is one so they move with it; the game puts glows on them.
            Transform head = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Head");
            for (int i = 0; i < entry.Eyes.Length; i++)
            {
                var marker = new GameObject("Eye " + i).transform;
                marker.SetParent(head != null ? head : root.transform, false);
                float eyeScale = entry.Drone ? 1f : entry.Height / entry.EyeHeight;
                marker.position = root.transform.TransformPoint(entry.Eyes[i] * eyeScale);
                marker.gameObject.AddComponent<EnemyEye>().Size = entry.EyeSize * eyeScale;
            }

            PrefabUtility.SaveAsPrefabAsset(root, $"{OutFolder}/{entry.Key}.prefab");
            Object.DestroyImmediate(root);
            Debug.Log($"[EnemyModels] {entry.Key}: scale {scale:0.00000}, size {bounds.size.x:0.00} x {bounds.size.y:0.00} x {bounds.size.z:0.00}, " +
                $"walk {(walk != null ? walk.name : "-")}, idle {(idle != null ? idle.name : "-")}");
        }

        static AnimationClip Clip(AnimationClip[] clips, params string[] names)
        {
            foreach (string name in names)
            {
                AnimationClip clip = clips.FirstOrDefault(c => c.name == name) ?? clips.FirstOrDefault(c => c.name.EndsWith("|" + name));
                if (clip != null)
                    return clip;
            }
            return null;
        }

        /// <summary>The bounds of the model as posed, from its meshes (skinned ones baked), in its parent's space.</summary>
        static Bounds PoseBounds(GameObject model)
        {
            Transform root = model.transform.parent;
            bool any = false;
            var bounds = new Bounds();
            var baked = new Mesh();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Vector3[] vertices;
                Matrix4x4 toRoot;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    // Baked without scale: positions in the renderer's own space, so its full matrix takes them out.
                    skinned.BakeMesh(baked, false);
                    vertices = baked.vertices;
                    // (Measured: the bake already carries the scale, in the renderer's orientation.)
                    toRoot = root.worldToLocalMatrix * Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                }
                else if (renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                {
                    vertices = filter.sharedMesh.vertices;
                    toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                }
                else
                {
                    continue;
                }
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = toRoot.MultiplyPoint3x4(vertex);
                    if (!any)
                        bounds = new Bounds(point, Vector3.zero);
                    else
                        bounds.Encapsulate(point);
                    any = true;
                }
            }
            Object.DestroyImmediate(baked);
            return bounds;
        }

        static Material LitMaterial(Entry entry)
        {
            var material = new Material(Shader.Find("Repulsor/Enemy Lit"));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(entry.Texture));
            material.SetFloat("_Smoothness", 0.35f);
            material.SetFloat("_Metallic", 0.2f);
            if (entry.Emissive != null)
            {
                // The model's own lights (eyes, panels) glow red.
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(entry.Emissive));
                material.SetColor("_EmissionColor", EyeRed * 2.5f);
            }
            return Save(material, $"{OutFolder}/Materials/{entry.Key}.mat");
        }

        /// <summary>A Lit copy of a plain-coloured imported material.</summary>
        static Material ColourMaterial(string key, Material original)
        {
            string path = $"{OutFolder}/Materials/{key}_{(original != null ? original.name : "Plain")}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;
            var material = new Material(Shader.Find("Repulsor/Enemy Lit"));
            Color color = original == null ? Color.grey
                : original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor")
                : original.HasProperty("_Color") ? original.color : Color.grey;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.4f);
            return Save(material, path);
        }

        /// <summary>A glTF material's colour, normal and glow maps on URP Lit (so the hit flash and lighting work as for the others).</summary>
        static Material TexturedMaterial(string key, Material original)
        {
            string path = $"{OutFolder}/Materials/{key}_{(original != null ? original.name : "Plain")}.mat";
            var material = new Material(Shader.Find("Repulsor/Enemy Lit"));
            if (original != null)
            {
                Texture colour = original.HasProperty("baseColorTexture") ? original.GetTexture("baseColorTexture") : null;
                Texture normal = original.HasProperty("normalTexture") ? original.GetTexture("normalTexture") : null;
                Texture glow = original.HasProperty("emissiveTexture") ? original.GetTexture("emissiveTexture") : null;
                if (colour != null)
                    material.SetTexture("_BaseMap", colour);
                if (original.HasProperty("baseColorFactor"))
                    material.SetColor("_BaseColor", original.GetColor("baseColorFactor"));
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }
                if (glow != null)
                {
                    material.SetTexture("_EmissionMap", glow);
                    material.SetColor("_EmissionColor", original.HasProperty("emissiveFactor") ? original.GetColor("emissiveFactor") : Color.white);
                    material.EnableKeyword("_EMISSION");
                }
            }
            material.SetFloat("_Smoothness", 0.35f);
            material.SetFloat("_Metallic", 0.3f);
            return Save(material, path);
        }

        static Material Save(Material material, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void ShrinkTexture(string path)
        {
            if (path == null)
                return;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.maxTextureSize == 1024)
                return;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }

        /// <summary>Batch: prints each model's parts, materials and clips.</summary>
        public static void Inspect()
        {
            var log = new StringBuilder();
            foreach (string path in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Enemies", "Assets/Props" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                MakeLegacy(path);
                var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                log.AppendLine($"== {path}");
                var size = new Bounds();
                bool first = true;
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (first) size = r.bounds; else size.Encapsulate(r.bounds);
                    first = false;
                }
                log.AppendLine($"  bounds centre {size.center} size {size.size} (renderer bounds, rough for skinned)");
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials)
                        log.AppendLine($"  {r.name}: material {(m != null ? m.name : "null")}");
                foreach (Object clip in AssetDatabase.LoadAllAssetRepresentationsAtPath(path).Where(o => o is AnimationClip))
                    log.AppendLine($"  clip {clip.name}");
                Object.DestroyImmediate(go);
            }
            File.WriteAllText("Logs/enemy_models.txt", log.ToString());
        }

        static void MakeLegacy(string path)
        {
            AssetImporter any = AssetImporter.GetAtPath(path);
            if (!(any is ModelImporter importer))
            {
                // glTF (glTFast): its animation method lives in its serialized import settings (1 = legacy).
                var settings = new SerializedObject(any);
                SerializedProperty method = settings.FindProperty("importSettings.animationMethod");
                if (method != null && method.intValue != 1)
                {
                    method.intValue = 1;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    any.SaveAndReimport();
                }
                return;
            }
            if (importer.animationType == ModelImporterAnimationType.Legacy)
                return;
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.SaveAndReimport();
        }
    }
}
