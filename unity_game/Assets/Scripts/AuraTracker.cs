using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Keeps an aura on a person, using where <see cref="PersonDetector"/> last saw them. Sightings come a
    /// few times a second, so this glides between them. When nobody is seen, the aura stays put.
    /// </summary>
    public sealed class AuraTracker
    {
        const float MaxSpeed = 3f;
        const float Smoothing = 7f;
        const float NearestJump = 0.5f;
        const float LongestJump = 2.5f;
        const float LogSeconds = 1f;

        Vector2 target;
        Vector2 shown;
        float lastSeen;
        float nextLog;
        int seen;
        int jumped;

        /// <summary>Starts following from where the aura was lit.</summary>
        public void Begin(Vector3 position)
        {
            target = new Vector2(position.x, position.z);
            shown = target;
            lastSeen = Time.time;
        }

        /// <summary>Returns where the aura should stand now.</summary>
        public Vector3 Tick(PersonDetector detector, float dt)
        {
            if (detector.TryTakeSighting(out Vector3 point))
                Follow(new Vector2(point.x, point.z));

            Vector2 smoothed = Vector2.Lerp(shown, target, 1f - Mathf.Exp(-Smoothing * dt));
            shown = Vector2.MoveTowards(shown, smoothed, MaxSpeed * dt);

            if (Time.time >= nextLog)
            {
                nextLog = Time.time + LogSeconds;
                Debug.Log($"[Repulsor] Aura following at ({shown.x:0.00}, {shown.y:0.00}), sightings {seen}, jumped {jumped}");
                seen = 0;
                jumped = 0;
            }
            return new Vector3(shown.x, 0f, shown.y);
        }

        void Follow(Vector2 next)
        {
            // Someone can only have moved so far since last seen. Further than that is someone else.
            float allowed = Mathf.Min(NearestJump + MaxSpeed * (Time.time - lastSeen), LongestJump);
            if (Vector2.Distance(next, target) > allowed)
            {
                jumped++;
                return;
            }

            seen++;
            lastSeen = Time.time;
            target = next;
        }
    }
}
