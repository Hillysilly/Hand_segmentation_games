using System;

namespace Repulsor
{
    public enum GameMode { PveSolo, PveCoop, Pvp }

    /// <summary>Gameplay toggles. The host owns these; the pause menu changes them.</summary>
    [Serializable]
    public sealed class MatchSettings
    {
        public bool EnemiesEnabled = true;
        public bool PvpEnabled;
        public bool FriendlyFire;
        public int Lives = 3;
        public float RespawnSeconds = 5f;
        public int Waves = 5;
        public int RoundsToWin = 2; // best of 3

        public static MatchSettings For(GameMode mode) => new MatchSettings
        {
            EnemiesEnabled = mode != GameMode.Pvp,
            PvpEnabled = mode == GameMode.Pvp,
        };
    }

    public enum WaveState { Countdown, Fighting, Between, Won, Lost }

    /// <summary>
    /// PVE: a countdown, then waves that each bring more enemies, then a win. The game spawns what
    /// <see cref="TakeSpawn"/> hands out and reports kills; this class decides when the next wave starts.
    /// The last wave is the boss (one spawn; it calls in its own helpers, added with <see cref="AddExtra"/>).
    /// </summary>
    public sealed class WaveDirector
    {
        public const float CountdownSeconds = 3f;
        public const float BetweenWavesSeconds = 6f;
        public const float SpawnGapSeconds = 1.6f;
        public const int MaxAlive = 5;

        readonly int waveCount;
        float timer;
        float spawnTimer;

        public WaveDirector(int waves, int firstWave = 1)
        {
            waveCount = Math.Max(1, waves);
            State = WaveState.Countdown;
            timer = CountdownSeconds;
            // Starting later (the boss fight on its own): the countdown leads into that wave.
            Wave = Math.Max(0, Math.Min(waveCount, firstWave) - 1);
        }

        /// <summary>The last wave, with waves of two or more, is the boss.</summary>
        public bool BossWave => waveCount >= 2 && Wave == waveCount && State != WaveState.Countdown;

        public WaveState State { get; private set; }
        /// <summary>1-based wave number; 0 during the first countdown.</summary>
        public int Wave { get; private set; }
        public int WaveCount => waveCount;
        public int ToSpawn { get; private set; }
        public int Alive { get; private set; }
        public int Kills { get; private set; }
        /// <summary>Seconds left on the countdown or the break between waves.</summary>
        public float Timer => timer;

        public static int EnemiesInWave(int wave) => 2 + wave * 2;
        /// <summary>Of a wave's enemies, how many are drones rather than robots.</summary>
        public static float DroneShare(int wave) => Math.Min(0.6f, 0.15f * wave);

        public void Tick(float dt)
        {
            switch (State)
            {
                case WaveState.Countdown:
                case WaveState.Between:
                    timer -= dt;
                    if (timer <= 0f)
                        StartWave(Wave + 1);
                    break;
                case WaveState.Fighting:
                    spawnTimer -= dt;
                    if (ToSpawn == 0 && Alive == 0)
                    {
                        if (Wave >= waveCount)
                        {
                            State = WaveState.Won;
                        }
                        else
                        {
                            State = WaveState.Between;
                            timer = BetweenWavesSeconds;
                        }
                    }
                    break;
            }
        }

        /// <summary>True when the game should spawn one enemy now. The caller then reports it with this call.</summary>
        public bool TakeSpawn()
        {
            if (State != WaveState.Fighting || ToSpawn <= 0 || Alive >= MaxAlive || spawnTimer > 0f)
                return false;
            ToSpawn--;
            Alive++;
            spawnTimer = SpawnGapSeconds;
            return true;
        }

        /// <summary>An enemy added outside the wave plan, such as from the debug menu.</summary>
        public void AddExtra() => Alive++;

        public void EnemyKilled()
        {
            Alive = Math.Max(0, Alive - 1);
            Kills++;
        }

        /// <summary>Every player is out of lives.</summary>
        public void AllPlayersOut()
        {
            if (State != WaveState.Won)
                State = WaveState.Lost;
        }

        /// <summary>Co-op client: copies the host's wave state instead of running its own.</summary>
        public void Mirror(int wave, WaveState state, float timer, int kills)
        {
            Wave = wave;
            State = state;
            this.timer = timer;
            Kills = kills;
            ToSpawn = 0;
        }

        /// <summary>Debug: ends the current wave by clearing what's left to spawn.</summary>
        public void SkipWave()
        {
            ToSpawn = 0;
            if (State == WaveState.Countdown || State == WaveState.Between)
                timer = 0f;
        }

        void StartWave(int wave)
        {
            Wave = wave;
            ToSpawn = waveCount >= 2 && wave == waveCount ? 1 : EnemiesInWave(wave);
            spawnTimer = 0f;
            State = WaveState.Fighting;
        }
    }

    /// <summary>A PVE run's score: kills and waves, damage, and a win bonus that's bigger the faster you won.</summary>
    public static class Scoring
    {
        public const int PerKill = 100;
        public const int PerWave = 250;
        public const int WinBonus = 1000;
        public const float ParSeconds = 600f;

        public static int Score(int kills, int wavesCleared, float damageDealt, bool won, float seconds)
        {
            float score = kills * PerKill + wavesCleared * PerWave + damageDealt * 2f;
            if (won)
                score += WinBonus + Math.Max(0f, ParSeconds - seconds) * 2f;
            return (int)Math.Round(score);
        }
    }

    /// <summary>PVP: rounds end when a player hits 0 HP; first to <c>roundsToWin</c> takes the match.</summary>
    public sealed class RoundTracker
    {
        readonly int roundsToWin;
        readonly int[] wins;

        public RoundTracker(int players, int roundsToWin)
        {
            this.roundsToWin = Math.Max(1, roundsToWin);
            wins = new int[players];
        }

        /// <summary>A tracker already part way through, as the host reported it.</summary>
        public static RoundTracker At(int round, int hostWins, int clientWins, int roundsToWin)
        {
            var tracker = new RoundTracker(2, roundsToWin) { Round = round };
            tracker.wins[0] = hostWins;
            tracker.wins[1] = clientWins;
            return tracker;
        }

        public int Round { get; private set; } = 1;
        public int Winner { get; private set; } = -1;
        public bool Over => Winner >= 0;
        public int Wins(int player) => wins[player];

        /// <summary>Records who won a round. Returns true if that ended the match.</summary>
        public bool RoundWon(int player)
        {
            if (Over)
                return true;
            wins[player]++;
            if (wins[player] >= roundsToWin)
            {
                Winner = player;
                return true;
            }
            Round++;
            return false;
        }
    }
}
