using RoR2;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// Debuff manipulation for Two-Crown's Strife: makes an enemy's debuffs last longer and adds stacks to them.
    /// "Debuffs" here are timed debuffs (BuffDef.isDebuff) and damage-over-time effects (bleed, burn, poison,
    /// blight...). Debuffs without a timer are left alone. Server only.
    /// The timers live in private lists of CharacterBody and DotController, reached by reflection.
    /// </summary>
    internal static class TweyenDebuffs
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo timedBuffsField = typeof(CharacterBody).GetField("timedBuffs", Instance);
        private static readonly FieldInfo dotStackListField = typeof(DotController).GetField("dotStackList", Instance);

        /// <summary>
        /// Adds <paramref name="extraSeconds"/> to every timed debuff and damage-over-time stack on the victim, then
        /// adds one stack to one stackable debuff picked at random (so repeated hits spread their stacks over them).
        /// </summary>
        public static void ExtendAndAddStack(CharacterBody victim, GameObject attacker, float extraSeconds)
        {
            if (!victim) return;
            // stackable debuffs found, with the longest remaining timer of each (the duration a new stack gets)
            var candidates = new List<(BuffDef buff, DotController.DotIndex dot, float duration, GameObject dotAttacker)>();

            if (timedBuffsField?.GetValue(victim) is List<CharacterBody.TimedBuff> timedBuffs)
            {
                var longest = new Dictionary<BuffIndex, float>();
                foreach (CharacterBody.TimedBuff timed in timedBuffs)
                {
                    BuffDef def = BuffCatalog.GetBuffDef(timed.buffIndex);
                    if (!def || !def.isDebuff) continue;
                    timed.timer += extraSeconds;
                    timed.totalDuration += extraSeconds;
                    longest[timed.buffIndex] = Mathf.Max(longest.TryGetValue(timed.buffIndex, out float t) ? t : 0f, timed.timer);
                }
                foreach (KeyValuePair<BuffIndex, float> pair in longest)
                {
                    BuffDef def = BuffCatalog.GetBuffDef(pair.Key);
                    if (def.canStack) candidates.Add((def, DotController.DotIndex.None, pair.Value, null));
                }
            }

            DotController dots = DotController.FindDotController(victim.gameObject);
            if (dots && dotStackListField?.GetValue(dots) is IList stacks && stacks.Count > 0)
            {
                System.Type stackType = stacks[0].GetType();
                FieldInfo timerField = stackType.GetField("timer", Instance);
                FieldInfo totalField = stackType.GetField("totalDuration", Instance);
                FieldInfo indexField = stackType.GetField("dotIndex", Instance);
                FieldInfo attackerField = stackType.GetField("attackerObject", Instance);
                var longest = new Dictionary<DotController.DotIndex, (float timer, GameObject attacker)>();
                foreach (object stack in stacks)
                {
                    float timer = (float)timerField.GetValue(stack) + extraSeconds;
                    timerField.SetValue(stack, timer);
                    totalField?.SetValue(stack, (float)totalField.GetValue(stack) + extraSeconds);
                    var index = (DotController.DotIndex)indexField.GetValue(stack);
                    if (!longest.TryGetValue(index, out var best) || timer > best.timer)
                    {
                        longest[index] = (timer, (GameObject)attackerField.GetValue(stack));
                    }
                }
                foreach (var pair in longest)
                {
                    candidates.Add((null, pair.Key, pair.Value.timer, pair.Value.attacker));
                }
            }

            if (candidates.Count == 0) return;
            var pick = candidates[Random.Range(0, candidates.Count)];
            using (TweyenDebuffApplied.As(attacker ? attacker.GetComponent<CharacterBody>() : null))
            if (pick.buff)
            {
                victim.AddTimedBuff(pick.buff, pick.duration);
            }
            else
            {
                // a new stack of the same effect, from whoever applied it (it deals their damage)
                InflictDotInfo info = new InflictDotInfo
                {
                    victimObject = victim.gameObject,
                    attackerObject = pick.dotAttacker ? pick.dotAttacker : attacker,
                    hitHurtBox = victim.mainHurtBox,
                    dotIndex = pick.dot,
                    duration = pick.duration,
                    damageMultiplier = 1f,
                };
                DotController.InflictDot(ref info);
            }
            TweyenDebuffWatch.Check(victim, attacker ? attacker.GetComponent<CharacterBody>() : null);
        }
    }
}
