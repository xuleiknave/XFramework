using System;

namespace XFramework.Battle
{
    /// <summary>2D fixed-point vector on the battle plane.</summary>
    [Serializable]
    public readonly struct FixVec2 : IEquatable<FixVec2>
    {
        public readonly Fix X;
        public readonly Fix Y;

        public FixVec2(Fix x, Fix y)
        {
            X = x;
            Y = y;
        }

        public static readonly FixVec2 Zero = new FixVec2(Fix.Zero, Fix.Zero);

        public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new FixVec2(a.X + b.X, a.Y + b.Y);
        public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new FixVec2(a.X - b.X, a.Y - b.Y);
        public static FixVec2 operator *(FixVec2 a, Fix s) => new FixVec2(a.X * s, a.Y * s);
        public static FixVec2 operator /(FixVec2 a, Fix s) => new FixVec2(a.X / s, a.Y / s);
        public static bool operator ==(FixVec2 a, FixVec2 b) => a.Equals(b);
        public static bool operator !=(FixVec2 a, FixVec2 b) => !a.Equals(b);

        public Fix SqrMagnitude => X * X + Y * Y;
        public Fix Magnitude => Fix.Sqrt(SqrMagnitude);

        public static Fix SqrDistance(FixVec2 a, FixVec2 b) => (a - b).SqrMagnitude;
        public static Fix Distance(FixVec2 a, FixVec2 b) => (a - b).Magnitude;

        /// <summary>Moves from current toward target by at most maxStep.</summary>
        public static FixVec2 MoveTowards(FixVec2 current, FixVec2 target, Fix maxStep)
        {
            FixVec2 delta = target - current;
            Fix dist = delta.Magnitude;
            if (dist <= maxStep || dist == Fix.Zero)
            {
                return target;
            }
            return current + delta * (maxStep / dist);
        }

        public bool Equals(FixVec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is FixVec2 other && Equals(other);
        public override int GetHashCode() => X.GetHashCode() * 31 + Y.GetHashCode();
        public override string ToString() => "(" + X + ", " + Y + ")";
    }
}
