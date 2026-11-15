using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Repulsor.EditorTools
{
    /// <summary>
    /// Fits each hand model onto a hand skeleton and saves it as a pair of skinned prefabs (Resources/HandSkins/
    /// {Name}_R and _L) that <see cref="HandSkin"/> drives from the tracked joints.
    ///
    /// 1. All the model's pieces become one mesh (one submesh per material), simplified first if it's too heavy.
    /// 2. It's turned and scaled into "hand space" from three hints read off the Inspect pictures (wrist point,
    ///    finger direction, palm direction): wrist at the origin, fingers along +Z, palm facing -Y, real size.
    /// 3. The XR Hands sample hand's joints are laid in, and each finger's joints are moved onto that finger's own
    ///    vertices (centroids along it), so the bones run down the model's fingers whatever pose it was made in.
    /// 4. Each vertex follows its nearest bone (blended with the next nearest near joints); anything behind the
    ///    wrist follows a forearm bone, so a sleeve doesn't swing round with the hand.
    /// 5. The left hand is the right one mirrored.
    /// Batch: -executeMethod Repulsor.EditorTools.HandSkinBuilder.Build (with graphics, for the fit pictures).
    /// </summary>
    public static class HandSkinBuilder
    {
        const string Source = "Assets/Characters/HandSkins";
        const string OutFolder = "Assets/Resources/HandSkins";
        /// <summary>Wrist to the tip of a straight middle finger on a real hand, metres.</summary>
        const float HandLength = 0.185f;  // reference only

        sealed class Spec
        {
            public string File, Name;
            /// <summary>The source is a left hand (mirrored to a right one first).</summary>
            public bool LeftSource;
            /// <summary>Points read off the face-on picture, in its (u, v): wrist, then index to little knuckle and tip, then thumb base and tip.</summary>
            public Vector2 Wrist;
            public Vector2[] Knuckles, Tips;
            public Vector2? ThumbBase, ThumbTip;
            /// <summary>Simplify: merge vertices closer than this (model units); 0 keeps the mesh as it is.</summary>
            public float Cell;
            /// <summary>Keep this much of a sleeve behind the wrist, metres (more is cut off).</summary>
            public float Sleeve = 0.24f;
        }

        static Vector2 V(float u, float v) => new Vector2(u, v);

        static readonly Spec[] Specs =
        {
            new Spec { File = "EnergyGauntlet", Name = "Technoman", LeftSource = true, Wrist = V(-0.0133f, -0.4667f),
                Knuckles = new[] { V(0.04f, -0.04f), V(-0.04f, -0.0267f), V(-0.1333f, -0.0267f), V(-0.2133f, -0.04f) },
                Tips = new[] { V(0.0067f, 0.40f), V(-0.1067f, 0.4293f), V(-0.2133f, 0.3333f), V(-0.3533f, 0.2f) } },
            new Spec { File = "LeatherGauntlet", Name = "Emberwitch", Wrist = V(0.391f, -1.369f),
                Knuckles = new[] { V(0.196f, 0f), V(0.489f, 0.024f), V(0.733f, 0f), V(0.953f, -0.073f) },
                Tips = new[] { V(0.098f, 1.198f), V(0.342f, 1.344f), V(0.66f, 1.051f), V(0.904f, 0.733f) },
                ThumbBase = V(-0.147f, -0.831f), ThumbTip = V(-1.027f, 0.024f) },
            new Spec { File = "BlackGloves", Name = "Lumen", Wrist = V(-0.022f, -0.1222f),
                Knuckles = new[] { V(0.0318f, 0.0245f), V(-0.0196f, 0.0269f), V(-0.0636f, 0.0245f), V(-0.1027f, 0.0098f) },
                Tips = new[] { V(0.043f, 0.1589f), V(-0.0205f, 0.1834f), V(-0.0709f, 0.1638f), V(-0.1198f, 0.1149f) },
                ThumbBase = V(0.0293f, -0.0734f), ThumbTip = V(0.1247f, 0.0416f) },
            new Spec { File = "MedievalGlove", Name = "Valkyr", Wrist = V(-0.0208f, -0.0611f),
                Knuckles = new[] { V(0.0306f, -0.0012f), V(0.0073f, 0.0012f), V(-0.0196f, 0f), V(-0.0464f, -0.0049f) },
                Tips = new[] { V(0.0367f, 0.0709f), V(0.0098f, 0.0831f), V(-0.0196f, 0.0787f), V(-0.0501f, 0.0574f) },
                ThumbBase = V(0.022f, -0.0464f), ThumbTip = V(0.0562f, 0.0244f) },
            new Spec { File = "StormcallerGauntlet", Name = "Tempest", Wrist = V(0.0267f, -0.4133f), Cell = 0.035f,
                Knuckles = new[] { V(-0.2133f, -0.0667f), V(-0.04f, -0.0667f), V(0.1467f, -0.0667f), V(0.3067f, -0.0533f) },
                Tips = new[] { V(-0.2667f, 0.3867f), V(-0.0667f, 0.5067f), V(0.1467f, 0.4533f), V(0.32f, 0.38f) },
                ThumbBase = V(-0.2533f, -0.2533f), ThumbTip = V(-0.54f, 0.0667f) },
        };

        // ------------------------------------------------------------------ the XR hand's joints, in hand space

        static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Little" };

        /// <summary>The sample right hand's joint positions relative to its wrist (wrist frame: fingers +Z, palm -Y).</summary>
        static Dictionary<string, Vector3> XrJoints()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Hands/RightHand.fbx");
            GameObject go = Object.Instantiate(prefab);
            Transform wrist = go.GetComponentsInChildren<Transform>().First(t => t.name == "R_Wrist");
            var joints = new Dictionary<string, Vector3>();
            foreach (Transform t in go.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("R_"))
                    joints[t.name.Substring(2)] = t.position - wrist.position;
            Object.DestroyImmediate(go);
            return joints;
        }

        // ------------------------------------------------------------------ mesh gathering

        sealed class Gathered
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<Material> Materials = new List<Material>();
            public readonly List<List<int>> Triangles = new List<List<int>>();
        }

        static Gathered Gather(GameObject go)
        {
            var g = new Gathered();
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;
                Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
                int start = g.Vertices.Count;
                Vector3[] v = mesh.vertices, n = mesh.normals;
                Vector2[] uv = mesh.uv;
                for (int i = 0; i < v.Length; i++)
                {
                    g.Vertices.Add(toWorld.MultiplyPoint3x4(v[i]));
                    g.Normals.Add(n.Length == v.Length ? toWorld.MultiplyVector(n[i]).normalized : Vector3.up);
                    g.Uvs.Add(uv.Length == v.Length ? uv[i] : Vector2.zero);
                }
                Material[] materials = renderer.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material material = materials[Mathf.Min(sub, materials.Length - 1)];
                    int slot = g.Materials.IndexOf(material);
                    if (slot < 0)
                    {
                        g.Materials.Add(material);
                        g.Triangles.Add(new List<int>());
                        slot = g.Materials.Count - 1;
                    }
                    foreach (int index in mesh.GetTriangles(sub))
                        g.Triangles[slot].Add(start + index);
                }
            }
            return g;
        }

        /// <summary>Vertex clustering: vertices in the same small cube with about the same texture spot become one.</summary>
        static Gathered Simplify(Gathered g, float cell)
        {
            var output = new Gathered();
            output.Materials.AddRange(g.Materials);
            var map = new Dictionary<(int, int, int, int, int), int>();
            var counts = new List<int>();
            var remap = new int[g.Vertices.Count];
            for (int i = 0; i < g.Vertices.Count; i++)
            {
                Vector3 p = g.Vertices[i] / cell;
                Vector2 uv = g.Uvs[i] * 96f;
                var key = (Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z), Mathf.RoundToInt(uv.x), Mathf.RoundToInt(uv.y));
                if (!map.TryGetValue(key, out int at))
                {
                    at = output.Vertices.Count;
                    map[key] = at;
                    output.Vertices.Add(Vector3.zero);
                    output.Normals.Add(Vector3.zero);
                    output.Uvs.Add(Vector2.zero);
                    counts.Add(0);
                }
                output.Vertices[at] += g.Vertices[i];
                output.Normals[at] += g.Normals[i];
                output.Uvs[at] += g.Uvs[i];
                counts[at]++;
                remap[i] = at;
            }
            for (int i = 0; i < output.Vertices.Count; i++)
            {
                output.Vertices[i] /= counts[i];
                output.Normals[i] = output.Normals[i].normalized;
                output.Uvs[i] /= counts[i];
            }
            foreach (List<int> tris in g.Triangles)
            {
                var kept = new List<int>();
                var seen = new HashSet<(int, int, int)>();
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    int a = remap[tris[t]], b = remap[tris[t + 1]], c = remap[tris[t + 2]];
                    if (a == b || b == c || a == c)
                        continue;
                    int lo = Mathf.Min(a, Mathf.Min(b, c)), hi = Mathf.Max(a, Mathf.Max(b, c)), mid = a + b + c - lo - hi;
                    if (!seen.Add((lo, mid, hi)))
                        continue;
                    kept.Add(a);
                    kept.Add(b);
                    kept.Add(c);
                }
                output.Triangles.Add(kept);
            }
            return output;
        }

        // ------------------------------------------------------------------ fitting

        /// <summary>A chain of joints: knuckle (or thumb base) to tip.</summary>
        sealed class Chain
        {
            public string Name;
            public Vector3[] Joints;
        }

        static float DistanceToChain(Vector3 p, Vector3[] joints, out float along)
        {
            float best = float.MaxValue;
            along = 0f;
            float travelled = 0f;
            for (int i = 0; i + 1 < joints.Length; i++)
            {
                Vector3 a = joints[i], b = joints[i + 1];
                Vector3 ab = b - a;
                float len = ab.magnitude;
                float t = len > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / (len * len)) : 0f;
                float d = Vector3.Distance(p, a + ab * t);
                if (d < best)
                {
                    best = d;
                    along = travelled + t * len;
                }
                travelled += len;
            }
            return best;
        }

        // ------------------------------------------------------------------ build

        public static void Build()
        {
            Directory.CreateDirectory(OutFolder);
            Dictionary<string, Vector3> xr = XrJoints();
            var log = new StringBuilder();
            foreach (Spec spec in Specs)
            {
                string path = $"{Source}/{spec.File}.glb";
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                {
                    log.AppendLine($"{spec.Name}: {path} not imported");
                    continue;
                }
                GameObject go = Object.Instantiate(asset);
                Gathered g = Gather(go);
                Object.DestroyImmediate(go);
                int before = g.Triangles.Sum(t => t.Count) / 3;
                if (spec.Cell > 0f)
                    g = Simplify(g, spec.Cell);
                int after = g.Triangles.Sum(t => t.Count) / 3;
                BuildOne(spec, g, xr, log, before, after);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[HandSkinBuilder]\n" + log);
        }

        /// <summary>A picture point turned into the model's space; its depth is the middle of the mesh along that line of sight.</summary>
        static Vector3 Landmark(HandSkinViews.View view, Vector2 uv, List<Vector3> vertices)
        {
            Vector3 point = view.ToModel(uv.x, uv.y);
            Vector3 look = view.Look.normalized;
            float radius = view.Half * 0.025f;
            float near = float.MaxValue, far = float.MinValue;
            foreach (Vector3 v in vertices)
            {
                Vector3 d = v - point;
                float along = Vector3.Dot(d, look);
                if ((d - look * along).sqrMagnitude > radius * radius)
                    continue;
                near = Mathf.Min(near, along);
                far = Mathf.Max(far, along);
            }
            return near <= far ? point + look * ((near + far) * 0.5f) : point;
        }

        static void BuildOne(Spec spec, Gathered g, Dictionary<string, Vector3> xr, StringBuilder log, int before, int after)
        {
            HandSkinViews.View view = HandSkinViews.Views[spec.File];
            Vector3 wrist = Landmark(view, spec.Wrist, g.Vertices);
            Vector3[] knuckles = spec.Knuckles.Select(k => Landmark(view, k, g.Vertices)).ToArray();
            Vector3[] tips = spec.Tips.Select(t => Landmark(view, t, g.Vertices)).ToArray();

            // The hand's own frame from its landmarks: fingers toward the middle knuckle, thumb side from little to index.
            Vector3 f = (knuckles[1] - wrist).normalized;
            Vector3 side = (knuckles[0] - knuckles[3]).normalized;
            Vector3 n = (spec.LeftSource ? Vector3.Cross(side, f) : Vector3.Cross(f, side)).normalized;
            Quaternion turn = Quaternion.Inverse(Quaternion.LookRotation(f, -n));
            float scale = xr["MiddleProximal"].magnitude / Mathf.Max(1e-6f, Vector3.Distance(wrist, knuckles[1]));
            float mirror = spec.LeftSource ? -1f : 1f;
            Vector3 ToHand(Vector3 v)
            {
                Vector3 h = turn * (v - wrist) * scale;
                h.x *= mirror;
                return h;
            }
            var verts = g.Vertices.Select(ToHand).ToList();
            var normals = g.Normals.Select(v => { Vector3 h = turn * v; h.x *= mirror; return h.normalized; }).ToList();
            if (spec.LeftSource)
                foreach (List<int> tris in g.Triangles)
                    for (int t = 0; t + 2 < tris.Count; t += 3)
                        (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);

            // Cut a long sleeve short (it would poke through your elbow).
            for (int sub = 0; sub < g.Triangles.Count; sub++)
            {
                var kept = new List<int>();
                List<int> tris = g.Triangles[sub];
                for (int t = 0; t + 2 < tris.Count; t += 3)
                    if (verts[tris[t]].z > -spec.Sleeve || verts[tris[t + 1]].z > -spec.Sleeve || verts[tris[t + 2]].z > -spec.Sleeve)
                        kept.AddRange(new[] { tris[t], tris[t + 1], tris[t + 2] });
                g.Triangles[sub] = kept;
            }
            var used = new HashSet<int>(g.Triangles.SelectMany(t => t));
            var usedPoints = used.Select(i => verts[i]).ToList();

            // Chains from the landmarks: knuckle to tip, the middle joints spaced as on the sample hand.
            var chains = new List<Chain>();
            for (int k = 0; k < 4; k++)
            {
                string name = FingerNames[k];
                float[] seg = Enumerable.Range(0, 3).Select(i => Vector3.Distance(xr[name + Joints4[i]], xr[name + Joints4[i + 1]])).ToArray();
                chains.Add(Straight(name, ToHand(knuckles[k]), ToHand(tips[k]), seg));
            }
            {
                float[] seg = { Vector3.Distance(xr["ThumbMetacarpal"], xr["ThumbProximal"]), Vector3.Distance(xr["ThumbProximal"], xr["ThumbDistal"]),
                    Vector3.Distance(xr["ThumbDistal"], xr["ThumbTip"]) };
                Vector3 thumbBase = spec.ThumbBase.HasValue ? ToHand(Landmark(view, spec.ThumbBase.Value, g.Vertices)) : xr["ThumbMetacarpal"];
                Vector3 thumbTip = spec.ThumbTip.HasValue ? ToHand(Landmark(view, spec.ThumbTip.Value, g.Vertices)) : xr["ThumbTip"];
                chains.Add(Straight("Thumb", thumbBase, thumbTip, seg));
            }
            // The middle joints moved onto the finger: the centre of the mesh in a thin slice across it there.
            for (int pass = 0; pass < 2; pass++)
                foreach (Chain chain in chains)
                    Refine(chain, chains, usedPoints);

            var joints = new Dictionary<string, Vector3> { ["Wrist"] = Vector3.zero };
            joints["Palm"] = (chains[1].Joints[0]) * 0.5f;
            foreach (Chain chain in chains)
            {
                string[] names = chain.Name == "Thumb"
                    ? new[] { "ThumbMetacarpal", "ThumbProximal", "ThumbDistal", "ThumbTip" }
                    : new[] { chain.Name + "Proximal", chain.Name + "Intermediate", chain.Name + "Distal", chain.Name + "Tip" };
                for (int k = 0; k < 4; k++)
                    joints[names[k]] = chain.Joints[k];
                if (chain.Name != "Thumb")
                    joints[chain.Name + "Metacarpal"] = chain.Joints[0] * 0.38f;
            }

            var bones = new List<(string bone, Vector3 a, Vector3 b)>();
            foreach (string finger in FingerNames)
            {
                bones.Add((finger + "Metacarpal", joints[finger + "Metacarpal"], joints[finger + "Proximal"]));
                bones.Add((finger + "Proximal", joints[finger + "Proximal"], joints[finger + "Intermediate"]));
                bones.Add((finger + "Intermediate", joints[finger + "Intermediate"], joints[finger + "Distal"]));
                bones.Add((finger + "Distal", joints[finger + "Distal"], joints[finger + "Tip"]));
            }
            bones.Add(("ThumbMetacarpal", joints["ThumbMetacarpal"], joints["ThumbProximal"]));
            bones.Add(("ThumbProximal", joints["ThumbProximal"], joints["ThumbDistal"]));
            bones.Add(("ThumbDistal", joints["ThumbDistal"], joints["ThumbTip"]));
            bones.Add(("Wrist", Vector3.zero, Vector3.forward * 0.02f));
            bones.Add(("Forearm", Vector3.zero, Vector3.back * 0.3f));

            log.AppendLine($"{spec.Name}: tris {before} -> {after}, scale {scale:0.####}, "
                + string.Join(", ", chains.Select(c => $"{c.Name} {c.Joints[0]:F3}->{c.Joints[3]:F3}")));

            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                SavePrefab(spec, g, verts, normals, joints, bones, sideIndex == 0, used);
        }

        static readonly string[] Joints4 = { "Proximal", "Intermediate", "Distal", "Tip" };

        /// <summary>A straight chain from base to tip, its joints spaced in the given proportions.</summary>
        static Chain Straight(string name, Vector3 from, Vector3 to, float[] segments)
        {
            float total = segments.Sum();
            var joints = new Vector3[4];
            joints[0] = from;
            float run = 0f;
            for (int k = 0; k < 3; k++)
            {
                run += segments[k];
                joints[k + 1] = Vector3.Lerp(from, to, run / total);
            }
            return new Chain { Name = name, Joints = joints };
        }

        /// <summary>
        /// Moves a chain's two middle joints to the centre of this finger's vertices in a thin slice across the finger
        /// there (only vertices nearer this chain than any other), so a curled finger's joints sit inside its curve.
        /// </summary>
        static void Refine(Chain chain, List<Chain> all, List<Vector3> points)
        {
            const float Slice = 0.006f;
            for (int k = 1; k <= 2; k++)
            {
                Vector3 a = chain.Joints[k - 1], b = chain.Joints[k + 1];
                Vector3 joint = chain.Joints[k];
                Vector3 axis = (b - a).normalized;
                Vector3 sum = Vector3.zero;
                int count = 0;
                foreach (Vector3 p in points)
                {
                    if (Mathf.Abs(Vector3.Dot(p - joint, axis)) > Slice || (p - joint).sqrMagnitude > 0.03f * 0.03f)
                        continue;
                    float mine = DistanceToChain(p, chain.Joints, out _);
                    bool nearest = true;
                    foreach (Chain other in all)
                        if (other != chain && DistanceToChain(p, other.Joints, out _) < mine)
                        {
                            nearest = false;
                            break;
                        }
                    if (!nearest)
                        continue;
                    sum += p;
                    count++;
                }
                if (count >= 6)
                    chain.Joints[k] = Vector3.Lerp(joint, sum / count, 0.8f);
            }
        }

        static void SavePrefab(Spec spec, Gathered g, List<Vector3> verts, List<Vector3> normals, Dictionary<string, Vector3> joints,
            List<(string bone, Vector3 a, Vector3 b)> bones, bool left, HashSet<int> used)
        {
            string p = left ? "L_" : "R_";
            Vector3 M(Vector3 v) => left ? new Vector3(-v.x, v.y, v.z) : v;

            // Skeleton: the same hierarchy as the XR hand, plus a forearm bone beside the wrist.
            var root = new GameObject($"{spec.Name}_{(left ? "L" : "R")}");
            var made = new Dictionary<string, Transform>();
            Transform Make(string name, string parent)
            {
                var t = new GameObject(p + name).transform;
                t.SetParent(parent == null ? root.transform : made[parent], false);
                t.position = M(joints.TryGetValue(name, out Vector3 at) ? at : Vector3.zero);
                made[name] = t;
                return t;
            }
            Make("Wrist", null);
            Make("Palm", "Wrist");
            Make("ThumbMetacarpal", "Wrist");
            Make("ThumbProximal", "ThumbMetacarpal");
            Make("ThumbDistal", "ThumbProximal");
            Make("ThumbTip", "ThumbDistal");
            foreach (string finger in FingerNames)
            {
                Make(finger + "Metacarpal", "Wrist");
                Make(finger + "Proximal", finger + "Metacarpal");
                Make(finger + "Intermediate", finger + "Proximal");
                Make(finger + "Distal", finger + "Intermediate");
                Make(finger + "Tip", finger + "Distal");
            }
            Transform forearm = new GameObject(p + "Forearm").transform;
            forearm.SetParent(root.transform, false);
            made["Forearm"] = forearm;

            Transform[] boneTransforms = bones.Select(b => made[b.bone]).ToArray();

            // Mesh: only the vertices still used, mirrored for the left hand.
            var index = new Dictionary<int, int>();
            var mv = new List<Vector3>();
            var mn = new List<Vector3>();
            var muv = new List<Vector2>();
            var weights = new List<BoneWeight>();
            foreach (int i in used.OrderBy(x => x))
            {
                index[i] = mv.Count;
                Vector3 v = verts[i];
                mv.Add(M(v));
                Vector3 nn = normals[i];
                mn.Add(left ? new Vector3(-nn.x, nn.y, nn.z) : nn);
                muv.Add(g.Uvs[i]);
                weights.Add(Weigh(v, bones));
            }
            var mesh = new Mesh { name = root.name, indexFormat = mv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(mv);
            mesh.SetNormals(mn);
            mesh.SetUVs(0, muv);
            mesh.subMeshCount = g.Triangles.Count;
            for (int s = 0; s < g.Triangles.Count; s++)
            {
                List<int> tris = g.Triangles[s];
                var outTris = new List<int>(tris.Count);
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    int a = index[tris[t]], b = index[tris[t + 1]], c = index[tris[t + 2]];
                    if (left)
                        outTris.AddRange(new[] { a, c, b });
                    else
                        outTris.AddRange(new[] { a, b, c });
                }
                mesh.SetTriangles(outTris, s);
            }
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = boneTransforms.Select(b => b.worldToLocalMatrix * root.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            string meshPath = $"{OutFolder}/{root.name}_Mesh.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var renderer = new GameObject("Mesh").AddComponent<SkinnedMeshRenderer>();
            renderer.transform.SetParent(root.transform, false);
            renderer.sharedMesh = mesh;
            renderer.bones = boneTransforms;
            renderer.rootBone = made["Wrist"];
            renderer.sharedMaterials = g.Materials.ToArray();
            renderer.updateWhenOffscreen = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            PrefabUtility.SaveAsPrefabAsset(root, $"{OutFolder}/{root.name}.prefab");
            if (!left)
                RenderFit(root, made);
            Object.DestroyImmediate(root);
        }

        /// <summary>The model in its rest pose with the fitted skeleton drawn over it: from above (back of the hand) and the thumb side.</summary>
        static void RenderFit(GameObject root, Dictionary<string, Transform> made)
        {
            const int Size = 520;
            var temp = new List<GameObject>();
            var light = new GameObject("Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(60f, -30f, 0f);
            temp.Add(light.gameObject);
            var unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            foreach (Transform t in made.Values)
            {
                if (t.name.EndsWith("Forearm") || t.parent == null)
                    continue;
                var line = new GameObject("Bone", typeof(LineRenderer)).GetComponent<LineRenderer>();
                line.sharedMaterial = unlit;
                Color c = t.name.Contains("Thumb") ? Color.yellow : t.name.Contains("Index") ? Color.red : t.name.Contains("Middle") ? Color.green
                    : t.name.Contains("Ring") ? Color.cyan : t.name.Contains("Little") ? Color.magenta : Color.white;
                line.startColor = line.endColor = c;
                line.material.color = c;
                line.widthMultiplier = 0.0025f;
                line.SetPositions(new[] { t.parent.position, t.position });
                temp.Add(line.gameObject);
            }
            var camGo = new GameObject("Cam", typeof(Camera));
            temp.Add(camGo);
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 0.14f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.25f, 0.27f, 0.3f);
            cam.nearClipPlane = 0.001f;
            var rt = new RenderTexture(Size, Size, 24) { antiAliasing = 4 };
            var sheet = new Texture2D(Size * 2, Size, TextureFormat.RGB24, false);
            Vector3 centre = new Vector3(0f, 0f, 0.06f);
            (Vector3 dir, Vector3 up)[] views = { (Vector3.down, Vector3.forward), (Vector3.right, Vector3.forward) };
            for (int i = 0; i < 2; i++)
            {
                camGo.transform.SetPositionAndRotation(centre - views[i].dir * 1f, Quaternion.LookRotation(views[i].dir, views[i].up));
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, Size, Size), i * Size, 0);
            }
            sheet.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes($"Logs/handskin_fit_{root.name}.png", sheet.EncodeToPNG());
            foreach (GameObject go in temp)
                Object.DestroyImmediate(go);
        }

        /// <summary>Nearest bone, blended with the next nearest where they're about as close (around joints).</summary>
        static BoneWeight Weigh(Vector3 v, List<(string bone, Vector3 a, Vector3 b)> bones)
        {
            // Behind the wrist: the forearm, blending into the wrist over the first couple of centimetres.
            int forearm = bones.FindIndex(b => b.bone == "Forearm");
            int wrist = bones.FindIndex(b => b.bone == "Wrist");
            if (v.z < 0.01f)
            {
                float w = Mathf.Clamp01((0.01f - v.z) / 0.04f);
                return new BoneWeight { boneIndex0 = forearm, weight0 = w, boneIndex1 = wrist, weight1 = 1f - w };
            }
            int best = -1, second = -1;
            float bestD = float.MaxValue, secondD = float.MaxValue;
            for (int i = 0; i < bones.Count; i++)
            {
                if (i == forearm)
                    continue;
                (string _, Vector3 a, Vector3 b) = bones[i];
                Vector3 ab = b - a;
                float t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(v - a, ab) / ab.sqrMagnitude) : 0f;
                float d = Vector3.Distance(v, a + ab * t);
                if (d < bestD)
                {
                    second = best;
                    secondD = bestD;
                    best = i;
                    bestD = d;
                }
                else if (d < secondD)
                {
                    second = i;
                    secondD = d;
                }
            }
            // Blend only when the second bone is nearly as close; otherwise stay rigid on the nearest.
            float gap = secondD - bestD;
            float share = second < 0 ? 0f : Mathf.Clamp01(1f - gap / 0.008f) * 0.5f;
            return new BoneWeight { boneIndex0 = best, weight0 = 1f - share, boneIndex1 = Mathf.Max(0, second), weight1 = share };
        }
    }
}
