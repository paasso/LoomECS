using Loom;

namespace Loom.SpeedDemo;

[OrderFirst]
sealed class MotionSystem : ISystem
{
    public void Update(Runtime runtime, CommandBuffer commands)
    {
        var world = runtime.World;
        ref var cfg = ref world.GetSingleton<DemoConfig>();
        if (cfg.Paused)
            return;

        float dt = world.GetSingleton<FrameTime>().Delta;
        float w = cfg.Width;
        float h = cfg.Height;

        if (cfg.UseParallel)
        {
            world.Query().ParallelEach<Position, Velocity>((Entity _, ref Position pos, ref Velocity vel) =>
                Integrate(ref pos, ref vel, dt, w, h));
        }
        else
        {
            world.Query().Each<Position, Velocity>((Entity _, ref Position pos, ref Velocity vel) =>
                Integrate(ref pos, ref vel, dt, w, h));
        }
    }

    private static void Integrate(ref Position pos, ref Velocity vel, float dt, float w, float h)
    {
        pos.X += vel.X * dt;
        pos.Y += vel.Y * dt;

        if (pos.X < 0f) { pos.X = 0f; vel.X = MathF.Abs(vel.X); }
        else if (pos.X > w) { pos.X = w; vel.X = -MathF.Abs(vel.X); }

        if (pos.Y < 0f) { pos.Y = 0f; vel.Y = MathF.Abs(vel.Y); }
        else if (pos.Y > h) { pos.Y = h; vel.Y = -MathF.Abs(vel.Y); }
    }
}

/// <summary>Adds/removes sparse <see cref="Pulse"/> on a fraction of entities each frame to show
/// churn without dense structural cost.</summary>
[UpdateAfter(typeof(MotionSystem))]
sealed class SparseChurnSystem : ISystem
{
    private int _cursor;

    public void Update(Runtime runtime, CommandBuffer commands)
    {
        var world = runtime.World;
        ref var cfg = ref world.GetSingleton<DemoConfig>();
        if (cfg.Paused || !cfg.SparseChurn)
            return;

        int count = world.EntityCount;
        if (count == 0)
            return;

        int visits = Math.Max(1, count / 200);
        for (int i = 0; i < visits; i++)
        {
            _cursor++;
            if (_cursor >= world.EntityCount + 8)
                _cursor = 1;

            if (!world.TryGetAliveEntity(_cursor, out var entity))
                continue;

            if (world.Has<Pulse>(entity))
            {
                ref var pulse = ref world.Get<Pulse>(entity);
                pulse.Age += world.GetSingleton<FrameTime>().Delta;
                if (pulse.Age > 0.35f)
                    world.Remove<Pulse>(entity);
            }
            else if ((_cursor & 3) == 0)
            {
                world.Add(entity, new Pulse { Age = 0f });
            }
        }
    }
}

struct FrameTime
{
    public float Delta;
}
