using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace TweyenR2RMod.Survivors.Tweyen.SkillStates
{
    /// <summary>
    /// Clincher (secondary): Tweyen's Clincher from Granblue Fantasy Relink (action 1200, motion 3420), played like the
    /// Railgunner's Supercharged Railgun. She draws her bow, then fires a huge arrow at the crosshair: a hitscan shot
    /// as wide as the supercharged railgun's that pierces everything (enemies, terrain and objects) and damages every
    /// enemy on its line, with the railgun's recoil and a very large knockback.
    /// - While drawing (<see cref="fireTime"/>): held in place (gravity still applies), +10 armor, can't be cancelled
    ///   and blocks the primary.
    /// - From the shot on (recoil applied): anything cancels the rest of the animation (moving, jumping, sprinting,
    ///   any skill).
    /// - Attack speed doesn't speed it up; it multiplies the damage instead.
    /// </summary>
    public class Clincher : BaseSkillState, IBlocksPrimary
    {
        public static float damageCoefficient = 4f;
        public static float procCoefficient = 3f;
        /// <summary>The arrow leaves at this point of the animation (s).</summary>
        public static float fireTime = 1.4f;
        /// <summary>The kept animation: draw, shot and recovery.</summary>
        public static float duration = 3f;
        /// <summary>The Supercharged Railgun's: bullet radius, knockback on enemies, recoil on the shooter and camera kick.</summary>
        public static float bulletRadius = 2f;
        public static float force = 4000f;
        public static float selfKnockbackForce = 3000f;
        public static float recoilAmplitudeY = 6f;
        public static float recoilAmplitudeX = 1f;
        /// <summary>Reach (m). The beam goes through terrain, so it is drawn this long too.</summary>
        public static float maxDistance = 1000f;
        /// <summary>The shot leaves this far to her left of the drawn arrow's tip (m, sideways to the aim).</summary>
        public static float muzzleLeftOffset = 0.35f;

        private const string Layer = "FullBody, Override";
        private const string State = "Clincher";
        private const string Muzzle = "ArrowTip";

        private bool hasFired;
        /// <summary>Used with 10+ Ultrasight: every hit crits, and the buff is used up once the shot is out.</summary>
        private bool ultrasight;

        public bool BlocksPrimary => !hasFired;

        public override void OnEnter()
        {
            base.OnEnter();

            if (isAuthority)
            {
                TweyenMovement.CancelMomentum(characterMotor); // also ends a Wheel of Death dash
                if (characterMotor) characterMotor.moveDirection = Vector3.zero;
                EntityStateMachine weapon = EntityStateMachine.FindByCustomName(gameObject, "Weapon");
                if (weapon && weapon.state is StellarArrow) weapon.SetNextStateToMain();
            }
            if (characterBody) characterBody.isSprinting = false;
            if (NetworkServer.active && characterBody) characterBody.AddBuff(TweyenBuffs.skillArmor);

            ultrasight = TweyenBuffs.UltrasightReady(characterBody);
            StartAimMode(GetAimRay(), duration);
            PlayCrossfade(Layer, State, 0.1f);
            Util.PlaySound(TweyenSounds.ClincherDraw, gameObject);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (characterBody) characterBody.isSprinting = false;

            if (!hasFired)
            {
                // drawing: rooted in place, but still falling if in the air
                if (isAuthority && characterMotor)
                {
                    characterMotor.moveDirection = Vector3.zero;
                    characterMotor.velocity = new Vector3(0f, characterMotor.velocity.y, 0f);
                }
                FaceAim();
                if (fixedAge >= fireTime) Fire();
                return;
            }

            if (isAuthority && (TryJumpCancel() || ShouldCancel() || fixedAge >= duration))
            {
                outer.SetNextStateToMain();
            }
        }

        private void FaceAim()
        {
            if (!characterDirection || !inputBank) return;
            Vector3 aim = inputBank.aimDirection;
            aim.y = 0f;
            if (aim.sqrMagnitude > 0.0001f) characterDirection.forward = aim.normalized;
        }

        private void Fire()
        {
            hasFired = true;
            Util.PlaySound(TweyenSounds.ClincherShot, gameObject);
            if (NetworkServer.active && characterBody) characterBody.RemoveBuff(TweyenBuffs.skillArmor);

            Ray aimRay = GetAimRay();
            StartAimMode(aimRay, 2f);
            AddRecoil(-1f * recoilAmplitudeY, -1.5f * recoilAmplitudeY, -1f * recoilAmplitudeX, 1f * recoilAmplitudeX);
            if (isAuthority) Shoot(aimRay);
            // the damage (with Ultrasight's attack speed) is set in the shot: only now is the buff used up
            if (ultrasight) TweyenBuffs.ConsumeUltrasight(characterBody);
        }

        private void Shoot(Ray aimRay)
        {

            // the shot (and its beam) starts at the bow and goes to whatever is under the crosshair
            Vector3 origin = MuzzlePosition(aimRay);
            Vector3 target = Util.CharacterRaycast(gameObject, aimRay, out RaycastHit hit, maxDistance,
                LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore) ? hit.point : aimRay.GetPoint(maxDistance);
            Vector3 toTarget = target - origin;
            Vector3 direction = toTarget.sqrMagnitude > 4f ? toTarget.normalized : aimRay.direction;
            bool twinStar = TweyenPassive.HasTwinStar(characterBody);

            new BulletAttack
            {
                owner = gameObject,
                // no weapon or muzzle: the beam then starts at the origin, which every player receives
                weapon = null,
                origin = origin,
                aimVector = direction,
                maxDistance = maxDistance,
                radius = bulletRadius,
                smartCollision = true,
                stopperMask = 0, // pierces everything, terrain included
                bulletCount = 1,
                minSpread = 0f,
                maxSpread = 0f,
                damage = damageCoefficient * damageStat * attackSpeedStat,
                procCoefficient = procCoefficient,
                force = force,
                isCrit = ultrasight || RollCrit(),
                falloffModel = BulletAttack.FalloffModel.None,
                damageColorIndex = DamageColorIndex.Default,
                damageType = DamageTypeCombo.GenericSecondary,
                tracerEffectPrefab = TweyenClincherContent.tracerEffect,
                hitEffectPrefab = TweyenClincherContent.impactEffect,
                HitEffectNormal = false,
                // Twin-Star Huntress: +50% (of her damage) per unique debuff on each enemy hit
                modifyOutgoingDamageCallback = (BulletAttack bullet, ref BulletAttack.BulletHit hit, DamageInfo damageInfo) =>
                {
                    if (!twinStar || !hit.hitHurtBox || !hit.hitHurtBox.healthComponent) return;
                    int debuffs = TweyenPassive.UniqueDebuffs(hit.hitHurtBox.healthComponent.body);
                    damageInfo.damage *= (damageCoefficient + TweyenPassive.CLINCHER_BONUS_PER_DEBUFF * debuffs) / damageCoefficient;
                },
            }.Fire();

            // the railgun's recoil: pushed back along the shot
            if (characterMotor) characterMotor.ApplyForce(-selfKnockbackForce * aimRay.direction);
        }

        /// <summary>The drawn arrow's tip, moved a little to her left; her aim origin if the tip isn't found.</summary>
        private Vector3 MuzzlePosition(Ray aimRay)
        {
            Transform tip = FindModelChild(Muzzle);
            if (!tip) return aimRay.origin;
            Vector3 left = Vector3.Cross(aimRay.direction, Vector3.up);
            if (left.sqrMagnitude < 0.0001f) left = -transform.right;
            return tip.position + left.normalized * muzzleLeftOffset;
        }

        /// <summary>After the shot: moving, sprinting or using another skill (the primary included) ends Clincher.</summary>
        private bool ShouldCancel()
        {
            if (inputBank)
            {
                if (inputBank.moveVector.sqrMagnitude > 0.01f) return true;
                if (inputBank.sprint.justPressed) return true;
            }
            EntityStateMachine weapon = EntityStateMachine.FindByCustomName(gameObject, "Weapon");
            return weapon && weapon.state is StellarArrow;
        }

        /// <summary>After the shot, a jump cancels it into a normal jump (keeping the recoil's momentum).</summary>
        private bool TryJumpCancel()
        {
            if (!inputBank || !characterMotor || !characterBody) return false;
            if (!inputBank.jump.justPressed || inputBank.jump.hasPressBeenClaimed) return false;
            if (characterMotor.jumpCount >= characterBody.maxJumpCount) return false;

            inputBank.jump.hasPressBeenClaimed = true;
            Vector3 velocity = characterMotor.velocity;
            velocity.y = characterBody.jumpPower * characterMotor.jumpPowerPenaltyCoefficient;
            characterMotor.velocity = velocity;
            characterMotor.Motor.ForceUnground();
            characterMotor.jumpCount++;
            characterBody.TriggerJumpEventGlobally();
            if (sfxLocator && !string.IsNullOrEmpty(sfxLocator.jumpSound)) Util.PlaySound(sfxLocator.jumpSound, gameObject);
            PlayCrossfade("Body", "Jump", 0.05f);
            return true;
        }

        public override void OnExit()
        {
            if (NetworkServer.active && characterBody && characterBody.HasBuff(TweyenBuffs.skillArmor))
            {
                characterBody.RemoveBuff(TweyenBuffs.skillArmor);
            }
            // cancelled before the end: let the animation go instead of finishing it over the next action
            Animator animator = GetModelAnimator();
            if (animator)
            {
                int layer = animator.GetLayerIndex(Layer);
                if (layer >= 0 && (animator.GetCurrentAnimatorStateInfo(layer).IsName(State) || animator.GetNextAnimatorStateInfo(layer).IsName(State)))
                {
                    animator.CrossFadeInFixedTime("BufferEmpty", 0.15f, layer);
                }
            }
            base.OnExit();
        }

        /// <summary>Nothing interrupts the draw; once the arrow is out (and the recoil applied), anything can.</summary>
        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return hasFired ? InterruptPriority.Any : InterruptPriority.Frozen;
        }
    }
}
