using RoR2;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// Tweyen's sounds. Her own are in the TweyenSounds bank (Soundbanks/TweyenSounds.sound, built by
    /// Tools/soundbank/build_soundbank.py and loaded by R2API.Sound); each event already picks a random variant.
    /// - Relink: the normal-attack shot (every skill's arrow but Clincher's), Clincher's draw and shot, the Multilock
    ///   Hail lock-on (Asteroid Jaeger's marks) and its charge, her movement loop (instead of footsteps: she floats),
    ///   jump, landing, and her Japanese voice when hurt and when she dies. Walking and sprinting only have the loop.
    /// - The player's own: spawn, character select, Two-Crown's Strife.
    /// - The bow draw of every skill but Clincher is the Huntress's.
    /// Skill sounds play on every client (the states run everywhere), except the marks', which only the aiming player
    /// has.
    /// </summary>
    internal static class TweyenSounds
    {
        public const string Shot = "Play_Tweyen_Shot";
        public const string Draw = "Play_huntress_m1_ready";
        public const string ClincherDraw = "Play_Tweyen_ClincherDraw";
        public const string ClincherShot = "Play_Tweyen_ClincherShot";
        public const string Mark = "Play_Tweyen_Mark";
        public const string JaegerCharge = "Play_Tweyen_JaegerCharge";
        public const string JaegerChargeStop = "Stop_Tweyen_JaegerCharge";
        public const string MoveLoop = "Play_Tweyen_MoveLoop";
        public const string MoveLoopStop = "Stop_Tweyen_MoveLoop";
        public const string Jump = "Play_Tweyen_Jump";
        public const string Land = "Play_Tweyen_Land";
        public const string HurtLight = "Play_Tweyen_HurtS";
        public const string HurtMedium = "Play_Tweyen_HurtM";
        public const string HurtHeavy = "Play_Tweyen_HurtL";
        public const string Death = "Play_Tweyen_Death";

        public const string Spawn = "Play_Tweyen_Spawn";
        public const string Select = "Play_Tweyen_Select";
        public const string TwoCrown = "Play_Tweyen_TwoCrown";

        /// <summary>Shots this close together (s) share one sound (volleys would otherwise stack dozens).</summary>
        public const float ShotSpacing = 0.05f;

        public static void Init(GameObject bodyPrefab, GameObject characterModel, GameObject displayPrefab)
        {
            SfxLocator sfx = bodyPrefab.GetComponent<SfxLocator>() ?? bodyPrefab.AddComponent<SfxLocator>();
            sfx.jumpSound = Jump;
            sfx.landingSound = Land;
            sfx.deathSound = Death;
            sfx.sprintLoopStart = "";
            sfx.sprintLoopStop = "";
            sfx.fallDamageSound = "";

            // she floats: no steps (the movement loop plays instead)
            FootstepHandler footsteps = characterModel.GetComponent<FootstepHandler>();
            if (footsteps)
            {
                footsteps.baseFootstepString = "";
                footsteps.sprintFootstepOverrideString = "";
                footsteps.baseFootliftString = "";
                footsteps.sprintFootliftOverrideString = "";
                footsteps.enableFootstepDust = false;
            }

            bodyPrefab.AddComponent<TweyenSoundController>();
            if (displayPrefab) displayPrefab.AddComponent<TweyenSelectSound>();
        }

        /// <summary>Plays a shot unless one played less than <see cref="ShotSpacing"/> ago (by the state's clock).</summary>
        public static void PlayShot(string sound, GameObject gameObject, float now, ref float lastShot)
        {
            if (now - lastShot < ShotSpacing) return;
            lastShot = now;
            Util.PlaySound(sound, gameObject);
        }
    }

    /// <summary>
    /// On Tweyen's body, on every client: the spawn sound, the movement loop while she moves on the ground (walking or
    /// sprinting), and a hurt voice line when she loses health (bigger hits, louder lines; not too often).
    /// </summary>
    public class TweyenSoundController : MonoBehaviour
    {
        public static float minMoveSpeed = 1.5f;
        public static float hurtCooldown = 0.8f;
        /// <summary>Damage (fraction of max health + shield) for the medium and heavy hurt lines.</summary>
        public static float mediumHit = 0.08f;
        public static float heavyHit = 0.2f;

        private CharacterBody body;
        private CharacterMotor motor;
        private HealthComponent health;
        private bool looping;
        private float lastHealth = -1f;
        private float hurtTimer;

        private void Start()
        {
            body = GetComponent<CharacterBody>();
            motor = GetComponent<CharacterMotor>();
            health = GetComponent<HealthComponent>();
            Util.PlaySound(TweyenSounds.Spawn, gameObject);
            // the death line (played by the death state from the SfxLocator) follows the voice-lines option
            SfxLocator sfx = GetComponent<SfxLocator>();
            if (sfx) sfx.deathSound = TweyenConfig.voiceLines.Value ? TweyenSounds.Death : "";
        }

        private void FixedUpdate()
        {
            bool alive = health && health.alive;
            bool moving = alive && TweyenConfig.movementSound.Value && motor && motor.isGrounded
                && new Vector3(motor.velocity.x, 0f, motor.velocity.z).sqrMagnitude >= minMoveSpeed * minMoveSpeed;
            if (moving != looping)
            {
                looping = moving;
                Util.PlaySound(moving ? TweyenSounds.MoveLoop : TweyenSounds.MoveLoopStop, gameObject);
            }

            hurtTimer -= Time.fixedDeltaTime;
            if (!health) return;
            // health + shield (not barrier, which drains on its own)
            float current = health.health + health.shield;
            if (lastHealth >= 0f && current < lastHealth - 0.5f && alive && hurtTimer <= 0f && TweyenConfig.voiceLines.Value)
            {
                float hit = (lastHealth - current) / Mathf.Max(1f, health.fullHealth + health.fullShield);
                Util.PlaySound(hit >= heavyHit ? TweyenSounds.HurtHeavy : hit >= mediumHit ? TweyenSounds.HurtMedium : TweyenSounds.HurtLight, gameObject);
                hurtTimer = hurtCooldown;
            }
            lastHealth = current;
        }

        private void OnDisable()
        {
            if (looping) Util.PlaySound(TweyenSounds.MoveLoopStop, gameObject);
            looping = false;
        }
    }

    /// <summary>On the character-select display: her select sound when she appears.</summary>
    public class TweyenSelectSound : MonoBehaviour
    {
        private void OnEnable()
        {
            Util.PlaySound(TweyenSounds.Select, gameObject);
        }
    }
}
