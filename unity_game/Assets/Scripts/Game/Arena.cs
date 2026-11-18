using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    public enum Phase { Title, Modes, Lobby, Characters, Playing, Results, Sandbox }

    /// <summary>
    /// The real game, on top of the old test game's weapons: menus, the match (waves, HP, meter, levels,
    /// lives), the shield, transformations and the HUD. Single player runs as a host with nobody joined.
    /// Lines tagged [Arena] go to the headset log.
    /// </summary>
    public sealed partial class Arena : MonoBehaviour
    {
        const float TransformShake = 0.4f;

        RepulsorGame game;
        HandRepulsor leftHand;
        HandRepulsor rightHand;
        Transform head;
        Camera eye;

        readonly HandInput input = new HandInput();
        readonly XArmsDetector xArms = new XArmsDetector();
        readonly DoubleOkDetector pauseGesture = new DoubleOkDetector();
        readonly PowerUpDetector powerUp = new PowerUpDetector();
        readonly MatchSettings settings = new MatchSettings();
        readonly List<IHittable> enemies = new List<IHittable>();
        Transform foes;
        Powerup powerup;
        float nextPowerup;

        Fighter player;
        WaveDirector waves;
        GameMode mode = GameMode.PveSolo;
        CharacterId character = CharacterId.Technoman;
        Color chosenColor;
        bool customColor;
        PlayerHud hud;
        ShieldView shield;
        DeathView death;
        HurtFlash hurtFlash;
        RoomLight roomLight;
        float lightSampleIn;
        float lightLogged = -1f;
        DamageNumbers damageNumbers;
        AudioClip enemyShotSound;
        AudioClip spawnSound;
        AudioClip chimeSound;
        AudioClip powerUpSound;
        AudioClip hurtSound;
        Phase phase;
        bool paused;
        int announcedWave = -1;
        float matchTime;
        Stance body;

        public Phase Phase => phase;
        public CharacterDef Character => CharacterDef.Get(character);
        public Color PlayerColor => customColor ? chosenColor : Hex(Character.Color);
        public HandInput Input => input;

        public void Init(RepulsorGame owner, HandRepulsor left, HandRepulsor right, RoomDim roomDim)
        {
            GameLog.Start();
            FxSpin.Eye = owner.Head;
            game = owner;
            leftHand = left;
            rightHand = right;
            head = owner.Head;
            eye = owner.Origin.Camera;

            hud = new PlayerHud(transform, head);
            hud.Visible = false;
            music = MusicPlayer.Create(transform);
            // Your own shield isn't hidden by live depth: your crossed arms are always in front of it.
            shield = new ShieldView(transform) { Occlude = false };
            death = new DeathView(eye);
            hurtFlash = new HurtFlash(transform, head);
            roomLight = new RoomLight(transform);
            damageNumbers = new DamageNumbers(transform, head);
            enemyShotSound = Sfx.EnemyShot();
            spawnSound = Sfx.Spawn();
            chimeSound = Sfx.Chime();
            powerUpSound = Sfx.PowerUp();
            hurtSound = Sfx.Hurt();
            foes = new GameObject("Enemies").transform;
            foes.SetParent(transform, false);
            CreateMenu();
            CreateGestureLab();
            CreateAbilityViews();
            CreateElementViews();
            CreateNet();
            CreatePvp();

            player = new Fighter(settings.Lives, settings.RespawnSeconds);
            player.Died += OnDied;
            player.Damaged += lost =>
            {
                hurtFlash.Hit(lost);
                game.PlayAt(head.position + head.forward * 0.3f, hurtSound);
            };
            player.Respawned += OnRespawned;
            player.Transformed += OnTransformed;
            player.Shield.Shattered += () =>
            {
                Log("Shield broke");
                shield.Shatter();
                hud.Message("SHIELD BROKEN", $"Back in {ShieldGauge.BreakCooldown:0} s. Let go of the X to save it next time", 1.6f);
                game.PlayAt(head.position, game.BoomSound);
            };
            player.Shield.Raised += () => Log($"Shield up, {PickedShield(character)}");
            game.Dealt += OnDealt;
            game.Killed += OnKilled;
            game.HandAim = (left, palm, palmDirection) => Aim.Arm(body, left ? input.Left : input.Right, palm, palmDirection);
            game.AssistAim = Aim.Assist;

            ShowTitle();
#if DEVELOPMENT_BUILD
            Tuning.WriteCurrent();
#endif
            Log("Ready");
        }

        Vector3 lastHead;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            input.Tick(game.Hands, game.TrackingSpace, dt);
            body = Stance.From(head.position, head.forward, game.Origin.transform.position.y);
            // Diagnostic: a head can't move 30 cm in a frame, so a jump means tracking re-found its position.
            if (lastHead != Vector3.zero && (head.position - lastHead).sqrMagnitude > 0.09f)
            {
                Log($"Head jumped {(head.position - lastHead).magnitude:0.00} m: tracking re-found its position; room map cleared");
                roomMap.Clear();
            }
            lastHead = head.position;

#if DEVELOPMENT_BUILD
            Tuning.Tick();
#endif
            TickMenu(dt);
            TickNet(dt);
            TickRoomMap();
            TickScan();

            if (pauseGesture.Step(input.Left, input.Right, dt) && (phase == Phase.Playing || phase == Phase.Sandbox))
            {
                if (paused && MultiplayerMatch)
                {
                    // In multiplayer the gesture says you're ready (both must be); again to take it back.
                    pauseReady = !pauseReady;
                    ShowPause();
                }
                else if (paused)
                    Resume();
                else
                    Pause();
            }

            if (phase == Phase.Playing && !paused)
            {
                TickMatch(Time.deltaTime);
                DrainHeldAttacks(Time.deltaTime);
            }
            UpdateRemoteTarget();

            bool playing = phase == Phase.Playing;
            // The HUD lab shows the real panel outside a match too.
            hud.Tick(playing || HudLabOpen ? player : null, Character, dt);
            Vector3 shieldFacing = character == CharacterId.Archer ? head.forward : Vector3.zero;
            shield.UseModel(character == CharacterId.Archer ? "KnightShield" : null);
            shield.Tick((playing || practicing) && player.Shield.Up, head.position, shieldFacing, PlayerColor, PickedShield(character), dt);
            TickVariantLab(dt);
            death.Tick(playing && player.IsDead, dt);
            ShowBossBar();
            TrackStanding(dt);
            TickRoomLight(Time.unscaledDeltaTime);
            if (phase == Phase.Playing)
                TickBuried(dt);
            hurtFlash.Tick(dt);
            damageNumbers.Tick(dt);
            TickGestureLab(dt);
            UpdateArms();
            LogPerformance(dt);
            TickHandAura();
            TickShowcase(Time.unscaledDeltaTime);
            music?.Play(WantedMusic(), paused);
        }

        MusicPlayer music;

        /// <summary>
        /// The soundtrack for where you are: the menus (title, modes, character select, results); fighting the
        /// robots; TRILOBITE's round; GOLIATH's round; a PVP match. Paused, it carries on quieter.
        /// </summary>
        MusicTrack WantedMusic()
        {
            if (phase != Phase.Playing)
                return MusicTrack.Menu;
            if (Pvp)
                return MusicTrack.Pvp;
            if (boss != null && boss.Alive)
                return boss.Kind == EnemyKind.Mech ? MusicTrack.FinalBoss : MusicTrack.Boss;
            if (waves != null && waves.BossWave && waves.State == WaveState.Fighting)
                return waves.Boss == EnemyKind.Mech ? MusicTrack.FinalBoss : MusicTrack.Boss;
            return MusicTrack.Gameplay;
        }

#if UNITY_EDITOR
        /// <summary>For tests: the track playing now.</summary>
        public MusicTrack TestMusic => music != null ? music.Playing : MusicTrack.None;
#endif

        readonly GameObject[] fistGlow = new GameObject[2];

        /// <summary>While posing to transform: energy gathering in both fists, growing until it goes off.</summary>
        void ShowPowerUpCharge(float progress)
        {
            hud.PowerUpProgress = progress;
            for (int i = 0; i < 2; i++)
            {
                HandPose hand = i == 0 ? input.Left : input.Right;
                bool show = progress > 0.02f && hand.Tracked;
                if (show && fistGlow[i] == null)
                    fistGlow[i] = game.MakeEnergyBall(transform);
                if (fistGlow[i] == null)
                    continue;
                fistGlow[i].SetActive(show);
                if (!show)
                    continue;
                fistGlow[i].transform.position = hand.Palm;
                fistGlow[i].transform.localScale = Vector3.one * Mathf.Lerp(0.03f, 0.16f, progress);
            }
        }

        float perfTimer;
        int perfFrames;
        float slowestFrame;

        /// <summary>Every 10 s in a match: frame rate, the slowest frame, and how much is going on. For reading after a test.</summary>
        void LogPerformance(float dt)
        {
            if (phase != Phase.Playing || paused)
            {
                perfTimer = perfFrames = 0;
                slowestFrame = 0f;
                return;
            }
            perfTimer += dt;
            perfFrames++;
            slowestFrame = Mathf.Max(slowestFrame, dt);
            if (perfTimer < 10f)
                return;
            Debug.Log($"[Perf] {perfFrames / perfTimer:0.0} FPS, slowest {slowestFrame * 1000f:0} ms, {enemies.Count} enemies, "
                + $"{Hittables.All.Count} hittable, {foes.childCount} effects, aura {(game.Aura.IsLit ? "on" : "off")}, {character} L{player.Level}");
            perfTimer = 0f;
            perfFrames = 0;
            slowestFrame = 0f;
        }

        void OnDestroy() => net?.Close();

        void OnApplicationPause(bool paused)
        {
            if (paused)
                Log("App paused");
        }

        void LateUpdate()
        {
            // Your own aura stays around you.
            if (phase == Phase.Playing && game.Aura.IsLit)
                game.Aura.MoveTo(head.position);
        }

        void TickMatch(float dt)
        {
            matchTime += dt;
            xArms.Step(input.Left, input.Right, body, dt);
            player.Shield.Hold(xArms.Held && !player.IsDead);
            player.Tick(dt);
            game.DamageScale = player.DamageMultiplier;

            if (powerUp.Step(input.Left, input.Right, body, dt) && player.CanTransform)
                player.TryTransform();
            ShowPowerUpCharge(player.CanTransform && powerUp.Posing ? powerUp.Progress : 0f);
            SyncHeld(dt);

            // In co-op the host runs the waves and enemies; the client mirrors them (Arena.Net).
            bool client = Multiplayer && net.Role == NetRole.Client;
            if (Pvp)
            {
                TickPvp(dt);
                TickPowerups();
                return;
            }
            if (!client)
            {
                waves.Tick(dt);
                if (settings.EnemiesEnabled)
                {
                    while (waves.TakeSpawn())
                        SpawnEnemy();
                }
                if (player.IsOut && RemoteOut)
                    waves.AllPlayersOut();
            }

            TickPowerups();
            TickDrops();
            TickBalance();
            Announce();
            if (waves.State == WaveState.Won || waves.State == WaveState.Lost)
                EndMatch(waves.State == WaveState.Won);
        }

        void Announce()
        {
            if (player.IsDead)
            {
                hud.Message("KNOCKED OUT", player.Lives > 0 ? $"Back in {Mathf.CeilToInt(player.RespawnIn)}" : "No lives left", 0.2f);
                return;
            }

            switch (waves.State)
            {
                case WaveState.Countdown:
                    // The countdown doubles as a reminder of how to attack.
                    hud.Message(Mathf.CeilToInt(waves.Timer).ToString(), Character.Controls[0][0], 0.2f);
                    break;
                case WaveState.Fighting:
                    if (announcedWave != waves.Wave)
                    {
                        announcedWave = waves.Wave;
                        // A kind's first round introduces it.
                        List<EnemyKind> fresh = WavePlan.NewIn(waves.Wave);
                        if (waves.FinalBossWave)
                            hud.Message("FINAL ROUND", "The boss is coming", 2f);
                        else if (waves.BossWave)
                            hud.Message($"ROUND {waves.Wave}: BOSS", EnemySpec.Of(waves.Boss).Name + " is coming", 2f);
                        else if (fresh.Count > 0 && !bossFight)
                            hud.Message($"ROUND {waves.Wave}", $"NEW ENEMY: {EnemySpec.Of(fresh[0]).Name}. {EnemySpec.Of(fresh[0]).Blurb}", 3.5f);
                        else
                            hud.Message($"ROUND {waves.Wave}", waves.Wave == 1 ? "FIGHT!" : $"of {waves.WaveCount}", 1.6f);
                        Log($"Round {waves.Wave} starts{(fresh.Count > 0 ? ", new: " + string.Join(", ", fresh) : "")}");
                    }
                    break;
                case WaveState.Between:
                    hud.Message($"ROUND {waves.Wave} CLEARED", $"Next round in {Mathf.CeilToInt(waves.Timer)}", 0.2f);
                    break;
            }
        }

        public void StartMatch()
        {
            game.Legacy = false;
            game.ClearOrbs();
            enemies.Clear();
            Ui.Clear(foes);
            player.StartingLives = settings.Lives;
            player.RespawnSeconds = settings.RespawnSeconds;
            // The host's (or solo player's) pick; a co-op client got the host's with the start.
            if (!(Multiplayer && net.Role == NetRole.Client))
                settings.Difficulty = PickedDifficulty;
            ApplyBalance();
            player.Reset();
            waves = new WaveDirector(settings.Waves, bossFight ? settings.Waves : 1, Random.Range(0, int.MaxValue));
            boss = null;
            // PVP: the shield gauge is also attack energy, and a little bigger (Arena.Energy).
            player.Shield.Capacity = Pvp ? PvpGaugeSeconds : ShieldGauge.HoldSeconds;
            player.Shield.Reset();
            if (Pvp)
                StartPvp();
            announcedWave = -1;
            matchTime = 0f;
            nextPowerup = Random.Range(20f, 30f);
            noFurnitureHint = 0f;
            smokeReady = 0f;
            phase = Phase.Playing;
            paused = false;
            pauseReady = false;
            // Readiness was for this start; character select asks again.
            localReady = remoteReady = false;
            Time.timeScale = 1f;
            HideMenu();
            hud.Visible = true;
            UpdateAura();
            Log($"Match starts: {mode}{(bossFight ? " (boss fight only)" : "")}, {Character.Name}, colour #{ColorUtility.ToHtmlStringRGB(PlayerColor)}, lives {settings.Lives}, {room.Count} furniture boxes from Space Setup");
        }

        void EndMatch(bool won)
        {
            Log($"Match over: {(won ? "won" : "lost")}, wave {waves.Wave}, kills {waves.Kills}, "
                + $"dealt {player.DamageDealt:0}, taken {player.DamageTaken:0}, {matchTime:0} s");
            phase = Phase.Results;
            if (Multiplayer && net.Role == NetRole.Host)
                net.SendEnd(won);
            CleanUpMatch();
            ShowResults(won);
        }

        void CleanUpMatch()
        {
            ShowPowerUpCharge(0f);
            boss = null;
            damageNumbers?.Clear();
            enemies.Clear();
            if (net != null)
                ClearProxies();
            if (foes != null)
                Ui.Clear(foes);
            if (game != null)
            {
                game.ClearOrbs();
                game.Aura.Extinguish();
            }
            hud.Visible = false;
            hud.ClearMessage();
            Time.timeScale = 1f;
            paused = false;
        }

        void Pause()
        {
            paused = true;
            pauseReady = false;
            sawRemotePause = false;
            // Everything stops, in multiplayer too (both headsets pause together, see TickMultiplayerPause).
            Time.timeScale = 0f;
            ShowPause();
            Log("Paused");
        }

        void Resume()
        {
            paused = false;
            pauseReady = false;
            sinceResume = 0f;
            Time.timeScale = 1f;
            HideMenu();
            Log("Resumed");
        }

        public void EnterSandbox()
        {
            CleanUpMatch();
            phase = Phase.Sandbox;
            game.Legacy = true;
            Shader.SetGlobalFloat("_AuraFloorHidden", 0f);
            HideMenu();
            Log("Old controls on");
        }

        void LeaveSandbox()
        {
            game.Legacy = false;
            game.ClearOrbs();
            game.Aura.Extinguish();
            Time.timeScale = 1f;
            paused = false;
            Log("Old controls off");
        }

        void ClearProxies()
        {
            proxies.Clear();
            killedAt.Clear();
        }

        void SpawnOrb()
        {
            Target orb = game.SpawnOrb(Damage.OrbHp);
            enemies.Add(orb);
            OverheadBar.Attach(orb, head, foes, false);
        }

        /// <summary>One powerup at a time, every 30-45 s, within 2 m of you. Touch it to take it.</summary>
        void TickPowerups()
        {
            if (powerup != null)
            {
                if (powerup.Expired)
                {
                    Destroy(powerup.gameObject);
                }
                else if (!player.IsDead && (powerup.Touched(input.Left) || powerup.Touched(input.Right)))
                {
                    player.ApplyPowerup(powerup.Kind);
                    hud.Message(Powerup.NameOf(powerup.Kind), "", 1.2f);
                    game.PlayAt(powerup.transform.position, chimeSound);
                    Log($"Powerup taken: {powerup.Kind}");
                    Destroy(powerup.gameObject);
                    powerup = null;
                }
                return;
            }
            if (matchTime < nextPowerup || waves.State == WaveState.Countdown)
                return;
            nextPowerup = matchTime + Random.Range(30f, 45f);
            SpawnPowerup((PowerupKind)Random.Range(0, 3));
        }

        void SpawnPowerup(PowerupKind kind)
        {
            if (powerup != null)
                Destroy(powerup.gameObject);
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            Vector3 direction = Quaternion.AngleAxis(Random.Range(-60f, 60f), Vector3.up) * forward;
            float distance = Random.Range(0.9f, 2f);
            if (game.Room.TryDistance(head.position, direction, out float wall))
                distance = Mathf.Clamp(wall - 0.3f, 0.6f, distance);
            Vector3 point = head.position + direction * distance;
            point.y = Mathf.Clamp(head.position.y - 0.35f, 0.8f, 1.5f);
            powerup = Powerup.Spawn(kind, point, foes);
            Log($"Powerup {kind} at {distance:0.0} m");
        }

        void OnDealt(IHittable target, float amount)
        {
            if ((phase == Phase.Playing || practicing) && !applyingRemoteHit)
            {
                player.DealtDamage(amount);
                damageNumbers.Add(target, amount);
            }
        }

        void OnKilled(IHittable target)
        {
            if (phase == Phase.Playing && enemies.Remove(target))
                waves.EnemyKilled();
            MaybeDrop(target);
            if (target is Enemy enemy && enemy.IsProxy)
                ForgetProxy(enemy.Id);
        }

        /// <summary>
        /// Damage to the local player, from enemies, other players or the debug menu. Enemy damage goes up by round and
        /// with the difficulty (Arena.Balance); <paramref name="fromEnemy"/> false for the rest.
        /// </summary>
        public void HurtPlayer(float amount, string source, bool fromEnemy = true)
        {
            if (phase != Phase.Playing)
                return;
            if (fromEnemy && !Pvp)
                amount *= enemyDamageScale;
            bool shieldUp = player.Shield.Up;
            float lost = player.TakeDamage(amount);
            if (shieldUp)
                shield.Flash();
            Log($"Player hit by {source}: {amount:0.0}, lost {lost:0.0}, HP {player.Hp:0}");
        }

        void OnDied()
        {
            game.Aura.Extinguish();
            game.PlayAt(head.position, game.BoomSound);
            Log($"Player knocked out, lives left {player.Lives}");
        }

        void OnRespawned()
        {
            hud.Message("BACK IN", "", 1f);
            UpdateAura();
            Log("Player back in");
        }

        void OnTransformed(int level)
        {
            // Archer and Mage send a flat ring (white for the Archer's wings, fire for the Mage); others a sphere.
            Vector3 waist = head.position + Vector3.down * TransformShake;
            if (character == CharacterId.Archer && level >= 3)
                TransformWave.SpawnRing(waist, Color.white, transform);
            else if (character == CharacterId.Mage)
                TransformWave.SpawnRing(new Vector3(waist.x, 0.1f, waist.z), new Color(1f, 0.45f, 0.1f), transform);
            else
                TransformWave.Spawn(waist, AttackColor, transform);
            game.PlayAt(head.position, powerUpSound);
            game.PlayAt(head.position, game.BoomSound);
            // Tell the player what the new level unlocked.
            hud.Message($"LEVEL {level}", LevelUpText(level), 2.5f);
            hud.PoweredUp();
            UpdateAura();
            Log($"Transformed to level {level}, HP {player.Hp:0}/{player.MaxHp:0}");
        }

        /// <summary>Lights or puts out your aura for your character and level.</summary>
        void UpdateAura()
        {
            int style = phase == Phase.Playing && !player.IsDead ? Character.AuraStyle[Tier - 1] : 0;
            if (style <= 0)
            {
                game.Aura.Extinguish();
                return;
            }
            // The Saber gets the light aura without its floor halo at level 2, and with it at level 3.
            Shader.SetGlobalFloat("_AuraFloorHidden", character == CharacterId.Saber && Tier == 2 ? 1f : 0f);
            Color tint = character == CharacterId.KiFighter && Tier >= 3 ? Hex("#3FA8FF") : PlayerColor;
            if (!game.Aura.IsLit)
                game.Aura.Ignite(head.position);
            game.Aura.ShowStyle(style - 1);
            game.Aura.SetTint(tint.linear, false);
        }

        /// <summary>What the hands can fire right now.</summary>
        void UpdateArms()
        {
            if (phase == Phase.Sandbox)
            {
                game.SetWeaponColor(RepulsorGame.DefaultWeaponColor);
                game.RepulsorAoe = 0f;
                return;
            }
            // Not while posing to transform either: opening the fists afterwards would fire into the floor.
            // The Variant Lab's practice counts as fighting, outside a match.
            bool inPlay = practicing || (phase == Phase.Playing && !paused && waves != null && !CountingDown);
            // Posing only disarms when the meter is full, or holding two fists low (Saber blades) would blink them out.
            bool fighting = inPlay && !player.IsDead && !player.Shield.Up && !xArms.Held && !(powerUp.Posing && player.CanTransform);
            game.Arm(fighting && handsBusyFor <= 0f ? WithEnergy(WeaponFor(character, Tier)) : Weapon.None);
            int level = phase == Phase.Playing || practicing ? Tier : 1;
            SetHandWeaponDamage(level);
            game.RepulsorAoe = character == CharacterId.Technoman && level >= 3 ? 0.8f : 0f;
            game.SetWeaponColor(phase == Phase.Playing || practicing ? AttackColor : RepulsorGame.DefaultWeaponColor);
            game.CometStyle = character == CharacterId.KiFighter ? PickedComet : 0;
            game.FlameLaser = character == CharacterId.Mage;
            TickAbilities(fighting, paused ? 0f : Time.deltaTime);
        }

        /// <summary>
        /// The hand weapons' damage at an ability level (1-3). PVP: the old numbers (lasers +40% a level, Ryuu's comets
        /// 1.3x from level 2). PVE (11/16): Technoman's laser per hand and his level 2 repulsor blast, the Emberwitch's
        /// flames per hand, everyone else's snap blast, each at its level multiplier (Arena.Balance).
        /// </summary>
        void SetHandWeaponDamage(int tier)
        {
            game.RepulsorSplash = Pvp ? 0.6f : Balance.BlastSplash;
            if (Pvp)
            {
                game.LaserDps = Damage.LaserPerSecond * (1f + 0.4f * (tier - 1));
                game.RepulsorDamage = Damage.Repulsor * (tier >= 2 && character == CharacterId.KiFighter ? 1.3f : 1f);
                return;
            }
            // Flames are the hand laser drawn as fire, at Flamethrower.DamageScale times the laser.
            game.LaserDps = character == CharacterId.Mage
                ? Dmg(0f, Balance.FlamesPerHand, 2) / Flamethrower.DamageScale
                : Dmg(0f, Balance.LaserPerHand, 1);
            game.RepulsorDamage = character == CharacterId.Technoman ? Dmg(0f, Balance.RepulsorBlast, 2) : Dmg(0f, Balance.Snap, 1);
        }

        static Weapon WeaponFor(CharacterId id, int level)
        {
            switch (id)
            {
                case CharacterId.Technoman:
                    return level >= 2 ? Weapon.LaserRepulsor : Weapon.Laser;
                case CharacterId.Mage:
                    // Fireballs from level 1; the flamethrower (palm out and hold) joins them at level 2.
                    return level >= 2 ? Weapon.LaserRepulsor : Weapon.Repulsor;
                case CharacterId.Earth:
                    // Rock orbs: the repulsor's fist-then-open, dressed as rocks (Arena.Elements).
                    return Weapon.Repulsor;
                case CharacterId.Saber:
                case CharacterId.Archer:
                case CharacterId.Spider:
                case CharacterId.Elementalist:
                    // Their attacks are all gestures in Arena.Abilities.
                    return Weapon.None;
                default:
                    // Until each character's own attacks are built, everyone else gets blasts.
                    return Weapon.Repulsor;
            }
        }

        static BubbleStyle ShieldStyle(CharacterId id)
        {
            switch (id)
            {
                case CharacterId.KiFighter: return BubbleStyle.Flame;
                case CharacterId.Saber: return BubbleStyle.Glass;
                case CharacterId.Mage: return BubbleStyle.Thin;
                case CharacterId.Spider: return BubbleStyle.Web;
                case CharacterId.Elementalist: return BubbleStyle.Wind;
                case CharacterId.Earth: return BubbleStyle.Rock;
                case CharacterId.Archer: return BubbleStyle.Glass;   // a clear shield, since it sits right in front of you
                default: return BubbleStyle.Hex;
            }
        }

#if UNITY_EDITOR
        /// <summary>PlayMode smoke test: start a solo match as this character.</summary>
        public void TestStart(CharacterId id)
        {
            CleanUpMatch();
            character = id;
            customColor = false;
            mode = GameMode.PveSolo;
            StartMatch();
        }

        /// <summary>PlayMode smoke test: jump the waves forward, e.g. past the countdown.</summary>
        public void TestSkipTime(float seconds)
        {
            waves.Tick(seconds);
            matchTime += seconds;
        }

        /// <summary>PlayMode smoke test: fill the meter and transform.</summary>
        public void TestAdvance()
        {
            // Hit every enemy once, as an attack would, so damage numbers and kills run too.
            foreach (IHittable foe in enemies.ToArray())
                if (foe is Object alive && alive != null && foe.Alive)
                    game.Strike(foe, 12f, foe.Center, "test");
            player.FillMeter();
            player.TryTransform();
        }

        public void TestSandbox() => EnterSandbox();

        /// <summary>PlayMode co-op test: host a co-op room on this port.</summary>
        public bool TestHost(int port, bool pvp = false)
        {
            mode = pvp ? GameMode.Pvp : GameMode.PveCoop;
            bool ok = net.Host(port);
            net.Mode = (byte)mode;
            phase = Phase.Lobby;
            return net.LocalPort > 0;
        }

        /// <summary>PlayMode co-op test: start the match as host without the ready handshake.</summary>
        public void TestStartHosting()
        {
            StartMatch();
            if (!Pvp)
                waves.Tick(WaveDirector.CountdownSeconds + 0.1f);
        }

        public float TestHp => player.Hp;

        /// <summary>PlayMode co-op test: join a host on this machine at this port.</summary>
        public bool TestJoin(int port)
        {
            mode = GameMode.PveCoop;
            phase = Phase.Characters;
            return net.Join(0, port, System.Net.IPAddress.Loopback);
        }

        public int TestEnemyCount => enemies.Count;

        /// <summary>PlayMode co-op test: hit the first enemy as an attack would.</summary>
        public void TestHitFirstEnemy(float damage)
        {
            foreach (IHittable foe in enemies)
            {
                if (foe is Object alive && alive != null && foe.Alive)
                {
                    game.Strike(foe, damage, foe.Center, "test");
                    return;
                }
            }
        }

        public bool TestPartnerVisible => remote.Visible;
        public bool TestPartnerConnected => net.Connected;

        /// <summary>PlayMode co-op test: the HP of the enemy with this id, or -1.</summary>
        public float TestEnemyHp(ushort id)
        {
            foreach (IHittable foe in enemies)
                if (foe is Enemy enemy && enemy != null && enemy.Id == id)
                    return enemy.Hp;
            return -1f;
        }

        /// <summary>PlayMode test: open the Variant Lab on one item from the title screen's debug menu.</summary>
        public void TestOpenLab(int item)
        {
            ShowTitle();
            ShowDebug(ShowTitle);
            labItem = item;
            ShowVariantLab();
        }

        /// <summary>PlayMode test: the bodies of the enemies standing in the lab's ENEMIES page, as "Look X, Look Y".</summary>
        public string TestLabEnemyLooks() => string.Join(", ", System.Linq.Enumerable.Select(labEnemies,
            e => e == null ? "none" : e.GetComponentInChildren<EnemyLook>(true) is EnemyLook look ? look.name : "placeholder"));

        /// <summary>
        /// PlayMode test: webs the first enemy that's ready, as a level 2 Spider's web would, and flings it at the
        /// floor. Returns its id and HP, or id 0 if no enemy was ready.
        /// </summary>
        public (ushort id, float hp) TestWebAndFling(bool byForce = false)
        {
            foreach (IHittable foe in enemies)
            {
                if (!(foe is Enemy enemy) || enemy == null || !enemy.Alive || enemy.Spawning || enemy.IsBoss)
                    continue;
                float hp = enemy.Hp;
                WebEnemy(enemy, 1, byForce);
                Webbed thrown = swinging;
                swinging = null;
                thrown.Velocity = Vector3.down * 3f;
                flung.Add(thrown);
                return (enemy.Id, hp);
            }
            return (0, 0f);
        }

        /// <summary>PlayMode test: grabs this enemy with the force, as Lumen's cupped hand does, and keeps holding it.</summary>
        public void TestForceGrab(Enemy enemy) => WebEnemy(enemy, 1, true);

        /// <summary>PlayMode test: whether a web or the force aimed straight at this enemy from your head would grab it.</summary>
        public bool TestCanGrab(Enemy enemy)
        {
            // Aimed straight at it, with every other enemy set aside (one wandering into the line would be picked instead).
            var others = new List<IHittable>();
            foreach (IHittable foe in Hittables.All)
                if (!ReferenceEquals(foe, enemy) && foe.Team == Team.Enemies)
                    others.Add(foe);
            foreach (IHittable foe in others)
                Hittables.Remove(foe);
            bool can = Grabbable(head.position, (enemy.Center - head.position).normalized) == enemy;
            foreach (IHittable foe in others)
                Hittables.Add(foe);
            return can;
        }

        /// <summary>PlayMode test: anything still held or flying in a web.</summary>
        public bool TestAnyWebbed => swinging != null || flung.Count > 0;

        /// <summary>PlayMode test: leave the lab the way its BACK button does.</summary>
        public void TestLeaveLab() => ShowDebug(debugBack);

        /// <summary>PlayMode test: practice state, as "practicing character level weapon dummy".</summary>
        public string TestPracticeState() =>
            $"{practicing} {character} {player.Level} {game.ArmedWeapon} {(practiceDummy != null && practiceDummy.Alive ? "dummy" : "none")}";

        /// <summary>PlayMode smoke test: knock the player out, then run the clock past the respawn.</summary>
        public void TestKnockOutAndRespawn()
        {
            HurtPlayer(10000f, "test", false);
            player.Tick(settings.RespawnSeconds + 0.1f);
        }

        /// <summary>PlayMode smoke test: open every screen, every Variant Lab page, and fire each variant once.</summary>
        public void TestEveryScreen()
        {
            ShowTitle();
            ShowCredits();
            ShowModes();
            ShowCoop();
            ShowRooms(GameMode.Pvp);
            ShowJoin();
            ShowCharacters();
            debugBack = ShowModes;
            ShowDebug(ShowModes);
            for (int item = 0; item < LabItems.Length; item++)
            {
                labItem = item;
                ShowVariantLab();
            }
            for (int style = 0; style < DragonFist.StyleNames.Length; style++)
                DragonFist.Launch(game, style, head.position + head.forward, head.forward, 0f);
            for (int style = 0; style < FirePillar.StyleNames.Length; style++)
                FirePillar.Spawn(head.position + head.forward * 2f, PlayerColor, style, transform);
            labItem = 0;
            HideLabBubble();
            StartMatch();
            paused = true;
            ShowPause();
            ShowResults(true);
            ShowTitle();
        }

        /// <summary>Editor preview: just the menus and HUD around a fixed head, with no XR or weapons.</summary>
        public void InitPreview(Transform previewHead)
        {
            head = previewHead;
            hud = new PlayerHud(transform, head);
            CreateMenu();
            player = new Fighter(settings.Lives, settings.RespawnSeconds);
            waves = new WaveDirector(settings.Waves);
        }

        public Fighter PreviewPlayer => player;
        public WaveDirector PreviewWaves => waves;
        public PlayerHud PreviewHud => hud;

        public void PreviewScreen(string name)
        {
            switch (name)
            {
                case "title": ShowTitle(); break;
                case "modes": ShowModes(); break;
                case "characters": ShowCharacters(); break;
                case "pause": ShowPause(); break;
                case "debug": ShowDebug(null); break;
                case "results": ShowResults(true); break;
            }
        }
#endif

        public static Color Hex(string html) => ColorUtility.TryParseHtmlString(html, out Color color) ? color : Color.white;

        static void Log(string message) => Debug.Log("[Arena] " + message);
    }
}
