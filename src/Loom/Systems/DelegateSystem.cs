using System;

namespace Loom.Systems
{
    /// <summary>Ad-hoc <see cref="ISystem"/> backed by a delegate — handy for tests and tiny
    /// samples without a named system type.</summary>
    public sealed class DelegateSystem : ISystem
    {
        private readonly Action<Runtime, CommandBuffer> _update;

        public DelegateSystem(Action<Runtime, CommandBuffer> update)
        {
            _update = update ?? throw new ArgumentNullException(nameof(update));
        }

        // ReSharper disable once InconsistentNaming
        public DelegateSystem(Action<Runtime> update)
        {
            if (update == null)
                throw new ArgumentNullException(nameof(update));
            _update = (runtime, _) => update(runtime);
        }

        public void Update(Runtime runtime, CommandBuffer commands) =>
            _update(runtime, commands);
    }
}
