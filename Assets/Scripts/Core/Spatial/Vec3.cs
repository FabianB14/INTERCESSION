using System;

namespace Session.Core.Spatial
{
    /// <summary>
    /// Minimal position/direction type for Session.Core.
    ///
    /// This exists because Core must compile without UnityEngine, and movement validation and voice
    /// falloff are gameplay rules that belong here rather than in a MonoBehaviour. The Runtime layer
    /// converts at the boundary — one struct copy, no allocation.
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);

        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator *(Vec3 a, float scalar) => new Vec3(a.X * scalar, a.Y * scalar, a.Z * scalar);

        public float SqrMagnitude => (X * X) + (Y * Y) + (Z * Z);

        public float Magnitude => (float)Math.Sqrt(SqrMagnitude);

        /// <summary>Horizontal magnitude. Vertical movement is governed by gravity, not by input speed.</summary>
        public float HorizontalMagnitude => (float)Math.Sqrt((X * X) + (Z * Z));

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Magnitude;

        public static float SqrDistance(Vec3 a, Vec3 b) => (a - b).SqrMagnitude;

        /// <summary>
        /// Point <paramref name="distance"/> along the way from <paramref name="from"/> to
        /// <paramref name="to"/>. Used to clamp an overspeeding client back to what was possible.
        /// </summary>
        public static Vec3 MoveTowards(Vec3 from, Vec3 to, float distance)
        {
            Vec3 delta = to - from;
            float magnitude = delta.Magnitude;

            if (magnitude <= distance || magnitude < 1e-6f)
            {
                return to;
            }

            return from + (delta * (distance / magnitude));
        }

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object? obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
            => "(" + X.ToString("0.00") + ", " + Y.ToString("0.00") + ", " + Z.ToString("0.00") + ")";
    }
}
