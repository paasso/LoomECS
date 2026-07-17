using Loom;
using Loom.Systems;
using Loom.Unity;
using UnityEngine;
using Xunit;

namespace Loom.GeneratorTests;

public class UnityBridgeTests
{
    private sealed class TestRunner : LoomRunner
    {
        public void Use(Runtime runtime) => Runtime = runtime;
    }

    [Fact]
    public void MathConversions_RoundTrip()
    {
        var v = MathConversions.ToVector3(1, 2, 3);
        MathConversions.FromVector3(v, out float x, out float y, out float z);
        Assert.Equal(1, x);
        Assert.Equal(2, y);
        Assert.Equal(3, z);
    }

    [Fact]
    public void TransformSyncSystem_PushesUnityPosition()
    {
        var world = new World();
        var sim = new Runtime(world);
        var systems = new SystemGroup();
        var sync = new TransformSyncSystem();
        systems.Add(sync);

        var entity = world.Create(new UnityPosition { X = 9, Y = 8, Z = 7 });
        var runner = new TestRunner();
        runner.Use(sim);

        var behaviour = new EntityBehaviour();
        behaviour.Bind(runner, entity);
        sync.Register(behaviour);

        sim.Run(systems);
        sim.EndFrame();
        Assert.Equal(9, behaviour.transform.position.x);
        Assert.Equal(8, behaviour.transform.position.y);
        Assert.Equal(7, behaviour.transform.position.z);
    }
}
