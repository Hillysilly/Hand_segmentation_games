using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Shows a suit on the person <see cref="PersonDetector"/> is following. The models load by name from
    /// Resources, because they're licensed and kept out of the project's history, so a copy of the game built
    /// without them simply has no suit styles. Bodies arrive far less often than frames, so joints glide
    /// between them, reaching a little ahead to make up for how old each body already is.
    /// </summary>
    public sealed class SuitView : MonoBehaviour
    {
        const float Smoothing = 18f;
        const float LeadSeconds = 0.08f;
        const float VelocityEase = 0.4f;
        const float MaxSpeed = 6f;
        const float HideAfterSeconds = 1.5f;

        static readonly string[] Names = { "IronMan", "SpiderMan" };

        sealed class Suit
        {
            public string Name;
            public GameObject Holder;
            public SuitRig Rig;
        }

        readonly List<Suit> suits = new List<Suit>();
        readonly Vector3[] shown = new Vector3[BodyFrame.JointCount];
        readonly Vector3[] goal = new Vector3[BodyFrame.JointCount];
        readonly Vector3[] velocity = new Vector3[BodyFrame.JointCount];
        readonly Vector3[] latest = new Vector3[BodyFrame.JointCount];
        Suit current;
        int seenVersion = -1;
        float lastBodyTime = float.MinValue;
        bool hasBody;

        public int Count => suits.Count;

        public string NameOf(int index) => suits[index].Name;

        void Awake()
        {
            foreach (string suitName in Names)
            {
                var prefab = Resources.Load<GameObject>("Suits/" + suitName);
                if (prefab == null)
                    continue;

                var holder = new GameObject(suitName);
                holder.transform.SetParent(transform, false);
                Instantiate(prefab, holder.transform);
                suits.Add(new Suit { Name = suitName, Holder = holder, Rig = new SuitRig(holder.transform) });
                holder.SetActive(false);
            }
            Debug.Log($"[Repulsor] {suits.Count} suits loaded");
        }

        /// <summary>Shows one suit, or none for a negative number.</summary>
        public void Show(int index)
        {
            current = index >= 0 && index < suits.Count ? suits[index] : null;
            foreach (Suit suit in suits)
            {
                if (suit != current)
                    suit.Holder.SetActive(false);
            }
        }

        /// <summary>Moves the shown suit towards the newest body.</summary>
        public void Tick(PersonDetector person, float dt)
        {
            if (current == null)
                return;

            if (person.BodyVersion != seenVersion)
            {
                seenVersion = person.BodyVersion;
                TakeBody(person.Joints);
            }

            bool visible = hasBody && Time.time - lastBodyTime < HideAfterSeconds;
            if (current.Holder.activeSelf != visible)
                current.Holder.SetActive(visible);
            if (!visible)
                return;

            float ease = 1f - Mathf.Exp(-Smoothing * dt);
            for (int i = 0; i < shown.Length; i++)
                shown[i] = Vector3.Lerp(shown[i], goal[i], ease);
            current.Rig.Pose(shown);
        }

        void TakeBody(Vector3[] joints)
        {
            float elapsed = Mathf.Max(Time.time - lastBodyTime, 0.001f);
            bool fresh = !hasBody || elapsed > HideAfterSeconds;
            for (int i = 0; i < joints.Length; i++)
            {
                if (fresh)
                {
                    shown[i] = goal[i] = latest[i] = joints[i];
                    velocity[i] = Vector3.zero;
                    continue;
                }

                Vector3 moved = Vector3.ClampMagnitude((joints[i] - latest[i]) / elapsed, MaxSpeed);
                velocity[i] = Vector3.Lerp(velocity[i], moved, VelocityEase);
                latest[i] = joints[i];
                goal[i] = joints[i] + velocity[i] * LeadSeconds;
            }
            lastBodyTime = Time.time;
            hasBody = true;
        }
    }
}
