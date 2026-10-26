namespace Starline.Battle
{
    /// <summary>64-bit FNV-1a hash over UTF-16 code units: stable across platforms and runtimes.</summary>
    public static class Fnv
    {
        public const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Hash(string text) => Mix(Offset, text);

        public static ulong Mix(ulong hash, string text)
        {
            foreach (char c in text)
            {
                hash ^= (byte)c;
                hash *= Prime;
                hash ^= (byte)(c >> 8);
                hash *= Prime;
            }
            return hash;
        }
    }
}
