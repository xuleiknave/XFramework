namespace Starline.Battle
{
    public enum CommanderSkill
    {
        /// <summary>Area damage at a chosen point after a short delay.</summary>
        OrbitalStrike,
        /// <summary>Restores half of every friendly unit's maximum shield.</summary>
        EmergencyShield,
    }

    /// <summary>Commander skill parameters, loaded from skills.json (together with the star multipliers).</summary>
    public sealed class CommanderSkillDef
    {
        public CommanderSkill Skill;
        public LocText Name => new LocText("skill." + Skill + ".name");
        public LocText Description => new LocText("skill." + Skill + ".description");
        public int Cost;
        public int CooldownSeconds;
        public bool NeedsTarget;
        /// <summary>Area skills: seconds before impact, radius and damage.</summary>
        public Fix DelaySeconds;
        public Fix Radius;
        public Fix Damage;

        public static CommanderSkillDef[] All { get; private set; } = new CommanderSkillDef[0];

        public static CommanderSkillDef Get(CommanderSkill skill) => All[(int)skill];

        // Orbital strike tuning.
        public static int StrikeDelayTicks => (Get(CommanderSkill.OrbitalStrike).DelaySeconds * BattleSim.TicksPerSecond).ToIntFloor();
        public static Fix StrikeRadius => Get(CommanderSkill.OrbitalStrike).Radius;
        public static Fix StrikeDamage => Get(CommanderSkill.OrbitalStrike).Damage;

        internal static Fix[] StarHpScale = { Fix.One, Fix.One, Fix.One };
        internal static Fix[] StarDamageScale = { Fix.One, Fix.One, Fix.One };

        internal static void Load(JsonValue root)
        {
            JsonValue skills = root["skills"];
            var all = new CommanderSkillDef[System.Enum.GetValues(typeof(CommanderSkill)).Length];
            foreach (JsonValue s in skills.Elements)
            {
                CommanderSkill skill = GameData.ParseEnum<CommanderSkill>(s["id"]);
                all[(int)skill] = new CommanderSkillDef
                {
                    Skill = skill,
                    Cost = s["cost"].AsInt(),
                    CooldownSeconds = s["cooldown"].AsInt(),
                    NeedsTarget = s.Bool("needsTarget"),
                    DelaySeconds = s.FixValue("delay"),
                    Radius = s.FixValue("radius"),
                    Damage = s.FixValue("damage"),
                };
            }
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                {
                    throw new System.FormatException("skills.json：缺少技能 " + (CommanderSkill)i);
                }
            }
            All = all;

            JsonValue stars = root["stars"];
            StarHpScale = ReadStars(stars["hp"]);
            StarDamageScale = ReadStars(stars["damage"]);
        }

        private static Fix[] ReadStars(JsonValue list)
        {
            if (list.Count != UnitDef.MaxStar)
            {
                throw new System.FormatException(list.Path + "：应有 " + UnitDef.MaxStar + " 个星级倍率");
            }
            var result = new Fix[UnitDef.MaxStar];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = list[i].AsFix();
            }
            return result;
        }
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
