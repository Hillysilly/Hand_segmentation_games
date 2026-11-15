using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// One hand at one moment of a select-screen move: where the wrist is (from that side's shoulder, x outward,
    /// y up, z forward, in metres for a 1.8 m figure), its shape, and which way the fingers point and the palm faces
    /// (x outward too). Off means the hand is left to the idle animation.
    /// </summary>
    public struct HandKey
    {
        public bool On;
        public Vector3 At;
        public SignShape Shape;
        public Vector3 Fingers;
        public Vector3 Palm;

        public static HandKey Of(Vector3 at, SignShape shape, Vector3 fingers, Vector3 palm) =>
            new HandKey { On = true, At = at, Shape = shape, Fingers = fingers.normalized, Palm = palm.normalized };
    }

    public sealed class PoseKey
    {
        public float T;
        public HandKey R;
        public HandKey L;
    }

    /// <summary>A hand's pose sampled at a time: blended between the keys either side.</summary>
    public struct HandSample
    {
        public float Weight;
        public Vector3 At;
        public Vector3 Fingers;
        public Vector3 Palm;
        public SignShape From;
        public SignShape To;
        public float Blend;
    }

    /// <summary>
    /// The hand and arm movements the select screen's mannequin and hand sign act out for each move. Each is built
    /// from what the game's gesture detectors look for (see Gestures.cs and ElementGestures.cs), so what it shows
    /// is what works.
    /// </summary>
    public static class ShowcasePoses
    {
        public static readonly Vector3 F = Vector3.forward, U = Vector3.up, D = Vector3.down, In = Vector3.left,
            Out = Vector3.right, B = Vector3.back;

        /// <summary>Arm out in front at shoulder height, a little toward the middle.</summary>
        public static readonly Vector3 ArmOut = new Vector3(-0.06f, -0.04f, 0.47f);

        static readonly HandKey Rest = default;

        public static PoseKey K(float t, HandKey right) => new PoseKey { T = t, R = right };
        public static PoseKey K(float t, HandKey right, HandKey left) => new PoseKey { T = t, R = right, L = left };
        static HandKey H(Vector3 at, SignShape shape, Vector3 fingers, Vector3 palm) => HandKey.Of(at, shape, fingers, palm);
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>Fist out in front, held to charge, then snapped open, palm out (repulsor, comet, fireball, rock orb).</summary>
        public static PoseKey[] FistThenOpen(float fire) => new[]
        {
            K(0f, Rest),
            K(0.3f, H(ArmOut, SignShape.Fist, U, F)),
            K(fire - 0.07f, H(ArmOut, SignShape.Fist, U, F)),
            K(fire, H(ArmOut + V(0f, 0f, 0.03f), SignShape.Open, U, F)),
            K(fire + 0.55f, H(ArmOut, SignShape.Open, U, F)),
            K(fire + 0.9f, Rest),
        };

        /// <summary>Open palm out, held (laser, flamethrower).</summary>
        public static PoseKey[] PalmOutHold(float until) => new[]
        {
            K(0f, Rest),
            K(0.3f, H(ArmOut, SignShape.Open, U, F)),
            K(until, H(ArmOut, SignShape.Open, U, F)),
            K(until + 0.35f, Rest),
        };

        /// <summary>Finger gun pointed out, then the wrist flicked up (blaster, crossbow).</summary>
        public static PoseKey[] FingerGunFlick(float flick) => new[]
        {
            K(0f, Rest),
            K(0.3f, H(ArmOut, SignShape.Gun, F, In)),
            K(flick - 0.08f, H(ArmOut, SignShape.Gun, F, In)),
            K(flick, H(ArmOut + V(0f, 0.03f, 0f), SignShape.Gun, V(0f, 0.65f, 0.76f), In)),
            K(flick + 0.25f, H(ArmOut, SignShape.Gun, F, In)),
            K(flick + 0.6f, Rest),
        };

        /// <summary>The spider sign held out, palm down (web shots).</summary>
        public static PoseKey[] SpiderSign(float until) => new[]
        {
            K(0f, Rest),
            K(0.28f, H(ArmOut, SignShape.Spider, F, D)),
            K(until, H(ArmOut, SignShape.Spider, F, D)),
            K(until + 0.3f, Rest),
        };

        /// <summary>Hands together in front of the chest, palms facing, then pushed forward (giant energy ball).</summary>
        public static PoseKey[] HandsTogetherPush(float push)
        {
            HandKey together = H(V(-0.14f, -0.24f, 0.32f), SignShape.Open, F, In);
            HandKey pushed = H(V(-0.12f, -0.12f, 0.5f), SignShape.Open, U, F);
            return new[]
            {
                K(0f, Rest, Rest),
                K(0.35f, together, together),
                K(push - 0.15f, together, together),
                K(push, pushed, pushed),
                K(push + 0.5f, pushed, pushed),
                K(push + 0.85f, Rest, Rest),
            };
        }

        /// <summary>Thumbs and index fingers touching in a triangle on the line to the floor spot, then both fists (fire pillar).</summary>
        public static PoseKey[] TriangleThenFists(float close)
        {
            // Fingers up and in, palms out, hands meeting in the middle in front of the face.
            HandKey triangle = H(V(-0.12f, -0.06f, 0.36f), SignShape.Triangle, V(-0.55f, 1f, 0.15f), F);
            HandKey fists = H(V(-0.11f, -0.08f, 0.34f), SignShape.Fist, V(-0.3f, 1f, 0.1f), F);
            return new[]
            {
                K(0f, Rest, Rest),
                K(0.4f, triangle, triangle),
                K(close - 0.08f, triangle, triangle),
                K(close, fists, fists),
                K(close + 0.7f, fists, fists),
                K(close + 1.05f, Rest, Rest),
            };
        }

        /// <summary>Cupped hand reaching at a rock on the floor, lifting it, a wind-up and a fast throw (force).</summary>
        public static PoseKey[] CupLiftThrow(float throwAt) => new[]
        {
            K(0f, Rest),
            K(0.35f, H(V(0.05f, -0.34f, 0.4f), SignShape.Cup, V(0.05f, 0.45f, 0.9f), V(0.25f, -0.85f, 0.45f))),
            K(0.9f, H(V(0.05f, 0f, 0.4f), SignShape.Cup, V(0f, 0.6f, 0.8f), V(0.15f, -0.2f, 1f))),
            K(throwAt - 0.15f, H(V(0.1f, 0.06f, 0.16f), SignShape.Cup, V(0f, 0.8f, 0.6f), V(0.1f, -0.1f, 1f))),
            K(throwAt, H(ArmOut, SignShape.Open, U, F)),
            K(throwAt + 0.45f, H(ArmOut, SignShape.Open, U, F)),
            K(throwAt + 0.8f, Rest),
        };

        /// <summary>Both fists touching at arm's length; the right pulls back like a bowstring, then opens (longbow).</summary>
        public static PoseKey[] BowDraw(float release)
        {
            HandKey grip = H(V(-0.17f, -0.03f, 0.5f), SignShape.Fist, F, In);
            HandKey nocked = H(V(-0.17f, -0.03f, 0.43f), SignShape.Fist, F, In);
            HandKey drawn = H(V(-0.1f, 0.0f, 0.04f), SignShape.Fist, F, In);
            HandKey loosed = H(V(-0.06f, 0.02f, 0.0f), SignShape.Open, F, In);
            return new[]
            {
                K(0f, Rest, Rest),
                K(0.35f, nocked, grip),
                K(0.6f, nocked, grip),
                K(release - 0.1f, drawn, grip),
                K(release, loosed, grip),
                K(release + 0.45f, loosed, grip),
                K(release + 0.8f, Rest, Rest),
            };
        }

        /// <summary>A fist swung from low behind up and forward (smoke bomb).</summary>
        public static PoseKey[] UnderhandThrow(float release) => new[]
        {
            K(0f, Rest),
            K(0.3f, H(V(0.06f, -0.62f, -0.16f), SignShape.Fist, D, F)),
            K(release, H(V(0f, -0.2f, 0.45f), SignShape.Fist, F, U)),
            K(release + 0.12f, H(V(-0.02f, -0.1f, 0.48f), SignShape.Open, F, U)),
            K(release + 0.5f, H(V(-0.02f, -0.1f, 0.48f), SignShape.Open, F, U)),
            K(release + 0.8f, Rest),
        };

        /// <summary>Spider sign at a rock down to the left, then the arm swung up and slammed down (web and fling).</summary>
        public static PoseKey[] WebAndSwing() => new[]
        {
            K(0f, Rest),
            K(0.25f, H(V(-0.3f, -0.3f, 0.38f), SignShape.Spider, V(-0.62f, -0.57f, 0.47f), D)),
            K(0.55f, H(V(-0.3f, -0.3f, 0.38f), SignShape.Fist, V(-0.62f, -0.57f, 0.47f), D)),
            K(0.9f, H(V(0.12f, 0.32f, 0.22f), SignShape.Fist, U, In)),
            K(1.15f, H(V(-0.05f, -0.18f, 0.5f), SignShape.Fist, F, D)),
            K(1.5f, H(V(-0.05f, -0.18f, 0.5f), SignShape.Fist, F, D)),
            K(1.8f, Rest),
        };

        /// <summary>An open palm drawn back to the chest, then shoved forward (gust).</summary>
        public static PoseKey[] PalmShove(float shove) => new[]
        {
            K(0f, Rest),
            K(shove - 0.2f, H(V(-0.02f, -0.2f, 0.14f), SignShape.Open, U, F)),
            K(shove, H(ArmOut + V(0f, 0f, 0.03f), SignShape.Open, U, F)),
            K(shove + 0.45f, H(ArmOut, SignShape.Open, U, F)),
            K(shove + 0.75f, Rest),
        };

        /// <summary>Both hands out in front, flat, palms down, held (chain lightning).</summary>
        public static PoseKey[] FlatHands(float until)
        {
            HandKey flat = H(V(-0.03f, -0.1f, 0.48f), SignShape.Open, F, D);
            return new[]
            {
                K(0f, Rest, Rest),
                K(0.35f, flat, flat),
                K(until, flat, flat),
                K(until + 0.35f, Rest, Rest),
            };
        }

        /// <summary>Both hands swirling round in circles in front of the chest, then pushed forward (tornado).</summary>
        public static PoseKey[] SwirlThenPush(float push)
        {
            var keys = new System.Collections.Generic.List<PoseKey> { K(0f, Rest, Rest) };
            const float start = 0.3f;
            const float radius = 0.1f;
            const float turnsPerSecond = 1.3f;
            for (float t = start; t < push - 0.15f; t += 0.06f)
            {
                float a = (t - start) * turnsPerSecond * Mathf.PI * 2f;
                // Each hand round its own circle, the two going opposite ways like stirring the air.
                Vector3 centre = V(-0.1f, -0.26f, 0.34f);
                HandKey right = H(centre + V(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius, SignShape.Open, F, In);
                HandKey left = H(centre + V(Mathf.Cos(a), -Mathf.Sin(a), 0f) * radius, SignShape.Open, F, In);
                keys.Add(K(t, right, left));
            }
            HandKey pushed = H(V(-0.08f, -0.12f, 0.5f), SignShape.Open, U, F);
            keys.Add(K(push, pushed, pushed));
            keys.Add(K(push + 0.5f, pushed, pushed));
            keys.Add(K(push + 0.85f, Rest, Rest));
            return keys.ToArray();
        }

        /// <summary>A fist low at the waist, then raised fast past the elbow (rock spikes).</summary>
        public static PoseKey[] Uppercut(float rise) => new[]
        {
            K(0f, Rest),
            K(rise - 0.22f, H(V(0f, -0.64f, 0.16f), SignShape.Fist, U, B)),
            K(rise, H(V(-0.04f, -0.06f, 0.3f), SignShape.Fist, U, B)),
            K(rise + 0.45f, H(V(-0.04f, -0.06f, 0.3f), SignShape.Fist, U, B)),
            K(rise + 0.75f, Rest),
        };

        /// <summary>A rock orb (fist, snap open), then the hand raised to snap its fingers (sticky rocks).</summary>
        public static PoseKey[] ThrowThenSnap(float fire, float snap) => new[]
        {
            K(0f, Rest),
            K(0.3f, H(ArmOut, SignShape.Fist, U, F)),
            K(fire - 0.07f, H(ArmOut, SignShape.Fist, U, F)),
            K(fire, H(ArmOut, SignShape.Open, U, F)),
            K(fire + 0.25f, H(ArmOut, SignShape.Open, U, F)),
            K(snap - 0.35f, H(V(-0.02f, -0.12f, 0.3f), SignShape.Snap, U, F)),
            K(snap - 0.05f, H(V(-0.02f, -0.12f, 0.3f), SignShape.Snap, U, F)),
            K(snap, H(V(-0.02f, -0.12f, 0.3f), SignShape.Snapped, U, F)),
            K(snap + 0.45f, H(V(-0.02f, -0.12f, 0.3f), SignShape.Snapped, U, F)),
            K(snap + 0.75f, Rest),
        };

        /// <summary>A hand shape held through a clip (the punch, the sword), for the fingers and the sign.</summary>
        public static PoseKey[] ShapeOnly(SignShape shape, Vector3 fingers, Vector3 palm, float until)
        {
            HandKey held = HandKey.Of(ArmOut, shape, fingers, palm);
            held.On = false;
            return new[] { K(0f, held), K(until, held) };
        }

        /// <summary>A hand's pose at <paramref name="time"/>, blended between keys.</summary>
        public static HandSample Sample(PoseKey[] keys, float time, bool left)
        {
            int i = 0;
            while (i + 1 < keys.Length && keys[i + 1].T <= time)
                i++;
            PoseKey a = keys[i];
            PoseKey b = i + 1 < keys.Length ? keys[i + 1] : a;
            float s = b == a || b.T <= a.T ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - a.T) / (b.T - a.T)));
            HandKey ha = left ? a.L : a.R;
            HandKey hb = left ? b.L : b.R;
            return Blend(ha, hb, s);
        }

        /// <summary>
        /// Like <see cref="Sample"/>, but only between the keys where the hand is doing something, so the sign holds
        /// its first shape before the move starts and its last after.
        /// </summary>
        public static HandSample SampleSign(PoseKey[] keys, float time, bool left)
        {
            int before = -1, after = -1;
            for (int i = 0; i < keys.Length; i++)
            {
                HandKey h = left ? keys[i].L : keys[i].R;
                if (!IsPosed(h))
                    continue;
                if (keys[i].T <= time)
                    before = i;
                else if (after < 0)
                    after = i;
            }
            if (before < 0 && after < 0)
                return default;
            if (before < 0)
                before = after;
            if (after < 0)
                after = before;
            HandKey ha = left ? keys[before].L : keys[before].R;
            HandKey hb = left ? keys[after].L : keys[after].R;
            float s = after == before ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - keys[before].T) / (keys[after].T - keys[before].T)));
            HandSample sample = Blend(Posed(ha), Posed(hb), s);
            sample.Weight = 1f;
            return sample;
        }

        /// <summary>A key that says how the hand is held, even if the arm is left to an animation clip.</summary>
        static bool IsPosed(HandKey h) => h.On || h.Fingers != Vector3.zero;

        static HandKey Posed(HandKey h)
        {
            h.On = true;
            return h;
        }

        /// <summary>Whether a move uses both hands (its sign shows two).</summary>
        public static bool TwoHanded(PoseKey[] keys)
        {
            foreach (PoseKey key in keys)
                if (IsPosed(key.L))
                    return true;
            return false;
        }

        static HandSample Blend(HandKey a, HandKey b, float s)
        {
            var sample = new HandSample
            {
                Weight = Mathf.Lerp(a.On ? 1f : 0f, b.On ? 1f : 0f, s),
                From = IsPosed(a) ? a.Shape : SignShape.Relaxed,
                To = IsPosed(b) ? b.Shape : SignShape.Relaxed,
                Blend = s,
            };
            if (a.On && b.On)
            {
                sample.At = Vector3.Lerp(a.At, b.At, s);
                sample.Fingers = Vector3.Slerp(a.Fingers, b.Fingers, s).normalized;
                sample.Palm = Vector3.Slerp(a.Palm, b.Palm, s).normalized;
            }
            else
            {
                HandKey held = a.On ? a : b.On ? b : IsPosed(a) ? a : b;
                sample.At = held.At;
                sample.Fingers = held.Fingers;
                sample.Palm = held.Palm;
            }
            return sample;
        }
    }
}
