using Loom;
using Loom.Unity;
using UnityEngine;

namespace Loom.Unity.Samples.FlappyBird
{
    /// <summary>
    /// Playable Flappy Bird–style demo on Loom: systems, command buffers, events, singletons.
    /// Rendering uses project textures (see <see cref="FlappyBirdIndirectRenderer"/>).
    /// </summary>
    [RequireComponent(typeof(FlappyBirdIndirectRenderer))]
    public sealed class FlappyBirdRunner : LoomRunner
    {
        private FlappyBirdIndirectRenderer _renderer = null!;

        protected override void Awake()
        {
            _renderer = GetComponent<FlappyBirdIndirectRenderer>();
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

            base.Update();
        }

        private void SampleInput()
        {
            ref var input = ref World.GetSingleton<InputState>();
            bool flap =
                Input.GetKeyDown(KeyCode.Space)
                || Input.GetKeyDown(KeyCode.UpArrow)
                || Input.GetMouseButtonDown(0);
            bool restart =
                Input.GetKeyDown(KeyCode.R)
                || Input.GetKeyDown(KeyCode.Space)
                || Input.GetMouseButtonDown(0);

            input.Flap = flap;
            input.Restart = restart;
        }
    }
}
