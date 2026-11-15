using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The select screen's hand sign: a big hand (or two) beside the mannequin making the move's hand shape, in
    /// time with it, seen from just behind and to the right as if they were your own hands. Uses the XR Hands
    /// sample hand (Unity), built into Resources/Showcase by ShowcaseModels.
    /// </summary>
    public sealed class ShowcaseSign
    {
        const float OneHandScale = 0.48f;
        const float TwoHandScale = 0.34f;
        /// <summary>How the player's forward, up and right show in the box: from behind, a little above and to the right.</summary>
        static readonly Quaternion BehindView = Quaternion.Euler(14f, -20f, 0f);
        /// <summary>For a hand pointing forward (it'd show only its wrist from behind): from the right side and above.</summary>
        static readonly Quaternion SideView = Quaternion.Euler(38f, -68f, 0f);
        /// <summary>For two hands pointing forward: from above and behind.</summary>
        static readonly Quaternion TopView = Quaternion.Euler(58f, -14f, 0f);
        /// <summary>For the spider sign: from above, so the index and little finger show sticking out.</summary>
        static readonly Quaternion AboveView = Quaternion.Euler(64f, -28f, 0f);
        /// <summary>For a cupped hand: from in front, looking into the cup.</summary>
        static readonly Quaternion FrontView = Quaternion.Euler(-18f, 155f, 0f);
        /// <summary>Half the shoulder width the keys are measured from (they're shoulder-relative).</summary>
        const float Shoulder = 0.18f;

        readonly Transform root;
        readonly Transform[] models = new Transform[2];
        readonly HandRig[] rigs = new HandRig[2];
        readonly Renderer[] renderers;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        bool twoHands;
        Quaternion View = BehindView;
        bool snapView;

        public Transform Root => root;
        /// <summary>For tests: 0 left, 1 right.</summary>
        public HandRig Rig(int side) => rigs[side];

        public ShowcaseSign(Transform parent)
        {
            root = new GameObject("Hand Sign").transform;
            root.SetParent(parent, false);
            for (int side = 0; side < 2; side++)
            {
                var prefab = Resources.Load<GameObject>(side == 1 ? "Showcase/RightHand" : "Showcase/LeftHand");
                if (prefab == null)
                    continue;
                models[side] = Object.Instantiate(prefab, root, false).transform;
                rigs[side] = HandRig.ForXrHand(models[side], side == 1);
            }
            renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (renderer is SkinnedMeshRenderer skinned)
                    skinned.updateWhenOffscreen = true;
            }
        }

        public void Place(Vector3 position, Quaternion rotation) => root.SetPositionAndRotation(position, rotation);

        public void Show(bool on) => root.gameObject.SetActive(on);

        public void Tint(Color color)
        {
            Color skin = Color.Lerp(color, Color.white, 0.45f);
            foreach (Renderer renderer in renderers)
            {
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", skin);
                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>Starts a move: one hand or two.</summary>
        public void Use(PoseKey[] keys)
        {
            twoHands = ShowcasePoses.TwoHanded(keys);
            snapView = true;
            root.localScale = Vector3.one * (twoHands ? TwoHandScale : OneHandScale);
            if (models[0] != null)
                models[0].gameObject.SetActive(twoHands);
        }

        static Vector3 Mirror(Vector3 v, bool left) => left ? new Vector3(-v.x, v.y, v.z) : v;

        /// <summary>The best angle to see a hand like this from.</summary>
        Quaternion ViewFor(HandSample right, HandSample left)
        {
            SignShape shape = right.Blend < 0.5f ? right.From : right.To;
            if (!twoHands && shape == SignShape.Spider)
                return AboveView;
            if (!twoHands && shape == SignShape.Cup)
                return FrontView;
            bool pointing = Vector3.Dot(right.Fingers, Vector3.forward) > 0.55f;
            if (!pointing)
                return BehindView;
            if (!twoHands)
                return SideView;
            // Two hands one behind the other (the bow) show best from the side, side by side from above.
            Vector3 apart = right.At + new Vector3(Shoulder, 0f, 0f) - (Mirror(left.At, true) - new Vector3(Shoulder, 0f, 0f));
            return Mathf.Abs(apart.z) > Mathf.Abs(apart.x) ? SideView : TopView;
        }

        /// <summary>Poses the hands for the move at <paramref name="time"/>.</summary>
        public void Pose(PoseKey[] keys, float time)
        {
            HandSample right = ShowcasePoses.SampleSign(keys, time, false);
            HandSample left = twoHands ? ShowcasePoses.SampleSign(keys, time, true) : default;
            Quaternion want = ViewFor(right, left);
            View = snapView ? want : Quaternion.Slerp(View, want, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            snapView = false;
            // Where the wrists are relative to the chest, for two hands; one hand sits in the middle.
            Vector3 rightAt = right.At + new Vector3(Shoulder, 0f, 0f);
            Vector3 leftAt = Mirror(left.At, true) - new Vector3(Shoulder, 0f, 0f);
            Vector3 middle = twoHands ? (rightAt + leftAt) * 0.5f : rightAt;
            // Keep the pair in the box as the arms move: only a little of the overall movement shows.
            Vector3 drift = twoHands ? Vector3.zero : (rightAt - FirstAt(keys)) * 0.25f;

            for (int side = 0; side < 2; side++)
            {
                if (rigs[side] == null || (side == 0 && !twoHands))
                    continue;
                HandSample s = side == 1 ? right : left;
                if (s.Fingers == Vector3.zero)
                    continue;
                bool isLeft = side == 0;
                Vector3 fingers = root.TransformDirection(View * Mirror(s.Fingers, isLeft));
                Vector3 palm = root.TransformDirection(View * Mirror(s.Palm, isLeft));
                HandRig rig = rigs[side];
                rig.Wrist.rotation = rig.Facing(fingers, palm);
                // Two hands sit closer together than they really are, so both fit in the box.
                Vector3 local = twoHands ? View * Squeeze((isLeft ? leftAt : rightAt) - middle) : View * drift;
                // One hand: centre its palm, not its wrist.
                if (!twoHands)
                    local -= View * Mirror(s.Fingers, false) * 0.1f;
                rig.Wrist.position = root.TransformPoint(local);
                rig.Shape(s.From, s.To, s.Blend);
            }

            if (twoHands && (right.To == SignShape.Triangle || right.From == SignShape.Triangle) && rigs[0] != null && rigs[1] != null)
                CloseTriangle();
        }

        static Vector3 Squeeze(Vector3 offset) =>
            new Vector3(Mathf.Clamp(offset.x * 0.6f, -0.15f, 0.15f), Mathf.Clamp(offset.y * 0.6f, -0.12f, 0.12f), offset.z * 0.6f);

        static Vector3 FirstAt(PoseKey[] keys)
        {
            foreach (PoseKey key in keys)
                if (key.R.On || key.R.Fingers != Vector3.zero)
                    return key.R.At + new Vector3(Shoulder, 0f, 0f);
            return Vector3.zero;
        }

        /// <summary>Slides the hands together so the index tips meet in the middle, and reaches the thumbs to meet too.</summary>
        void CloseTriangle()
        {
            Vector3 normal = root.TransformDirection(View * Vector3.right);
            Vector3 centre = root.position;
            for (int side = 0; side < 2; side++)
            {
                HandRig rig = rigs[side];
                float off = Vector3.Dot(rig.Tip(0) - centre, normal);
                rig.Wrist.position -= normal * off;
            }
            for (int side = 0; side < 2; side++)
            {
                HandRig rig = rigs[side];
                Vector3 thumb = rig.ThumbTipPosition;
                rig.ReachThumb(thumb - normal * Vector3.Dot(thumb - centre, normal));
            }
        }
    }
}
