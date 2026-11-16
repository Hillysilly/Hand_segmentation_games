using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// What you see of the other player's attacks and auras (11/15; before, only plain shots crossed over):
    /// - every shot in its own look (ki ball, arrow, crossbow bolt, rock orb, web, repulsor comet style);
    /// - held attacks drawn from their hands, sent with each player update: laser, flames, light saber blades,
    ///   lightning;
    /// - one-off effects: dragon fist, fire pillar, gust, tornado, rock spikes, explosions, smoke bombs;
    /// - their real aura (a copy of the same aura model, with its own colour and strength) and their wind or stone
    ///   aura, instead of the old stand-in shell.
    /// Replays here do no damage: the attacker's own headset deals it (and in PVP its hits are sent separately).
    /// </summary>
    public sealed partial class Arena
    {
        ShotLook nextLook;
        byte nextStyle;

        /// <summary>The look of the very next shot fired (read when it goes out to the other headset).</summary>
        void NextShot(ShotLook look, int style = 0)
        {
            nextLook = look;
            nextStyle = (byte)style;
        }

        /// <summary>The look of a shot being fired now: what was set for it, else the character's usual one.</summary>
        (ShotLook look, byte style) TakeShotLook()
        {
            ShotLook look = nextLook;
            byte style = nextStyle;
            nextLook = ShotLook.Plain;
            nextStyle = 0;
            if (look == ShotLook.Plain && game.FiringComet)
                return game.Dress != null ? (ShotLook.Rock, (byte)0) : (ShotLook.Comet, (byte)game.CometStyle);
            return (look, style);
        }

        /// <summary>Draws the other player's shot the way it looked on their headset.</summary>
        void DressReplayedShot(Blast blast, Vector3 origin, ShotLook look, byte style, Color color)
        {
            if (blast == null)
                return;
            switch (look)
            {
                case ShotLook.KiBall:
                    DecorateBall(blast.Head, style, color);
                    break;
                case ShotLook.Arrow:
                    ArcherGear.Dress(blast, "Arrow", color);
                    break;
                case ShotLook.Bolt:
                    ArcherGear.Dress(blast, "Bolt", color);
                    break;
                case ShotLook.Rock:
                    DressRockLook(blast);
                    break;
                case ShotLook.Web:
                    WebStrand.Attach(blast, origin, style == 1 ? new Color(0.25f, 0.2f, 0.3f) : Color.white, foes);
                    break;
                case ShotLook.Comet:
                    RepulsorGame.DressComet(blast, style, color);
                    break;
            }
        }

        /// <summary>Tells the other headset about a one-off effect: a point, a direction, a number, a colour, a style.</summary>
        void SendEffect(NetEffect kind, Vector3 a, Vector3 direction, float f = 0f, Color color = default, int style = 0)
        {
            if (replayingShot || !Multiplayer || !net.Connected)
                return;
            net.SendEffect(new EffectEvent
            {
                Kind = kind,
                A = frame.ToShared(a),
                B = frame.DirectionToShared(direction),
                F = f,
                Color = color,
                Style = (byte)style,
            });
        }

        void ReplayEffect(EffectEvent e)
        {
            if (phase != Phase.Playing)
                return;
            Vector3 a = frame.ToWorld(e.A);
            Vector3 b = frame.DirectionToWorld(e.B);
            Color color = e.Color;
            replayingShot = true;
            game.Replaying = true;
            switch (e.Kind)
            {
                case NetEffect.DragonFist:
                    DragonFist.Launch(game, e.Style, a, b, 0f);
                    break;
                case NetEffect.FirePillar:
                    game.Explode(a + Vector3.up * 0.6f, 1.1f, 0f, color);
                    FirePillar.Spawn(a, color, e.Style, foes);
                    break;
                case NetEffect.Gust:
                    WindGust.Launch(game, a, b, 0f, foes);
                    break;
                case NetEffect.Tornado:
                    Tornado.Launch(game, a, b, e.F, FloorY, 0f, foes);
                    break;
                case NetEffect.RockSpikes:
                    RockSpikes.Erupt(game, new Vector3(a.x, FloorY, a.z), b, 0f, rockMaterial, foes);
                    break;
                case NetEffect.Explosion:
                    game.Explode(a, e.F, 0f, color);
                    break;
                case NetEffect.Smoke:
                    SmokeCloud.Throw(a, b * e.F, foes);
                    break;
            }
            game.Replaying = false;
            replayingShot = false;
        }

        // ------------------------------------------------------------------ held attacks from the hands

        /// <summary>What one of your hands is drawing now, for the player update.</summary>
        HandBeam BeamOf(int i, out Vector3 from, out Vector3 to)
        {
            from = to = Vector3.zero;
            if (phase != Phase.Playing || player.IsDead)
                return HandBeam.None;
            if (character == CharacterId.Archer && player.Level >= 2 && bow.Formed)
            {
                from = bow.BowPosition;
                to = i == 0 ? bow.StringPosition : bow.BowPosition + bow.Direction * (0.05f + bow.Draw);
                return i == 0 ? HandBeam.Bow : HandBeam.BowAim;
            }
            if (character == CharacterId.Archer && gun[i].Aiming)
            {
                HandPose aiming = i == 0 ? input.Left : input.Right;
                from = aiming.Palm;
                to = from + gun[i].Direction * (0.1f + Mathf.Clamp01(1f - reload[i] / CrossbowReload));
                return HandBeam.Crossbow;
            }
            if (character == CharacterId.KiFighter && i == 0 && kiCharge.Holding)
            {
                from = kiCharge.Center;
                to = from + Vector3.up * kiCharge.Charge;
                return HandBeam.KiCharge;
            }
            if (lightning != null && lightning[i].ShownNow)
            {
                from = lightning[i].ShownFrom;
                to = lightning[i].ShownTo;
                return HandBeam.Lightning;
            }
            HandRepulsor hand = i == 0 ? leftHand : rightHand;
            if (hand == null)
                return HandBeam.None;
            HandBeam beam = hand.ShownBeam;
            from = hand.ShownFrom;
            to = hand.ShownTo;
            return beam;
        }

        readonly LaserBeamView[] remoteBeams = new LaserBeamView[2];
        readonly Flamethrower[] remoteFlames = new Flamethrower[2];
        ChainLightning[] remoteLightning;
        Color remoteBeamColor;

        ArcherGear remoteArcher;
        GameObject remoteChargeBall;
        Transform remoteBody;

        /// <summary>A point riding on the other player's body (things stuck to them hang off it).</summary>
        Transform RemoteBody
        {
            get
            {
                if (remoteBody == null)
                {
                    remoteBody = new GameObject("Remote Body").transform;
                    remoteBody.SetParent(transform, false);
                }
                return remoteBody;
            }
        }

        /// <summary>For tests: the other player's charging ball shows.</summary>
        public bool TestRemoteCharge => remoteChargeBall != null && remoteChargeBall.activeSelf;

        /// <summary>The other player's bow, crossbows and charging ball.</summary>
        void TickRemoteGear(bool show)
        {
            PlayerSnapshot p = remote.Latest;
            Color color = p.Color;
            if (remoteBody != null || show)
                RemoteBody.position = remote.ShownHead + Vector3.down * 0.35f;
            var left = (HandBeam)(show ? p.LeftBeam : 0);
            var right = (HandBeam)(show ? p.RightBeam : 0);

            if (left == HandBeam.Bow || right == HandBeam.Crossbow || left == HandBeam.Crossbow || remoteArcher != null)
            {
                remoteArcher ??= new ArcherGear(transform);
                if (left == HandBeam.Bow && right == HandBeam.BowAim)
                {
                    Vector3 aim = p.RightBeamTo - p.RightBeamFrom;
                    remoteArcher.Bow(true, p.LeftBeamFrom, p.LeftBeamTo, aim.normalized, Mathf.Clamp01(aim.magnitude - 0.05f), color);
                }
                else
                    remoteArcher.Bow(false, default, default, Vector3.forward, 0f, color);
                for (int i = 0; i < 2; i++)
                {
                    bool crossbow = (i == 0 ? left : right) == HandBeam.Crossbow;
                    Vector3 from = i == 0 ? p.LeftBeamFrom : p.RightBeamFrom;
                    Vector3 along = (i == 0 ? p.LeftBeamTo : p.RightBeamTo) - from;
                    remoteArcher.Crossbow(i, crossbow, from, crossbow && along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.forward,
                        Mathf.Clamp01(along.magnitude - 0.1f), color);
                }
            }

            bool charging = left == HandBeam.KiCharge;
            if (charging && remoteChargeBall == null)
            {
                remoteChargeBall = game.MakeEnergyBall(transform);
                DecorateBall(remoteChargeBall.transform, 0, color);
            }
            if (remoteChargeBall != null)
            {
                remoteChargeBall.SetActive(charging);
                if (charging)
                {
                    remoteChargeBall.transform.position = p.LeftBeamFrom;
                    float charge = Mathf.Clamp01((p.LeftBeamTo - p.LeftBeamFrom).magnitude);
                    remoteChargeBall.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.28f, charge);
                }
            }
        }

        void TickRemoteBeams(bool show)
        {
            PlayerSnapshot p = remote.Latest;
            Color color = p.Color;
            TickRemoteGear(show);
            for (int i = 0; i < 2; i++)
            {
                var kind = (HandBeam)(show ? (i == 0 ? p.LeftBeam : p.RightBeam) : 0);
                Vector3 from = i == 0 ? p.LeftBeamFrom : p.RightBeamFrom;
                Vector3 to = i == 0 ? p.LeftBeamTo : p.RightBeamTo;
                if (kind == HandBeam.Laser || kind == HandBeam.Blade)
                {
                    if (remoteBeams[i] == null || remoteBeamColor != color)
                    {
                        for (int k = 0; k < 2; k++)
                            if (remoteBeams[k] != null)
                                Destroy(remoteBeams[k].gameObject);
                        remoteBeamColor = color;
                        remoteBeams[0] = game.MakeLaserBeam(transform, color);
                        remoteBeams[1] = game.MakeLaserBeam(transform, color);
                    }
                    // The beam fades out by itself the first frame it isn't aimed.
                    Vector3 along = to - from;
                    remoteBeams[i].Aim(from, to, kind == HandBeam.Laser, along.sqrMagnitude > 1e-6f ? -along.normalized : Vector3.up);
                }
                if (kind == HandBeam.Flames)
                {
                    remoteFlames[i] ??= Flamethrower.Create(transform);
                    Vector3 along = to - from;
                    if (along.sqrMagnitude > 1e-6f)
                        remoteFlames[i].Pour(from, along.normalized, color);
                }
                else
                    remoteFlames[i]?.Stop();
                if (kind == HandBeam.Lightning)
                {
                    remoteLightning ??= CreateRemoteLightning();
                    remoteLightning[i].ShowBolt(from, to);
                }
                else
                    remoteLightning?[i].Hide();
            }
        }

        ChainLightning[] CreateRemoteLightning()
        {
            GameObject ball = game.MakeEnergyBall(transform);
            LineRenderer arc = ball.GetComponentInChildren<LineRenderer>(true);
            Material material = arc != null ? arc.sharedMaterial : Resources.Load<Material>("AimLine");
            Destroy(ball);
            Color bolt = new Color(0.6f, 0.8f, 1f);
            return new[] { new ChainLightning(transform, material, bolt), new ChainLightning(transform, material, bolt) };
        }

        // ------------------------------------------------------------------ their auras

        GameObject remoteAura;
        int remoteAuraStyle = -1;
        Renderer[] remoteAuraRenderers;
        float remoteAuraPower;
        MaterialPropertyBlock remoteAuraBlock;
        ElementAura remoteElementAura;
        static readonly int AuraPowerId = Shader.PropertyToID("_AuraPower");
        static readonly int AuraTintId = Shader.PropertyToID("_AuraTint");
        static readonly int AuraFootId = Shader.PropertyToID("_AuraFoot");
        static readonly int AuraFloorHiddenId = Shader.PropertyToID("_AuraFloorHidden");

        /// <summary>
        /// The other player's aura: a copy of the same aura model standing where they stand, with its strength and
        /// colour set on the copy alone (the real one's are shared values), plus their wind or stone aura.
        /// </summary>
        void TickRemoteAura(bool show, float dt)
        {
            PlayerSnapshot p = remote.Latest;
            var id = (CharacterId)Mathf.Clamp(p.Character, 0, CharacterDef.All.Length - 1);
            CharacterDef def = CharacterDef.Get(id);
            int level = Mathf.Clamp(p.Level, 1, 3);
            int style = show && !p.Dead ? def.AuraStyle[level - 1] : 0;

            remoteAuraPower = Mathf.MoveTowards(remoteAuraPower, style > 0 ? 1f : 0f, dt / (style > 0 ? 0.3f : 0.5f));
            if (style > 0 && style - 1 != remoteAuraStyle)
            {
                if (remoteAura != null)
                    Destroy(remoteAura);
                remoteAura = null;
                remoteAuraStyle = style - 1;
                GameObject source = game.Aura.StyleObject(remoteAuraStyle);
                if (source != null)
                {
                    remoteAura = new GameObject("Remote Aura");
                    remoteAura.transform.SetParent(transform, false);
                    GameObject copy = Instantiate(source, remoteAura.transform, false);
                    copy.SetActive(true);
                    remoteAuraRenderers = copy.GetComponentsInChildren<Renderer>(true);
                }
            }
            if (remoteAura != null)
            {
                bool visible = remoteAuraPower > 0f;
                if (remoteAura.activeSelf != visible)
                    remoteAura.SetActive(visible);
                if (visible)
                {
                    Vector3 at = remote.ShownHead;
                    remoteAura.transform.position = new Vector3(at.x, 0f, at.z);
                    Color tint = id == CharacterId.KiFighter && level >= 3 ? Hex("#3FA8FF") : (Color)p.Color;
                    remoteAuraBlock ??= new MaterialPropertyBlock();
                    foreach (Renderer renderer in remoteAuraRenderers)
                    {
                        if (renderer == null)
                            continue;
                        renderer.GetPropertyBlock(remoteAuraBlock);
                        remoteAuraBlock.SetFloat(AuraPowerId, remoteAuraPower);
                        remoteAuraBlock.SetVector(AuraTintId, (Vector4)tint.linear);
                        remoteAuraBlock.SetVector(AuraFootId, remoteAura.transform.position);
                        remoteAuraBlock.SetFloat(AuraFloorHiddenId, id == CharacterId.Saber && level == 2 ? 1f : 0f);
                        renderer.SetPropertyBlock(remoteAuraBlock);
                    }
                }
            }

            remoteElementAura ??= new ElementAura(transform, rockMaterial, rockGold);
            bool element = show && !p.Dead;
            remoteElementAura.Show(element ? id : CharacterId.Technoman, element ? level : 1, remote.ShownHead, FloorY, dt);
        }

        /// <summary>For tests: whether the other player's beam on this hand, their aura copy and their shield show.</summary>
        public bool TestRemoteBeam(int hand) => remoteBeams[hand] != null && remoteBeams[hand].GetComponentInChildren<Renderer>() != null
            && remoteBeams[hand].GetComponentInChildren<Renderer>().enabled && remoteBeams[hand].GetComponentInChildren<Renderer>().gameObject.activeInHierarchy;
        public bool TestRemoteAura => remoteAura != null && remoteAura.activeSelf && remoteAuraPower > 0.5f;

        /// <summary>Per frame: the other player's beams and auras.</summary>
        void TickRemoteLooks(float dt)
        {
            bool show = phase == Phase.Playing && Multiplayer && remote.Visible;
            TickRemoteBeams(show);
            TickRemoteAura(show, dt);
        }
    }
}
