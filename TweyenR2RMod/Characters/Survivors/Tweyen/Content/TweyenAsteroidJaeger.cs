using JetBrains.Annotations;
using R2API;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using System.Collections.Generic;
using TweyenR2RMod.Modules;
using TweyenR2RMod.Survivors.Tweyen.Projectiles;
using TweyenR2RMod.Survivors.Tweyen.SkillStates;
using UnityEngine;
using UnityEngine.AddressableAssets;
using GameAssetPaths = RoR2BepInExPack.GameAssetPathsBetter;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// Asteroid Jaeger (secondary, alternative to Clincher), built like the Railgunner's scope: hold the
    /// secondary to aim (<see cref="AsteroidJaegerAim"/>) and mark enemies; while aiming, the primary is replaced by
    /// Multilock Hail (<see cref="MultilockHailFire"/>), which fires a homing arrow at every mark. Its cooldown is spent
    /// only when Multilock Hail fires; extra secondary charges add arrows instead of uses.
    /// </summary>
    internal static class TweyenAsteroidJaeger
    {
        public const int BASE_MAX_ARROWS = 11;
        public const int ARROWS_PER_EXTRA_CHARGE = 5;
        public const float DAMAGE_COEFFICIENT = 0.3f;
        public const float PROC_COEFFICIENT = 1f;
        /// <summary>Per Ultrasight stack: +10% of the arrows' damage (multiplied in, 10 stacks = double).</summary>
        public const float DAMAGE_PER_ULTRASIGHT = 0.1f;
        /// <summary>The arrows fly where they were fired (upwards) this long (s) before homing in.</summary>
        public const float HOMING_DELAY = 0.25f;

        public static SkillDef asteroidJaegerDef;
        public static SkillDef multilockHailDef;
        public static GameObject multilockArrow;
        public static GameObject markIndicator;

        /// <summary>Slightly purple-tinted reticle (still mostly the Huntress's blue).</summary>
        private static readonly Color reticleTint = new Color(0.9f, 0.85f, 1f, 1f);

        /// <summary>Creates the skill defs (call while building the skills), the arrow and the reticle.</summary>
        public static SkillDef Init(AssetBundle assetBundle)
        {
            string prefix = TweyenSurvivor.TWEYEN_PREFIX;
            Modules.Language.Add(prefix + "SECONDARY_ASTEROID_JAEGER_NAME", "Asteroid Jaeger");
            Modules.Language.Add(prefix + "SECONDARY_ASTEROID_JAEGER_DESCRIPTION",
                $"Hold to aim and mark enemies (max. {BASE_MAX_ARROWS}). Use your Primary Skill while aiming to fire homing arrows at each mark for <style=cIsDamage>{100f * DAMAGE_COEFFICIENT:0}% damage</style> per arrow.");
            Modules.Language.Add(prefix + "PRIMARY_MULTILOCK_HAIL_NAME", "Multilock Hail");
            Modules.Language.Add(prefix + "PRIMARY_MULTILOCK_HAIL_DESCRIPTION",
                $"Each mark adds a charge. Fire every charge as a homing arrow at its marked enemy for <style=cIsDamage>{100f * DAMAGE_COEFFICIENT:0}% damage</style>.");

            asteroidJaegerDef = Skills.CreateSkillDef<AsteroidJaegerSkillDef>(new SkillDefInfo
            {
                skillName = "TweyenAsteroidJaeger",
                skillNameToken = prefix + "SECONDARY_ASTEROID_JAEGER_NAME",
                skillDescriptionToken = prefix + "SECONDARY_ASTEROID_JAEGER_DESCRIPTION",
                skillIcon = assetBundle.LoadAsset<Sprite>("MultilockSkill"),
                activationState = new EntityStates.SerializableEntityStateType(typeof(AsteroidJaegerAim)),
                activationStateMachineName = "Body",
                // Skill: enough to cut Wheel of Death (and Clincher after its shot) short
                interruptPriority = EntityStates.InterruptPriority.Skill,
                baseRechargeInterval = 6f,
                baseMaxStock = 1,
                // aiming costs nothing: Multilock Hail spends the charge when it fires
                stockToConsume = 0,
                requiredStock = 1,
                isCombatSkill = true,
                mustKeyPress = true,
                cancelSprintingOnActivation = false,
                canceledFromSprinting = false,
            });

            // only ever given to the primary while aiming (a skill override), never in a loadout
            multilockHailDef = Skills.CreateSkillDef<MultilockHailSkillDef>(new SkillDefInfo
            {
                skillName = "TweyenMultilockHail",
                skillNameToken = prefix + "PRIMARY_MULTILOCK_HAIL_NAME",
                skillDescriptionToken = prefix + "PRIMARY_MULTILOCK_HAIL_DESCRIPTION",
                skillIcon = assetBundle.LoadAsset<Sprite>("TwoStarSkill"),
                activationState = new EntityStates.SerializableEntityStateType(typeof(MultilockHailFire)),
                activationStateMachineName = "Body",
                interruptPriority = EntityStates.InterruptPriority.Skill,
                // its charges are the marks (set by AsteroidJaegerAim, see MultilockHailSkillDef): no recharge
                baseRechargeInterval = 0f,
                baseMaxStock = BASE_MAX_ARROWS,
                rechargeStock = 0,
                stockToConsume = 0,
                requiredStock = 1,
                fullRestockOnAssign = false,
                dontAllowPastMaxStocks = true,
                isCombatSkill = true,
                mustKeyPress = true,
                cancelSprintingOnActivation = false,
                canceledFromSprinting = false,
            });

            CreateArrow();
            CreateReticle();
            return asteroidJaegerDef;
        }

        private static void CreateArrow()
        {
            // Two-Crown's Strife's arrow look and homing, plain hits (counted per cast for "Tweyen: Sagacity", see MultilockArrowImpact), tagged as a secondary hit (Peerless Eye gives
            // nothing for it: AsteroidJaegerSkillDef sets its use's cap to 0)
            multilockArrow = TweyenArrows.Create<MultilockArrowImpact>("TweyenMultilockArrow");
            multilockArrow.GetComponent<ProjectileController>().procCoefficient = PROC_COEFFICIENT;
            multilockArrow.GetComponent<ProjectileDamage>().damageType = DamageTypeCombo.GenericSecondary;
            multilockArrow.AddComponent<ProjectileHomingDelay>().delay = HOMING_DELAY;
            // each arrow keeps the enemy it was fired at and never looks for another: if that enemy dies, it flies on
            // in a straight line
            foreach (ProjectileDirectionalTargetFinder finder in multilockArrow.GetComponents<ProjectileDirectionalTargetFinder>())
            {
                Object.DestroyImmediate(finder);
            }
            foreach (ProjectileSphereTargetFinder finder in multilockArrow.GetComponents<ProjectileSphereTargetFinder>())
            {
                Object.DestroyImmediate(finder);
            }
            multilockArrow.AddComponent<ProjectileDropDeadTarget>();
        }

        private static void CreateReticle()
        {
            GameObject huntress = Addressables.LoadAssetAsync<GameObject>(GameAssetPaths.RoR2_Base_Huntress.HuntressTrackingIndicator_prefab).WaitForCompletion();
            if (!huntress)
            {
                Log.Warning("Huntress tracking indicator not found; Asteroid Jaeger marks won't show");
                return;
            }
            markIndicator = PrefabAPI.InstantiateClone(huntress, "TweyenMarkIndicator", false);
            foreach (SpriteRenderer sprite in markIndicator.GetComponentsInChildren<SpriteRenderer>(true))
            {
                sprite.color *= reticleTint;
            }
        }

        /// <summary>Most arrows a volley can have: 11, +5 per extra secondary charge.</summary>
        public static int MaxArrows(CharacterBody body)
        {
            GenericSkill secondary = body && body.skillLocator ? body.skillLocator.secondary : null;
            int extra = secondary ? Mathf.Max(0, secondary.maxStock - 1) : 0;
            return BASE_MAX_ARROWS + ARROWS_PER_EXTRA_CHARGE * extra;
        }
    }

    /// <summary>Asteroid Jaeger's secondary: costs nothing to start aiming (the shot spends it).</summary>
    public class AsteroidJaegerSkillDef : SkillDef { }

    /// <summary>
    /// Multilock Hail: one charge per mark (up to the most marks, 11 +5 per extra secondary charge), so the icon shows
    /// how many are marked; usable once at least one enemy is marked. It never recharges: the marks set its charges.
    /// </summary>
    public class MultilockHailSkillDef : SkillDef
    {
        public override int GetMaxStock([NotNull] GenericSkill skillSlot)
        {
            return TweyenAsteroidJaeger.MaxArrows(skillSlot.characterBody);
        }

        public override void OnFixedUpdate([NotNull] GenericSkill skillSlot, float deltaTime) { }

        public override bool IsReady([NotNull] GenericSkill skillSlot)
        {
            AsteroidJaegerMarks marks = skillSlot.characterBody ? skillSlot.characterBody.GetComponent<AsteroidJaegerMarks>() : null;
            return base.IsReady(skillSlot) && marks && marks.marks.Count > 0;
        }
    }

    /// <summary>
    /// The enemies marked while aiming (one entry per arrow; an enemy can appear several times), and their reticles.
    /// Kept by the aiming player (the body's authority); Multilock Hail reads it.
    /// </summary>
    public class AsteroidJaegerMarks : MonoBehaviour
    {
        public readonly List<HurtBox> marks = new List<HurtBox>();
        private readonly Dictionary<HurtBox, Indicator> reticles = new Dictionary<HurtBox, Indicator>();

        public void SetMarks(List<HurtBox> newMarks)
        {
            marks.Clear();
            marks.AddRange(newMarks);
            ShowReticles(true);
        }

        public void Clear()
        {
            marks.Clear();
            ShowReticles(false);
        }

        /// <summary>One reticle per marked enemy (however many arrows it gets).</summary>
        public void ShowReticles(bool show)
        {
            var wanted = new HashSet<HurtBox>();
            if (show) foreach (HurtBox h in marks) if (h) wanted.Add(h);
            foreach (HurtBox h in wanted)
            {
                if (reticles.ContainsKey(h) || !TweyenAsteroidJaeger.markIndicator) continue;
                reticles[h] = new Indicator(gameObject, TweyenAsteroidJaeger.markIndicator) { targetTransform = h.transform, active = true };
            }
            var stale = new List<HurtBox>();
            foreach (KeyValuePair<HurtBox, Indicator> pair in reticles)
            {
                if (!pair.Key || !wanted.Contains(pair.Key))
                {
                    pair.Value.active = false;
                    stale.Add(pair.Key);
                }
            }
            foreach (HurtBox h in stale) reticles.Remove(h);
        }

        private void OnDestroy()
        {
            foreach (Indicator indicator in reticles.Values) indicator.active = false;
            reticles.Clear();
        }
    }
}
