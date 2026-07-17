namespace Loom.Systems
{
    /// <summary>
    /// A unit of per-frame (or per-<see cref="Runtime.Run"/>) work.
    /// Systems are plain code — Loom does not infer read/write component sets. Register them
    /// on a <see cref="SystemGroup"/> you own and drive via <see cref="Runtime.Run"/>; use
    /// <see cref="UpdateAfterAttribute"/> /
    /// <see cref="UpdateBeforeAttribute"/> / <see cref="OrderFirstAttribute"/> /
    /// <see cref="OrderLastAttribute"/> for ordering. Data-parallel work inside a system can use
    /// <c>Query.ParallelEach</c>; systems that implement <see cref="IParallelSystem"/> may also
    /// run concurrently with other parallel systems in the same dependency wave.
    /// </summary>
    /// <remarks>
    /// Each system receives its own <see cref="CommandBuffer"/> for the duration of
    /// <see cref="Update"/>. <see cref="SystemGroup.Run"/> plays that buffer back before the next
    /// (sequential) system, or after a parallel wave completes. Prefer this buffer over immediate
    /// structural APIs when mutating entities under an active <c>Query.Each</c>.
    /// Access entity storage via <see cref="Runtime.World"/>.
    /// <para>
    /// Implement <see cref="ISystemLifecycle"/> for <c>OnCreate</c>/<c>OnDestroy</c> hooks when
    /// the system is first run against a simulation / removed from its group.
    /// </para>
    /// </remarks>
    public interface ISystem
    {
        void Update(Runtime runtime, CommandBuffer commands);
    }

    /// <summary>
    /// Optional lifecycle for systems. <see cref="OnCreate"/> runs once per
    /// (<see cref="SystemGroup"/>, <see cref="Runtime"/>) before the first <see cref="ISystem.Update"/>
    /// on that simulation; <see cref="OnDestroy"/> runs when the system is removed from the group
    /// (for every simulation it was created on).
    /// </summary>
    public interface ISystemLifecycle
    {
        void OnCreate(Runtime runtime);
        void OnDestroy(Runtime runtime);
    }

    /// <summary>
    /// Marks a system as safe to run concurrently with other <see cref="IParallelSystem"/> peers
    /// in the same dependency wave. Contract: no immediate structural mutation on
    /// <see cref="World"/> from other threads; use only the provided <see cref="CommandBuffer"/>
    /// for structural changes, and prefer <c>Query.ParallelEach</c> / read-only queries for data.
    /// Command buffers from a wave are played back sequentially (registration order) after the wave.
    /// </summary>
    public interface IParallelSystem : ISystem
    {
    }
}
