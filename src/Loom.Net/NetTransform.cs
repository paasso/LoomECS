using System;

namespace Loom.Net
{
    /// <summary>
    /// Lightweight pos+vel sample for client render buffers (interpolation / prediction).
    /// Independent of game component types — copy from your <c>Pos</c>/<c>Vel</c> at the boundary.
    /// </summary>
    public struct NetTransform
    {
        public float PosX;
        public float PosY;
        public float PosZ;
        public float VelX;
        public float VelY;
        public float VelZ;

        public NetTransform(float posX, float posY, float posZ, float velX, float velY, float velZ)
        {
            PosX = posX;
            PosY = posY;
            PosZ = posZ;
            VelX = velX;
            VelY = velY;
            VelZ = velZ;
        }

        /// <summary>Linear blend of position and velocity (alpha in 0..1).</summary>
        public static NetTransform Lerp(in NetTransform a, in NetTransform b, float alpha)
        {
            alpha = alpha < 0f ? 0f : (alpha > 1f ? 1f : alpha);
            float inv = 1f - alpha;
            return new NetTransform(
                a.PosX * inv + b.PosX * alpha,
                a.PosY * inv + b.PosY * alpha,
                a.PosZ * inv + b.PosZ * alpha,
                a.VelX * inv + b.VelX * alpha,
                a.VelY * inv + b.VelY * alpha,
                a.VelZ * inv + b.VelZ * alpha);
        }

        /// <summary>Euclidean distance between positions (XY only by default via Z=0 samples).</summary>
        public static float PositionError(in NetTransform a, in NetTransform b)
        {
            float dx = a.PosX - b.PosX;
            float dy = a.PosY - b.PosY;
            float dz = a.PosZ - b.PosZ;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
