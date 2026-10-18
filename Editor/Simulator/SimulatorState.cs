using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace susaplay.SDK.Editor.Simulator
{
    /// <summary>
    /// Everything the platform would have stored for the simulated player: saves, achievements,
    /// wallets, inventory, spend answers and rewarded-ad claims. Kept between Plays so a game can
    /// test loading on its next launch, and only ever on this machine.
    /// </summary>
    [Serializable]
    internal sealed class SimulatorState
    {
        public int format = 1;
        public string uid;
        public string displayName = "Simulated Player";
        public long xp;
        public PlatformWalletRecord platformWallet = new PlatformWalletRecord();
        public List<SaveRecord> saves = new List<SaveRecord>();
        public List<AchievementRecord> achievements = new List<AchievementRecord>();
        public List<GameWalletRecord> gameWallets = new List<GameWalletRecord>();
        public List<InventoryRecord> inventories = new List<InventoryRecord>();
        public List<RequestClaim> requestClaims = new List<RequestClaim>();
        public List<AdClaimRecord> adClaims = new List<AdClaimRecord>();

        public static SimulatorState CreateNew()
        {
            return new SimulatorState { uid = NewUid() };
        }

        /// <summary>"sim_" and 8 random lowercase letters and digits.</summary>
        public static string NewUid()
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
            var random = new System.Random(Guid.NewGuid().GetHashCode());
            var chars = new char[8];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = alphabet[random.Next(alphabet.Length)];
            }
            return "sim_" + new string(chars);
        }

        public SaveRecord FindSave(string gameId, string slot)
        {
            return saves.Find(s => s.gameId == gameId && s.slot == slot);
        }

        public AchievementRecord FindAchievement(string gameId, string achievementId, bool create)
        {
            var record = achievements.Find(a => a.gameId == gameId && a.achievementId == achievementId);
            if (record == null && create)
            {
                record = new AchievementRecord { gameId = gameId, achievementId = achievementId };
                achievements.Add(record);
            }
            return record;
        }

        public GameWalletRecord FindGameWallet(string gameId)
        {
            return gameWallets.Find(w => w.gameId == gameId);
        }

        public InventoryRecord FindInventory(string gameId, bool create)
        {
            var record = inventories.Find(i => i.gameId == gameId);
            if (record == null && create)
            {
                record = new InventoryRecord { gameId = gameId };
                inventories.Add(record);
            }
            return record;
        }

        public RequestClaim FindClaim(string key)
        {
            return requestClaims.Find(c => c.key == key);
        }

        public AdClaimRecord FindAdClaim(string gameId, string date, bool create)
        {
            var record = adClaims.Find(a => a.gameId == gameId && a.date == date);
            if (record == null && create)
            {
                record = new AdClaimRecord { gameId = gameId, date = date };
                adClaims.Add(record);
            }
            return record;
        }
    }

    [Serializable]
    internal sealed class PlatformWalletRecord
    {
        public double coins;
        public double gems;
        public long version;
        public string lastModified;
        // The platform folds a player's old per-game wallets into the platform wallet the first
        // time it is read, which bumps the version once. A simulated player has no old wallets,
        // but the version bump is visible to a game, so it is reproduced.
        public bool migrated;
        public string migratedAt;
    }

    [Serializable]
    internal sealed class SaveRecord
    {
        public string gameId;
        public string slot;
        public string data;
        public long version;
        public string savedAt;
    }

    [Serializable]
    internal sealed class AchievementRecord
    {
        public string gameId;
        public string achievementId;
        public bool unlocked;
        public string unlockedAt;
        public long progress;
        public bool hasProgress;
    }

    [Serializable]
    internal sealed class GameWalletRecord
    {
        public string gameId;
        public double coins;
        public double gems;
        public long version;
        public string lastModified;
    }

    [Serializable]
    internal sealed class InventoryRecord
    {
        public string gameId;
        public List<CountEntry> items = new List<CountEntry>();
        public List<CountEntry> consumables = new List<CountEntry>();
        public List<ItemStateRecord> itemStates = new List<ItemStateRecord>();
        public string lastModified;

        public long Count(List<CountEntry> entries, string itemId)
        {
            var entry = entries.Find(e => e.key == itemId);
            return entry != null ? entry.value : 0;
        }

        public void Set(List<CountEntry> entries, string itemId, long value)
        {
            var entry = entries.Find(e => e.key == itemId);
            if (entry == null)
            {
                entries.Add(new CountEntry { key = itemId, value = value });
            }
            else
            {
                entry.value = value;
            }
        }
    }

    [Serializable]
    internal sealed class CountEntry
    {
        public string key;
        public long value;
    }

    [Serializable]
    internal sealed class ItemStateRecord
    {
        public string itemId;
        public string source;
        public string lastOrderId;
        public string grantedAt;
    }

    /// <summary>The stored answer to a spend or consume, replayed for a repeated requestId.</summary>
    [Serializable]
    internal sealed class RequestClaim
    {
        public string key;
        public string json;
    }

    [Serializable]
    internal sealed class AdClaimRecord
    {
        public string gameId;
        public string date;
        public int count;
        public string lastClaimAt;
    }

    /// <summary>Reads and writes the state file.</summary>
    internal sealed class SimulatorStateStore
    {
        public const string DefaultDirectory = "Library/SusaPlaySimulator";

        private readonly string _path;

        public SimulatorStateStore(string directory = DefaultDirectory)
        {
            _path = Path.Combine(directory, "state.json");
        }

        public string FilePath => _path;

        /// <summary>
        /// The saved state, or a new one. A file that cannot be read is renamed to
        /// state.json.bak rather than deleted, so a developer who edited it by hand can see
        /// what went wrong.
        /// </summary>
        public SimulatorState Load()
        {
            if (!File.Exists(_path))
            {
                return SimulatorState.CreateNew();
            }

            try
            {
                var state = JsonUtility.FromJson<SimulatorState>(File.ReadAllText(_path));
                if (state != null && !string.IsNullOrEmpty(state.uid))
                {
                    return state;
                }
                throw new FormatException("the file has no player id");
            }
            catch (Exception exception)
            {
                var backup = _path + ".bak";
                try
                {
                    File.Copy(_path, backup, true);
                    File.Delete(_path);
                }
                catch (Exception)
                {
                    // The warning below still tells the developer; a fresh state still begins.
                }
                Debug.LogWarning("[SusaPlay Simulator] Could not read " + _path + " (" + exception.Message +
                                 "). It was set aside as " + backup + " and a new simulated player was created.");
                return SimulatorState.CreateNew();
            }
        }

        public void Save(SimulatorState state)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            // Written beside and then moved over, so a crash mid-write cannot leave half a file.
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(state, true));
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
            File.Move(temporary, _path);
        }

        public void Delete()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }
}
