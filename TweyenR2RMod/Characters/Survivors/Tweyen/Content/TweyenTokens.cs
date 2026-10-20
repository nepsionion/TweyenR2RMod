using System;
using TweyenR2RMod.Modules;

namespace TweyenR2RMod.Survivors.Tweyen
{
    public static class TweyenTokens
    {
        public static void Init()
        {
            string prefix = TweyenSurvivor.TWEYEN_PREFIX;

            string desc = "One of the Eternals—they who stand astride the skies and across the path of any would-be calamity. "
             + "She destroys those that wrong her in a hail of shining arrows."
             // tips, laid out like the vanilla survivors'
             + "<style=cSub>" + Environment.NewLine + Environment.NewLine
             + "< ! > Tweyen moves faster than other survivors, so use her speed to keep your distance while your arrows do the work." + Environment.NewLine + Environment.NewLine
             + "< ! > Clincher pierces everything in its path, terrain included, and its damage grows with attack speed. With Twin-Star Huntress, every unique debuff on the target makes it hit even harder." + Environment.NewLine + Environment.NewLine
             + "< ! > Asteroid Jaeger marks the toughest enemies on screen first. Hold it to mark more targets, then fire to send arrows after each of them." + Environment.NewLine + Environment.NewLine
             + "< ! > With Peerless Eye, your skills give you Ultrasight, raising your attack speed. When you have 10 or more, your next Secondary or Special crits on every hit and spends them. Asteroid Jaeger does not give Ultrasight." + Environment.NewLine + Environment.NewLine
             + (TweyenConfig.RestockableTwoCrown
                ? "< ! > Two-Crown's Strife can only be used after 100 seconds since you entered the stage. Save it for a boss or a swarm, especially with debuffs to intensify."
                : "< ! > Two-Crown's Strife can only be used once per stage, and only after 100 seconds since you entered the stage. Save it for a boss or a swarm, especially with debuffs to intensify.")
             + "</style>" + Environment.NewLine;

            Language.Add(prefix + "NAME", "Tweyen");
            Language.Add(prefix + "DESCRIPTION", desc);
            Language.Add(prefix + "SUBTITLE", "Tweyen, the Second Eternal");
            string lore = "The Dark Huntress. A member of the Ten Eternals who are known as the \"Strongest in all the Skies\", and a master with the bow."
                + Environment.NewLine + Environment.NewLine
                + "With eyes that allow her to see for miles, and magical flight to rise high above, she uses her magical bow to hunt and kill all that she targets with natural grace. Once the legendary sharpshooter sets her sights on a target, there is no escape—no matter the distance. She strives for perfection, relentlessly pursuing her every flaw. Only by learning from the past, accepting the present, and focusing on the future can she face the next day with dignity. However, because of her power, she finds herself isolated and lonely."
                + Environment.NewLine + Environment.NewLine
                + "\"I'm good at finding things. No matter how small a clue is, my eyes can track anything down. And once a target's in my sight, I always strike true. I am the dark huntress... and there's nowhere you can run from me.\""
                + Environment.NewLine + Environment.NewLine
                + "\"I'm hunting a certain quarry; a monster with power that can rival even my own. My eyes have landed on the moon, but I wonder—what is it he sees when he looks at me?\"";
            Language.Add(prefix + "LORE", lore);
            Language.Add(prefix + "OUTRO_FLAVOR", "..and so she left, her next target already in sight.");
            Language.Add(prefix + "OUTRO_FAILURE", "..and so she vanished, her last arrow never fired.");

            Language.Add(prefix + "DEFAULT_SKIN_NAME", "Default");
            string[] skinNames = { "Splendor", "Glimmer", "Stellar", "Night", "Silver Relic", "Sunlight", "Bloody", "Duality", "Umbra", "Gold" };
            for (int i = 1; i < TweyenSurvivor.SKIN_COUNT; i++)
            {
                Language.Add(prefix + "SKIN_" + i + "_NAME", skinNames[i - 1]);
            }


            Language.Add(prefix + "PRIMARY_STELLAR_ARROW_NAME", "Stellar Arrow");
            Language.Add(prefix + "SECONDARY_CLINCHER_NAME", "Clincher");
            Language.Add(prefix + "SECONDARY_CLINCHER_DESCRIPTION", $"Draw back and fire a massive <style=cIsDamage>piercing arrow</style> for <style=cIsDamage>{100f * SkillStates.Clincher.damageCoefficient}% damage</style>. Damage scales with attack speed.");
            Language.Add(prefix + "SPECIAL_TWO_CROWN_STRIFE_NAME", "Two-Crown's Strife");
            Language.Add(prefix + "SPECIAL_TWO_CROWN_STRIFE_DESCRIPTION", $"<style=cIsDamage>Unstoppable.</style> Leap back and fire <style=cIsDamage>{SkillStates.TwoCrownStrife.baseArrowCount}</style> homing arrows that spread across enemies for <style=cIsDamage>{100f * SkillStates.TwoCrownStrife.damageCoefficient}% damage</style> each. Every hit extends and intensifies the enemy's debuffs. Usable only after <style=cIsUtility>100 seconds</style> after entering a stage"
                + (TweyenConfig.RestockableTwoCrown ? "." : ", and only <style=cIsUtility>once per stage</style>.")
                + " Additional charges are consumed together and increase the number of arrows.");
            Language.Add(prefix + "UTILITY_VALIANT_WEAVE_NAME", "Valiant Weave");
            Language.Add(prefix + "UTILITY_VALIANT_WEAVE_DESCRIPTION", $"Dash forward while firing an arrow for <style=cIsDamage>{100f * SkillStates.ValiantWeave.damageCoefficient}% damage</style>.");
            Language.Add(prefix + "UTILITY_WHEEL_OF_DEATH_NAME", "Wheel of Death");
            Language.Add(prefix + "UTILITY_WHEEL_OF_DEATH_DESCRIPTION", $"Dash forward while spinning twice, firing arrows in every direction for <style=cIsDamage>20x{100f * SkillStates.WheelOfDeath.damageCoefficient}% damage</style>. <style=cIsUtility>Immune to fall damage while spinning.</style>");

            Language.Add(prefix + "PRIMARY_STELLAR_ARROW_DESCRIPTION", $"Fire a homing arrow for <style=cIsDamage>{100f * SkillStates.StellarArrow.damageCoefficient}% damage</style>.");
        }
    }
}
