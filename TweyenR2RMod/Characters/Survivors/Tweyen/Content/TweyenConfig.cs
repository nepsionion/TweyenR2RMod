using BepInEx.Configuration;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// Player preferences (BepInEx config, section "Tweyen"; also in Risk of Options' menu when it is installed).
    /// Read when used, so changes apply in game, except the ones marked "Requires restart.", read once at startup.
    /// </summary>
    internal static class TweyenConfig
    {
        public static ConfigEntry<bool> firstPersonAim;
        public static ConfigEntry<bool> voiceLines;
        public static ConfigEntry<bool> movementSound;
        public static ConfigEntry<bool> restockableTwoCrown;
        public static ConfigEntry<bool> japaneseName;
        public static ConfigEntry<bool> unlockDlcSkins;

        /// <summary>The restockable option as it was at startup (the skill's text is set then).</summary>
        public static bool RestockableTwoCrown { get; private set; }
        private static bool useJapaneseName;
        /// <summary>The DLC skins option as it was at startup (the skins are made then).</summary>
        public static bool UnlockDlcSkins { get; private set; }

        public static void Init(AssetBundle assetBundle)
        {
            const string section = "Tweyen";
            firstPersonAim = Modules.Config.BindAndOptions(section, "First-person Asteroid Jaeger aim", true,
                "Aim Asteroid Jaeger in first person. Off: the camera stays behind her and only zooms in.");
            voiceLines = Modules.Config.BindAndOptions(section, "Voice lines", true,
                "Play her voice lines when she is hurt and when she dies.");
            movementSound = Modules.Config.BindAndOptions(section, "Movement sound", true,
                "Play the sound of her walking and running.");
            restockableTwoCrown = Modules.Config.BindAndOptions(section, "Restockable Two-Crown's Strife", false,
                "Two-Crown's Strife recharges and can be restocked like any other skill, instead of being usable once per stage. Requires restart.");
            japaneseName = Modules.Config.BindAndOptions(section, "Japanese name", false,
                "Rename Tweyen (EN name) to Song (JP name). Requires restart.");
            unlockDlcSkins = Modules.Config.BindAndOptions(section, "Unlock DLC skins", false,
                "Unlock the Sunlight and Umbra skins, whose achievements need the Seekers of the Storm and Survivors of the Void expansions. Requires restart.");
            RestockableTwoCrown = restockableTwoCrown.Value;
            UnlockDlcSkins = unlockDlcSkins.Value;
            useJapaneseName = japaneseName.Value;
            Modules.Config.SetModInfo(assetBundle.LoadAsset<Texture>("TweyenCharacter"), Named("Tweyen of the Eternals."));
        }

        /// <summary>The text with every "Tweyen" changed to "Song" when the Japanese name option is on (at startup).</summary>
        public static string Named(string text)
        {
            return useJapaneseName && text != null ? text.Replace("Tweyen", "Song") : text;
        }
    }
}
