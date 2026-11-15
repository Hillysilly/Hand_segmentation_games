using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A character's hand model worn over a tracked hand: gauntlets, gloves. Every model is fitted (in the editor, see
    /// HandSkins) onto the XR Hands sample hand's skeleton, whose bones are named after the game's <see cref="Joint"/>s;
    /// each frame the bones are turned to point where your tracked joints point, so the fingers bend with yours.
    /// Bone lengths stay the model's; the whole hand is scaled to the size of yours.
    /// </summary>
    public sealed class HandSkin
    {
        sealed class Bone
        {
            public Transform T;
            public Joint From;
            public Joint To;
            /// <summary>Rest-pose "along the bone" and "back of the hand" directions, in the bone's own space.</summary>
            public Quaternion RestFrame;
        }

        readonly bool left;
        readonly Transform root;
        readonly Transform wrist;
        readonly Transform forearm;
        readonly Quaternion forearmRest;
        readonly List<Bone> bones = new List<Bone>();
        readonly Quaternion wristRest;
        readonly float restSize;
        readonly Renderer[] renderers;

        public Transform Root => root;

        /// <summary>The bone each joint drives toward: a finger joint points at the next one along the finger.</summary>
        /// Parents come before their children, so each bone is turned after the one it hangs from.
        static readonly (Joint from, Joint to)[] Chains =
        {
            (Joint.IndexMetacarpal, Joint.IndexProximal), (Joint.MiddleMetacarpal, Joint.MiddleProximal),
            (Joint.RingMetacarpal, Joint.RingProximal), (Joint.LittleMetacarpal, Joint.LittleProximal),
            (Joint.ThumbMetacarpal, Joint.ThumbProximal), (Joint.ThumbProximal, Joint.ThumbDistal), (Joint.ThumbDistal, Joint.ThumbTip),
            (Joint.IndexProximal, Joint.IndexIntermediate), (Joint.IndexIntermediate, Joint.IndexDistal), (Joint.IndexDistal, Joint.IndexTip),
            (Joint.MiddleProximal, Joint.MiddleIntermediate), (Joint.MiddleIntermediate, Joint.MiddleDistal), (Joint.MiddleDistal, Joint.MiddleTip),
            (Joint.RingProximal, Joint.RingIntermediate), (Joint.RingIntermediate, Joint.RingDistal), (Joint.RingDistal, Joint.RingTip),
            (Joint.LittleProximal, Joint.LittleIntermediate), (Joint.LittleIntermediate, Joint.LittleDistal), (Joint.LittleDistal, Joint.LittleTip),
        };

        HandSkin(Transform root, bool left)
        {
            this.root = root;
            this.left = left;
            string p = left ? "L_" : "R_";
            var named = new Dictionary<string, Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                named[t.name] = t;
            named.TryGetValue(p + "Wrist", out wrist);
            named.TryGetValue(p + "Forearm", out forearm);
            Vector3 handUp = Vector3.up;
            if (wrist != null && named.TryGetValue(p + "MiddleProximal", out Transform middle) && named.TryGetValue(p + "IndexProximal", out Transform index)
                && named.TryGetValue(p + "LittleProximal", out Transform little))
            {
                Vector3 forward = (middle.position - wrist.position).normalized;
                Vector3 thumbSide = (index.position - little.position).normalized;
                Vector3 palm = (!left ? Vector3.Cross(forward, thumbSide) : Vector3.Cross(thumbSide, forward)).normalized;
                handUp = -palm;
                wristRest = Quaternion.Inverse(wrist.rotation) * Quaternion.LookRotation(forward, handUp);
                restSize = Vector3.Distance(wrist.position, middle.position);
                if (forearm != null)
                    forearmRest = Quaternion.Inverse(forearm.rotation) * Quaternion.LookRotation(-forward, handUp);
            }
            foreach ((Joint from, Joint to) in Chains)
            {
                if (!named.TryGetValue(p + from, out Transform bone) || !named.TryGetValue(p + to, out Transform next))
                    continue;
                Vector3 along = next.position - bone.position;
                if (along.sqrMagnitude < 1e-10f)
                    continue;
                Quaternion frame = Quaternion.LookRotation(along, handUp);
                bones.Add(new Bone { T = bone, From = from, To = to, RestFrame = Quaternion.Inverse(bone.rotation) * frame });
            }
            renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (renderer is SkinnedMeshRenderer skinned)
                    skinned.updateWhenOffscreen = true;
            }
        }

        /// <summary>A hand model from Resources (fitted to the XR hand skeleton); null if it isn't there.</summary>
        public static HandSkin Create(string resource, Transform parent, bool left)
        {
            var prefab = Resources.Load<GameObject>(resource);
            if (prefab == null)
                return null;
            GameObject go = Object.Instantiate(prefab, parent, false);
            go.name = resource;
            var skin = new HandSkin(go.transform, left);
            return skin.wrist != null ? skin : null;
        }

        public void Show(bool on)
        {
            if (root.gameObject.activeSelf != on)
                root.gameObject.SetActive(on);
        }

        /// <summary>
        /// Puts the model on the tracked hand: wrist where yours is, each finger bone pointing as yours does. A sleeve
        /// (forearm bone) points from the wrist toward <paramref name="elbow"/>, a guess at where your elbow is.
        /// </summary>
        public void Follow(HandPose hand, Vector3 elbow)
        {
            if (!hand.Tracked || wrist == null)
            {
                Show(false);
                return;
            }
            Show(true);
            Vector3 handUp = -hand.PalmNormal;
            Vector3 forward = hand[Joint.MiddleProximal] - hand.Wrist;
            if (forward.sqrMagnitude < 1e-8f)
                return;
            // Scale the whole model to your hand, then place the wrist.
            float size = forward.magnitude;
            if (restSize > 1e-5f)
                root.localScale = Vector3.one * (size / restSize);
            wrist.rotation = Quaternion.LookRotation(forward, handUp) * Quaternion.Inverse(wristRest);
            wrist.position = hand.Wrist;
            if (forearm != null)
            {
                Vector3 back = elbow - hand.Wrist;
                if (back.sqrMagnitude < 1e-6f)
                    back = -forward;
                forearm.rotation = Quaternion.LookRotation(back, handUp) * Quaternion.Inverse(forearmRest);
                forearm.position = hand.Wrist;
            }
            foreach (Bone bone in bones)
            {
                Vector3 along = hand[bone.To] - hand[bone.From];
                if (along.sqrMagnitude < 1e-10f)
                    continue;
                bone.T.rotation = Quaternion.LookRotation(along, handUp) * Quaternion.Inverse(bone.RestFrame);
            }
        }

        public void Destroy()
        {
            if (root != null)
                Object.Destroy(root.gameObject);
        }
    }
}
