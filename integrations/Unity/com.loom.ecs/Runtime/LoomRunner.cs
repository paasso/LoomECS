using UnityEngine;
using Loom.Systems;

namespace Loom.Unity
{
    /// <summary>
    /// Owns a <see cref="Runtime"/>, a default <see cref="Systems"/> group, and drives
    /// <see cref="Runtime.Run"/> + <see cref="Runtime.EndFrame"/> once per <c>Update</c>.
    /// Subclass and override <see cref="OnRuntimeCreated"/> to register systems / spawn entities.
    /// </summary>
    public class LoomRunner : MonoBehaviour
    {
        public Runtime Runtime { get; protected set; } = null!;

        /// <summary>Default gameplay group run each frame. Add more groups yourself if you need
        /// separate rates or stages.</summary>
        public SystemGroup Systems { get; protected set; } = null!;

        public World World => Runtime.World;

        /// <summary>When true, writes <see cref="UnityEngine.Time.deltaTime"/> into a
        /// <c>FrameDelta</c> singleton each frame before systems run.</summary>
        public bool DriveFrameDeltaSingleton = true;

        protected virtual void Awake()
        {
            Runtime = new Runtime(new World());
            Systems = new SystemGroup("Systems");
            if (DriveFrameDeltaSingleton)
                World.SetSingleton(new FrameDelta { Seconds = 0f });
            OnRuntimeCreated(Runtime);
        }

        /// <summary>Called once after the runtime is constructed — register systems on
        /// <see cref="Systems"/> (or your own groups) and spawn entities.</summary>
        protected virtual void OnRuntimeCreated(Runtime runtime) { }

        protected virtual void Update()
        {
            if (Runtime == null)
                return;

            if (DriveFrameDeltaSingleton && World.HasSingleton<FrameDelta>())
                World.GetSingleton<FrameDelta>().Seconds = Time.deltaTime;

            Runtime.Run(Systems);
            Runtime.EndFrame();
        }

        protected virtual void OnDestroy()
        {
            Runtime = null!;
            Systems = null!;
        }
    }

    /// <summary>Optional per-frame dt singleton written by <see cref="LoomRunner"/>.</summary>
    public struct FrameDelta
    {
        public float Seconds;
    }
}
