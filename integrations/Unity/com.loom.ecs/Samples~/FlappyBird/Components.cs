namespace Loom.Unity.Samples.FlappyBird
{
    enum GamePhase : byte
    {
        Ready = 0,
        Playing = 1,
        Dead = 2,
    }

    /// <summary>Per-world frame clock (written by <see cref="FlappyBirdRunner"/>).</summary>
    struct FrameTime
    {
        public float Delta;
    }

    /// <summary>Input sampled once per frame before systems run.</summary>
    struct InputState
    {
        public bool Flap;
        public bool Restart;
    }

    struct GameSession
    {
        public GamePhase Phase;
        public int Score;
        public int Best;
        public float SpawnTimer;
    }

    struct Position
    {
        public float X, Y;
    }

    struct Velocity
    {
        public float X, Y;
    }

    /// <summary>Marker for the player bird.</summary>
    struct Bird
    {
        public float Radius;
    }

    /// <summary>Vertical pipe pair. <see cref="Position.X"/> is the left edge of the column.</summary>
    struct Pipe
    {
        public float GapCenterY;
        public float GapSize;
        public float Width;
        public bool Scored;
    }

    struct BirdDied
    {
        public int FinalScore;
    }

    struct Scored
    {
        public int Score;
    }
}
