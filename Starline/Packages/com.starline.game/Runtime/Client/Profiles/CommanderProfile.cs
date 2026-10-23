using System;

namespace Starline.Client
{
    /// <summary>
    /// One save slot: the commander's name and play statistics. Expedition progress and
    /// collection will be added here with the save system (M3).
    /// </summary>
    [Serializable]
    public sealed class CommanderProfile
    {
        public const int MaxNameLength = 8;

        public int slot;
        public string name;
        public long createdTicks;
        public long lastPlayedTicks;
        public double playSeconds;
        public int runsStarted;
        public int runsWon;

        public static CommanderProfile Create(int slot, string name)
        {
            long now = DateTime.Now.Ticks;
            return new CommanderProfile { slot = slot, name = name, createdTicks = now, lastPlayedTicks = now };
        }

        public DateTime LastPlayed => new DateTime(lastPlayedTicks);

        /// <summary>Trims the name and returns an error message, or null when it can be used.</summary>
        public static string ValidateName(ref string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return "请输入指挥官的名字。";
            }
            if (name.Length > MaxNameLength)
            {
                return "名字最多 " + MaxNameLength + " 个字。";
            }
            return null;
        }

        public static string FormatPlayTime(double seconds)
        {
            int minutes = (int)(seconds / 60.0);
            int hours = minutes / 60;
            minutes %= 60;
            return hours > 0 ? hours + " 小时 " + minutes + " 分" : minutes + " 分";
        }
    }
}
