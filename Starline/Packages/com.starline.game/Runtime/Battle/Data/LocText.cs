namespace Starline.Battle
{
    /// <summary>
    /// Player-facing text produced by the core: a key into the Game string table plus the
    /// values for its {0}, {1}… placeholders. The core never holds words; the client looks the
    /// key up in the selected language (Localization/Game.csv). An argument may itself be a
    /// LocText, such as a unit name, and is translated first.
    /// </summary>
    public readonly struct LocText
    {
        public readonly string Key;
        public readonly object[] Args;

        public LocText(string key, params object[] args)
        {
            Key = key;
            Args = args ?? new object[0];
        }

        public bool IsEmpty => string.IsNullOrEmpty(Key);

        public static LocText UnitName(string unitId) => new LocText("unit." + unitId + ".name");

        public static string Stars(int star) => new string('★', star);

        /// <summary>The key and arguments, for logs and debugging.</summary>
        public override string ToString() => Args.Length == 0 ? Key : Key + "(" + string.Join(", ", Args) + ")";
    }
}
