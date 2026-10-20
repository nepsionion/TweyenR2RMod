using RoR2;
using RoR2.Skills;
using System;
using System.Collections.Generic;
using TweyenR2RMod.Modules;
using TweyenR2RMod.Survivors.Tweyen.SkillStates;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// Tweyen's passives, the choices of the loadout's first row: Twin-Star Huntress (below) or Peerless Eye (skill hits
    /// build up attack speed and a guaranteed-crit secondary or special: see <see cref="Components.PeerlessEyeTracker"/>).
    ///
    /// Twin-Star Huntress: Tweyen's hits have a 50% x proc coefficient chance to
    /// inflict a random debuff (Clincher's chance is also multiplied by her attack speed). Chances above 100% inflict
    /// more than one: every full 100% is one, the rest is one more roll. Luck (57 Leaf Clover) improves the rolls.
    /// - Counts every hit she deals with a proc coefficient (item procs included), not damage over time.
    /// - Nothing is applied to an enemy the hit killed.
    /// - Each debuff is picked among the ones that would do something: a debuff that doesn't stack is left out if the
    ///   enemy already has it.
    /// Server only.
    /// It also makes Clincher deal +<see cref="CLINCHER_BONUS_PER_DEBUFF"/> damage coefficient (additive: 400% -> 450%
    /// -> 500%...; 600% at 4) per unique debuff on each enemy it hits (stacks don't count); applied in Clincher's shot.
    /// </summary>
    internal static class TweyenPassive
    {
        public const float BASE_CHANCE = 50f;
        public const float DEFAULT_DURATION = 5f;
        public const float CLINCHER_BONUS_PER_DEBUFF = 0.5f;

        private enum Kind { Timed, Permanent, DamageOverTime }

        private struct Debuff
        {
            /// <summary>The game's buff (read when needed: the content classes fill in after loading).</summary>
            public Func<BuffDef> buff;
            public Kind kind;
            public DotController.DotIndex dot;
            public float duration;
            public Debuff(Func<BuffDef> buff, Kind kind, float duration, DotController.DotIndex dot = DotController.DotIndex.None)
            {
                this.buff = buff; this.kind = kind; this.duration = duration; this.dot = dot;
            }
        }

        /// <summary>The debuffs it picks from. Damage over time is as strong as one Tri-Tip Dagger bleed.</summary>
        private static readonly Debuff[] debuffs =
        {
            new Debuff(() => JunkContent.Buffs.Slow30, Kind.Timed, DEFAULT_DURATION),
            new Debuff(() => RoR2Content.Buffs.BeetleJuice, Kind.Timed, DEFAULT_DURATION),
            new Debuff(() => RoR2Content.Buffs.Bleeding, Kind.DamageOverTime, 3f, DotController.DotIndex.Bleed),
            new Debuff(() => RoR2Content.Buffs.Blight, Kind.DamageOverTime, 5f, DotController.DotIndex.Blight),
            new Debuff(() => RoR2Content.Buffs.OnFire, Kind.DamageOverTime, 4f, DotController.DotIndex.Burn),
            new Debuff(() => DLC1Content.Buffs.Fracture, Kind.DamageOverTime, 3f, DotController.DotIndex.Fracture),
            new Debuff(() => RoR2Content.Buffs.Fruiting, Kind.Timed, DEFAULT_DURATION),
            new Debuff(() => RoR2Content.Buffs.HealingDisabled, Kind.Timed, DEFAULT_DURATION),
            new Debuff(() => DLC2Content.Buffs.lunarruin, Kind.DamageOverTime, 5f, DotController.DotIndex.LunarRuin),
            new Debuff(() => DLC1Content.Buffs.PermanentDebuff, Kind.Permanent, 0f),
            new Debuff(() => DLC3Content.Buffs.SolusWingWeakpointDestroyed, Kind.Timed, DEFAULT_DURATION),
            new Debuff(() => RoR2Content.Buffs.Weak, Kind.Timed, DEFAULT_DURATION),
            new Debuff(() => DLC2Content.Buffs.Oiled, Kind.Timed, DEFAULT_DURATION),
        };

        public static SkillDef passiveSkillDef;
        public static SkillDef peerlessEyeSkillDef;

        /// <summary>Creates the passive's loadout row; call first in InitializeSkills so it is the first row.</summary>
        public static void InitSkill(GameObject bodyPrefab, AssetBundle assetBundle)
        {
            string prefix = TweyenSurvivor.TWEYEN_PREFIX;
            Modules.Language.Add(prefix + "LOADOUT_PASSIVE", "Passive");
            Modules.Language.Add(prefix + "PASSIVE_TWIN_STAR_HUNTRESS_NAME", "Twin-Star Huntress");
            Modules.Language.Add(prefix + "PASSIVE_TWIN_STAR_HUNTRESS_DESCRIPTION", $"Hits have a <style=cIsDamage>{BASE_CHANCE}%</style> chance to inflict a <style=cIsDamage>random debuff</style>. Clincher deals <style=cIsDamage>+{100f * CLINCHER_BONUS_PER_DEBUFF}% damage</style> for each unique debuff on the enemy hit.");

            GenericSkill slot = Skills.CreateGenericSkillWithSkillFamily(bodyPrefab, "TweyenPassive", "Passive");
            slot.loadoutTitleToken = prefix + "LOADOUT_PASSIVE";
            passiveSkillDef = Skills.CreateSkillDef(new SkillDefInfo
            {
                skillName = "TweyenTwinStarHuntress",
                skillNameToken = prefix + "PASSIVE_TWIN_STAR_HUNTRESS_NAME",
                skillDescriptionToken = prefix + "PASSIVE_TWIN_STAR_HUNTRESS_DESCRIPTION",
                skillIcon = assetBundle.LoadAsset<Sprite>("MerculightSkill"),
                activationState = new EntityStates.SerializableEntityStateType(typeof(NoOp)),
                activationStateMachineName = "Weapon",
                baseRechargeInterval = 0f,
                baseMaxStock = 1,
                isCombatSkill = false,
                mustKeyPress = true,
            });
            Modules.Language.Add(prefix + "PASSIVE_PEERLESS_EYE_NAME", "Peerless Eye");
            Modules.Language.Add(prefix + "PASSIVE_PEERLESS_EYE_DESCRIPTION",
                $"Hitting enemies with skills grants <style=cIsDamage>+{100f * TweyenBuffs.ULTRASIGHT_ATTACK_SPEED:0}% attack speed</style> " +
                $"(up to {100f * TweyenBuffs.ULTRASIGHT_ATTACK_SPEED * TweyenBuffs.ULTRASIGHT_MAX_STACKS:0}%). " +
                $"At {TweyenBuffs.ULTRASIGHT_READY_STACKS} or more, your next Secondary or Special <style=cIsDamage>always crits</style> and resets the bonus.");
            peerlessEyeSkillDef = Skills.CreateSkillDef(new SkillDefInfo
            {
                skillName = "TweyenPeerlessEye",
                skillNameToken = prefix + "PASSIVE_PEERLESS_EYE_NAME",
                skillDescriptionToken = prefix + "PASSIVE_PEERLESS_EYE_DESCRIPTION",
                skillIcon = assetBundle.LoadAsset<Sprite>("PeerlessEyeSkill"),
                activationState = new EntityStates.SerializableEntityStateType(typeof(NoOp)),
                activationStateMachineName = "Weapon",
                baseRechargeInterval = 0f,
                baseMaxStock = 1,
                isCombatSkill = false,
                mustKeyPress = true,
            });
            Skills.AddSkillsToFamily(slot.skillFamily, passiveSkillDef, peerlessEyeSkillDef);
            slot.skillFamily.variants[1].unlockableDef = TweyenUnlocks.repudiator; // "Tweyen: Repudiator"
            bodyPrefab.AddComponent<Components.PeerlessEyeTracker>();
            bodyPrefab.AddComponent<Components.UltrasightNotifier>(); // chime at the 11th stack

            GlobalEventManager.onServerDamageDealt += OnServerDamageDealt;
        }

        /// <summary>Different debuffs on the body (damage over time counts through its debuff; stacks don't count).</summary>
        public static int UniqueDebuffs(CharacterBody body)
        {
            if (!body || BuffCatalog.debuffBuffIndices == null) return 0;
            int count = 0;
            foreach (BuffIndex debuff in BuffCatalog.debuffBuffIndices)
            {
                if (body.HasBuff(debuff)) count++;
            }
            return count;
        }

        /// <summary>Whether Twin-Star Huntress is the body's selected passive.</summary>
        public static bool HasTwinStar(CharacterBody body) => HasSelected(body, passiveSkillDef);

        /// <summary>Whether Peerless Eye is the body's selected passive.</summary>
        public static bool HasPeerlessEye(CharacterBody body) => HasSelected(body, peerlessEyeSkillDef);

        private static bool HasSelected(CharacterBody body, SkillDef passive)
        {
            if (!body || !body.skillLocator || !passive) return false;
            foreach (GenericSkill skill in body.skillLocator.allSkills)
            {
                if (skill && skill.skillDef == passive) return true;
            }
            return false;
        }

        private static void OnServerDamageDealt(DamageReport report)
        {
            DamageInfo info = report.damageInfo;
            CharacterBody attacker = report.attackerBody;
            CharacterBody victim = report.victimBody;
            if (info == null || !attacker || !victim || info.procCoefficient <= 0f) return;
            if (HasPeerlessEye(attacker))
            {
                attacker.GetComponent<Components.PeerlessEyeTracker>()?.OnHit(info);
                return;
            }
            if (info.dotIndex != DotController.DotIndex.None) return; // damage over time doesn't count
            if (!victim.healthComponent || !victim.healthComponent.alive) return; // killed by the hit
            if (!HasTwinStar(attacker)) return;

            float chance = BASE_CHANCE * info.procCoefficient;
            // Clincher's beam (a secondary hit that isn't a projectile: Asteroid Jaeger's arrows are)
            bool clincher = (info.damageType.damageSource & DamageSource.Secondary) != 0
                && !(info.inflictor && info.inflictor.GetComponent<RoR2.Projectile.ProjectileController>());
            if (clincher) chance *= attacker.attackSpeed;
            CharacterMaster master = attacker.master;
            int count = Mathf.FloorToInt(chance / 100f);
            if (Util.CheckRoll(chance - count * 100f, master)) count++;

            for (int i = 0; i < count; i++)
            {
                if (!Inflict(victim, attacker, info)) break;
            }
        }

        /// <summary>Inflicts one random debuff among those that would do something. False if there is none.</summary>
        private static bool Inflict(CharacterBody victim, CharacterBody attacker, DamageInfo info)
        {
            var candidates = new List<int>();
            for (int i = 0; i < debuffs.Length; i++)
            {
                BuffDef def = debuffs[i].buff();
                if (!def) continue;
                if (!def.canStack && victim.HasBuff(def)) continue; // would do nothing
                candidates.Add(i);
            }
            if (candidates.Count == 0) return false;

            Debuff pick = debuffs[candidates[UnityEngine.Random.Range(0, candidates.Count)]];
            BuffDef buff = pick.buff();
            using (TweyenDebuffApplied.As(attacker))
            switch (pick.kind)
            {
                case Kind.Timed:
                    victim.AddTimedBuff(buff, pick.duration);
                    break;
                case Kind.Permanent:
                    victim.AddBuff(buff);
                    break;
                case Kind.DamageOverTime:
                    InflictDotInfo dot = new InflictDotInfo
                    {
                        victimObject = victim.gameObject,
                        attackerObject = attacker.gameObject,
                        hitHurtBox = victim.mainHurtBox,
                        dotIndex = pick.dot,
                        duration = pick.duration,
                        damageMultiplier = 1f,
                        procChainMask = info.procChainMask,
                    };
                    DotController.InflictDot(ref dot);
                    break;
            }
            TweyenDebuffWatch.Check(victim, attacker);
            return true;
        }
    }
}
