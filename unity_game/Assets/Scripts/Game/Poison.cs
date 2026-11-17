using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Poison from the Valkyr's poison bomb (11/16): damage every frame for a few seconds (25 a second for 4 s, 100
    /// in all), and a green flicker on the enemy. Doesn't stack: poisoning a poisoned enemy only starts its time again.
    /// </summary>
    public sealed class Poison : MonoBehaviour
    {
        RepulsorGame game;
        IHittable target;
        float perSecond;
        float left;
        Bubble glow;

        public float SecondsLeft => left;

        public static Poison Apply(RepulsorGame game, IHittable target, GameObject on, float perSecond, float seconds)
        {
            var poison = on.GetComponent<Poison>();
            if (poison == null)
            {
                poison = on.AddComponent<Poison>();
                poison.glow = Bubble.Create("Poison Glow", on.transform);
            }
            poison.game = game;
            poison.target = target;
            poison.perSecond = perSecond;
            poison.left = seconds;
            return poison;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (target == null || !target.Alive || game == null || left <= 0f)
            {
                if (glow != null)
                    Destroy(glow.gameObject);
                Destroy(this);
                return;
            }
            float step = Mathf.Min(dt, left);
            left -= dt;
            // Powerups were applied when it was thrown.
            game.Strike(target, perSecond * step, target.Center, "Poison", false);
            if (glow != null)
            {
                glow.transform.position = target.Center;
                glow.transform.localScale = Vector3.one * (target.Radius * 2.6f);
                glow.Show(new Color(0.35f, 1f, 0.25f), BubbleStyle.Smoke, 0.35f + 0.15f * Mathf.Sin(Time.time * 9f));
            }
        }
    }
}
