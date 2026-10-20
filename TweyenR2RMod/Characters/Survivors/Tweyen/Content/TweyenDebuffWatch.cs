using RoR2;
using System;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// Tells "Tweyen: Repudiator" when a Tweyen may have just debuffed an enemy (the game doesn't record who applied a
    /// buff, so these are the moments she can): after each of her hits (on-hit effects included: items, equipment,
    /// elite effects), after a damage over time she inflicts, and after her own debuff code (Twin-Star Huntress,
    /// Two-Crown's Strife). Server only.
    /// </summary>
    internal static class TweyenDebuffWatch
    {
        /// <summary>(enemy, the Tweyen who may have debuffed it, its debuff count).</summary>
        public static event Action<CharacterBody, CharacterBody, int> onEnemyDebuffed;

        public static void Init()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy += OnHitEnemy;
            DotController.onDotInflictedServerGlobal += OnDotInflicted;
        }

        private static void OnHitEnemy(On.RoR2.GlobalEventManager.orig_OnHitEnemy orig, GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);
            if (damageInfo != null && damageInfo.attacker && victim)
            {
                Check(victim.GetComponent<CharacterBody>(), damageInfo.attacker.GetComponent<CharacterBody>());
            }
        }

        private static void OnDotInflicted(DotController dotController, ref InflictDotInfo info)
        {
            if (dotController && info.attackerObject)
            {
                Check(dotController.victimBody, info.attackerObject.GetComponent<CharacterBody>());
            }
        }

        /// <summary>Call after Tweyen's own code debuffs an enemy.</summary>
        public static void Check(CharacterBody victim, CharacterBody attacker)
        {
            if (!victim || !attacker || onEnemyDebuffed == null) return;
            if (attacker.bodyIndex != BodyCatalog.FindBodyIndex(TweyenSurvivor.instance.bodyName)) return;
            onEnemyDebuffed(victim, attacker, CountDebuffs(victim));
        }

        /// <summary>Debuffs on a body, counted like Death Mark: each debuff once, each active damage over time once.</summary>
        public static int CountDebuffs(CharacterBody body)
        {
            int count = 0;
            foreach (BuffIndex debuff in BuffCatalog.debuffBuffIndices)
            {
                if (body.HasBuff(debuff)) count++;
            }
            DotController dots = DotController.FindDotController(body.gameObject);
            if (dots)
            {
                for (DotController.DotIndex dot = DotController.DotIndex.Bleed; dot < DotController.DotIndex.Count; dot++)
                {
                    if (dots.HasDotActive(dot)) count++;
                }
            }
            return count;
        }
    }
}
