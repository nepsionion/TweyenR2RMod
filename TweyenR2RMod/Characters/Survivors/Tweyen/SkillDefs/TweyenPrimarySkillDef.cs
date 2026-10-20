using JetBrains.Annotations;
using RoR2;
using RoR2.Skills;
using TweyenR2RMod.Survivors.Tweyen.SkillStates;

namespace TweyenR2RMod.Survivors.Tweyen.SkillDefs
{
    /// <summary>
    /// Tweyen's primary: unavailable while the body is in an <see cref="IBlocksPrimary"/> state that blocks it (e.g.
    /// Wheel of Death, Clincher's draw).
    /// Firing while sprinting is allowed and stops the sprint (cancelSprintingOnActivation).
    /// </summary>
    public class TweyenPrimarySkillDef : SkillDef
    {
        public override bool IsReady([NotNull] GenericSkill skillSlot)
        {
            if (!base.IsReady(skillSlot)) return false;
            CharacterBody body = skillSlot.characterBody;
            EntityStateMachine bodyMachine = body ? EntityStateMachine.FindByCustomName(body.gameObject, "Body") : null;
            return !(bodyMachine && bodyMachine.state is IBlocksPrimary blocker && blocker.BlocksPrimary);
        }
    }
}
