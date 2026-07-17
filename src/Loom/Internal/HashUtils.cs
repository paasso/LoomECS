using System;
using System.Runtime.CompilerServices;

namespace Loom.Internal
{
    public static class DeterministicHashProvider
    {
        public static int StartHash = 216613261;
        public static int HashFactor = 16777619;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CombineAsDeterministicHash(this int h1, int h2)
        {
            return ((h1 << 5) + h1) ^ h2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeDeterministicHash(this Type data)
        {
            return GetTypeNameWithGenerics(data).ComputeDeterministicHash();
            static string GetTypeNameWithGenerics(Type type)
            {
                var name = $"{type.Namespace}.{type.Name}";
                if (type.IsGenericType)
                {
                    foreach (var t in type.GenericTypeArguments)
                    {
                        name += $"|{GetTypeNameWithGenerics(t)}|";
                    }
                }
                return name;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeDeterministicHash(this string data)
        {
            return ComputeDeterministicHash(data.AsSpan());
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeDeterministicHash(this ReadOnlySpan<byte> data)
        {
            unchecked
            {
                var p = HashFactor;
                var hash = StartHash;

                for (int i = 0; i < data.Length; i++)
                    hash = (hash ^ data[i]) * p;

                return hash;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeDeterministicHash(this ReadOnlySpan<int> data)
        {
            unchecked
            {
                var p = HashFactor;
                var hash = StartHash;

                for (int i = 0; i < data.Length; i++)
                    hash = (hash ^ data[i]) * p;

                return hash;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeDeterministicHash(this ReadOnlySpan<char> data)
        {
            unchecked
            {
                var p = HashFactor;
                var hash = StartHash;

                for (int i = 0; i < data.Length; i++)
                    hash = (hash ^ data[i]) * p;

                return hash;
            }
        }
    }
}