using System;
using System.Threading.Tasks;
using Starline.Online;
using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// The account's save slots, loaded once after login and kept in memory so screens can read them
    /// directly. <see cref="ServerSaveRepository"/> keeps them on the game server (UOS); builds without
    /// the Func Stateless SDK use <see cref="LocalSaveRepository"/> (files on this computer, as before).
    /// </summary>
    public abstract class SaveRepository
    {
        public const int SlotCount = ServerLimits.SlotCount;

        protected readonly CommanderProfile[] Slots = new CommanderProfile[SlotCount];

        /// <summary>Repository for the signed-in account: the game server when this build has one.</summary>
        public static SaveRepository ForCurrentAccount() =>
            GameServerClient.IsAvailable ? new ServerSaveRepository() : new LocalSaveRepository();

        /// <summary>The profile in a slot, or null when the slot is empty.</summary>
        public CommanderProfile Get(int slot) => slot >= 0 && slot < SlotCount ? Slots[slot] : null;

        /// <summary>The most recently played profile, or null when there is no save yet.</summary>
        public CommanderProfile LastPlayed()
        {
            CommanderProfile last = null;
            foreach (CommanderProfile profile in Slots)
            {
                if (profile != null && (last == null || profile.lastPlayedTicks > last.lastPlayedTicks))
                {
                    last = profile;
                }
            }
            return last;
        }

        /// <summary>The first empty slot, or -1 when all are taken.</summary>
        public virtual int FirstEmptySlot() => Array.IndexOf(Slots, null);

        /// <summary>Reads all slots; fails with <see cref="ServerCallException"/> when the server cannot be reached.</summary>
        public abstract Task Load();

        /// <summary>Names a new commander in an empty slot; fails with <see cref="ServerCallException"/>.</summary>
        public abstract Task<CommanderProfile> Create(int slot, string name);

        /// <summary>Deletes the save in a slot; fails with <see cref="ServerCallException"/>.</summary>
        public abstract Task Delete(int slot);

        /// <summary>
        /// Keeps the play time and expedition counts of a profile. Local saves write the file at once;
        /// the server gets a report when something worth sending changed (<paramref name="force"/>: now).
        /// </summary>
        public abstract void Save(CommanderProfile profile, bool force = false);
    }

    /// <summary>Save slots as files on this computer (<see cref="ProfileStore"/>), for builds without the game server.</summary>
    public sealed class LocalSaveRepository : SaveRepository
    {
        public override Task Load()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                Slots[i] = ProfileStore.Load(i);
            }
            return Task.CompletedTask;
        }

        /// <summary>A slot whose file could not be read is not offered for a new commander.</summary>
        public override int FirstEmptySlot() => ProfileStore.FirstEmptySlot();

        public override Task<CommanderProfile> Create(int slot, string name)
        {
            CommanderProfile profile = CommanderProfile.Create(slot, name);
            ProfileStore.Save(profile);
            Slots[slot] = profile;
            return Task.FromResult(profile);
        }

        public override Task Delete(int slot)
        {
            ProfileStore.Delete(slot);
            Slots[slot] = null;
            return Task.CompletedTask;
        }

        public override void Save(CommanderProfile profile, bool force = false) => ProfileStore.Save(profile);
    }

    /// <summary>
    /// Save slots on the game server (docs/server-design.md section 7, saves.*). Play time and expedition
    /// counts are counted here and reported as differences; the server caps them by the time that passed.
    /// </summary>
    public sealed class ServerSaveRepository : SaveRepository
    {
        /// <summary>Play time sent at most this often, unless the expedition counts changed or a report is forced.</summary>
        private const double ReportEverySeconds = 60.0;

        /// <summary>What the server already has of each slot, to send only the difference.</summary>
        private readonly CommanderProfile[] _reported = new CommanderProfile[SlotCount];
        private bool _reporting;

        public override async Task Load()
        {
            ServerResponse response = await GameServerClient.Call(new ServerRequest { op = ServerOps.ListSaves });
            Array.Clear(Slots, 0, SlotCount);
            Array.Clear(_reported, 0, SlotCount);
            foreach (SaveInfo info in response.saves ?? Array.Empty<SaveInfo>())
            {
                Keep(info);
            }
        }

        public override async Task<CommanderProfile> Create(int slot, string name)
        {
            ServerResponse response = await GameServerClient.Call(new ServerRequest { op = ServerOps.CreateSave, slot = slot, name = name });
            return Keep(response.save);
        }

        public override async Task Delete(int slot)
        {
            try
            {
                await GameServerClient.Call(new ServerRequest { op = ServerOps.DeleteSave, slot = slot });
            }
            catch (ServerCallException e) when (e.Code == ServerErrors.SlotEmpty)
            {
                // Already gone (deleted on another device, or a resent delete): the slot is empty either way.
            }
            Slots[slot] = null;
            _reported[slot] = null;
        }

        public override async void Save(CommanderProfile profile, bool force = false)
        {
            CommanderProfile reported = _reported[profile.slot];
            if (reported == null || Slots[profile.slot] != profile || _reporting)
            {
                return;
            }
            double seconds = profile.playSeconds - reported.playSeconds;
            int started = profile.runsStarted - reported.runsStarted;
            int won = profile.runsWon - reported.runsWon;
            if (started == 0 && won == 0 && seconds < (force ? 1.0 : ReportEverySeconds))
            {
                return;
            }
            var request = new ServerRequest
            {
                op = ServerOps.ReportPlay,
                slot = profile.slot,
                playMs = (long)(seconds * 1000.0),
                runsStarted = started,
                runsWon = won,
            };
            _reporting = true;
            try
            {
                await GameServerClient.Call(request);
                // Count what was sent as reported; time played meanwhile goes with the next report.
                reported.playSeconds += request.playMs / 1000.0;
                reported.runsStarted += started;
                reported.runsWon += won;
            }
            catch (ServerCallException e)
            {
                // Kept for the next report; a lost session is handled by GameServerClient.SessionLost.
                Debug.LogWarning("Starline：上报游戏时长失败：" + e.Code);
            }
            finally
            {
                _reporting = false;
            }
        }

        private CommanderProfile Keep(SaveInfo info)
        {
            var profile = new CommanderProfile
            {
                slot = info.slot,
                name = info.name,
                createdTicks = LocalTicks(info.createdAt),
                lastPlayedTicks = LocalTicks(info.lastPlayedAt),
                playSeconds = info.playMs / 1000.0,
                runsStarted = info.runsStarted,
                runsWon = info.runsWon,
            };
            Slots[info.slot] = profile;
            _reported[info.slot] = new CommanderProfile
            {
                slot = info.slot,
                playSeconds = profile.playSeconds,
                runsStarted = profile.runsStarted,
                runsWon = profile.runsWon,
            };
            return profile;
        }

        /// <summary>Server time (Unix ms, UTC) as local DateTime ticks, which CommanderProfile shows.</summary>
        private static long LocalTicks(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.Ticks;
    }
}
