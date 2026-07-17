using Loom;

namespace Loom.Unity.Samples.HordeRush
{
    struct Position
    {
        public float X, Y;
    }

    struct Velocity
    {
        public float X, Y;
    }

    /// <summary>Player avatar — dense fields change every frame.</summary>
    struct Player
    {
        public float AimX, AimY;
        public float FireCooldown;
    }

    /// <summary>Marker for hostile units.</summary>
    struct Enemy
    {
        public float Radius;
    }

    /// <summary>Player projectile.</summary>
    struct Bullet
    {
        public float Radius;
        public int Damage;
    }

    struct Health
    {
        public int Current;
        public int Max;
    }

    /// <summary>Interned per wave archetype — many enemies share one speed/tint/score.</summary>
    struct EnemyKind : ISharedComponent
    {
        public float Speed;
        public float R, G, B;
        public int ScoreValue;
    }

    /// <summary>High-churn TTL on bullets — sparse so add/remove never moves dense columns.</summary>
    struct Lifetime : ISparseComponent
    {
        public float Remaining;
    }

    /// <summary>Brief damage flash — sparse status effect.</summary>
    struct HitFlash : ISparseComponent
    {
        public float Age;
    }

    struct FrameTime
    {
        public float Delta;
    }

    struct InputState
    {
        public float MoveX, MoveY;
        public float AimX, AimY;
        public bool Fire;
        public bool Restart;
    }

    enum GamePhase : byte
    {
        Playing = 0,
        Dead = 1,
    }

    struct GameSession
    {
        public GamePhase Phase;
        public int Score;
        public int Kills;
        public int Wave;
        public float WaveTimer;
        public int AliveEnemies;
        public bool UseParallel;
    }

    struct ArenaConfig
    {
        public float Width;
        public float Height;
    }

    struct EnemyKilled
    {
        public int ScoreValue;
        public int Wave;
    }

    struct PlayerDied
    {
        public int FinalScore;
    }
}
