using R2API;
using RoR2;
using TweyenR2RMod.Modules;
using UnityEngine;
using UnityEngine.Networking;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>Tweyen's buffs.</summary>
    internal static class TweyenBuffs
    {
        public const float SKILL_ARMOR = 10f;

        /// <summary>
        /// +<see cref="SKILL_ARMOR"/> armor while she commits to a skill (Clincher's draw, Two-Crown's Strife).
        /// Hidden, and deliberately left out of the skill descriptions.
        /// </summary>
        public static BuffDef skillArmor;

        /// <summary>
        /// Ultrasight: +<see cref="ULTRASIGHT_ATTACK_SPEED"/> attack speed per stack. With
        /// <see cref="ULTRASIGHT_READY_STACKS"/> or more, the next secondary or special (Clincher, Two-Crown's Strife)
        /// crits with every hit and uses the buff up (after the skill's damage is set, so the stacks' attack speed
        /// still counts: 10 stacks take a 400% Clincher to 720%). At most
        /// <see cref="ULTRASIGHT_MAX_STACKS"/> stacks. Granted by a passive still to come (use <see cref="AddUltrasight"/>).
        /// </summary>
        public static BuffDef ultrasight;
        public const float ULTRASIGHT_ATTACK_SPEED = 0.08f;
        public const int ULTRASIGHT_READY_STACKS = 10;
        public const int ULTRASIGHT_MAX_STACKS = 11;

        public static void Init(AssetBundle assetBundle)
        {
            ultrasight = Content.CreateAndAddBuff("TweyenUltrasight", assetBundle.LoadAsset<Sprite>("UltrasightBuff128"),
                Color.white, canStack: true, isDebuff: false);
            skillArmor = Content.CreateAndAddBuff("TweyenSkillArmor", null, new Color(0.55f, 0.65f, 1f), canStack: false, isDebuff: false);
            skillArmor.isHidden = true;
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (!body) return;
                if (body.HasBuff(skillArmor)) args.armorAdd += SKILL_ARMOR;
                args.attackSpeedMultAdd += ULTRASIGHT_ATTACK_SPEED * Mathf.Min(body.GetBuffCount(ultrasight), ULTRASIGHT_MAX_STACKS);
            };
        }

        /// <summary>Whether the body has enough Ultrasight for a guaranteed-crit secondary or special.</summary>
        public static bool UltrasightReady(CharacterBody body)
        {
            return body && ultrasight && body.GetBuffCount(ultrasight) >= ULTRASIGHT_READY_STACKS;
        }

        /// <summary>Adds Ultrasight stacks, up to <see cref="ULTRASIGHT_MAX_STACKS"/>. Server only.</summary>
        public static void AddUltrasight(CharacterBody body, int stacks = 1)
        {
            if (!body || !ultrasight || !NetworkServer.active) return;
            int count = Mathf.Min(body.GetBuffCount(ultrasight) + stacks, ULTRASIGHT_MAX_STACKS);
            body.SetBuffCount(ultrasight.buffIndex, count);
        }

        /// <summary>Removes all Ultrasight stacks. Server only.</summary>
        public static void ConsumeUltrasight(CharacterBody body)
        {
            if (!body || !ultrasight || !NetworkServer.active) return;
            body.SetBuffCount(ultrasight.buffIndex, 0);
            // Peerless Eye: the spending use's own hits grant their stacks only now
            body.GetComponent<Components.PeerlessEyeTracker>()?.OnUltrasightSpent();
        }
    }
}
