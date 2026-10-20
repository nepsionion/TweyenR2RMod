using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// Tells when a Tweyen applies a debuff (Stellar Termination's cooldown). The game doesn't record who applies a
    /// buff, so whoever's damage is being dealt (or on-hit effects processed: items, equipment, elite effects) is the
    /// applier of the debuffs added meanwhile, as is the attacker of a damage over time, and her own debuff code
    /// declares itself (<see cref="As"/>). Each application counts once (a debuff added inside another, like a damage
    /// over time's icon, doesn't), refreshes included. Server only.
    /// </summary>
    internal static class TweyenDebuffApplied
    {
        /// <summary>(the Tweyen who applied it).</summary>
        public static event Action<CharacterBody> onDebuffApplied;

        private static readonly List<CharacterBody> appliers = new List<CharacterBody>();
        private static int applying;

        public static void Init()
        {
            On.RoR2.HealthComponent.TakeDamage += TakeDamage;
            On.RoR2.GlobalEventManager.OnHitEnemy += OnHitEnemy;
            On.RoR2.CharacterBody.AddBuff_BuffIndex += AddBuff;
            On.RoR2.CharacterBody.AddTimedBuff_BuffDef_float += AddTimedBuff;
            On.RoR2.CharacterBody.AddTimedBuff_BuffDef_float_int += AddTimedBuffMax;
            On.RoR2.CharacterBody.AddTimedBuff_BuffIndex_float += AddTimedBuffIndex;
            On.RoR2.CharacterBody.AddTimedBuffDontRefreshDuration += AddTimedBuffDontRefresh;
            On.RoR2.DotController.InflictDot_refInflictDotInfo += InflictDot;
        }

        /// <summary>Makes <paramref name="applier"/> the applier of the debuffs added until disposed.</summary>
        public static IDisposable As(CharacterBody applier) => new Scope(applier);

        private sealed class Scope : IDisposable
        {
            public Scope(CharacterBody applier) => appliers.Add(applier);
            public void Dispose() => appliers.RemoveAt(appliers.Count - 1);
        }

        private static CharacterBody Current => appliers.Count > 0 ? appliers[appliers.Count - 1] : null;

        private static CharacterBody BodyOf(GameObject attacker) => attacker ? attacker.GetComponent<CharacterBody>() : null;

        private static void TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            using (As(BodyOf(damageInfo?.attacker))) orig(self, damageInfo);
        }

        private static void OnHitEnemy(On.RoR2.GlobalEventManager.orig_OnHitEnemy orig, GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            using (As(BodyOf(damageInfo?.attacker))) orig(self, damageInfo, victim);
        }

        /// <summary>Runs one application, reporting it if it is the outermost one and a Tweyen debuffs someone else.</summary>
        private static void Apply(CharacterBody victim, bool debuff, CharacterBody applier, Action orig)
        {
            if (applying == 0 && debuff && victim && applier && applier != victim
                && applier.bodyIndex == BodyCatalog.FindBodyIndex(TweyenSurvivor.instance.bodyName))
            {
                onDebuffApplied?.Invoke(applier);
            }
            applying++;
            try { orig(); }
            finally { applying--; }
        }

        private static bool IsDebuff(BuffIndex buff) => BuffCatalog.GetBuffDef(buff) is BuffDef def && def.isDebuff;

        private static void AddBuff(On.RoR2.CharacterBody.orig_AddBuff_BuffIndex orig, CharacterBody self, BuffIndex buffType)
        {
            Apply(self, IsDebuff(buffType), Current, () => orig(self, buffType));
        }

        private static void AddTimedBuff(On.RoR2.CharacterBody.orig_AddTimedBuff_BuffDef_float orig, CharacterBody self, BuffDef buffDef, float duration)
        {
            Apply(self, buffDef && buffDef.isDebuff, Current, () => orig(self, buffDef, duration));
        }

        private static void AddTimedBuffMax(On.RoR2.CharacterBody.orig_AddTimedBuff_BuffDef_float_int orig, CharacterBody self, BuffDef buffDef, float duration, int maxStacks)
        {
            Apply(self, buffDef && buffDef.isDebuff, Current, () => orig(self, buffDef, duration, maxStacks));
        }

        private static void AddTimedBuffIndex(On.RoR2.CharacterBody.orig_AddTimedBuff_BuffIndex_float orig, CharacterBody self, BuffIndex buffIndex, float duration)
        {
            Apply(self, IsDebuff(buffIndex), Current, () => orig(self, buffIndex, duration));
        }

        private static void AddTimedBuffDontRefresh(On.RoR2.CharacterBody.orig_AddTimedBuffDontRefreshDuration orig, CharacterBody self, BuffDef buffDef, float duration, int maxStacks)
        {
            Apply(self, buffDef && buffDef.isDebuff, Current, () => orig(self, buffDef, duration, maxStacks));
        }

        private static void InflictDot(On.RoR2.DotController.orig_InflictDot_refInflictDotInfo orig, ref InflictDotInfo inflictDotInfo)
        {
            InflictDotInfo info = inflictDotInfo;
            // her own code declares itself (it may add a stack of someone else's damage over time); otherwise the attacker
            CharacterBody applier = Current ?? BodyOf(info.attackerObject);
            Apply(BodyOf(info.victimObject), true, applier, () => orig(ref info));
            inflictDotInfo = info;
        }
    }
}
