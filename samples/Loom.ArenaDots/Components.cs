namespace Loom.ArenaDots;

/// <summary>float3 position (X, Y, Z). Arena uses X/Y; Z unused.</summary>
struct Pos
{
    public float X;
    public float Y;
    public float Z;
}

/// <summary>float3 velocity (X, Y, Z).</summary>
struct Vel
{
    public float X;
    public float Y;
    public float Z;
}

/// <summary>Marks a player-controlled dot; <see cref="PeerId"/> matches the client's loopback id.</summary>
struct PlayerOwner
{
    public int PeerId;
}

/// <summary>Seconds remaining before a projectile despawns.</summary>
struct Lifetime
{
    public float Seconds;
}
