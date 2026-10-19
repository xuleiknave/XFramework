namespace XFramework.Battle
{
    public enum CommanderSkill
    {
        /// <summary>Area damage at a chosen point after a short delay.</summary>
        OrbitalStrike,
        /// <summary>Restores half of every friendly unit's maximum shield.</summary>
        EmergencyShield,
    }

    public sealed class CommanderSkillDef
    {
        public CommanderSkill Skill;
        public string DisplayName;
        public int Cost;
        public int CooldownSeconds;
        public bool NeedsTarget;

        public static readonly CommanderSkillDef[] All =
        {
            new CommanderSkillDef { Skill = CommanderSkill.OrbitalStrike, DisplayName = "轨道打击", Cost = 4, CooldownSeconds = 10, NeedsTarget = true },
            new CommanderSkillDef { Skill = CommanderSkill.EmergencyShield, DisplayName = "紧急护盾", Cost = 3, CooldownSeconds = 15 },
        };

        public static CommanderSkillDef Get(CommanderSkill skill) => All[(int)skill];

        // Orbital strike tuning.
        public static readonly int StrikeDelayTicks = BattleSim.TicksPerSecond;
        public static readonly Fix StrikeRadius = 3;
        public static readonly Fix StrikeDamage = 220;
    }

    /// <summary>
    /// A command issued during battle. Commands are queued and applied at the start of
    /// the next tick, so the same command list always yields the same battle.
    /// </summary>
    public sealed class Command
    {
        public int Side;
        public CommanderSkill Skill;
        public FixVec2 Point;

        public static Command Cast(int side, CommanderSkill skill, FixVec2 point = default(FixVec2)) =>
            new Command { Side = side, Skill = skill, Point = point };
    }
}
