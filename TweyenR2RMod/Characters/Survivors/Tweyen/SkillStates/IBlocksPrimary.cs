namespace TweyenR2RMod.Survivors.Tweyen.SkillStates
{
    /// <summary>
    /// A "Body" state during which the primary can't be used while <see cref="BlocksPrimary"/> is true (e.g. Wheel of
    /// Death, or Clincher until its shot: they play full-body animations). Checked by
    /// <see cref="SkillDefs.TweyenPrimarySkillDef"/>.
    /// </summary>
    public interface IBlocksPrimary
    {
        bool BlocksPrimary { get; }
    }
}
