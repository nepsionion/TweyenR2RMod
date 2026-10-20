using RoR2;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.SkillStates
{
    /// <summary>Movement helpers shared by Tweyen's skills.</summary>
    public static class TweyenMovement
    {
        /// <summary>
        /// Stops all of the body's momentum (dashes, knockback, falling speed). Skills that cancel Wheel of Death
        /// call this on entering, so the cancel doesn't carry the dash (or a fall) into the new skill.
        /// Run it on the body's authority, where its movement is simulated.
        /// </summary>
        public static void CancelMomentum(CharacterMotor motor)
        {
            if (!motor) return;
            motor.velocity = Vector3.zero;
            motor.rootMotion = Vector3.zero;
        }
    }
}
