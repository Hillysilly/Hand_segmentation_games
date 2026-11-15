using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Repulsor.EditorTools
{
    /// <summary>
    /// Checks each built hand model: for every finger bone, the angle between the bone (joint to next joint) and the
    /// long axis of the mesh it carries. A big angle means the bone runs off the finger, and the finger is drawn bent
    /// that much more (or less) than your real one. Also the bend between bones in the rest pose.
    /// Batch: -executeMethod Repulsor.EditorTools.HandSkinCheck.Run
    /// </summary>
    public static class HandSkinCheck
    {
        public static void Run()
        {
            var log = new StringBuilder();
            foreach (string name in new[] { "Technoman", "Emberwitch", "Lumen", "Valkyr", "Tempest" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/HandSkins/{name}_R.prefab");
                if (prefab == null)
                    continue;
                var smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
                Mesh mesh = smr.sharedMesh;
                Vector3[] v = mesh.vertices;
                BoneWeight[] w = mesh.boneWeights;
                Transform[] bones = smr.bones;
                var byName = prefab.GetComponentsInChildren<Transform>().ToDictionary(t => t.name);
                log.AppendLine(name);
                foreach (string finger in new[] { "Index", "Middle", "Ring", "Little" })
                {
                    var line = new StringBuilder($"  {finger,-7}");
                    string[] joints = { "Proximal", "Intermediate", "Distal", "Tip" };
                    Vector3 lastAxis = Vector3.zero;
                    for (int k = 0; k < 3; k++)
                    {
                        Transform a = byName["R_" + finger + joints[k]], b = byName["R_" + finger + joints[k + 1]];
                        Vector3 axis = (b.position - a.position).normalized;
                        int index = System.Array.IndexOf(bones, a);
                        var pts = new List<Vector3>();
                        for (int i = 0; i < v.Length; i++)
                            if (w[i].boneIndex0 == index && w[i].weight0 > 0.7f)
                                pts.Add(v[i]);
                        float off = float.NaN;
                        if (pts.Count > 8)
                        {
                            Vector3 c = pts.Aggregate(Vector3.zero, (s, p) => s + p) / pts.Count;
                            // Principal axis by power iteration on the covariance.
                            Vector3 dir = axis;
                            for (int it = 0; it < 30; it++)
                            {
                                Vector3 next = Vector3.zero;
                                foreach (Vector3 p in pts)
                                    next += (p - c) * Vector3.Dot(p - c, dir);
                                dir = next.normalized;
                            }
                            off = Vector3.Angle(axis, dir);
                            off = Mathf.Min(off, 180f - off);
                        }
                        float bend = k == 0 ? 0f : Vector3.Angle(lastAxis, axis);
                        line.Append($" | {joints[k],-12} off {off,4:0}  bend {bend,3:0}  n {pts.Count,4}");
                        lastAxis = axis;
                    }
                    log.AppendLine(line.ToString());
                }
            }
            Debug.Log("[HandSkinCheck]\n" + log);
        }
    }
}
