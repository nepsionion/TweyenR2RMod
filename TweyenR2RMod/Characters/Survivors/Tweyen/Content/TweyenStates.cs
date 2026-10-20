using TweyenR2RMod.Survivors.Tweyen.SkillStates;

namespace TweyenR2RMod.Survivors.Tweyen
{
    public static class TweyenStates
    {
        public static void Init()
        {
            Modules.Content.AddEntityState(typeof(NoOp));
            Modules.Content.AddEntityState(typeof(StellarArrow));
            Modules.Content.AddEntityState(typeof(WheelOfDeath));
            Modules.Content.AddEntityState(typeof(Clincher));
            Modules.Content.AddEntityState(typeof(TwoCrownStrife));
            Modules.Content.AddEntityState(typeof(TweyenDeathState));
            Modules.Content.AddEntityState(typeof(AsteroidJaegerAim));
            Modules.Content.AddEntityState(typeof(MultilockHailFire));
            Modules.Content.AddEntityState(typeof(ValiantWeave));
        }
    }
}
