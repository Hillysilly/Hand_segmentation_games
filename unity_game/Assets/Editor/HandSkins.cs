using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Repulsor.EditorTools
{
    /// <summary>
    /// The level 3 hand models (user's picks, Sketchfab, CC BY 4.0): each GLB in Assets/Characters/HandSkins is lined
    /// up over the XR Hands sample right hand with its fingers straightened flat, skinned to that skeleton (each
    /// vertex follows its nearest bones), and saved as Resources/HandSkins/{Name}_R and a mirrored _L.
    /// Batch: -executeMethod Repulsor.EditorTools.HandSkins.Inspect (pictures) / .Build
    /// </summary>
    public static class HandSkins
    {
        const string Folder = "Assets/Characters/HandSkins";

        /// <summary>Pictures of each raw model from the front, side and top, with its size, to work out how to line it up.</summary>
        public static void Inspect()
        {
            var log = new StringBuilder();
            foreach (string path in Directory.GetFiles(Folder, "*.glb"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                if (asset == null)
                {
                    log.AppendLine($"{path}: not imported");
                    continue;
                }
                GameObject go = Object.Instantiate(asset);
                Bounds b = WorldBounds(go);
                log.AppendLine($"{Path.GetFileNameWithoutExtension(path)}: centre {b.center:F3} size {b.size:F3}");
                foreach (MeshFilter f in go.GetComponentsInChildren<MeshFilter>())
                {
                    Bounds pb = f.GetComponent<Renderer>().bounds;
                    log.AppendLine($"   {f.name}: verts {f.sharedMesh.vertexCount}, tris {f.sharedMesh.triangles.Length / 3}, centre {pb.center:F3} size {pb.size:F3}");
                }
                string name = Path.GetFileNameWithoutExtension(path);
                Render(go, b, name);
                Object.DestroyImmediate(go);
            }
            Debug.Log("[HandSkins] Inspect\n" + log);
        }

        static float NiceStep(float raw)
        {
            float p = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(raw)));
            float m = raw / p;
            return (m < 1.5f ? 1f : m < 3.5f ? 2f : m < 7.5f ? 5f : 10f) * p;
        }

        public static Bounds WorldBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds();
            foreach (Renderer r in renderers)
                b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Front (looking down +Z), side (down -X) and top (down -Y) views side by side, with axis lines.</summary>
        static void Render(GameObject go, Bounds b, string name)
        {
            const int Size = 600;
            var light = new GameObject("Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(35f, -40f, 0f);
            var camGo = new GameObject("Cam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(b.size.x, b.size.y, b.size.z) * 0.6f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.22f, 0.24f, 0.28f);
            cam.nearClipPlane = 0.001f;
            cam.farClipPlane = 100f;
            var axes = new List<GameObject>();
            float len = Mathf.Max(b.size.x, b.size.y, b.size.z) * 0.5f;
            foreach ((Vector3 dir, Color col) in new[] { (Vector3.right, Color.red), (Vector3.up, Color.green), (Vector3.forward, Color.blue) })
            {
                var line = new GameObject("Axis", typeof(LineRenderer)).GetComponent<LineRenderer>();
                line.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                line.sharedMaterial.color = col;
                line.widthMultiplier = len * 0.03f;
                line.SetPositions(new[] { b.center, b.center + dir * len });
                axes.Add(line.gameObject);
            }
            // A grid through the centre on each view's plane, labelled in the model's own units.
            float step = NiceStep(Mathf.Max(b.size.x, b.size.y, b.size.z) / 8f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int axis = 0; axis < 3; axis++)
            {
                for (int k = -6; k <= 6; k++)
                {
                    Vector3 offset = Vector3.zero;
                    offset[axis] = Mathf.Round(b.center[axis] / step) * step + k * step - b.center[axis];
                    Vector3 at = b.center + offset;
                    for (int other = 0; other < 3; other++)
                    {
                        if (other == axis)
                            continue;
                        Vector3 dir = Vector3.zero;
                        dir[other] = 1f;
                        var gl = new GameObject("Grid", typeof(LineRenderer)).GetComponent<LineRenderer>();
                        gl.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                        gl.sharedMaterial.color = new Color(1f, 1f, 1f, 1f) * 0.45f;
                        gl.widthMultiplier = len * 0.004f;
                        gl.SetPositions(new[] { at - dir * len * 1.4f, at + dir * len * 1.4f });
                        axes.Add(gl.gameObject);
                    }
                    var label = new GameObject("Label", typeof(TextMesh));
                    var text = label.GetComponent<TextMesh>();
                    text.font = font;
                    label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                    text.text = "xyz"[axis] + (b.center[axis] + offset[axis]).ToString("0.###");
                    text.characterSize = len * 0.012f;
                    text.fontSize = 40;
                    text.color = axis == 0 ? new Color(1f, 0.5f, 0.5f) : axis == 1 ? new Color(0.5f, 1f, 0.5f) : new Color(0.5f, 0.7f, 1f);
                    label.transform.position = at;
                    axes.Add(label);
                }
            }
            var rt = new RenderTexture(Size, Size, 24) { antiAliasing = 4 };
            var sheet = new Texture2D(Size * 3, Size, TextureFormat.RGB24, false);
            Vector3[] dirs = { Vector3.forward, Vector3.left, Vector3.down };
            Vector3[] ups = { Vector3.up, Vector3.up, Vector3.forward };
            for (int i = 0; i < 3; i++)
            {
                camGo.transform.SetPositionAndRotation(b.center - dirs[i] * 10f, Quaternion.LookRotation(dirs[i], ups[i]));
                foreach (GameObject a in axes)
                    if (a.TryGetComponent(out TextMesh tm))
                        a.transform.rotation = camGo.transform.rotation;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, Size, Size), i * Size, 0);
            }
            sheet.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes($"Logs/handskin_{name}.png", sheet.EncodeToPNG());
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(light.gameObject);
            foreach (GameObject a in axes)
                Object.DestroyImmediate(a);
        }
    }
}
