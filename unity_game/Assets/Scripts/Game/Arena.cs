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
            game = owner;
            leftHand = left;
            rightHand = right;
            head = owner.Head;
            eye = owner.Origin.Camera;

            hud = new PlayerHud(transform, head);
            hud.Visible = false;
            shield = new ShieldView(transform);
            death = new DeathView(eye);
            hurtFlash = new HurtFlash(transform, head);
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
            player.Shield.Shattered += () => Log("Shield broke");
            game.Dealt += OnDealt;
            game.Killed += OnKilled;

            ShowTitle();
#if DEVELOPMENT_BUILD
            Tuning.WriteCurrent();
#endif
            Log("Ready");
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            input.Tick(game.Hands, game.TrackingSpace, dt);
            body = Stance.From(head.position, head.forward, game.Origin.transform.position.y);

#if DEVELOPMENT_BUILD
            Tuning.Tick();
#endif
            TickMenu(dt);
            TickNet(dt);

            if (pauseGesture.Step(input.Left, input.Right, dt) && (phase == Phase.Playing || phase == Phase.Sandbox))
            {
                if (paused)
                    Resume();
                else
                    Pause();
            }

            if (phase == Phase.Playing && !paused)
                TickMatch(Time.deltaTime);
            UpdateRemoteTarget();

            bool playing = phase == Phase.Playing;
            // The HUD lab shows the real panel outside a match too.
            hud.Tick(playing || labItem == 4 ? player : null, Character, dt);
            shield.Tick(playing && player.Shield.Up, head.position, PlayerColor, PickedShield(character), dt);
            TickVariantLab(dt);
            death.Tick(playing && player.IsDead, dt);
            hurtFlash.Tick(dt);
            TickGestureLab(dt);
            UpdateArms();
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
                        hud.Message($"WAVE {waves.Wave}", waves.Wave == 1 ? "FIGHT!" : $"of {waves.WaveCount}", 1.6f);
                        Log($"Wave {waves.Wave} starts");
                    }
                    break;
                case WaveState.Between:
                    hud.Message($"WAVE {waves.Wave} CLEARED", $"Next wave in {Mathf.CeilToInt(waves.Timer)}", 0.2f);
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
            player.Reset();
            waves = new WaveDirector(settings.Waves);
            if (Pvp)
                StartPvp();
            announcedWave = -1;
            matchTime = 0f;
            nextPowerup = Random.Range(20f, 30f);
            noFurnitureHint = 0f;
            smokeReady = 0f;
            phase = Phase.Playing;
            paused = false;
            Time.timeScale = 1f;
            HideMenu();
            hud.Visible = true;
            UpdateAura();
            Log($"Match starts: {mode}, {Character.Name}, colour #{ColorUtility.ToHtmlStringRGB(PlayerColor)}, lives {settings.Lives}");
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
            if (mode == GameMode.PveSolo || phase == Phase.Sandbox)
                Time.timeScale = 0f;
            ShowPause();
            Log("Paused");
        }

        void Resume()
        {
            paused = false;
            Time.timeScale = 1f;
            HideMenu();
            Log("Resumed");
        }

        public void EnterSandbox()
        {
            CleanUpMatch();
            phase = Phase.Sandbox;
            game.Legacy = true;
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

        void SpawnEnemy()
        {
            EnemyKind kind = Random.value < WaveDirector.DroneShare(Mathf.Max(1, waves.Wave)) ? EnemyKind.Drone : EnemyKind.Robot;
            SpawnEnemy(kind);
        }

        void SpawnEnemy(EnemyKind kind)
        {
            // Somewhere ahead-ish, 2.5-4 m away, kept on this side of walls and furniture.
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            Vector3 direction = Quaternion.AngleAxis(Random.Range(-70f, 70f), Vector3.up) * forward;
            // Out of the wall in that direction if there's one within reach, so they seem to step out of your room.
            float distance = Random.Range(2.5f, 4f);
            if (game.Room.TryDistance(head.position, direction, out float wall) && wall < 6f)
                distance = Mathf.Clamp(wall - 0.5f, 1.5f, 5f);
            Vector3 point = head.position + direction * distance;
            point.y = kind == EnemyKind.Robot ? 0.65f : head.position.y + 0.2f;
            Enemy enemy = Enemy.Spawn(kind, point, this, head, foes, ++nextEnemyId);
            enemies.Add(enemy);
            OverheadBar.Attach(enemy, head, foes, false);
            game.PlayAt(point, spawnSound);
            Log($"{kind} spawned {distance:0.0} m away");
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

        /// <summary>An enemy shot. It stops at the first real surface along its path.</summary>
        public void FireEnemyBolt(Vector3 muzzle, Vector3 direction, Enemy shooter)
        {
            float range = game.Room.TryDistance(muzzle, direction, out float wall) ? wall : 12f;
            EnemyBolt.Launch(muzzle, direction, range, this, head, foes);
            if (Multiplayer && net.Connected)
                net.SendEnemyBolt(frame.ToShared(muzzle), frame.DirectionToShared(direction));
            game.PlayAt(muzzle, enemyShotSound);
        }

        void OnDealt(IHittable target, float amount)
        {
            if (phase == Phase.Playing && !applyingRemoteHit)
                player.DealtDamage(amount);
        }

        void OnKilled(IHittable target)
        {
            if (phase == Phase.Playing && enemies.Remove(target))
                waves.EnemyKilled();
            if (target is Enemy enemy && enemy.IsProxy)
                ForgetProxy(enemy.Id);
        }

        /// <summary>Damage to the local player, from enemies, other players or the debug menu.</summary>
        public void HurtPlayer(float amount, string source)
        {
            if (phase != Phase.Playing)
                return;
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
            TransformWave.Spawn(head.position + Vector3.down * TransformShake, PlayerColor, transform);
            game.PlayAt(head.position, powerUpSound);
            game.PlayAt(head.position, game.BoomSound);
            // Tell the player what the new level unlocked.
            hud.Message($"LEVEL {level}", Character.Controls[Mathf.Clamp(level - 1, 0, Character.Controls.Length - 1)][0], 2.5f);
            UpdateAura();
            Log($"Transformed to level {level}, HP {player.Hp:0}/{player.MaxHp:0}");
        }

        /// <summary>Lights or puts out your aura for your character and level.</summary>
        void UpdateAura()
        {
            int style = phase == Phase.Playing && !player.IsDead ? Character.AuraStyle[player.Level - 1] : 0;
            if (style <= 0)
            {
                game.Aura.Extinguish();
                return;
            }
            Color tint = character == CharacterId.KiFighter && player.Level >= 3 ? Hex("#3FA8FF") : PlayerColor;
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
            bool fighting = phase == Phase.Playing && !paused && !player.IsDead && !player.Shield.Up && !xArms.Held
                && waves != null && !CountingDown;
            game.Arm(fighting ? WeaponFor(character, player.Level) : Weapon.None);
            int level = phase == Phase.Playing ? player.Level : 1;
            game.LaserDps = Damage.LaserPerSecond * (1f + 0.4f * (level - 1));
            game.RepulsorDamage = Damage.Repulsor * (level >= 2 && character == CharacterId.KiFighter ? 1.3f : 1f);
            game.RepulsorAoe = character == CharacterId.Technoman && level >= 3 ? 0.8f : 0f;
            game.SetWeaponColor(phase == Phase.Playing ? AttackColor : RepulsorGame.DefaultWeaponColor);
            TickAbilities(fighting, paused ? 0f : Time.deltaTime);
        }

        static Weapon WeaponFor(CharacterId id, int level)
        {
            switch (id)
            {
                case CharacterId.Technoman:
                    return level >= 2 ? Weapon.LaserRepulsor : Weapon.Laser;
                case CharacterId.Saber:
                case CharacterId.Archer:
                case CharacterId.Spider:
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
            player.FillMeter();
            player.TryTransform();
        }

        public void TestSandbox() => EnterSandbox();

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
