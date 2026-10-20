using RoR2;
using RoR2.Achievements;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// TEMP diagnostics (switched off: the calls are commented out, uncomment them to test again) for Tweyen's achievements (lines tagged [TweyenAch] in the BepInEx log): an achievement whose
    /// unlockable is missing, when each starts/stops watching (on the player's game and on the host), when it is
    /// granted, and (from the achievements themselves, via <see cref="Write"/>) what their checks see. The game only
    /// runs an achievement's checks while it is still locked, so earned ones stay quiet.
    /// </summary>
    internal static class TweyenAchievementLog
    {
        private const string Prefix = "NepsionionTweyen";

        public static void Init()
        {
            AchievementManager.onAchievementsRegistered += OnRegistered;
            On.RoR2.Achievements.BaseAchievement.OnInstall += (orig, self) => { orig(self); Ours(self.achievementDef, "installed (watching this profile)"); };
            On.RoR2.Achievements.BaseAchievement.OnUninstall += (orig, self) => { Ours(self.achievementDef, "uninstalled"); orig(self); };
            On.RoR2.Achievements.BaseAchievement.OnBodyRequirementMet += (orig, self) => { orig(self); Ours(self.achievementDef, "playing Tweyen: tracking"); };
            On.RoR2.Achievements.BaseAchievement.OnBodyRequirementBroken += (orig, self) => { Ours(self.achievementDef, "not playing Tweyen: not tracking"); orig(self); };
            On.RoR2.Achievements.BaseAchievement.Grant += (orig, self) => { Ours(self.achievementDef, "GRANTED"); orig(self); };
            On.RoR2.Achievements.BaseServerAchievement.OnInstall += (orig, self) => { orig(self); Ours(self.achievementDef, "host tracking installed"); };
            On.RoR2.Achievements.BaseServerAchievement.Grant += (orig, self) => { Ours(self.achievementDef, "host GRANTED (sent to the player)"); orig(self); };
        }

        private static void OnRegistered()
        {
            foreach (AchievementDef def in AchievementManager.allAchievementDefs)
            {
                if (def == null || !def.identifier.StartsWith(Prefix)) continue;
                // only problems: an achievement whose reward isn't registered
                if (!UnlockableCatalog.GetUnlockableDef(def.unlockableRewardIdentifier))
                {
                    Write($"{def.identifier}: its unlockable {def.unlockableRewardIdentifier} is MISSING");
                }
            }
        }

        private static void Ours(AchievementDef def, string what)
        {
            if (def != null && def.identifier.StartsWith(Prefix)) Write($"{def.identifier}: {what}");
        }

        public static void Write(string message)
        {
            Log.Info("[TweyenAch] " + message);
        }
    }
}
