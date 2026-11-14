using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Repulsor
{
    /// <summary>
    /// The tight aura as you see it on yourself: a glow hugging your hands and forearms (the rest of your body is out
    /// of view), from the hand-tracking joints. The shader outlines all the bones as one shape, like the body mask's
    /// glow on others. Moving hands leave fading copies behind, as afterimages. Fainter than on others, since you
    /// look through it.
    /// </summary>
    public sealed class HandGlow
    {
        const int GhostsPerHand = 5;
        const float GhostEvery = 0.07f;
        const float GhostLife = 0.45f;
        const float GhostMinMove = 0.015f;
        const float ForearmLength = 0.26f;
        const float SelfAlpha = 0.8f;
        const int MaxBones = 24;

        /// <summary>Bones as joint pairs, with the hand's thickness at each end.</summary>
        static readonly (Joint a, Joint b, float ra, float rb)[] Bones = BuildBones();

        static (Joint, Joint, float, float)[] BuildBones()
        {
            var bones = new List<(Joint, Joint, float, float)>
            {
                (Joint.Wrist, Joint.ThumbMetacarpal, 0.02f, 0.016f),
                (Joint.ThumbMetacarpal, Joint.ThumbProximal, 0.015f, 0.012f),
                (Joint.ThumbProximal, Joint.ThumbDistal, 0.012f, 0.0105f),
                (Joint.ThumbDistal, Joint.ThumbTip, 0.0105f, 0.009f),
            };
            Joint[] metacarpals = { Joint.IndexMetacarpal, Joint.MiddleMetacarpal, Joint.RingMetacarpal, Joint.LittleMetacarpal };
            float[] widths = { 1f, 1.03f, 0.97f, 0.88f };
            for (int f = 0; f < 4; f++)
            {
                Joint m = metacarpals[f];
                float w = widths[f];
                // The palm (wrist to each knuckle), then the finger's three bones, thinner toward the tip.
                bones.Add((Joint.Wrist, m + 1, 0.02f * w, 0.014f * w));
                bones.Add((m + 1, m + 2, 0.0115f * w, 0.0105f * w));
                bones.Add((m + 2, m + 3, 0.0105f * w, 0.0095f * w));
                bones.Add((m + 3, m + 4, 0.0095f * w, 0.0085f * w));
            }
            return bones.ToArray();
        }

        sealed class Snapshot
        {
            public readonly Vector4[] A = new Vector4[MaxBones];
            public readonly Vector4[] B = new Vector4[MaxBones];
            public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public Bounds Box;
            public Vector3 Wrist;
            public Vector3 Back;
            public float Age = float.MaxValue;

            public void CopyFrom(Snapshot other)
            {
                System.Array.Copy(other.A, A, MaxBones);
                System.Array.Copy(other.B, B, MaxBones);
                Box = other.Box;
                Wrist = other.Wrist;
                Back = other.Back;
            }
        }

        readonly Mesh cube;
        readonly Material material;
        readonly Snapshot[] current = new Snapshot[2];
        readonly Snapshot[][] ghosts = new Snapshot[2][];
        readonly float[] ghostTimer = new float[2];
        readonly Vector3[] lastGhostAt = new Vector3[2];
        readonly int[] nextGhost = new int[2];
        float shown;

        public HandGlow()
        {
            cube = Resources.Load<Mesh>("CubeMesh");
            material = new Material(Resources.Load<Material>("TightGlow"));
            for (int hand = 0; hand < 2; hand++)
            {
                current[hand] = new Snapshot();
                ghosts[hand] = new Snapshot[GhostsPerHand];
                for (int i = 0; i < GhostsPerHand; i++)
                    ghosts[hand][i] = new Snapshot();
            }
        }

        /// <summary>Draws this frame's glow. <paramref name="strength"/> 0 hides it (it fades in and out).</summary>
        public void Tick(HandPose left, HandPose right, float strength, Color color, float dt)
        {
            shown = Mathf.MoveTowards(shown, strength, dt * (strength > shown ? 4f : 1.5f));
            for (int hand = 0; hand < 2; hand++)
            {
                HandPose pose = hand == 0 ? left : right;
                Snapshot now = current[hand];
                bool live = shown > 0.001f && pose != null && pose.Tracked;
                if (live)
                {
                    Capture(pose, now);
                    // Leave an afterimage behind every so often, if the hand has moved enough to see one.
                    ghostTimer[hand] -= dt;
                    if (ghostTimer[hand] <= 0f && Vector3.Distance(lastGhostAt[hand], now.Wrist) > GhostMinMove)
                    {
                        ghostTimer[hand] = GhostEvery;
                        lastGhostAt[hand] = now.Wrist;
                        Snapshot ghost = ghosts[hand][nextGhost[hand]];
                        nextGhost[hand] = (nextGhost[hand] + 1) % GhostsPerHand;
                        ghost.CopyFrom(now);
                        ghost.Age = 0f;
                    }
                }

                foreach (Snapshot ghost in ghosts[hand])
                {
                    if (ghost.Age >= GhostLife)
                        continue;
                    ghost.Age += dt;
                    float left01 = 1f - Mathf.Clamp01(ghost.Age / GhostLife);
                    Draw(ghost, color, shown * SelfAlpha * 0.5f * left01 * left01);
                }
                if (live)
                    Draw(now, color, shown * SelfAlpha);
            }
        }

        void Capture(HandPose pose, Snapshot into)
        {
            Vector3 wrist = pose.Wrist;
            var box = new Bounds(wrist, Vector3.zero);
            for (int i = 0; i < Bones.Length; i++)
            {
                (Joint a, Joint b, float ra, float rb) = Bones[i];
                Vector3 pa = pose[a], pb = pose[b];
                into.A[i] = new Vector4(pa.x, pa.y, pa.z, ra);
                into.B[i] = new Vector4(pb.x, pb.y, pb.z, rb);
                box.Encapsulate(pb);
            }
            // The forearm runs back from the wrist; it fades out toward the elbow in the shader.
            Vector3 back = (wrist - pose[Joint.MiddleProximal]).normalized;
            Vector3 elbow = wrist + back * ForearmLength;
            into.A[Bones.Length] = new Vector4(wrist.x, wrist.y, wrist.z, 0.031f);
            into.B[Bones.Length] = new Vector4(elbow.x, elbow.y, elbow.z, 0.04f);
            box.Encapsulate(elbow);
            // Room for the thickest bone and the glow beyond its edge.
            box.Expand(0.12f);
            into.Box = box;
            into.Wrist = wrist;
            into.Back = back;
        }

        void Draw(Snapshot snapshot, Color color, float alpha)
        {
            if (alpha <= 0.002f || cube == null)
                return;
            MaterialPropertyBlock block = snapshot.Block;
            block.SetColor("_Color", color);
            block.SetFloat("_Alpha", alpha);
            block.SetVectorArray("_BoneA", snapshot.A);
            block.SetVectorArray("_BoneB", snapshot.B);
            block.SetFloat("_BoneCount", Bones.Length + 1);
            block.SetVector("_FadeOrigin", snapshot.Wrist + snapshot.Back * 0.04f);
            Vector3 back = snapshot.Back;
            block.SetVector("_FadeDir", new Vector4(back.x, back.y, back.z, ForearmLength - 0.04f));
            var parameters = new RenderParams(material)
            {
                matProps = block,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };
            Graphics.RenderMesh(parameters, cube, 0, Matrix4x4.TRS(snapshot.Box.center, Quaternion.identity, snapshot.Box.size));
        }
    }

    /// <summary>
    /// The tight aura on a person the headset's camera sees (the other player, or you in a mirror): their body mask
    /// from the pose model, a little bigger and glowing. A new mask arrives several times a second; each one stays
    /// where it was seen and fades, so a moving body leaves afterimages.
    /// </summary>
    public sealed class BodyGlow
    {
        const int Copies = 6;
        const float Life = 0.7f;
        const float LostAfter = 1.2f;

        sealed class Copy
        {
            public GameObject Holder;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public Material Material;
            public Texture2D Texture;
            public float Age = float.MaxValue;
            public bool Newest;
        }

        readonly Copy[] copies = new Copy[Copies];
        readonly byte[] pixels = new byte[BodyTracker.MaskSize * BodyTracker.MaskSize];
        readonly Transform parent;
        int seenVersion = -1;
        int next;
        float sinceNew = float.MaxValue;
        float shown;

        public BodyGlow(Transform parent) => this.parent = parent;

        /// <summary>Whether a mask is showing right now (one arrived recently).</summary>
        public bool Showing => shown > 0.01f && sinceNew < LostAfter;

        public void Tick(PersonDetector detector, float strength, Color color, float dt)
        {
            shown = Mathf.MoveTowards(shown, strength, dt * (strength > shown ? 4f : 1.5f));
            sinceNew += dt;
            if (detector != null && strength > 0f && detector.MaskVersion != seenVersion && detector.Mask != null)
            {
                seenVersion = detector.MaskVersion;
                Add(detector.Mask, detector.MaskCorners);
            }

            foreach (Copy copy in copies)
            {
                if (copy == null)
                    continue;
                copy.Age += dt;
                // The newest stays lit until a newer one comes or the person is lost; the others fade away.
                float alpha = copy.Newest
                    ? (sinceNew < LostAfter ? 1f : 1f - Mathf.Clamp01((sinceNew - LostAfter) / 0.4f))
                    : Mathf.Pow(1f - Mathf.Clamp01(copy.Age / Life), 1.5f) * 0.6f;
                alpha *= shown;
                bool on = alpha > 0.003f;
                if (copy.Renderer.enabled != on)
                    copy.Renderer.enabled = on;
                if (!on)
                    continue;
                copy.Material.SetColor("_Color", color);
                copy.Material.SetFloat("_Alpha", alpha);
            }
        }

        void Add(float[] mask, Vector3[] corners)
        {
            Copy copy = copies[next] ??= Create(next);
            next = (next + 1) % Copies;
            foreach (Copy other in copies)
            {
                if (other != null && other.Newest)
                {
                    other.Newest = false;
                    other.Age = 0f;
                }
            }
            copy.Newest = true;
            copy.Age = 0f;
            sinceNew = 0f;

            // The mask's rows run from its top; a texture's from its bottom.
            int size = BodyTracker.MaskSize;
            for (int y = 0; y < size; y++)
            {
                int from = y * size, to = (size - 1 - y) * size;
                for (int x = 0; x < size; x++)
                    pixels[to + x] = (byte)(Mathf.Clamp01(mask[from + x]) * 255f);
            }
            copy.Texture.SetPixelData(pixels, 0);
            copy.Texture.Apply(false);

            copy.Mesh.vertices = new[] { corners[0], corners[1], corners[2], corners[3] };
            copy.Mesh.RecalculateBounds();
        }

        Copy Create(int index)
        {
            var holder = new GameObject("Body Glow " + index, typeof(MeshFilter), typeof(MeshRenderer));
            holder.transform.SetParent(parent, false);
            // The corners are in the room, so the holder has to sit at the room's origin.
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            holder.transform.localScale = Vector3.one;
            var mesh = new Mesh { name = "Body Glow" };
            mesh.vertices = new Vector3[4];
            mesh.uv = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.MarkDynamic();
            holder.GetComponent<MeshFilter>().sharedMesh = mesh;
            var texture = new Texture2D(BodyTracker.MaskSize, BodyTracker.MaskSize, TextureFormat.R8, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var material = new Material(Resources.Load<Material>("BodyGlow"));
            material.SetTexture("_MaskTex", texture);
            var renderer = holder.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return new Copy { Holder = holder, Mesh = mesh, Renderer = renderer, Material = material, Texture = texture };
        }

        public void Hide()
        {
            shown = 0f;
            foreach (Copy copy in copies)
                if (copy != null)
                    copy.Renderer.enabled = false;
        }
    }
}
