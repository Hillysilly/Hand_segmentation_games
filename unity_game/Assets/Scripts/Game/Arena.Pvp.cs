using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// PVP, and friendly fire in co-op. The other player is a target your attacks can hit; the victim's headset
    /// applies the damage (so their shield and invincibility count). In PVP a round ends when someone is knocked
    /// out; the host keeps score, best of 3, and levels reset each round.
    /// </summary>
    public sealed partial class Arena
    {
        const float RoundCountdown = 3f;

        RemoteTarget remoteTarget;
        RoundTracker rounds;
        float roundCountdown;
        bool remoteWasDead;
        bool roundOver;

        bool Pvp => mode == GameMode.Pvp;
        bool CountingDown => Pvp ? roundCountdown > 0f : waves != null && waves.State == WaveState.Countdown;

        /// <summary>The other player, as something your attacks can hit.</summary>
        sealed class RemoteTarget : IHittable
        {
            readonly Arena arena;

            public RemoteTarget(Arena owner) => arena = owner;

            PlayerSnapshot Latest => arena.remote.Latest;
            public Vector3 Center => Latest.Head + Vector3.down * 0.35f;
            public float Radius => 0.32f;
            public bool Alive => arena.remote.Visible && !Latest.Dead;
            public Team Team => Team.Enemies;
            public float Hp => Latest.Hp;
            public float MaxHp => Mathf.Max(1f, Latest.MaxHp);
            public Vector3 BarPoint => Latest.Head + Vector3.up * 0.3f;

            public float TakeHit(float damage, Vector3 from)
            {
                if (!Alive)
                    return 0f;
                arena.net.SendPlayerHit(damage);
                return Mathf.Min(damage, Hp);
            }
        }

        void CreatePvp()
        {
            remoteTarget = new RemoteTarget(this);
            net.PlayerHitReceived += damage =>
            {
                if (phase == Phase.Playing && !CountingDown)
                    HurtPlayer(damage, "other player");
            };
            net.RoundReceived += OnRound;
        }

        /// <summary>Whether the other player can be hit right now: PVP, or co-op with friendly fire on.</summary>
        void UpdateRemoteTarget()
        {
            bool hittable = phase == Phase.Playing && Multiplayer && net.Connected && !CountingDown
                && (Pvp || settings.FriendlyFire);
            if (hittable)
                Hittables.Add(remoteTarget);
            else
                Hittables.Remove(remoteTarget);
        }

        void StartPvp()
        {
            rounds = new RoundTracker(2, settings.RoundsToWin);
            BeginRound();
        }

        void BeginRound()
        {
            player.Reset();
            roundCountdown = RoundCountdown;
            roundOver = false;
            remoteWasDead = false;
            UpdateAura();
        }

        void TickPvp(float dt)
        {
            roundCountdown = Mathf.Max(0f, roundCountdown - dt);
            if (roundCountdown > 0f)
            {
                hud.Message(Mathf.CeilToInt(roundCountdown).ToString(), $"ROUND {rounds.Round}   {rounds.Wins(0)} - {rounds.Wins(1)}", 0.2f);
                return;
            }
            if (player.IsDead)
                hud.Message("KNOCKED OUT", "", 0.2f);

            // The host decides who won the round.
            if (net.Role != NetRole.Host || roundOver)
                return;
            bool remoteDead = remote.Visible && remote.Latest.Dead;
            int winner = -1;
            if (player.IsDead)
                winner = 1;
            else if (remoteDead && !remoteWasDead)
                winner = 0;
            remoteWasDead = remoteDead;
            if (winner < 0)
                return;

            roundOver = true;
            bool over = rounds.RoundWon(winner);
            net.SendRound((byte)rounds.Round, (byte)rounds.Wins(0), (byte)rounds.Wins(1), (byte)winner, over);
            OnRound((byte)rounds.Round, (byte)rounds.Wins(0), (byte)rounds.Wins(1), (byte)winner, over);
        }

        void OnRound(byte round, byte hostWins, byte clientWins, byte winner, bool over)
        {
            if (phase != Phase.Playing)
                return;
            bool iWon = winner == net.Slot;
            Log($"Round over: {(iWon ? "won" : "lost")}, {hostWins} - {clientWins}{(over ? ", match over" : "")}");
            if (net.Role == NetRole.Client)
            {
                rounds ??= new RoundTracker(2, settings.RoundsToWin);
            }
            if (over)
            {
                EndMatch(iWon);
                return;
            }
            hud.Message(iWon ? "ROUND WON" : "ROUND LOST", $"{hostWins} - {clientWins}", 2f);
            if (net.Role == NetRole.Client)
                rounds = RoundTracker.At(round, hostWins, clientWins, settings.RoundsToWin);
            BeginRound();
        }
    }
}
