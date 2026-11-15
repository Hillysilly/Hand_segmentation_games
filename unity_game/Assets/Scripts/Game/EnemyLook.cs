using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// An enemy's body: one of the CC0 models built into Resources/Enemies (Quaternius mechs, robot and sci-fi kit),
    /// walking, running or idling, with glowing red eyes and a white flash when hit. Each enemy kind has its own
    /// model (<see cref="EnemySpec.Model"/>).
    /// </summary>
    public sealed class EnemyLook : MonoBehaviour
    {
        static readonly Color EyeRed = new Color(1.5f, 0.12f, 0.06f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Renderer[] renderers;
        Animation animation;
        MaterialPropertyBlock flashBlock;
        MaterialPropertyBlock plainBlock;
        bool flashing;
        string playing;
        float shootFor;

        /// <summary>Makes a model's body under <paramref name="parent"/>; null if the key is the placeholder or missing.</summary>
        public static EnemyLook Create(string key, Transform parent)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            var prefab = Resources.Load<GameObject>("Enemies/" + key);
            if (prefab == null)
            {
                Debug.LogWarning($"[Enemy] No model Enemies/{key}");
                return null;
            }
            GameObject go = Instantiate(prefab, parent, false);
            go.name = "Look " + key;
            var look = go.AddComponent<EnemyLook>();
            look.renderers = go.GetComponentsInChildren<Renderer>();
            look.animation = go.GetComponentInChildren<Animation>();
            look.flashBlock = new MaterialPropertyBlock();
            look.flashBlock.SetColor(BaseColorId, new Color(3f, 3f, 3f, 1f));
            look.plainBlock = new MaterialPropertyBlock();
            foreach (EnemyEye eye in go.GetComponentsInChildren<EnemyEye>())
            {
                float scale = Mathf.Max(1e-5f, eye.transform.lossyScale.x);
                FxSpin glow = BallLook.Sprite("Eye Glow", eye.transform, "FxGlow", eye.Size / scale, EyeRed).Set(billboard: true, flicker: 0.08f);
                glow.transform.localPosition = Vector3.zero;
            }
            return look;
        }

        /// <summary>Walks (or runs) while moving, idles when stopped, and shows the hit flash.</summary>
        public void Tick(float dt, bool walking, bool running, float flash)
        {
            bool flashNow = flash > 0f;
            if (flashNow != flashing)
            {
                flashing = flashNow;
                foreach (Renderer renderer in renderers)
                    renderer.SetPropertyBlock(flashNow ? flashBlock : plainBlock);
            }
            if (animation == null)
                return;
            shootFor -= dt;
            if (shootFor > 0f)
                return;
            string clip = running && animation.GetClip("run") != null ? "run"
                : walking && animation.GetClip("walk") != null ? "walk"
                : animation.GetClip("idle") != null ? "idle" : "walk";
            AnimationState state = animation[clip];
            // No idle clip (the mech boss): standing still holds the walk's pose instead of walking on the spot.
            if (state != null && clip == "walk")
                state.speed = walking || running ? 1f : 0f;
            if (clip != playing && state != null)
            {
                playing = clip;
                state.wrapMode = WrapMode.Loop;
                animation.CrossFade(clip, 0.2f);
            }
        }

        /// <summary>Editor previews: poses the model partway through its walk.</summary>
        public void Pose(float seconds)
        {
            AnimationClip clip = animation != null ? animation.GetClip("walk") ?? animation.GetClip("idle") : null;
            if (clip != null)
                clip.SampleAnimation(animation.gameObject, seconds);
        }

        /// <summary>Plays the shooting (or punching) move, if the model has one.</summary>
        public void Shoot() => PlayOnce("shoot");

        /// <summary>Plays one of the model's clips once (shoot, attack, hit), then goes back to walking or idling.</summary>
        public void PlayOnce(string clip, float speed = 1f)
        {
            if (animation == null || animation[clip] == null)
                return;
            animation[clip].wrapMode = WrapMode.Once;
            animation[clip].speed = speed;
            animation.CrossFade(clip, 0.1f);
            shootFor = animation[clip].length / Mathf.Max(0.1f, speed) * 0.9f;
            playing = clip;
        }
    }
}
