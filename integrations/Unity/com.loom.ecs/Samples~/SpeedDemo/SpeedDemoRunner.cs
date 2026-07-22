using Loom;
using Loom.Systems;
using Loom.Unity;
using UnityEngine;

namespace Loom.Unity.Samples.SpeedDemo
{
    /// <summary>
    /// Stress demo: many bouncing entities, optional ParallelEach + sparse Pulse churn.
    /// Drawn with <see cref="Graphics.DrawMeshInstancedIndirect"/>.
    /// </summary>
    [RequireComponent(typeof(SpeedDemoIndirectRenderer))]
    public sealed class SpeedDemoRunner : LoomRunner
    {
        [SerializeField] private int initialEntities = DemoBootstrap.DefaultEntities;
        [SerializeField] private float worldWidth = 1280f;
        [SerializeField] private float worldHeight = 720f;

        private SpeedDemoIndirectRenderer _renderer = null!;

        protected override void Awake()
        {
            _renderer = GetComponent<SpeedDemoIndirectRenderer>();
            base.Awake();
            _renderer.World = World;
        }

        protected override void OnRuntimeCreated(Runtime runtime)
        {
            DemoBootstrap.Configure(Runtime, Systems, worldWidth, worldHeight, initialEntities);
        }

        protected override void Update()
        {
            if (Runtime == null)
                return;

            HandleInput();

            float dt = Time.deltaTime;
            if (dt > 0.05f)
                dt = 0.05f;
            World.GetSingleton<FrameTime>().Delta = dt;

            if (!World.GetSingleton<DemoConfig>().Paused || Input.GetKeyDown(KeyCode.Period))
                DemoBootstrap.TickMeasured(Runtime, Systems);
            else if (DriveFrameDeltaSingleton && World.HasSingleton<FrameDelta>())
                World.GetSingleton<FrameDelta>().Seconds = dt;
        }

        private void HandleInput()
        {
            ref var cfg = ref World.GetSingleton<DemoConfig>();
            if (Input.GetKeyDown(KeyCode.Space))
                cfg.Paused = !cfg.Paused;
            if (Input.GetKeyDown(KeyCode.P))
                cfg.UseParallel = !cfg.UseParallel;
            if (Input.GetKeyDown(KeyCode.C))
                cfg.SparseChurn = !cfg.SparseChurn;
            if (Input.GetKeyDown(KeyCode.D))
                cfg.Draw = !cfg.Draw;
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
                DemoBootstrap.Spawn(World, DemoBootstrap.BatchSize);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
                DemoBootstrap.Despawn(World, DemoBootstrap.BatchSize);
            if (Input.GetKeyDown(KeyCode.R))
            {
                World.ClearEntities();
                DemoBootstrap.Spawn(World, DemoBootstrap.DefaultEntities);
            }
        }
    }
}
