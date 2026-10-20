using RoR2;
using RoR2.EntitlementManagement;
using RoR2.ExpansionManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Path = System.IO.Path;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// "Tweyen: Dark Huntress" progress: which common monsters each profile has seen die while playing Tweyen.
    /// - Common monsters: every monster logbook entry that isn't a boss (champion). Separate logbook entries are
    ///   separate monsters (Lemurian and Elder Lemurian, the three Lunar Chimeras). Versions of one entry (bodies that
    ///   share it, e.g. logbook-only stand-ins) and elites (the same body) count for it.
    /// - <see cref="excluded"/> bodies are never required on their own: an entry made only of them isn't required, but
    ///   one that shares an entry with a required monster still counts for it.
    /// - Only monsters from expansions the player owns are required.
    /// - A kill counts when the monster dies (anyone's kill) while the local player has Tweyen picked, dead or alive.
    ///   Every client sees the deaths, so each player's own game records them.
    /// - Kept across runs in the game's data folder (%USERPROFILE%/AppData/LocalLow/Hopoo Games, LLC/Risk of Rain 2/
    ///   TweyenR2RMod/hunt_&lt;profile&gt;.txt, one logbook entry per line), shared by every mod manager profile.
    /// </summary>
    internal static class TweyenHunt
    {
        /// <summary>Bodies with a logbook entry that aren't required on their own (special enemies), by body name.</summary>
        private static readonly HashSet<string> excluded = new HashSet<string>
        {
            "PolymorphLemurianBody", // an unknown Lemurian version
            "MimicBody",             // too rare
        };

        /// <summary>Called on the client when a profile's progress changes.</summary>
        public static event Action<UserProfile> onProgress;

        private static List<Monster> monsters;
        private static readonly Dictionary<string, HashSet<string>> progress = new Dictionary<string, HashSet<string>>();

        public static void Init()
        {
            CharacterBody.onBodyDestroyGlobal += OnBodyDestroyed;
        }

        /// <summary>One monster (logbook entry): the bodies that count for it.</summary>
        public class Monster
        {
            public string log;
            public List<CharacterBody> bodies;
            public ExpansionDef expansion;
        }

        /// <summary>All common monsters (any expansion), found once the body catalog is ready.</summary>
        public static List<Monster> Monsters
        {
            get
            {
                if (monsters != null) return monsters;
                monsters = BodyCatalog.allBodyPrefabBodyBodyComponents
                    .Where(b => b && !b.isChampion
                        && b.GetComponent<DeathRewards>() && b.GetComponent<DeathRewards>().logUnlockableDef)
                    .GroupBy(b => b.GetComponent<DeathRewards>().logUnlockableDef.cachedName)
                    .Where(g => g.Any(b => !excluded.Contains(b.name)))
                    .Select(g => new Monster { log = g.Key, bodies = g.ToList(), expansion = g.Select(Expansion).FirstOrDefault(e => e) })
                    .OrderBy(m => m.log)
                    .ToList();
                return monsters;
            }
        }

        private static ExpansionDef Expansion(CharacterBody body)
        {
            ExpansionRequirementComponent req = body.GetComponent<ExpansionRequirementComponent>();
            return req ? req.requiredExpansion : null;
        }

        /// <summary>The common monsters this player has to hunt: the base game's and those of expansions they own.</summary>
        public static List<Monster> Required(LocalUser user)
        {
            return Monsters.Where(m => !m.expansion || !m.expansion.requiredEntitlement
                || EntitlementManager.localUserEntitlementTracker.UserHasEntitlement(user, m.expansion.requiredEntitlement)).ToList();
        }

        /// <summary>TEMP diagnostics: the required monsters not hunted yet.</summary>
        public static List<string> Missing(LocalUser user)
        {
            HashSet<string> hunted = Load(user.userProfile);
            return Required(user).Where(m => !hunted.Contains(m.log)).Select(m => m.bodies[0].name).ToList();
        }

        public static bool Complete(LocalUser user)
        {
            HashSet<string> hunted = Load(user.userProfile);
            return Required(user).All(m => hunted.Contains(m.log));
        }

        private static void OnBodyDestroyed(CharacterBody body)
        {
            // died (not just removed at the end of a stage), and not one of the players' allies
            if (!body || !body.healthComponent || body.healthComponent.alive) return;
            if (body.teamComponent && body.teamComponent.teamIndex == TeamIndex.Player) return;
            string name = BodyCatalog.GetBodyName(body.bodyIndex);
            Monster monster = string.IsNullOrEmpty(name) ? null : Monsters.FirstOrDefault(m => m.bodies.Any(b => b.name == name));
            if (monster == null) return;

            BodyIndex tweyen = BodyCatalog.FindBodyIndex(TweyenSurvivor.instance.bodyName);
            foreach (LocalUser user in LocalUserManager.readOnlyLocalUsersList)
            {
                NetworkUser networkUser = user.currentNetworkUser;
                if (!networkUser || networkUser.bodyIndexPreference != tweyen || user.userProfile == null) continue;
                HashSet<string> hunted = Load(user.userProfile);
                if (!hunted.Add(monster.log)) continue;
                Save(user.userProfile, hunted);
                if (!user.userProfile.HasAchievement(TweyenUnlocks.HUNTRESS_ACHIEVEMENT))
                {
                    TweyenAchievementLog.Write($"Dark Huntress: hunted {monster.log} ({name})"); // TEMP
                }
                onProgress?.Invoke(user.userProfile);
            }
        }

        /// <summary>
        /// In the game's own data folder, shared by every mod manager profile (like the save it goes with), not in a
        /// profile's BepInEx folder.
        /// </summary>
        private static string FilePath(UserProfile profile)
        {
            return Path.Combine(UnityEngine.Application.persistentDataPath, "TweyenR2RMod", "hunt_" + profile.fileName + ".txt");
        }

        /// <summary>Where the first test builds kept it (this mod manager profile's config folder): still read.</summary>
        private static string OldFilePath(UserProfile profile)
        {
            return Path.Combine(BepInEx.Paths.ConfigPath, "TweyenR2RMod", "hunt_" + profile.fileName + ".txt");
        }

        private static HashSet<string> Load(UserProfile profile)
        {
            string key = profile.fileName ?? "";
            if (progress.TryGetValue(key, out HashSet<string> hunted)) return hunted;
            hunted = new HashSet<string>();
            try
            {
                foreach (string path in new[] { FilePath(profile), OldFilePath(profile) })
                {
                    if (File.Exists(path)) hunted.UnionWith(File.ReadAllLines(path).Where(l => l.Length > 0));
                }
            }
            catch (Exception e)
            {
                Log.Warning("Dark Huntress progress could not be read: " + e.Message);
            }
            progress[key] = hunted;
            return hunted;
        }

        private static void Save(UserProfile profile, HashSet<string> hunted)
        {
            try
            {
                string path = FilePath(profile);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, hunted.OrderBy(n => n));
            }
            catch (Exception e)
            {
                Log.Warning("Dark Huntress progress could not be saved: " + e.Message);
            }
        }
    }
}
