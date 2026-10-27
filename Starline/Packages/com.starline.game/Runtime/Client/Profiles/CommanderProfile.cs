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
        public const int MaxNameLength = Starline.Online.ServerLimits.MaxCommanderName;

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
                return Loc.Ui("commander.name_empty");
            }
            if (name.Length > MaxNameLength)
            {
                return Loc.Ui("commander.name_too_long", MaxNameLength);
            }
            return null;
        }

        public static string FormatPlayTime(double seconds)
        {
            int minutes = (int)(seconds / 60.0);
            int hours = minutes / 60;
            minutes %= 60;
            return hours > 0 ? Loc.Ui("time.hours_minutes", hours, minutes) : Loc.Ui("time.minutes", minutes);
        }
    }
}
