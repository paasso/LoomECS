namespace Loom.Tests;

public class EachChunkTests
{
    [Fact]
    public void EachChunk_MutatesLikeEach()
    {
        var world = new World();
        for (int i = 0; i < 2500; i++)
            world.Create(new Position { X = i }, new Velocity { X = 1, Y = 2 });

        world.Query().EachChunk<Position, Velocity>((entities, pos, vel) =>
        {
            for (int i = 0; i < pos.Length; i++)
            {
                pos[i].X += vel[i].X;
                pos[i].Y += vel[i].Y;
            }
        });

        int n = 0;
        world.Query().Each<Position>((Entity _, ref Position p) =>
        {
            Assert.Equal(n + 1, p.X);
            Assert.Equal(2, p.Y);
            n++;
        });
        Assert.Equal(2500, n);
    }

    [Fact]
    public void EachChunk_RejectsSparseFilter()
    {
        var world = new World();
        world.Create(new Position());
        Assert.Throws<InvalidOperationException>(() =>
            world.Query().Enabled().EachChunk<Position>((entities, pos) => { _ = pos.Length; }));
    }
}