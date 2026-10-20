using RoR2;
using RoR2.Skills;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TweyenR2RMod.Survivors.Tweyen.SkillDefs;
using UnityEngine;
using UnityEngine.Networking;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// Stellar Termination: Two-Crown's Strife upgraded by the Ancient Scepter (StandaloneAncientScepter, soft
    /// dependency: without it the skill exists but nothing gives it). The same skill (charges spent together for more
    /// arrows), but restockable whatever the option says, back to its full cooldown on use, and every debuff she
    /// applies (items included, see <see cref="TweyenDebuffApplied"/>) takes <see cref="COOLDOWN_PER_DEBUFF"/> s off
    /// its current cooldown.
    /// The debuffs are seen by the server and the cooldown runs where she is controlled: for another player's Tweyen
    /// the server sends the reduction through the game's "deduct cooldown from all skills" message, with a value no
    /// vanilla code sends (<see cref="RPC_CODE"/> minus the seconds), which her player's game turns into a reduction of
    /// this skill only.
    /// </summary>
    internal static class TweyenScepter
    {
        public const string SCEPTER_GUID = "com.DestroyedClone.AncientScepter";
        public const float COOLDOWN_PER_DEBUFF = 1f;
        private const float RPC_CODE = -100000f;

        public static SkillDef stellarTermination;

        /// <summary>Server: seconds waiting to be sent to each remote player's Tweyen (one message per tick).</summary>
        private static readonly Dictionary<CharacterBody, float> pending = new Dictionary<CharacterBody, float>();

        public static void Init(SkillDef twoCrownStrife, AssetBundle assetBundle, string bodyName)
        {
            string prefix = TweyenSurvivor.TWEYEN_PREFIX;
            Modules.Language.Add(prefix + "SPECIAL_STELLAR_TERMINATION_NAME", "Stellar Termination");
            Modules.Language.Add(prefix + "SPECIAL_STELLAR_TERMINATION_DESCRIPTION",
                $"<style=cIsDamage>Unstoppable.</style> Leap back and fire <style=cIsDamage>{SkillStates.TwoCrownStrife.baseArrowCount}</style> homing arrows that spread across enemies for <style=cIsDamage>{100f * SkillStates.TwoCrownStrife.damageCoefficient}% damage</style> each. Every hit extends and intensifies the enemy's debuffs. <color=#d299ff>Starts each stage on cooldown.</color> Additional charges are consumed together and increase the number of arrows."
                + $"\n<color=#d299ff>SCEPTER: Every debuff you apply reduces its cooldown by {COOLDOWN_PER_DEBUFF:0} second.</color>");

            OncePerStageSkillDef def = Object.Instantiate((OncePerStageSkillDef)twoCrownStrife);
            def.skillName = "TweyenStellarTermination";
            (def as ScriptableObject).name = def.skillName;
            def.skillNameToken = prefix + "SPECIAL_STELLAR_TERMINATION_NAME";
            def.skillDescriptionToken = prefix + "SPECIAL_STELLAR_TERMINATION_DESCRIPTION";
            def.icon = assetBundle.LoadAsset<Sprite>("DepravitySkill");
            def.alwaysRestockable = true;
            def.resetCooldownTimerOnUse = true; // back to the full cooldown, whatever debuffs took off before
            Modules.Content.AddSkillDef(def);
            stellarTermination = def;

            TweyenDebuffApplied.Init();
            TweyenDebuffApplied.onDebuffApplied += OnDebuffApplied;
            RoR2Application.onFixedUpdate += SendPending;
            On.RoR2.SkillLocator.RpcDeductCooldownFromAllSkillsServer += OnDeductCooldownRpc;

            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(SCEPTER_GUID)) Register(def, bodyName);
        }

        // only called when the Ancient Scepter is installed (its types are loaded only then)
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void Register(SkillDef def, string bodyName)
        {
            AncientScepter.AncientScepterItem.instance.RegisterScepterSkill(def, bodyName, SkillSlot.Special, 0);
        }

        private static void OnDebuffApplied(CharacterBody tweyen)
        {
            GenericSkill special = tweyen.skillLocator ? tweyen.skillLocator.special : null;
            if (!special || special.skillDef != stellarTermination) return;
            if (tweyen.hasEffectiveAuthority)
            {
                Reduce(special, COOLDOWN_PER_DEBUFF);
            }
            else
            {
                pending[tweyen] = (pending.TryGetValue(tweyen, out float seconds) ? seconds : 0f) + COOLDOWN_PER_DEBUFF;
            }
        }

        private static void SendPending()
        {
            if (pending.Count == 0) return;
            if (NetworkServer.active)
            {
                foreach (KeyValuePair<CharacterBody, float> pair in pending)
                {
                    if (pair.Key && pair.Key.skillLocator) pair.Key.skillLocator.CallRpcDeductCooldownFromAllSkillsServer(RPC_CODE - pair.Value);
                }
            }
            pending.Clear();
        }

        private static void OnDeductCooldownRpc(On.RoR2.SkillLocator.orig_RpcDeductCooldownFromAllSkillsServer orig, SkillLocator self, float deduction)
        {
            if (deduction > RPC_CODE)
            {
                orig(self, deduction);
                return;
            }
            CharacterBody body = self.GetComponent<CharacterBody>();
            if (body && body.hasEffectiveAuthority && self.special && self.special.skillDef == stellarTermination)
            {
                Reduce(self.special, RPC_CODE - deduction);
            }
        }

        /// <summary>Takes <paramref name="seconds"/> off the current cooldown (a charge it completes comes on the next tick).</summary>
        private static void Reduce(GenericSkill skill, float seconds)
        {
            if (skill.stock < skill.maxStock) skill.rechargeStopwatch += seconds;
        }
    }
}
