using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Repulsor.EditorTools
{
    /// <summary>
    /// Face-on pictures of each hand model with a grid in the picture's own (u, v) coordinates, for reading off where
    /// its wrist, knuckles and fingertips are. <see cref="HandSkinBuilder"/> turns those (u, v) points back into the
    /// model's space (and finds their depth from the mesh). Batch: -executeMethod Repulsor.EditorTools.HandSkinViews.Render
    /// </summary>
    public static class HandSkinViews
    {
        public sealed class View
        {
            public string File;
            /// <summary>Where the camera looks, and which way is up in the picture (the fingers point up).</summary>
            public Vector3 Look, Up;
            /// <summary>The picture's centre in the model's space, and half its height in model units.</summary>
            public Vector3 Centre;
            public float Half;

            public Vector3 Right => Vector3.Cross(Up.normalized, Look.normalized).normalized;
            public Vector3 UpAxis => Vector3.Cross(Look.normalized, Right).normalized;
            public Vector3 ToModel(float u, float v) => Centre + Right * u + UpAxis * v;
        }

        public static readonly Dictionary<string, View> Views = new Dictionary<string, View>
        {
            ["EnergyGauntlet"] = new View { File = "EnergyGauntlet", Look = Vector3.down, Up = Vector3.left, Centre = new Vector3(-1.3f, 0.3f, -1.15f), Half = 0.6f },
            ["LeatherGauntlet"] = new View { File = "LeatherGauntlet", Look = new Vector3(0f, -0.59f, -0.81f), Up = new Vector3(0.02f, -0.81f, 0.59f), Centre = new Vector3(3.95f, -9f, 2.4f), Half = 2.2f },
            ["BlackGloves"] = new View { File = "BlackGloves", Look = Vector3.forward, Up = Vector3.up, Centre = new Vector3(0.015f, 0.21f, -0.01f), Half = 0.22f },
            ["MedievalGlove"] = new View { File = "MedievalGlove", Look = Vector3.forward, Up = new Vector3(-0.44f, -0.9f, 0f), Centre = new Vector3(-0.255f, 1.06f, 0.51f), Half = 0.11f },
            ["StormcallerGauntlet"] = new View { File = "StormcallerGauntlet", Look = Vector3.left, Up = new Vector3(0f, -0.98f, 0.17f), Centre = new Vector3(0.45f, -0.45f, 0.3f), Half = 0.6f },
        };

        public static void Render()
        {
            foreach (View view in Views.Values)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Characters/HandSkins/{view.File}.glb");
                if (asset == null)
                    continue;
                GameObject go = Object.Instantiate(asset);
                RenderOne(go, view);
                Object.DestroyImmediate(go);
            }
        }

        static void RenderOne(GameObject go, View view)
        {
            const int Size = 900;
            var temp = new List<GameObject>();
            var light = new GameObject("Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.LookRotation(view.Look + view.UpAxis * -0.5f + view.Right * 0.3f);
            temp.Add(light.gameObject);
            var camGo = new GameObject("Cam", typeof(Camera));
            temp.Add(camGo);
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = view.Half;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.22f, 0.26f);
            cam.nearClipPlane = 0.0001f;
            cam.farClipPlane = 1000f;
            camGo.transform.SetPositionAndRotation(view.Centre - view.Look.normalized * 50f, Quaternion.LookRotation(view.Look, view.UpAxis));

            // Grid in front of the model, labelled in (u, v).
            float step = view.Half / 5f;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            Vector3 front = view.Centre - view.Look.normalized * 40f;
            for (int k = -6; k <= 6; k++)
            {
                for (int axis = 0; axis < 2; axis++)
                {
                    Vector3 across = axis == 0 ? view.Right : view.UpAxis;
                    Vector3 along = axis == 0 ? view.UpAxis : view.Right;
                    var line = new GameObject("Grid", typeof(LineRenderer)).GetComponent<LineRenderer>();
                    line.sharedMaterial = lineMaterial;
                    lineMaterial.color = new Color(1f, 1f, 1f) * 0.5f;
                    line.widthMultiplier = view.Half * (k == 0 ? 0.006f : 0.002f);
                    line.SetPositions(new[] { front + across * (k * step) - along * view.Half * 1.2f, front + across * (k * step) + along * view.Half * 1.2f });
                    temp.Add(line.gameObject);
                    var label = new GameObject("Label", typeof(TextMesh));
                    var text = label.GetComponent<TextMesh>();
                    text.font = font;
                    label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                    text.text = (axis == 0 ? "u" : "v") + (k * step).ToString("0.###");
                    text.characterSize = view.Half * 0.006f;
                    text.fontSize = 50;
                    text.color = axis == 0 ? new Color(1f, 0.6f, 0.6f) : new Color(0.6f, 1f, 0.6f);
                    label.transform.SetPositionAndRotation(front + across * (k * step) - along * view.Half * (axis == 0 ? 0.97f : -0.0f) + (axis == 0 ? Vector3.zero : -view.Right * view.Half * 0.97f),
                        camGo.transform.rotation);
                    temp.Add(label);
                }
            }

            var rt = new RenderTexture(Size, Size, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes($"Logs/handview_{view.File}.png", image.EncodeToPNG());
            foreach (GameObject t in temp)
                Object.DestroyImmediate(t);
        }
    }
}
