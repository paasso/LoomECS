using System;

namespace Loom.Net
{
    /// <summary>Opaque peer handle for transport send/receive. 0 is reserved as "unknown/local".</summary>
    public readonly struct NetPeerId : IEquatable<NetPeerId>
    {
        public static readonly NetPeerId None = default;

        public NetPeerId(int value) => Value = value;

        public int Value { get; }

        public bool Equals(NetPeerId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is NetPeerId other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString();

        public static bool operator ==(NetPeerId left, NetPeerId right) => left.Equals(right);

        public static bool operator !=(NetPeerId left, NetPeerId right) => !left.Equals(right);
    }
}
