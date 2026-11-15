using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Repulsor.EditorTools
{
    /// <summary>
    /// Tempest's tornado prefab (Resources/Props/Tornado) from "Tornado 2" by Jacob Quintana (CC BY 4.0). The model
    /// comes with a wide cloud deck over the funnel, which is dropped (it would fill a room); the funnel and its
    /// debris keep their own animation (legacy, looping). Scaled so the funnel is 1 m tall, origin at the middle of
    /// its foot, drawn with Repulsor/CloudModel (its cloud texture, see-through).
    /// Batch: -executeMethod Repulsor.EditorTools.TornadoModel.Build, then .Render (without -nographics).
    /// </summary>
    public static class TornadoModel
    {
        const string Source = "Assets/Props/Tornado2/Tornado2.glb";
        const string Prefab = "Assets/Resources/Props/Tornado.prefab";
        const string MaterialPath = "Assets/Resources/Props/Materials/Tornado2.mat";
        /// <summary>Nodes left out: the cloud deck (about 50 units across against a 17-unit funnel) and the two flat
        /// swirls of debris along the floor (about twice the funnel's width: 2 m in a room).</summary>
        static readonly string[] Dropped = { "clouds_19", "Object_42", "Object_45" };
        const string Funnel = "Object_8";

        public static void Build()
        {
            MakeLegacy(Source);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            if (source == null)
            {
                Debug.LogError("[TornadoModel] Missing " + Source);
                return;
            }
            var root = new GameObject("Prop Tornado");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true).Where(t => Dropped.Contains(t.name)).ToArray())
                Object.DestroyImmediate(t.gameObject);

            var funnel = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == Funnel);
            Bounds bounds = Baked(funnel, root.transform);
            float scale = 1f / Mathf.Max(1e-5f, bounds.size.y);
            model.transform.localScale *= scale;
            bounds = Baked(funnel, root.transform);
            model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            Texture texture = null;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in renderer.sharedMaterials)
                    if (texture == null && m != null)
                        texture = m.HasProperty("baseColorTexture") ? m.GetTexture("baseColorTexture") : m.mainTexture;
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Repulsor/CloudModel"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = Shader.Find("Repulsor/CloudModel");
            material.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(material);

            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                if (renderer is SkinnedMeshRenderer skinned)
                    skinned.updateWhenOffscreen = true;
            }
            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
                Object.DestroyImmediate(animator);
            var animation = model.GetComponentInChildren<Animation>(true);
            if (animation != null)
            {
                animation.wrapMode = WrapMode.Loop;
                animation.playAutomatically = true;
                if (animation.clip != null)
                    animation.clip.wrapMode = WrapMode.Loop;
            }
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TornadoModel] scale {scale:0.00000}, texture {(texture != null ? texture.name : "none")}, animation {(animation != null && animation.clip != null ? animation.clip.name + " " + animation.clip.length.ToString("0.0") + " s" : "none")}");
        }

        /// <summary>Batch (with graphics): the tornado at a few moments of its animation, side-on and from head height, to Logs/preview_tornado_*.png.</summary>
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = Resources.Load<GameObject>("Props/Tornado");
            var log = new StringBuilder();
            float[] times = { 0f, 8f, 30f, 60f };
            for (int i = 0; i < times.Length; i++)
            {
                var go = Object.Instantiate(prefab);
                go.transform.position = new Vector3(i * 1.6f, 0f, 0f);
                go.transform.localScale = new Vector3(1.25f, 2f, 1.25f) * 0.95f;
                var animation = go.GetComponentInChildren<Animation>();
                if (animation != null && animation.clip != null)
                {
                    AnimationState state = animation[animation.clip.name];
                    animation.Play(animation.clip.name);
                    state.time = times[i] % animation.clip.length;
                    animation.Sample();
                }
                foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
                {
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_Color", new Color(1f, 1f, 1f, 0.7f));
                    renderer.SetPropertyBlock(block);
                }
                var funnel = go.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == Funnel);
                Bounds b = Baked(funnel, null);
                log.AppendLine($"t {times[i]}: funnel {b.size} centre {b.center - go.transform.position}");
                foreach (SkinnedMeshRenderer other in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if (other != funnel)
                        log.AppendLine($"   {other.name}: {Baked(other, null).size}");
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.position = new Vector3(2.4f, 0f, 0f);
            floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.35f, 0.33f, 0.3f) };
            Snap(new Vector3(2.4f, 1f, 6.5f), new Vector3(2.4f, 1f, 0f), 50f, "Logs/preview_tornado_side.png");
            Snap(new Vector3(0f, 1.6f, 2.2f), new Vector3(0f, 0.9f, 0f), 70f, "Logs/preview_tornado_near.png");
            Debug.Log("[TornadoModel] Render\n" + log);
        }

        /// <summary>The skinned mesh's posed vertices, in <paramref name="space"/>'s local space (world when null).</summary>
        static Bounds Baked(SkinnedMeshRenderer skinned, Transform space)
        {
            var baked = new Mesh();
            skinned.BakeMesh(baked, true);
            Matrix4x4 toSpace = (space != null ? space.worldToLocalMatrix : Matrix4x4.identity) * skinned.transform.localToWorldMatrix;
            var bounds = new Bounds();
            bool any = false;
            foreach (Vector3 v in baked.vertices)
            {
                Vector3 p = toSpace.MultiplyPoint3x4(v);
                if (!any)
                    bounds = new Bounds(p, Vector3.zero);
                else
                    bounds.Encapsulate(p);
                any = true;
            }
            Object.DestroyImmediate(baked);
            return bounds;
        }

        static void MakeLegacy(string path)
        {
            AssetImporter any = AssetImporter.GetAtPath(path);
            if (any == null || any is ModelImporter)
                return;
            var settings = new SerializedObject(any);
            SerializedProperty method = settings.FindProperty("importSettings.animationMethod");
            if (method != null && method.intValue != 1)
            {
                method.intValue = 1;
                settings.ApplyModifiedPropertiesWithoutUndo();
                any.SaveAndReimport();
            }
        }

        static void Snap(Vector3 position, Vector3 target, float fov, string outputPath)
        {
            var cameraObject = new GameObject("Preview Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.53f, 0.5f);
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
        }
    }
}
