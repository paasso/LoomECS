using Loom;
using Loom.Entities;
using Loom.Systems;
using Loom.Unity;
using UnityEngine;

namespace Loom.Unity.Samples.HordeRush
{
    /// <summary>
    /// HordeRush-style twin-stick horde: ParallelEach steering/motion, sparse Lifetime/HitFlash,
    /// shared <see cref="EnemyKind"/>, CommandBuffer combat, GPU-instanced draw.
    /// </summary>
    [RequireComponent(typeof(HordeRushIndirectRenderer))]
    public sealed class HordeRushRunner : LoomRunner
    {
        private HordeRushIndirectRenderer _renderer = null!;
        private Camera _camera = null!;

        protected override void Awake()
        {
            DriveFrameDeltaSingleton = false;
            _renderer = GetComponent<HordeRushIndirectRenderer>();
            _camera = GetComponent<Camera>();
            if (_camera == null)
                _camera = gameObject.AddComponent<Camera>();
            base.Awake();
            _renderer.World = World;
        }

        protected override void OnRuntimeCreated(Runtime runtime)
        {
            GameFactory.Configure(Runtime, Systems);
        }

        protected override void Update()
        {
            if (Runtime == null)
                return;

            SampleInput();

            float dt = Time.deltaTime;
            if (dt > 0.05f)
                dt = 0.05f;
            World.GetSingleton<FrameTime>().Delta = dt;

            if (Input.GetKeyDown(KeyCode.P))
                World.GetSingleton<GameSession>().UseParallel =
                    !World.GetSingleton<GameSession>().UseParallel;

            base.Update();
        }

        private void SampleInput()
        {
            ref var input = ref World.GetSingleton<InputState>();
            input.MoveX = 0f;
            input.MoveY = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) input.MoveX -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) input.MoveX += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) input.MoveY -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) input.MoveY += 1f;

            // Mouse aim in arena space (Y-up playfield).
            Vector3 mouse = Input.mousePosition;
            float px = 0f, py = 0f;
            World.Query().With<Player>().Each<Position>((Entity _, ref Position pos) =>
            {
                px = pos.X;
                py = pos.Y;
            });

            if (_camera != null)
            {
                var worldPoint = _camera.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, -_camera.transform.position.z));
                input.AimX = worldPoint.x - px;
                input.AimY = worldPoint.y - py;
            }
            else
            {
                input.AimX = 1f;
                input.AimY = 0f;
            }

            input.Fire = Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space);
            input.Restart = Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0);
        }
    }
}
