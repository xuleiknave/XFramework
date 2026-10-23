using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>A commander skill cast in a replay: the tick it applied at and the raw fixed-point target.</summary>
    public sealed class ReplayCommand
    {
        public int Tick;
        public CommanderSkill Skill;
        public long X;
        public long Y;

        public Command ToCommand() =>
            Command.Cast(BoardLayout.PlayerSide, Skill, new FixVec2(Fix.FromRaw(X), Fix.FromRaw(Y)));
    }

    /// <summary>
    /// Everything needed to replay a stage battle (replay-server.md section 4): the player's
    /// formation, the seed and the commands. The enemy comes from the stage itself. Because the
    /// kernel is deterministic, re-running these inputs reproduces the battle exactly.
    /// </summary>
    public sealed class Replay
    {
        /// <summary>Replay file layout version; bump when fields change.</summary>
        public const int FormatVersion = 1;

        /// <summary>Battle rules version; bump whenever a kernel change alters how battles play out.</summary>
        public const int KernelVersion = 1;

        public int Format = FormatVersion;
        public int Kernel = KernelVersion;
        /// <summary><see cref="GameData.Fingerprint"/> of the data the battle was played with.</summary>
        public string Data;
        public string Stage;
        public ulong Seed;
        /// <summary>Deployed power as computed by <see cref="PowerRating"/>; the server recomputes it.</summary>
        public int Power;
        public Formation Player = new Formation();
        public List<ReplayCommand> Commands = new List<ReplayCommand>();

        // Result, checked when the replay is verified.
        public int Winner = BattleSim.DrawSide;
        public int Ticks;
        public ulong Hash;

        /// <summary>Captures a finished battle of <paramref name="stageId"/>.</summary>
        public static Replay Record(string stageId, BattleSim battle)
        {
            var replay = new Replay
            {
                Data = GameData.Fingerprint,
                Stage = stageId,
                Seed = battle.Setup.Seed,
                Player = battle.Setup.Player.Clone(),
                Winner = battle.Winner,
                Ticks = battle.Tick,
                Hash = battle.ComputeStateHash(),
            };
            replay.Power = PowerRating.Of(replay.Player);
            foreach (IssuedCommand issued in battle.IssuedCommands)
            {
                if (issued.Command.Side != BoardLayout.PlayerSide)
                {
                    continue;
                }
                replay.Commands.Add(new ReplayCommand
                {
                    Tick = issued.Tick,
                    Skill = issued.Command.Skill,
                    X = issued.Command.Point.X.Raw,
                    Y = issued.Command.Point.Y.Raw,
                });
            }
            return replay;
        }

        /// <summary>True if this replay uses at least one commander skill (shown on the leaderboard).</summary>
        public bool UsesSkills => Commands.Count > 0;
    }

    /// <summary>
    /// Plays a replay back: a battle against the stage's enemy that issues the recorded commands
    /// at their ticks. The client steps it like a live battle; the server runs it to the end.
    /// </summary>
    public sealed class ReplayPlayer
    {
        private readonly Replay _replay;
        private int _next;

        public ReplayPlayer(Replay replay, Encounter stage)
        {
            _replay = replay;
            Battle = new BattleSim(stage.CreateSetup(replay.Player.Clone(), replay.Seed));
        }

        public BattleSim Battle { get; }

        public void Step()
        {
            while (_next < _replay.Commands.Count && _replay.Commands[_next].Tick <= Battle.Tick)
            {
                Battle.Issue(_replay.Commands[_next].ToCommand());
                _next++;
            }
            Battle.Step();
        }

        public void RunToEnd()
        {
            while (!Battle.IsOver)
            {
                Step();
            }
        }
    }
}
