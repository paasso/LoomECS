namespace Loom.Queries
{
    /// <summary>
    /// Struct job for <see cref="Query.Each{TJob, T1}"/>. Pass a concrete <c>struct</c> implementing
    /// this interface as <c>ref TJob</c> so the JIT can devirtualize/inline <see cref="Execute"/> —
    /// no per-entity delegate invoke. Do not pass the interface type itself (that boxes).
    /// </summary>
    public interface IJob<T1> where T1 : struct
    {
        void Execute(Entity entity, ref T1 c1);
    }

    public interface IJob<T1, T2> where T1 : struct where T2 : struct
    {
        void Execute(Entity entity, ref T1 c1, ref T2 c2);
    }

    public interface IJob<T1, T2, T3> where T1 : struct where T2 : struct where T3 : struct
    {
        void Execute(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3);
    }

    public interface IJob<T1, T2, T3, T4>
        where T1 : struct where T2 : struct where T3 : struct where T4 : struct
    {
        void Execute(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4);
    }

    public interface IJob<T1, T2, T3, T4, T5>
        where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
    {
        void Execute(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, ref T5 c5);
    }

    public interface IJob<T1, T2, T3, T4, T5, T6>
        where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
    {
        void Execute(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, ref T5 c5, ref T6 c6);
    }

    public interface IJob<T1, T2, T3, T4, T5, T6, T7>
        where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        where T5 : struct where T6 : struct where T7 : struct
    {
        void Execute(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, ref T5 c5, ref T6 c6, ref T7 c7);
    }

    public interface IJob<T1, T2, T3, T4, T5, T6, T7, T8>
        where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        where T5 : struct where T6 : struct where T7 : struct where T8 : struct
    {
        void Execute(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, ref T5 c5, ref T6 c6, ref T7 c7, ref T8 c8);
    }
}
