using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace TweyenR2RMod.Survivors.Tweyen.Components
{
    /// <summary>
    /// Peerless Eye (passive): Tweyen's skill hits grant Ultrasight stacks (<see cref="TweyenBuffs.ultrasight"/>),
    /// capped per use of a skill:
    /// - primary and utility: 1 per use;
    /// - secondary and special: 1 per enemy hit, up to 3 per use, or up to 1 if that use spends the stacks (10+);
    ///   Asteroid Jaeger: none.
    /// Only her skills' own hits count (tagged with their skill slot): not damage over time, not item procs.
    /// When a use spends the stacks, its hits' stacks are granted after they are removed (a hit can land before the
    /// skill removes them, e.g. Clincher's beam): those wait until then.
    /// Stacks are not kept between stages (each stage has a new body). Server only.
    /// </summary>
    public class PeerlessEyeTracker : MonoBehaviour
    {
        public const int CAP_BASIC = 1;
        public const int CAP_POWER = 3;
        public const int CAP_AFTER_SPENDING = 1;

        private class Use
        {
            public int cap;
            public int granted;
            /// <summary>This use spends the stacks and hasn't yet: its stacks wait in <see cref="pending"/>.</summary>
            public bool spendPending;
            public int pending;
        }

        private CharacterBody body;
        private readonly Use[] uses = { new Use(), new Use(), new Use(), new Use() }; // primary, secondary, utility, special

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
        }

        private void OnEnable()
        {
            if (body) body.onSkillActivatedServer += OnSkillActivated;
        }

        private void OnDisable()
        {
            if (body) body.onSkillActivatedServer -= OnSkillActivated;
        }

        private void OnSkillActivated(GenericSkill skill)
        {
            if (!TweyenPassive.HasPeerlessEye(body)) return;
            int slot = SlotOf(skill);
            if (slot < 0) return;
            if (skill.skillDef is AsteroidJaegerSkillDef)
            {
                // Asteroid Jaeger never grants stacks (its Multilock Hail still spends them, see MultilockHailFire)
                uses[slot] = new Use { cap = 0 };
                return;
            }
            bool power = slot == 1 || slot == 3;
            bool spends = power && TweyenBuffs.UltrasightReady(body);
            uses[slot] = new Use
            {
                cap = !power ? CAP_BASIC : (spends ? CAP_AFTER_SPENDING : CAP_POWER),
                spendPending = spends,
            };
        }

        private int SlotOf(GenericSkill skill)
        {
            SkillLocator locator = body ? body.skillLocator : null;
            if (!locator || !skill) return -1;
            if (skill == locator.primary) return 0;
            if (skill == locator.secondary) return 1;
            if (skill == locator.utility) return 2;
            if (skill == locator.special) return 3;
            return -1;
        }

        /// <summary>A hit of hers landed on an enemy: grants a stack if it is a skill's own hit and the use's cap allows.</summary>
        public void OnHit(DamageInfo info)
        {
            if (!NetworkServer.active || info == null) return;
            if (info.dotIndex != DotController.DotIndex.None) return;
            int slot = SlotOf(info.damageType.damageSource);
            if (slot < 0) return;
            Use use = uses[slot];
            if (use.granted >= use.cap) return;
            use.granted++;
            if (use.spendPending) use.pending++;
            else TweyenBuffs.AddUltrasight(body);
        }

        /// <summary>The stacks were just spent: the spending use's waiting stacks are granted now.</summary>
        public void OnUltrasightSpent()
        {
            foreach (Use use in uses)
            {
                if (!use.spendPending) continue;
                use.spendPending = false;
                if (use.pending > 0) TweyenBuffs.AddUltrasight(body, use.pending);
                use.pending = 0;
            }
        }

        private static int SlotOf(DamageSource source)
        {
            if ((source & DamageSource.Primary) != 0) return 0;
            if ((source & DamageSource.Secondary) != 0) return 1;
            if ((source & DamageSource.Utility) != 0) return 2;
            if ((source & DamageSource.Special) != 0) return 3;
            return -1;
        }
    }
}
