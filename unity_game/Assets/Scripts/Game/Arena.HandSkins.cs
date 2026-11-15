using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Level 3 hand models (the user's picks, 11/14): Technoman's energy gauntlets, Emberwitch's black and gold
    /// gloves, Lumen's black gloves, Valkyr's plated gloves, Tempest's storm gauntlets. Worn over your tracked hands
    /// while playing at level 3; each is a pair of prefabs in Resources/HandSkins built by HandSkins (editor).
    /// A character without its prefabs simply shows none.
    /// </summary>
    public sealed partial class Arena
    {
        static readonly Dictionary<CharacterId, string> HandSkinNames = new Dictionary<CharacterId, string>
        {
            [CharacterId.Technoman] = "Technoman",
            [CharacterId.Mage] = "Emberwitch",
            [CharacterId.Saber] = "Lumen",
            [CharacterId.Archer] = "Valkyr",
            [CharacterId.Elementalist] = "Tempest",
        };

        CharacterId skinFor = (CharacterId)255;
        readonly HandSkin[] skins = new HandSkin[2];

        /// <summary>Whether this character wears a hand model at this level.</summary>
        public static bool WearsHandSkin(CharacterId id, int level) => level >= 3 && HandSkinNames.ContainsKey(id);

        /// <summary>True while hand models are on (Technoman's old hand glow steps aside then).</summary>
        bool HandSkinsShown => skins[0] != null || skins[1] != null;

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

        void TickHandSkins()
        {
            bool inPlay = (phase == Phase.Playing && !player.IsDead) || practicing;
            bool wear = inPlay && WearsHandSkin(character, player.Level);
            if (!wear)
            {
                for (int i = 0; i < 2; i++)
                    skins[i]?.Show(false);
                return;
            }
            if (skinFor != character)
            {
                for (int i = 0; i < 2; i++)
                {
                    skins[i]?.Destroy();
                    skins[i] = HandSkin.Create($"HandSkins/{HandSkinNames[character]}_{(i == 0 ? "L" : "R")}", transform, i == 0);
                }
                skinFor = character;
                if (!HandSkinsShown)
                    Log($"No hand models yet for {character}");
            }
            skins[0]?.Follow(input.Left, ElbowGuess(input.Left));
            skins[1]?.Follow(input.Right, ElbowGuess(input.Right));
        }
    }
}
