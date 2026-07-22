using Loom.Components;

namespace Loom.Unity.Samples.SpeedDemo
{
    struct Position
    {
        public float X, Y;
    }

    struct Velocity
    {
        public float X, Y;
    }

    struct Pulse : ISparseComponent
    {
        public float Age;
    }

    struct DemoConfig
    {
        public float Width;
        public float Height;
        public bool UseParallel;
        public bool SparseChurn;
        public bool Paused;
        public bool Draw;
        public int DrawBudget;
    }

    struct FrameMetrics
    {
        public double TickMilliseconds;
        public double Fps;
        public double EntitiesPerSecond;
        public int EntityCount;
        public long Frame;
    }

    struct FrameTime
    {
        public float Delta;
    }
}
