using EntityStates;

namespace TweyenR2RMod.Survivors.Tweyen.SkillStates
{
    /// <summary>
    /// A skill state that does nothing and hands control straight back: the activation state of the loadout-only
    /// "skills" (the passives and the bow cosmetic), which are never used.
    /// </summary>
    public class NoOp : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority)
            {
                outer.SetNextStateToMain();
            }
        }

        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return InterruptPriority.Any;
        }
    }
}
