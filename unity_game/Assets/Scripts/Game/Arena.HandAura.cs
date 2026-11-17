using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Level 3's hand aura (11/15, replacing the hand models: gloves fitted to other skeletons never quite matched
    /// your fingers). A see-through glow in your attack colour around each tracked hand: the XR Hands sample hand,
    /// whose skeleton is the tracking skeleton itself, posed like yours by <see cref="HandSkin"/> and drawn with
    /// Repulsor/HandAura (an outline plus flickering energy, your real hand visible through it). Every character.
    /// </summary>
    public sealed partial class Arena
    {
        readonly HandSkin[] auraHands = new HandSkin[2];
        readonly Renderer[][] auraRenderers = new Renderer[2][];
        MaterialPropertyBlock auraBlock;

        /// <summary>Whether this character's hands glow at this level.</summary>
        public static bool WearsHandAura(int level) => level >= 3;

        /// <summary>True while the aura is on (for tests).</summary>
        public bool TestHandAuraShown => auraHands[1] != null && auraHands[1].Root.gameObject.activeSelf;

        /// <summary>
        /// Where the elbow probably is (only the hands are tracked): back from the wrist along the line toward the
        /// shoulder, a forearm's length away, sagging a little.
        /// </summary>
        Vector3 ElbowGuess(HandPose hand)
        {
            Vector3 shoulder = body.Shoulder(hand.IsLeft);
            Vector3 toShoulder = shoulder - hand.Wrist;
            Vector3 along = Vector3.Slerp(toShoulder.normalized, -(hand[Joint.MiddleProximal] - hand.Wrist).normalized, 0.3f);
            return hand.Wrist + (along + Vector3.down * 0.25f).normalized * Mathf.Min(0.27f, toShoulder.magnitude * 0.6f);
        }

        void TickHandAura()
        {
            bool inPlay = (phase == Phase.Playing && !player.IsDead) || practicing;
            bool wear = inPlay && WearsHandAura(Tier);
            for (int i = 0; i < 2; i++)
            {
                if (!wear)
                {
                    auraHands[i]?.Show(false);
                    continue;
                }
                if (auraHands[i] == null)
                {
                    auraHands[i] = MakeAuraHand(i == 0);
                    if (auraHands[i] == null)
                        continue;
                    auraRenderers[i] = auraHands[i].Root.GetComponentsInChildren<Renderer>(true);
                }
                HandPose hand = i == 0 ? input.Left : input.Right;
                auraHands[i].Follow(hand, ElbowGuess(hand));
                if (!hand.Tracked)
                    continue;
                auraBlock ??= new MaterialPropertyBlock();
                Color color = AttackColor;
                color.a = 1f;
                foreach (Renderer renderer in auraRenderers[i])
                {
                    renderer.GetPropertyBlock(auraBlock);
                    auraBlock.SetColor("_Color", color);
                    auraBlock.SetFloat("_Strength", 0.85f + 0.15f * Mathf.Sin(Time.time * 5f + i));
                    auraBlock.SetVector("_WristPos", hand.Wrist);
                    auraBlock.SetVector("_WristFwd", hand[Joint.MiddleProximal] - hand.Wrist);
                    renderer.SetPropertyBlock(auraBlock);
                }
            }
        }

        HandSkin MakeAuraHand(bool left)
        {
            HandSkin hand = HandSkin.Create(left ? "Showcase/LeftHand" : "Showcase/RightHand", transform, left);
            var material = Resources.Load<Material>("HandAura");
            if (hand == null || material == null)
            {
                hand?.Destroy();
                Log("No hand aura (sample hand or material missing)");
                return null;
            }
            hand.Root.name = left ? "Hand Aura L" : "Hand Aura R";
            foreach (Renderer renderer in hand.Root.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int k = 0; k < slots.Length; k++)
                    slots[k] = material;
                renderer.sharedMaterials = slots;
                renderer.receiveShadows = false;
            }
            return hand;
        }
    }
}
