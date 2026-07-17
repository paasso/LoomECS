using UnityEngine;

namespace Loom.Unity
{
    /// <summary>Small helpers to move data between Loom components and Unity types.</summary>
    public static class MathConversions
    {
        public static Vector3 ToVector3(float x, float y, float z = 0f) => new Vector3(x, y, z);

        public static void FromVector3(Vector3 v, out float x, out float y, out float z)
        {
            x = v.x;
            y = v.y;
            z = v.z;
        }
    }
}
