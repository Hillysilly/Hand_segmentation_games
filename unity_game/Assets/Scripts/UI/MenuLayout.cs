namespace Repulsor
{
    /// <summary>Where menus sit and how deep a poke clicks. Live-tunable in development builds (see Tuning).</summary>
    public static class MenuLayout
    {
        /// <summary>Metres in front of the eyes.</summary>
        public static float Distance = 0.42f;
        /// <summary>Metres below eye height.</summary>
        public static float Drop = 0.2f;
        /// <summary>Millimetres a key must be pushed in to click.</summary>
        public static float ClickDepth = 9f;
    }
}
