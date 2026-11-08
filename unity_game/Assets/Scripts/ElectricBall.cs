using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A ball of energy with small arcs of lightning crackling off it. The glow is a shader; the arcs are real
    /// lines, so both eyes see them in the right place. The transform's scale is the ball's diameter.
    /// </summary>
    public sealed class ElectricBall : MonoBehaviour
    {
        const float SurfaceRadius = 0.5f;
        const float MinReach = 0.85f;
        const float MaxReach = 1.5f;
        const float Jitter = 0.13f;
        const float WidthOverDiameter = 0.12f;
        const float ReshapeSeconds = 0.035f;

        [SerializeField] LineRenderer[] arcs;

        readonly System.Random random = new System.Random();
        float[] timers;
        Vector3[][] paths;
        Vector3[] tips;
        float reshapeIn;

        void Update() => Crackle(Time.deltaTime);

        /// <summary>Each arc strikes for a moment, reshaping as it does, then waits and strikes somewhere else.</summary>
        public void Crackle(float dt)
        {
            if (timers == null)
                Prepare();

            reshapeIn -= dt;
            bool reshape = reshapeIn <= 0f;
            if (reshape)
                reshapeIn = ReshapeSeconds;

            float width = transform.lossyScale.x * WidthOverDiameter;
            for (int i = 0; i < arcs.Length; i++)
            {
                LineRenderer arc = arcs[i];
                timers[i] -= dt;
                bool struck = false;
                if (timers[i] <= 0f)
                {
                    arc.enabled = !arc.enabled;
                    struck = arc.enabled;
                    timers[i] = arc.enabled ? Random.Range(0.05f, 0.16f) : Random.Range(0.02f, 0.18f);
                    if (struck)
                        tips[i] = Random.onUnitSphere;
                }

                if (!arc.enabled || !reshape && !struck)
                    continue;

                Vector3 start = tips[i] * SurfaceRadius;
                Vector3 end = (tips[i] + Random.insideUnitSphere * 0.5f).normalized * Random.Range(MinReach, MaxReach);
                GameMath.LightningPath(start, end, Jitter, paths[i], random);
                arc.SetPositions(paths[i]);
                arc.widthMultiplier = width;
            }
        }

        void Prepare()
        {
            timers = new float[arcs.Length];
            tips = new Vector3[arcs.Length];
            paths = new Vector3[arcs.Length][];
            for (int i = 0; i < arcs.Length; i++)
            {
                paths[i] = new Vector3[arcs[i].positionCount];
                arcs[i].enabled = false;
                timers[i] = Random.Range(0f, 0.2f);
            }
        }
    }
}
