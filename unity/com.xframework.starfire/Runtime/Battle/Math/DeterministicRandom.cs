namespace XFramework.Battle
{
    /// <summary>
    /// xorshift64* generator. Same seed always produces the same sequence,
    /// independent of platform or .NET runtime.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(ulong seed)
        {
            _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public ulong State => _state;

        public ulong NextULong()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>Returns an integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            ulong span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % span);
        }

        /// <summary>Returns a fixed-point value in [0, 1).</summary>
        public Fix NextFix() => Fix.FromRaw((long)(NextULong() >> (64 - Fix.FractionBits)));
    }
}
