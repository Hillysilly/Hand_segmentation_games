namespace Repulsor
{
    public enum CharacterId { Technoman, KiFighter, Mage, Saber, Archer, Spider, Elementalist, Earth }

    /// <summary>
    /// What a character is: name, colour, and how each level plays. Names are placeholders until the
    /// user picks from the shortlist in game_schedule.md. Controls are written per character, because
    /// the same gesture does different things for different characters.
    /// </summary>
    public sealed class CharacterDef
    {
        public CharacterId Id;
        public string Name;
        public string Tagline;
        /// <summary>Default colour as #RRGGBB; players can change it on the select screen.</summary>
        public string Color;
        /// <summary>Aura style number (1-based, as in AuraView) at levels 1, 2, 3; 0 = none.</summary>
        public int[] AuraStyle;
        public string Shield;
        /// <summary>One line per control, for each level. Each level adds to the ones before it.</summary>
        public string[][] Controls;
        public bool Ready;

        public string ControlsUpTo(int level)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < level && i < Controls.Length; i++)
            {
                text.Append("<b>LV ").Append(i + 1).Append("</b>   ");
                text.Append(string.Join(".  ", Controls[i])).Append(".\n");
            }
            return text.ToString();
        }

        public static readonly CharacterDef[] All =
        {
            new CharacterDef
            {
                Id = CharacterId.Technoman, Name = "Technoman", Tagline = "Armoured inventor",
                Color = "#4FC3FF", AuraStyle = new[] { 0, 0, 0 }, Shield = "Blue hex bubble", Ready = true,
                Controls = new[]
                {
                    new[] { "Palm out and hold: hand laser" },
                    new[] { "Fist, then snap open: repulsor blast", "Lasers get stronger" },
                    new[] { "Repulsor blasts explode in an area", "Lasers get stronger", "Energy gauntlets on your hands" },
                },
            },
            new CharacterDef
            {
                Id = CharacterId.KiFighter, Name = "Ryuu", Tagline = "Martial artist",
                Color = "#FFD23F", AuraStyle = new[] { 0, 2, 2 }, Shield = "Flaming bubble", Ready = true,
                Controls = new[]
                {
                    new[] { "Fist, then snap open: comet blast" },
                    new[] { "Hands together to charge, push forward: giant energy ball", "Comet blasts get stronger", "Golden flame aura" },
                    new[] { "Punch fast: dragon fist", "Aura turns blue" },
                },
            },
            new CharacterDef
            {
                Id = CharacterId.Mage, Name = "Emberwitch", Tagline = "Fire mage",
                Color = "#FF6A20", AuraStyle = new[] { 0, 5, 5 }, Shield = "Thin magic bubble",
                Controls = new[]
                {
                    new[] { "Fist, then snap open: fireball" },
                    new[] { "Palm out and hold: flamethrower", "Rune circle aura" },
                    new[] { "Triangle with both hands: aim a fire circle, close both fists: fire pillar", "Black and gold gloves" },
                },
            },
            new CharacterDef
            {
                Id = CharacterId.Saber, Name = "Lumen", Tagline = "Laser swordfighter",
                Color = "#3F8CFF", AuraStyle = new[] { 0, 3, 3 }, Shield = "Glassy bubble",
                Controls = new[]
                {
                    new[] { "Finger gun, flick up: blaster bolt" },
                    new[] { "Make a fist: laser sword", "Light aura" },
                    new[] { "Cupped hand at an enemy or object: lift it, then throw. Make a fist on a held enemy: crush it", "Brighter aura with a halo", "Black gloves" },
                },
            },
            new CharacterDef
            {
                Id = CharacterId.Archer, Name = "Valkyr", Tagline = "Winged archer",
                Color = "#E8E8FF", AuraStyle = new[] { 0, 0, 0 }, Shield = "Knight's shield",
                Controls = new[]
                {
                    new[] { "Finger gun, flick up: crossbow bolt (reloads)" },
                    new[] { "Fists together at arm's length: bow. Pull one back, open it to shoot" },
                    new[] { "Underhand throw with a fist: smoke bomb", "Arrows pierce", "Wings", "Plated gloves" },
                },
            },
            new CharacterDef
            {
                Id = CharacterId.Spider, Name = "Webline", Tagline = "Web slinger",
                Color = "#E02030", AuraStyle = new[] { 0, 0, 0 }, Shield = "Web cocoon",
                Controls = new[]
                {
                    new[] { "Spider sign (index and little finger out): webs, hold it to keep firing", "Spider sense warns of unseen enemies" },
                    new[] { "Web an enemy or an object, then swing your arm to smash and fling it" },
                    new[] { "Black webs, more damage" },
                },
            },
            new CharacterDef
            {
                Id = CharacterId.Elementalist, Name = "Tempest", Tagline = "Storm caller",
                Color = "#BFF4FF", AuraStyle = new[] { 0, 0, 0 }, Shield = "Wind bubble",
                Controls = new[]
                {
                    new[] { "Push an open palm forward: gust of wind (knocks enemies back)" },
                    new[] { "Both hands out flat: chain lightning, up to 5 s at a time", "Swirling wind aura" },
                    new[] { "Swirl both hands round in circles to charge, push both forward: tornado that pulls enemies in", "Faster, thicker wind", "Storm gauntlets" },
                },
            },
            new CharacterDef
            {
                Id = CharacterId.Earth, Name = "Terra", Tagline = "Stone shaper",
                Color = "#E07A2E", AuraStyle = new[] { 0, 0, 0 }, Shield = "Rock wall",
                Controls = new[]
                {
                    new[] { "Fist, then snap open: rock orb", "Stone on your hands" },
                    new[] { "Fist low at your waist, raise it fast: rock spikes burst from the floor", "Crater around your feet" },
                    new[] { "Rock orbs stick to enemies. Snap your fingers: they explode", "Rocks orbit you, flecked with gold" },
                },
            },
        };

        public static CharacterDef Get(CharacterId id) => All[(int)id];
    }
}
