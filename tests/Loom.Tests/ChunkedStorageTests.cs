using Loom.Internal;

namespace Loom.Tests;

/// <summary>Exercises archetype storage past a single chunk (ChunkCapacity = 1024 entities),
/// since the existing test suite mostly stays well under that and would never catch a
/// chunk-boundary bug (row-to-chunk mapping, cross-chunk swap-back, cross-chunk archetype moves).</summary>
public class ChunkedStorageTests
{
    [Fact]
    public void EntitiesSpanningMultipleChunks_AllRetainCorrectValues()
    {
        var world = new World();
        int n = Archetype.ChunkCapacity * 2 + 37; // spans 3 chunks, the last one partial
        var entities = new Entity[n];
        for (int i = 0; i < n; i++)
            entities[i] = world.Create(new Position { X = i }, new Velocity { X = i * 2 });

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(i, world.Get<Position>(entities[i]).X);
            Assert.Equal(i * 2, world.Get<Velocity>(entities[i]).X);
        }
    }

    [Fact]
    public void RemovingEntityInEarlyChunk_SwapsInLastEntity_CorrectlyAcrossChunks()
    {
        var world = new World();
        int n = Archetype.ChunkCapacity + 10; // chunk 0 full, chunk 1 holds 10 entities
        var entities = new Entity[n];
        for (int i = 0; i < n; i++)
            entities[i] = world.Create(new Position { X = i });

        // entities[5] lives in the first (full) chunk; the archetype's overall last entity
        // (entities[n - 1]) lives at the end of the second chunk and must get swapped in.
        world.Destroy(entities[5]);

        Assert.False(world.IsAlive(entities[5]));
        for (int i = 0; i < n; i++)
        {
            if (i == 5 || i == n - 1) continue;
            Assert.Equal(i, world.Get<Position>(entities[i]).X);
        }
        Assert.True(world.IsAlive(entities[n - 1]));
        Assert.Equal(n - 1, world.Get<Position>(entities[n - 1]).X);
    }

    [Fact]
    public void Each_VisitsEveryEntityAcrossMultipleChunksExactlyOnce()
    {
        var world = new World();
        int n = Archetype.ChunkCapacity * 2 + 5;
        for (int i = 0; i < n; i++)
            world.Create(new Position { X = i });

        var seen = new HashSet<float>();
        int visited = 0;
        world.Query().Each<Position>((Entity e, ref Position p) =>
        {
            visited++;
            seen.Add(p.X);
        });

        Assert.Equal(n, visited);
        Assert.Equal(n, seen.Count);
    }

    [Fact]
    public void AddComponent_OnEntityInNonLastChunk_MovesCorrectValueAcrossChunks()
    {
        var world = new World();
        int n = Archetype.ChunkCapacity + 3;
        var entities = new Entity[n];
        for (int i = 0; i < n; i++)
            entities[i] = world.Create(new Position { X = i });

        // entities[0] lives in the first (full) chunk of the {Position} archetype; adding
        // Velocity moves it into the {Position, Velocity} archetype's (freshly created) chunk.
        world.Add(entities[0], new Velocity { X = 999 });

        Assert.Equal(0, world.Get<Position>(entities[0]).X);
        Assert.Equal(999, world.Get<Velocity>(entities[0]).X);
        Assert.False(world.Has<Velocity>(entities[1]));
    }

    [Fact]
    public void AddComponent_ToEveryEntityInFrontToBackOrder_AcrossManyChunks_DoesNotThrow()
    {
        // Regression test: draining a large archetype strictly front-to-back (each Add<T> moves
        // that entity out via swap-back) used to throw IndexOutOfRangeException once the
        // archetype's trailing chunk had been fully emptied by earlier swap-backs but was still
        // being treated as "the last chunk" — see Archetype.RemoveRowSwapBack.
        var world = new World();
        int n = Archetype.ChunkCapacity * 3 + 17;
        var entities = new Entity[n];
        for (int i = 0; i < n; i++)
            entities[i] = world.Create(new Position { X = i });

        for (int i = 0; i < n; i++)
            world.Add(entities[i], new Velocity { X = i * 10 });

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(i, world.Get<Position>(entities[i]).X);
            Assert.Equal(i * 10, world.Get<Velocity>(entities[i]).X);
        }
    }

    [Fact]
    public void Destroy_EveryEntityInFrontToBackOrder_AcrossManyChunks_DoesNotThrow()
    {
        var world = new World();
        int n = Archetype.ChunkCapacity * 3 + 17;
        var entities = new Entity[n];
        for (int i = 0; i < n; i++)
            entities[i] = world.Create(new Position { X = i });

        for (int i = 0; i < n; i++)
            world.Destroy(entities[i]);

        Assert.Equal(0, world.EntityCount);
        for (int i = 0; i < n; i++)
            Assert.False(world.IsAlive(entities[i]));
    }

    [Fact]
    public void RefillingArchetype_AfterFullyDraining_ReusesChunksCorrectly()
    {
        // After draining an archetype all the way back to zero (emptying every chunk it had),
        // adding entities again must land them in valid, freshly-usable slots.
        var world = new World();
        int n = Archetype.ChunkCapacity * 2 + 9;
        var first = new Entity[n];
        for (int i = 0; i < n; i++)
            first[i] = world.Create(new Position { X = i });
        for (int i = 0; i < n; i++)
            world.Destroy(first[i]);

        var second = new Entity[n];
        for (int i = 0; i < n; i++)
            second[i] = world.Create(new Position { X = i + 1000 });

        for (int i = 0; i < n; i++)
            Assert.Equal(i + 1000, world.Get<Position>(second[i]).X);
    }
}
