using System;

namespace XFramework.Battle
{
    /// <summary>
    /// Q48.16 fixed-point number. All battle logic uses this instead of float
    /// so that results are identical on every machine.
    /// </summary>
    [Serializable]
    public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
    {
        public const int FractionBits = 16;
        public const long OneRaw = 1L << FractionBits;

        public readonly long Raw;

        private Fix(long raw)
        {
            Raw = raw;
        }

        public static readonly Fix Zero = new Fix(0);
        public static readonly Fix One = new Fix(OneRaw);
        public static readonly Fix Half = new Fix(OneRaw / 2);
        public static readonly Fix MaxValue = new Fix(long.MaxValue);

        public static Fix FromRaw(long raw) => new Fix(raw);
        public static Fix FromInt(int value) => new Fix((long)value << FractionBits);

        /// <summary>Exact construction from a ratio, e.g. FromRatio(5, 2) == 2.5.</summary>
        public static Fix FromRatio(long numerator, long denominator) =>
            new Fix((numerator << FractionBits) / denominator);

        public static implicit operator Fix(int value) => FromInt(value);

        public static Fix operator +(Fix a, Fix b) => new Fix(a.Raw + b.Raw);
        public static Fix operator -(Fix a, Fix b) => new Fix(a.Raw - b.Raw);
        public static Fix operator -(Fix a) => new Fix(-a.Raw);
        public static Fix operator *(Fix a, Fix b) => new Fix((a.Raw * b.Raw) >> FractionBits);
        public static Fix operator /(Fix a, Fix b) => new Fix((a.Raw << FractionBits) / b.Raw);

        public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
        public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;
        public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
        public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
        public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
        public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;

        public static Fix Min(Fix a, Fix b) => a.Raw < b.Raw ? a : b;
        public static Fix Max(Fix a, Fix b) => a.Raw > b.Raw ? a : b;
        public static Fix Clamp(Fix v, Fix min, Fix max) => Max(min, Min(max, v));
        public static Fix Abs(Fix v) => v.Raw < 0 ? new Fix(-v.Raw) : v;

        /// <summary>Integer-only square root, deterministic across platforms.</summary>
        public static Fix Sqrt(Fix v)
        {
            if (v.Raw <= 0)
            {
                return Zero;
            }

            // sqrt(raw / 2^16) * 2^16 == sqrt(raw * 2^16)
            ulong n = (ulong)v.Raw << FractionBits;
            ulong x = n;
            ulong y = (x + 1) >> 1;
            while (y < x)
            {
                x = y;
                y = (x + n / x) >> 1;
            }
            return new Fix((long)x);
        }

        public int ToIntFloor() => (int)(Raw >> FractionBits);
        public float ToFloat() => Raw / (float)OneRaw;

        public bool Equals(Fix other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fix other && Raw == other.Raw;
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);
        public override string ToString() => ToFloat().ToString("0.###");
    }
}
