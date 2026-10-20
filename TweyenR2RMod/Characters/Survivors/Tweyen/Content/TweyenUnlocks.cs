using RoR2;
using RoR2.Achievements;
using RoR2.ExpansionManagement;
using UnityEngine.AddressableAssets;
using GameAssetPaths = RoR2BepInExPack.GameAssetPathsBetter;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// Tweyen's achievements and what they unlock. Each achievement grants one unlockable; several things (e.g. two
    /// skins) can share it. The game finds the achievement classes by their RegisterAchievement attribute; their name
    /// and description are the ACHIEVEMENT_{IDENTIFIER}_NAME / _DESCRIPTION tokens, their icon the unlockable's.
    /// </summary>
    internal static class TweyenUnlocks
    {
        public const string MASTERY_ACHIEVEMENT = "NepsionionTweyenClearGameMonsoon";
        public const string MASTERY_UNLOCKABLE = "Skins.NepsionionTweyen.Mastery";
        public const string MOONSLAYER_ACHIEVEMENT = "NepsionionTweyenKillMithrixMonsoon";
        public const string MOONSLAYER_UNLOCKABLE = "Skins.NepsionionTweyen.Moonslayer";

        public const string WHOLE_ACHIEVEMENT = "NepsionionTweyenKillTwistedScavengerMonsoon";
        public const string WHOLE_UNLOCKABLE = "Skins.NepsionionTweyen.Whole";
        public const string ICARUS_ACHIEVEMENT = "NepsionionTweyenKillFalseSonMonsoon";
        public const string ICARUS_UNLOCKABLE = "Skins.NepsionionTweyen.Icarus";
        public const string HUNTRESS_ACHIEVEMENT = "NepsionionTweyenKillEveryMonster";
        public const string HUNTRESS_UNLOCKABLE = "Skins.NepsionionTweyen.DarkHuntress";
        public const string BETRAYAL_ACHIEVEMENT = "NepsionionTweyenKillNewt";
        public const string BETRAYAL_UNLOCKABLE = "Skins.NepsionionTweyen.Betrayal";
        public const string VOIDED_ACHIEVEMENT = "NepsionionTweyenVoidEndingMonsoon";
        public const string VOIDED_UNLOCKABLE = "Skins.NepsionionTweyen.Voided";
        public const string TRANSCENDENCE_ACHIEVEMENT = "NepsionionTweyenAllSkins";
        public const string TRANSCENDENCE_UNLOCKABLE = "Skins.NepsionionTweyen.Transcendence";
        public const string SAGACITY_ACHIEVEMENT = "NepsionionTweyenMultilockElevenHits";
        public const string SAGACITY_UNLOCKABLE = "Skills.NepsionionTweyen.Hercules";
        public const string EXALTED_ACHIEVEMENT = "NepsionionTweyenHundredItems";
        public const string EXALTED_UNLOCKABLE = "Skills.NepsionionTweyen.EternalSplendor";
        public const string RENUNCIATOR_ACHIEVEMENT = "NepsionionTweyenValiantWeaveKill";
        public const string RENUNCIATOR_UNLOCKABLE = "Skills.NepsionionTweyen.WheelOfDeath";
        public const string REPUDIATOR_ACHIEVEMENT = "NepsionionTweyenTenDebuffs";
        public const string REPUDIATOR_UNLOCKABLE = "Skills.NepsionionTweyen.PeerlessEye";
        /// <summary>Debuffs (Death Mark's count) one enemy must have at once for "Tweyen: Repudiator".</summary>
        public const int REPUDIATOR_DEBUFFS = 10;
        /// <summary>Items (every copy counts) to hold at once for "Tweyen: Exalted Wings".</summary>
        public const int EXALTED_ITEMS = 100;
        /// <summary>Arrows of one Multilock Hail that must hit one enemy for "Tweyen: Sagacity".</summary>
        public const int SAGACITY_HITS = 11;

        /// <summary>Unlocks skins 2 and 3 (Glimmer, Stellar).</summary>
        public static UnlockableDef mastery;
        /// <summary>Unlocks skin 4 (Night).</summary>
        public static UnlockableDef moonslayer;
        /// <summary>Unlocks skin 5 (Silver Relic).</summary>
        public static UnlockableDef whole;
        /// <summary>Unlocks skin 6 (Sunlight).</summary>
        public static UnlockableDef icarus;
        /// <summary>Unlocks skin 7 (Bloody).</summary>
        public static UnlockableDef darkHuntress;
        /// <summary>Unlocks skin 8 (Duality).</summary>
        public static UnlockableDef betrayal;
        /// <summary>Unlocks skin 9 (Umbra).</summary>
        public static UnlockableDef voided;
        /// <summary>Unlocks skin 10 (Gold).</summary>
        public static UnlockableDef transcendence;

        /// <summary>Unlocks bow 3 (Hercules).</summary>
        public static UnlockableDef sagacity;
        /// <summary>Unlocks bow 4 (Bow of Eternal Splendor).</summary>
        public static UnlockableDef exaltedWings;
        /// <summary>Unlocks the Peerless Eye passive.</summary>
        public static UnlockableDef repudiator;
        /// <summary>Unlocks the Wheel of Death utility.</summary>
        public static UnlockableDef renunciator;

        /// <summary>The unlockables of the skins "Tweyen: Transcendence" asks for (every other locked skin).</summary>
        public static UnlockableDef[] OtherSkins => TweyenConfig.UnlockDlcSkins
            ? new[] { mastery, moonslayer, whole, darkHuntress, betrayal } // the DLC skins are already unlocked
            : new[] { mastery, moonslayer, whole, icarus, darkHuntress, betrayal, voided };

        /// <summary>Creates the unlockables (before the skins and skills that use them) and the achievements' text.</summary>
        public static void Init(AssetBundle assetBundle)
        {
            mastery = Unlockable(MASTERY_ACHIEVEMENT, MASTERY_UNLOCKABLE, "Tweyen: Mastery",
                "As Tweyen, beat the game or obliterate on Monsoon.", assetBundle.LoadAsset<Sprite>("texTweyenSkin02"));
            moonslayer = Unlockable(MOONSLAYER_ACHIEVEMENT, MOONSLAYER_UNLOCKABLE, "Tweyen: Moonslayer",
                "As Tweyen, kill Mithrix on Monsoon.", assetBundle.LoadAsset<Sprite>("texTweyenSkin04"));
            whole = Unlockable(WHOLE_ACHIEVEMENT, WHOLE_UNLOCKABLE, "Tweyen: Whole",
                "As Tweyen, kill the Scavenger in A Moment, Whole on Monsoon.", assetBundle.LoadAsset<Sprite>("texTweyenSkin05"));
            icarus = Unlockable(ICARUS_ACHIEVEMENT, ICARUS_UNLOCKABLE, "Tweyen: Icarus",
                "As Tweyen, kill False Son on Monsoon.", assetBundle.LoadAsset<Sprite>("texTweyenSkin06"));
            darkHuntress = Unlockable(HUNTRESS_ACHIEVEMENT, HUNTRESS_UNLOCKABLE, "Tweyen: Dark Huntress",
                "As Tweyen, hunt down every common monster.", assetBundle.LoadAsset<Sprite>("texTweyenSkin07"));
            betrayal = Unlockable(BETRAYAL_ACHIEVEMENT, BETRAYAL_UNLOCKABLE, "Tweyen: Betrayal",
                "As Tweyen, kill Newt <style=cIsDamage>yourself</style> while in the Shop.", assetBundle.LoadAsset<Sprite>("texTweyenSkin08"));
            voided = Unlockable(VOIDED_ACHIEVEMENT, VOIDED_UNLOCKABLE, "Tweyen: Voided",
                "As Tweyen, lose yourself in the Void on Monsoon.", assetBundle.LoadAsset<Sprite>("texTweyenSkin09"));
            transcendence = Unlockable(TRANSCENDENCE_ACHIEVEMENT, TRANSCENDENCE_UNLOCKABLE, "Tweyen: Transcendence",
                "Unlock all the other Tweyen skins.", assetBundle.LoadAsset<Sprite>("texTweyenSkin10"));
            sagacity = Unlockable(SAGACITY_ACHIEVEMENT, SAGACITY_UNLOCKABLE, "Tweyen: Sagacity",
                "As Tweyen, hit a single enemy with <style=cIsDamage>11 arrows</style> shot from a single <style=cIsDamage>Asteroid Jaeger</style>.",
                assetBundle.LoadAsset<Sprite>("Hercules"));
            exaltedWings = Unlockable(EXALTED_ACHIEVEMENT, EXALTED_UNLOCKABLE, "Tweyen: Exalted Wings",
                "As Tweyen, get 100 items in a single run.", assetBundle.LoadAsset<Sprite>("BowOfEternalSplendor"));
            repudiator = Unlockable(REPUDIATOR_ACHIEVEMENT, REPUDIATOR_UNLOCKABLE, "Tweyen: Repudiator",
                "As Tweyen, apply 10 debuffs simultaneously on a single enemy.", assetBundle.LoadAsset<Sprite>("PeerlessEyeSkill"));
            renunciator = Unlockable(RENUNCIATOR_ACHIEVEMENT, RENUNCIATOR_UNLOCKABLE, "Tweyen: Renunciator",
                "As Tweyen, kill an enemy with the arrow shot by Valiant Weave.", assetBundle.LoadAsset<Sprite>("WheelOfDeathSkill"));
            TweyenDebuffWatch.Init();

            // skins only an expansion's content can unlock belong to that expansion: like the game's own DLC skins,
            // players without it don't see them (and "Transcendence" doesn't ask for them)
            icarus.requiredExpansion = Addressables.LoadAssetAsync<ExpansionDef>(GameAssetPaths.RoR2_DLC2_Common.DLC2_asset).WaitForCompletion();   // False Son
            voided.requiredExpansion = Addressables.LoadAssetAsync<ExpansionDef>(GameAssetPaths.RoR2_DLC1_Common.DLC1_asset).WaitForCompletion();   // Voidling
            TweyenHunt.Init();
            TweyenAchievementLog.Init(); // TEMP
        }

        /// <summary>The achievement that unlocks bow <paramref name="index"/> (1 = the default, in the loadout's order; null: always available).</summary>
        public static UnlockableDef BowUnlockable(int index)
        {
            switch (index)
            {
                case 3: return sagacity; // Hercules
                case 4: return exaltedWings; // Bow of Eternal Splendor
                default: return null;
            }
        }

        /// <summary>Whether the current run is on Monsoon or harder (Eclipse counts), like the vanilla masteries.</summary>
        public static bool HardMode()
        {
            DifficultyDef difficulty = Run.instance ? DifficultyCatalog.GetDifficultyDef(Run.instance.selectedDifficulty) : null;
            return difficulty != null && difficulty.countsAsHardMode;
        }

        private static UnlockableDef Unlockable(string achievement, string identifier, string name, string description, Sprite icon)
        {
            string nameToken = Modules.Tokens.GetAchievementNameToken(achievement);
            Modules.Language.Add(nameToken, name);
            Modules.Language.Add(Modules.Tokens.GetAchievementDescriptionToken(achievement), description);
            return Modules.Content.CreateAndAddUnlockbleDef(identifier, nameToken, icon);
        }
    }

    /// <summary>"Tweyen: Mastery": as Tweyen, win (or obliterate) on Monsoon or harder, like the vanilla masteries.</summary>
    [RegisterAchievement(TweyenUnlocks.MASTERY_ACHIEVEMENT, TweyenUnlocks.MASTERY_UNLOCKABLE, null, 10u, null)]
    public class TweyenMasteryAchievement : BasePerSurvivorClearGameMonsoonAchievement
    {
        public override BodyIndex LookUpRequiredBodyIndex()
        {
            return BodyCatalog.FindBodyIndex(TweyenSurvivor.instance.bodyName);
        }

        // TEMP diagnostics: what the run ended with
        public override void OnBodyRequirementMet()
        {
            base.OnBodyRequirementMet();
            Run.onClientGameOverGlobal += LogGameOver;
        }

        public override void OnBodyRequirementBroken()
        {
            Run.onClientGameOverGlobal -= LogGameOver;
            base.OnBodyRequirementBroken();
        }

        private void LogGameOver(Run run, RunReport report)
        {
            DifficultyDef difficulty = DifficultyCatalog.GetDifficultyDef(report.ruleBook.FindDifficulty());
            TweyenAchievementLog.Write($"Mastery: run over, ending {(report.gameEnding ? report.gameEnding.cachedName : "none")}, win {(report.gameEnding && report.gameEnding.isWin)}, difficulty {difficulty?.nameToken} (Monsoon or harder: {difficulty != null && difficulty.countsAsHardMode})");
        }
    }


    /// <summary>
    /// An achievement for being Tweyen when one of some enemies dies (anyone's kill counts) on Monsoon or harder.
    /// Checked by the host (deaths are only known there), like the vanilla Mithrix achievements; the server half is a
    /// <see cref="TweyenKillServerAchievement"/> naming the enemies.
    /// </summary>
    public abstract class TweyenKillAchievement : BaseAchievement
    {
        public override BodyIndex LookUpRequiredBodyIndex()
        {
            return BodyCatalog.FindBodyIndex(TweyenSurvivor.instance.bodyName);
        }

        public override void OnBodyRequirementMet()
        {
            base.OnBodyRequirementMet();
            SetServerTracked(true);
        }

        public override void OnBodyRequirementBroken()
        {
            SetServerTracked(false);
            base.OnBodyRequirementBroken();
        }
    }

    public abstract class TweyenKillServerAchievement : BaseServerAchievement
    {
        /// <summary>Body names of the enemies whose death grants it.</summary>
        protected abstract string[] Victims { get; }
        /// <summary>Only if the player's own character lands the killing blow (not anyone, not their minions).</summary>
        protected virtual bool MustBeKiller => false;
        /// <summary>Whether it also needs Monsoon or harder.</summary>
        protected virtual bool NeedsHardMode => true;

        private readonly System.Collections.Generic.HashSet<BodyIndex> victims = new System.Collections.Generic.HashSet<BodyIndex>();

        public override void OnInstall()
        {
            base.OnInstall();
            foreach (string name in Victims)
            {
                BodyIndex index = BodyCatalog.FindBodyIndex(name);
                if (index == BodyIndex.None) Log.Warning($"Tweyen achievement {GetType().DeclaringType?.Name}: no body named {name}");
                else victims.Add(index);
            }
            GlobalEventManager.onDeathProcServer += OnDeath;
        }

        public override void OnUninstall()
        {
            GlobalEventManager.onDeathProcServer -= OnDeath;
            base.OnUninstall();
        }

        private void OnDeath(DamageReport report)
        {
            if (!victims.Contains(report.victimBodyIndex)) return;
            bool killer = report.attackerMaster && report.attackerMaster == serverAchievementTracker.networkUser.master;
            TweyenAchievementLog.Write($"{GetType().DeclaringType?.Name}: {BodyCatalog.GetBodyName(report.victimBodyIndex)} died, Monsoon or harder {TweyenUnlocks.HardMode()} (needed {NeedsHardMode}), killed by this player {killer} (needed {MustBeKiller})");
            if (NeedsHardMode && !TweyenUnlocks.HardMode()) return;
            if (MustBeKiller && !killer) return;
            Grant();
        }
    }

    /// <summary>"Tweyen: Moonslayer": be Tweyen when Mithrix dies (his last phase) on Monsoon or harder.</summary>
    [RegisterAchievement(TweyenUnlocks.MOONSLAYER_ACHIEVEMENT, TweyenUnlocks.MOONSLAYER_UNLOCKABLE, null, 10u, typeof(ServerAchievement))]
    public class TweyenMoonslayerAchievement : TweyenKillAchievement
    {
        private class ServerAchievement : TweyenKillServerAchievement
        {
            protected override string[] Victims => new[] { "BrotherHurtBody" };
        }
    }

    /// <summary>
    /// "Tweyen: Whole": be Tweyen when one of A Moment, Whole's Twisted Scavengers dies on Monsoon or harder (they only
    /// appear there).
    /// </summary>
    [RegisterAchievement(TweyenUnlocks.WHOLE_ACHIEVEMENT, TweyenUnlocks.WHOLE_UNLOCKABLE, null, 10u, typeof(ServerAchievement))]
    public class TweyenWholeAchievement : TweyenKillAchievement
    {
        private class ServerAchievement : TweyenKillServerAchievement
        {
            protected override string[] Victims => new[] { "ScavLunar1Body", "ScavLunar2Body", "ScavLunar3Body", "ScavLunar4Body" };
        }
    }

    /// <summary>
    /// "Tweyen: Icarus": be Tweyen when the False Son boss finally dies on Monsoon or harder. His body carries on
    /// between phases, so this listens for his final death like the vanilla False Son achievements.
    /// </summary>
    [RegisterAchievement(TweyenUnlocks.ICARUS_ACHIEVEMENT, TweyenUnlocks.ICARUS_UNLOCKABLE, null, 10u, typeof(ServerAchievement))]
    public class TweyenIcarusAchievement : TweyenKillAchievement
    {
        private class ServerAchievement : BaseServerAchievement
        {
            public override void OnInstall()
            {
                base.OnInstall();
                EntityStates.FalseSonBoss.SkyJumpDeathState.falseSonDeathEvent += OnFalseSonDeath;
            }

            public override void OnUninstall()
            {
                EntityStates.FalseSonBoss.SkyJumpDeathState.falseSonDeathEvent -= OnFalseSonDeath;
                base.OnUninstall();
            }

            private void OnFalseSonDeath()
            {
                TweyenAchievementLog.Write($"Icarus: False Son defeated, Monsoon or harder {TweyenUnlocks.HardMode()}");
                if (TweyenUnlocks.HardMode()) Grant();
            }
        }
    }

    /// <summary>
    /// "Tweyen: Dark Huntress": over any number of runs, be Tweyen when one of each common monster dies (see
    /// <see cref="TweyenHunt"/>). Tracked on each player's own game.
    /// </summary>
    [RegisterAchievement(TweyenUnlocks.HUNTRESS_ACHIEVEMENT, TweyenUnlocks.HUNTRESS_UNLOCKABLE, null, 22u, null)]
    public class TweyenDarkHuntressAchievement : BaseAchievement
    {
        public override void OnInstall()
        {
            base.OnInstall();
            TweyenHunt.onProgress += OnProgress;
            Check();
        }

        public override void OnUninstall()
        {
            TweyenHunt.onProgress -= OnProgress;
            base.OnUninstall();
        }

        private void OnProgress(UserProfile profile)
        {
            if (profile == localUser.userProfile) Check();
        }

        private void Check()
        {
            TweyenAchievementLog.Write("Dark Huntress: still to hunt: " + string.Join(", ", TweyenHunt.Missing(localUser)));
            if (TweyenHunt.Complete(localUser)) Grant();
        }
    }

    /// <summary>
    /// "Tweyen: Betrayal": as Tweyen, land the killing blow on Newt in the Bazaar Between (actually killing him, not
    /// being thrown out). Any difficulty.
    /// </summary>
    [RegisterAchievement(TweyenUnlocks.BETRAYAL_ACHIEVEMENT, TweyenUnlocks.BETRAYAL_UNLOCKABLE, null, 12u, typeof(ServerAchievement))]
    public class TweyenBetrayalAchievement : TweyenKillAchievement
    {
        private class ServerAchievement : TweyenKillServerAchievement
        {
            protected override string[] Victims => new[] { "ShopkeeperBody" };
            protected override bool MustBeKiller => true;
            protected override bool NeedsHardMode => false;
        }
    }

    /// <summary>
    /// "Tweyen: Voided": as Tweyen, end a run on Monsoon or harder by entering the portal that opens after Voidling
    /// dies (the game's Void ending), checked at the end of the run like the mastery.
    /// </summary>
    [RegisterAchievement(TweyenUnlocks.VOIDED_ACHIEVEMENT, TweyenUnlocks.VOIDED_UNLOCKABLE, null, 10u, null)]
    public class TweyenVoidedAchievement : BaseAchievement
    {
        public override BodyIndex LookUpRequiredBodyIndex()
        {
            return BodyCatalog.FindBodyIndex(TweyenSurvivor.instance.bodyName);
        }

        public override void OnBodyRequirementMet()
        {
            base.OnBodyRequirementMet();
            Run.onClientGameOverGlobal += OnGameOver;
        }

        public override void OnBodyRequirementBroken()
        {
            Run.onClientGameOverGlobal -= OnGameOver;
            base.OnBodyRequirementBroken();
        }

        private void OnGameOver(Run run, RunReport report)
        {
            DifficultyDef difficulty = DifficultyCatalog.GetDifficultyDef(report.ruleBook.FindDifficulty());
            TweyenAchievementLog.Write($"Voided: run over, ending {(report.gameEnding ? report.gameEnding.cachedName : "none")} (Void ending {report.gameEnding == DLC1Content.GameEndings.VoidEnding}), Monsoon or harder {difficulty != null && difficulty.countsAsHardMode}");
            if (report.gameEnding != DLC1Content.GameEndings.VoidEnding) return;
            if (difficulty != null && difficulty.countsAsHardMode) Grant();
        }
    }

    /// <summary>
    /// "Tweyen: Transcendence": unlock every other locked Tweyen skin. Skins of expansions the player doesn't own
    /// (hidden from them, like the game's own DLC skins) aren't asked for. Checked on this player's profile whenever
    /// it unlocks something.
    /// </summary>
    [RegisterAchievement(TweyenUnlocks.TRANSCENDENCE_ACHIEVEMENT, TweyenUnlocks.TRANSCENDENCE_UNLOCKABLE, null, 11u, null)]
    public class TweyenTranscendenceAchievement : BaseAchievement
    {
        public override void OnInstall()
        {
            base.OnInstall();
            UserProfile.onUnlockableGranted += OnUnlockableGranted;
            Check();
        }

        public override void OnUninstall()
        {
            UserProfile.onUnlockableGranted -= OnUnlockableGranted;
            base.OnUninstall();
        }

        private void OnUnlockableGranted(UserProfile profile, UnlockableDef unlockable)
        {
            if (profile == userProfile) Check();
        }

        private void Check()
        {
            var missing = new System.Collections.Generic.List<string>();
            foreach (UnlockableDef skin in TweyenUnlocks.OtherSkins)
            {
                bool available = RoR2.EntitlementManagement.EntitlementManager.localUserEntitlementTracker.UserHasEntitlementForUnlockable(localUser, skin);
                if (available && !userProfile.HasUnlockable(skin)) missing.Add(skin.cachedName);
            }
            TweyenAchievementLog.Write("Transcendence: skins still locked: " + (missing.Count == 0 ? "none" : string.Join(", ", missing)));
            if (missing.Count == 0) Grant();
        }
    }

    /// <summary>
    /// "Tweyen: Sagacity": as Tweyen, hit one enemy with <see cref="TweyenUnlocks.SAGACITY_HITS"/> arrows of the
    /// same Multilock Hail (counted by <see cref="Projectiles.MultilockArrowImpact"/>). Any difficulty; checked by the host.
    /// </summary>
    [RegisterAchievement(TweyenUnlocks.SAGACITY_ACHIEVEMENT, TweyenUnlocks.SAGACITY_UNLOCKABLE, null, 10u, typeof(ServerAchievement))]
    public class TweyenSagacityAchievement : TweyenKillAchievement
    {
        private class ServerAchievement : BaseServerAchievement
        {
            public override void OnInstall()
            {
                base.OnInstall();
                Projectiles.MultilockArrowImpact.onCastHits += OnCastHits;
            }

            public override void OnUninstall()
            {
                Projectiles.MultilockArrowImpact.onCastHits -= OnCastHits;
                base.OnUninstall();
            }

            private void OnCastHits(GameObject owner, int hits)
            {
                CharacterBody body = owner ? owner.GetComponent<CharacterBody>() : null;
                if (hits >= 5) TweyenAchievementLog.Write($"Sagacity: {hits} arrows of one Multilock Hail on one enemy (this player's: {body && body.master == serverAchievementTracker.networkUser.master})");
                if (hits < TweyenUnlocks.SAGACITY_HITS) return;
                if (body && body.master && body.master == serverAchievementTracker.networkUser.master) Grant();
            }
        }
    }

    /// <summary>
    /// "Tweyen: Exalted Wings": as Tweyen, hold <see cref="TweyenUnlocks.EXALTED_ITEMS"/> items at once in a run, every
    /// copy counting (3 Tri-Tip Daggers = 3), items of every tier (hidden internal items have none). Checked on the
    /// player's own game whenever their inventory changes.
    /// </summary>
    [RegisterAchievement(TweyenUnlocks.EXALTED_ACHIEVEMENT, TweyenUnlocks.EXALTED_UNLOCKABLE, null, 10u, null)]
    public class TweyenExaltedWingsAchievement : BaseAchievement
    {
        private int loggedTens = -1; // TEMP diagnostics

        public override BodyIndex LookUpRequiredBodyIndex()
        {
            return BodyCatalog.FindBodyIndex(TweyenSurvivor.instance.bodyName);
        }

        public override void OnBodyRequirementMet()
        {
            base.OnBodyRequirementMet();
            Inventory.onInventoryChangedGlobal += OnInventoryChanged;
            if (localUser.cachedMaster) OnInventoryChanged(localUser.cachedMaster.inventory);
        }

        public override void OnBodyRequirementBroken()
        {
            Inventory.onInventoryChangedGlobal -= OnInventoryChanged;
            base.OnBodyRequirementBroken();
        }

        private void OnInventoryChanged(Inventory inventory)
        {
            if (!inventory || !localUser.cachedMaster || inventory != localUser.cachedMaster.inventory) return;
            int items = 0;
            foreach (ItemTierDef tier in ItemTierCatalog.allItemTierDefs)
            {
                if (tier && tier.tier != ItemTier.NoTier) items += inventory.GetTotalItemCountOfTier(tier.tier);
            }
            if (items / 10 != loggedTens)
            {
                loggedTens = items / 10;
                TweyenAchievementLog.Write($"Exalted Wings: {items} items");
            }
            if (items >= TweyenUnlocks.EXALTED_ITEMS) Grant();
        }
    }

    /// <summary>
    /// "Tweyen: Repudiator": as Tweyen, have an enemy carry <see cref="TweyenUnlocks.REPUDIATOR_DEBUFFS"/> debuffs at
    /// once (counted like Death Mark), checked whenever she may have debuffed it (see <see cref="TweyenDebuffWatch"/>).
    /// Any difficulty; checked by the host.
    /// </summary>
    [RegisterAchievement(TweyenUnlocks.REPUDIATOR_ACHIEVEMENT, TweyenUnlocks.REPUDIATOR_UNLOCKABLE, null, 6u, typeof(ServerAchievement))]
    public class TweyenRepudiatorAchievement : TweyenKillAchievement
    {
        private class ServerAchievement : BaseServerAchievement
        {
            private int mostDebuffs; // TEMP diagnostics

            public override void OnInstall()
            {
                base.OnInstall();
                TweyenDebuffWatch.onEnemyDebuffed += OnEnemyDebuffed;
            }

            public override void OnUninstall()
            {
                TweyenDebuffWatch.onEnemyDebuffed -= OnEnemyDebuffed;
                base.OnUninstall();
            }

            private void OnEnemyDebuffed(CharacterBody victim, CharacterBody attacker, int debuffs)
            {
                bool mine = attacker.master && attacker.master == serverAchievementTracker.networkUser.master;
                if (mine && debuffs > mostDebuffs)
                {
                    mostDebuffs = debuffs;
                    TweyenAchievementLog.Write($"Repudiator: new highest debuff count {debuffs} on {BodyCatalog.GetBodyName(victim.bodyIndex)}");
                }
                if (debuffs >= TweyenUnlocks.REPUDIATOR_DEBUFFS && mine) Grant();
            }
        }
    }

    /// <summary>
    /// "Tweyen: Renunciator": as Tweyen, kill an enemy with Valiant Weave's arrow (the killing blow's inflictor is a
    /// <see cref="Projectiles.TweyenArrows.valiantArrow"/> she fired). Any difficulty; checked by the host.
    /// </summary>
    [RegisterAchievement(TweyenUnlocks.RENUNCIATOR_ACHIEVEMENT, TweyenUnlocks.RENUNCIATOR_UNLOCKABLE, null, 6u, typeof(ServerAchievement))]
    public class TweyenRenunciatorAchievement : TweyenKillAchievement
    {
        private class ServerAchievement : BaseServerAchievement
        {
            public override void OnInstall()
            {
                base.OnInstall();
                GlobalEventManager.onDeathProcServer += OnDeath;
            }

            public override void OnUninstall()
            {
                GlobalEventManager.onDeathProcServer -= OnDeath;
                base.OnUninstall();
            }

            private void OnDeath(DamageReport report)
            {
                if (!report.attackerMaster || report.attackerMaster != serverAchievementTracker.networkUser.master) return;
                GameObject inflictor = report.damageInfo != null ? report.damageInfo.inflictor : null;
                RoR2.Projectile.ProjectileController arrow = inflictor ? inflictor.GetComponent<RoR2.Projectile.ProjectileController>() : null;
                bool valiant = arrow && arrow.catalogIndex == ProjectileCatalog.GetProjectileIndex(Projectiles.TweyenArrows.valiantArrow);
                if (valiant) Grant();
            }
        }
    }
}
