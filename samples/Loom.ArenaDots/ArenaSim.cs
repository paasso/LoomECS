using System.Buffers.Binary;
using Loom;
using Loom.Entities;
using Loom.Net;

namespace Loom.ArenaDots;

/// <summary>Authoritative arena rules: move commands, integration, bounds, optional projectiles.</summary>
sealed class ArenaSim : IAuthoritativeSimulation
{
    public const float HalfExtent = 10f;
    public const float MaxSpeed = 8f;
    public const float Accel = 28f;
    public const float Friction = 6f;
    public const float ProjectileSpeed = 14f;
    public const float ProjectileLife = 0.6f;
    public const float PlayerRadius = 0.45f;

    public const byte OpMove = 1;
    public const byte OpFire = 2;

    private readonly Dictionary<int, (float Dx, float Dy)> _thrust = new();
    private readonly HashSet<int> _fire = new();

    public static byte[] EncodeMove(float dx, float dy)
    {
        var bytes = new byte[1 + 8];
        bytes[0] = OpMove;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(1), dx);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(5), dy);
        return bytes;
    }

    public static byte[] EncodeFire() => new[] { OpFire };

    public static bool TryDecodeMove(ReadOnlySpan<byte> payload, out float dx, out float dy)
    {
        dx = 0f;
        dy = 0f;
        if (payload.Length < 1 + 8 || payload[0] != OpMove)
            return false;
        dx = Math.Clamp(BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(1)), -1f, 1f);
        dy = Math.Clamp(BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(5)), -1f, 1f);
        return true;
    }

    /// <summary>
    /// Shared player integration used by the authoritative server and client prediction.
    /// </summary>
    public static void IntegratePlayer(
        ref float posX, ref float posY,
        ref float velX, ref float velY,
        float thrustX, float thrustY,
        float dt)
    {
        velX += thrustX * Accel * dt;
        velY += thrustY * Accel * dt;

        float damp = MathF.Exp(-Friction * dt);
        velX *= damp;
        velY *= damp;

        float len = MathF.Sqrt(velX * velX + velY * velY);
        if (len > MaxSpeed)
        {
            float s = MaxSpeed / len;
            velX *= s;
            velY *= s;
        }

        posX += velX * dt;
        posY += velY * dt;
        posX = Math.Clamp(posX, -HalfExtent + PlayerRadius, HalfExtent - PlayerRadius);
        posY = Math.Clamp(posY, -HalfExtent + PlayerRadius, HalfExtent - PlayerRadius);
    }

    public void ApplyCommand(World world, NetCommand command)
    {
        var payload = command.Payload;
        if (payload.Length == 0)
            return;

        int peer = command.Client.Value;
        if (!TryFindPlayer(world, peer, out _))
            return;

        switch (payload[0])
        {
            case OpMove:
            {
                if (!TryDecodeMove(payload, out float dx, out float dy))
                    return;
                _thrust[peer] = (dx, dy);
                break;
            }
            case OpFire:
                _fire.Add(peer);
                break;
        }
    }

    public void Simulate(World world, NetworkTick tick)
    {
        float dt = tick.DeltaTime;

        foreach (var peer in _fire)
        {
            if (!TryFindPlayer(world, peer, out var player))
                continue;
            var pos = world.Get<Pos>(player);
            var vel = world.Get<Vel>(player);
            float len = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
            float dirX = len > 0.05f ? vel.X / len : 1f;
            float dirY = len > 0.05f ? vel.Y / len : 0f;
            world.Create(
                new Pos { X = pos.X + dirX * 0.7f, Y = pos.Y + dirY * 0.7f, Z = 0 },
                new Vel { X = dirX * ProjectileSpeed, Y = dirY * ProjectileSpeed, Z = 0 },
                new Lifetime { Seconds = ProjectileLife });
        }
        _fire.Clear();

        world.Query().Each<Pos, Vel, PlayerOwner>((Entity e, ref Pos pos, ref Vel vel, ref PlayerOwner owner) =>
        {
            float ox = vel.X, oy = vel.Y, opx = pos.X, opy = pos.Y;
            float tx = 0f, ty = 0f;
            if (_thrust.TryGetValue(owner.PeerId, out var thrust))
            {
                tx = thrust.Dx;
                ty = thrust.Dy;
            }

            IntegratePlayer(ref pos.X, ref pos.Y, ref vel.X, ref vel.Y, tx, ty, dt);

            if (vel.X != ox || vel.Y != oy)
                world.MarkChanged<Vel>(e);
            if (pos.X != opx || pos.Y != opy)
                world.MarkChanged<Pos>(e);
        });
        _thrust.Clear();

        world.Query().Each<Pos, Vel, Lifetime>((Entity e, ref Pos pos, ref Vel vel, ref Lifetime _) =>
        {
            pos.X += vel.X * dt;
            pos.Y += vel.Y * dt;
            world.MarkChanged<Pos>(e);
        });

        var doomed = new List<Entity>();
        world.Query().Each<Lifetime>((Entity e, ref Lifetime life) =>
        {
            life.Seconds -= dt;
            world.MarkChanged<Lifetime>(e);
            if (life.Seconds <= 0f)
                doomed.Add(e);
        });
        for (int i = 0; i < doomed.Count; i++)
            world.Destroy(doomed[i]);
    }

    public static Entity SpawnPlayer(World world, int peerId, float x, float y)
    {
        return world.Create(
            new Pos { X = x, Y = y, Z = 0 },
            new Vel { X = 0, Y = 0, Z = 0 },
            new PlayerOwner { PeerId = peerId });
    }

    public static bool TryFindPlayer(World world, int peerId, out Entity player)
    {
        Entity found = default;
        bool ok = false;
        world.Query().Each<PlayerOwner>((Entity e, ref PlayerOwner owner) =>
        {
            if (!ok && owner.PeerId == peerId)
            {
                found = e;
                ok = true;
            }
        });
        player = found;
        return ok;
    }

    /// <summary>Spawn positions around the origin for up to 4 players.</summary>
    public static (float X, float Y) SpawnPoint(int index, int total)
    {
        if (total <= 1)
            return (-3f, 0f);
        float angle = index * (MathF.PI * 2f / total) - MathF.PI / 2f;
        const float r = 4f;
        return (MathF.Cos(angle) * r, MathF.Sin(angle) * r);
    }
}
