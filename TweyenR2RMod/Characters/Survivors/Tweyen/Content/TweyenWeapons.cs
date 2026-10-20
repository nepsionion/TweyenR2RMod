using RoR2;
using RoR2.Skills;
using RoR2.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TweyenR2RMod.Modules;
using TweyenR2RMod.Survivors.Tweyen.Components;
using TweyenR2RMod.Survivors.Tweyen.SkillStates;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// Tweyen's bows from Granblue Fantasy Relink, picked in the loadout's "Weapon Cosmetic" row (below Skin).
    /// Cosmetic only: every bow fires the same arrows.
    /// - The choice is a loadout GenericSkill whose variants are never activated (no input reaches a slot outside the
    ///   main four), so the game saves it per profile and syncs it to every player like any skill choice.
    /// - All bows are in the model on the shared bow skeleton (so they play the same animations); renderers are tagged
    ///   with their bow by the Unity builder, and only the chosen bow's are active (<see cref="Show"/>).
    /// </summary>
    internal static class TweyenWeapons
    {
        public struct Weapon
        {
            public string modelId;
            public string name;
            /// <summary>Flavour text shown in the loadout tooltip.</summary>
            public string description;
            /// <summary>Sprite in Art/Icons/Bows.</summary>
            public string icon;
        }

        /// <summary>In Relink's weapon menu order. The first is the default.</summary>
        public static readonly Weapon[] weapons =
        {
            new Weapon
            {
                modelId = "wp2300", name = "Bow of Dismissal", icon = "BowOfDismissal",
                description = "The original bow of this naturally talented huntress; with it and her eagle eyes, she can track and shoot prey far beyond the horizon.",
            },
            new Weapon
            {
                modelId = "wp2306", name = "Desolation-Crown Bow", icon = "DesolationCrownBow",
                description = "Brandished by Tweyen of the Eternals, the arrows fired from the bow resemble meteors blazing through the blue sky.",
            },
            new Weapon
            {
                modelId = "wp2303", name = "Hercules", icon = "Hercules",
                description = "A colossal bow with countless legends, its emerald body glitters when bent and shimmers like no other when an arrow is loosed.",
            },
            new Weapon
            {
                modelId = "wp2305", name = "Bow of Eternal Splendor", icon = "BowOfEternalSplendor",
                description = "When the one chosen by this bow releases its arrows, their radiant light shall illuminate the battlefield like stars in the night sky.",
            },
        };

        public const string GENERIC_SKILL_NAME = "TweyenWeaponCosmetic";
        public static readonly string TITLE_TOKEN = TweyenSurvivor.TWEYEN_PREFIX + "LOADOUT_WEAPON_COSMETIC";

        private static GameObject bodyPrefab;
        /// <summary>Renderer name -> the bow (model id) it belongs to.</summary>
        private static Dictionary<string, string> rendererWeapons;

        public static void Init(GameObject body, AssetBundle assetBundle, IEnumerable<TweyenSurvivor.SkinRenderer> renderers)
        {
            bodyPrefab = body;
            rendererWeapons = renderers.Where(r => !string.IsNullOrEmpty(r.weapon)).ToDictionary(r => r.renderer, r => r.weapon);

            Modules.Language.Add(TITLE_TOKEN, "Weapon\nCosmetic");
            GenericSkill slot = Skills.CreateGenericSkillWithSkillFamily(body, GENERIC_SKILL_NAME, "WeaponCosmetic");
            slot.loadoutTitleToken = TITLE_TOKEN;

            SkillDef[] variants = weapons.Select(w =>
            {
                string token = TweyenSurvivor.TWEYEN_PREFIX + "WEAPON_" + w.icon.ToUpperInvariant();
                Modules.Language.Add(token + "_NAME", w.name);
                Modules.Language.Add(token + "_DESCRIPTION", w.description);
                return Skills.CreateSkillDef(new SkillDefInfo
                {
                    skillName = "Tweyen" + w.icon,
                    skillNameToken = token + "_NAME",
                    skillDescriptionToken = token + "_DESCRIPTION",
                    skillIcon = assetBundle.LoadAsset<Sprite>(w.icon),
                    activationState = new EntityStates.SerializableEntityStateType(typeof(NoOp)),
                    activationStateMachineName = "Weapon",
                    interruptPriority = EntityStates.InterruptPriority.Any,
                    baseRechargeInterval = 0f,
                    baseMaxStock = 1,
                    isCombatSkill = false,
                    cancelSprintingOnActivation = false,
                    mustKeyPress = true,
                });
            }).ToArray();
            Skills.AddSkillsToFamily(slot.skillFamily, variants);
            for (int i = 0; i < variants.Length; i++)
            {
                slot.skillFamily.variants[i].unlockableDef = TweyenUnlocks.BowUnlockable(i + 1);
            }

            body.AddComponent<TweyenWeaponCosmetic>();
            On.RoR2.UI.LoadoutPanelController.Rebuild += LoadoutPanelRebuild;
        }

        /// <summary>The display model in character select follows the player's choice too.</summary>
        public static void SetupDisplay(GameObject display)
        {
            CharacterSelectSurvivorPreviewDisplayController preview = display.GetComponent<CharacterSelectSurvivorPreviewDisplayController>();
            if (!preview)
            {
                preview = display.AddComponent<CharacterSelectSurvivorPreviewDisplayController>();
                preview.bodyPrefab = bodyPrefab;
                preview.skillChangeResponses = Array.Empty<CharacterSelectSurvivorPreviewDisplayController.SkillChangeResponse>();
                preview.skinChangeResponses = Array.Empty<CharacterSelectSurvivorPreviewDisplayController.SkinChangeResponse>();
            }
            display.AddComponent<TweyenWeaponCosmeticDisplay>();
            ChildLocator locator = display.GetComponent<ChildLocator>();
            if (locator) Show(locator, 0);
        }

        /// <summary>Index of the weapon GenericSkill among the body's GenericSkills: its slot in the loadout.</summary>
        public static int LoadoutSlot(GameObject body)
        {
            GenericSkill[] skills = body.GetComponents<GenericSkill>();
            return Array.FindIndex(skills, s => s.skillName == GENERIC_SKILL_NAME);
        }

        public static int LoadoutSlot() => bodyPrefab ? LoadoutSlot(bodyPrefab) : -1;

        public static BodyIndex BodyIndex => bodyPrefab ? BodyCatalog.FindBodyIndex(bodyPrefab) : BodyIndex.None;

        /// <summary>The chosen weapon in a loadout (0, the default, if there is none).</summary>
        public static int Chosen(Loadout loadout, BodyIndex bodyIndex, int slot)
        {
            if (loadout == null || bodyIndex == BodyIndex.None || slot < 0) return 0;
            int chosen = (int)loadout.bodyLoadoutManager.GetSkillVariant(bodyIndex, slot);
            return chosen >= 0 && chosen < weapons.Length ? chosen : 0;
        }

        /// <summary>Shows weapon <paramref name="index"/>'s renderers on a model (or display) and hides the other bows'.</summary>
        public static void Show(ChildLocator locator, int index)
        {
            if (!locator || rendererWeapons == null) return;
            string modelId = weapons[Mathf.Clamp(index, 0, weapons.Length - 1)].modelId;
            foreach (KeyValuePair<string, string> pair in rendererWeapons)
            {
                Transform renderer = locator.FindChild(pair.Key);
                if (renderer) renderer.gameObject.SetActive(pair.Value == modelId);
            }
        }

        #region loadout row below Skin
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo rowsField = typeof(LoadoutPanelController).GetField("rows", Instance);

        /// <summary>
        /// The loadout panel always puts the Skin row last; this moves the "Weapon Cosmetic" row right under it and
        /// redoes the panel's up/down gamepad navigation for the new order.
        /// </summary>
        private static void LoadoutPanelRebuild(On.RoR2.UI.LoadoutPanelController.orig_Rebuild orig, LoadoutPanelController self)
        {
            orig(self);
            try
            {
                MoveRowBelowSkin(self);
            }
            catch (Exception e)
            {
                Log.Warning($"could not move the Weapon Cosmetic row below Skin: {e}");
            }
        }

        private static void MoveRowBelowSkin(LoadoutPanelController panel)
        {
            if (!(rowsField?.GetValue(panel) is IList rows) || rows.Count < 2) return;
            Type rowType = rows[0].GetType();
            FieldInfo panelTransformField = rowType.GetField("rowPanelTransform", Instance);
            FieldInfo rowDataField = rowType.GetField("rowData", Instance);

            int ours = -1, skin = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                RectTransform rowTransform = (RectTransform)panelTransformField.GetValue(rows[i]);
                if (!rowTransform) continue;
                foreach (LanguageTextMeshController label in rowTransform.GetComponentsInChildren<LanguageTextMeshController>(true))
                {
                    if (label.token == TITLE_TOKEN) ours = i;
                    else if (label.token == "LOADOUT_SKIN") skin = i;
                }
            }
            if (ours < 0 || skin < 0 || ours > skin) return;

            object row = rows[ours];
            RectTransform ourTransform = (RectTransform)panelTransformField.GetValue(row);
            RectTransform skinTransform = (RectTransform)panelTransformField.GetValue(rows[skin]);
            ourTransform.SetSiblingIndex(skinTransform.GetSiblingIndex()); // ours was above: Skin moves up one
            rows.RemoveAt(ours);
            rows.Insert(skin, row);

            // up/down navigation between rows, as the panel does it (left/right stays as it was)
            List<List<MPButton>> buttons = new List<List<MPButton>>();
            foreach (object r in rows)
            {
                IList data = (IList)rowDataField.GetValue(r);
                List<MPButton> rowButtons = new List<MPButton>();
                foreach (object d in data) rowButtons.Add((MPButton)d.GetType().GetField("button", Instance).GetValue(d));
                buttons.Add(rowButtons);
            }
            for (int j = 0; j < buttons.Count; j++)
            {
                for (int k = 0; k < buttons[j].Count; k++)
                {
                    UnityEngine.UI.Navigation navigation = buttons[j][k].navigation;
                    navigation.selectOnUp = j > 0 && buttons[j - 1].Count > 0 ? buttons[j - 1][Mathf.Min(k, buttons[j - 1].Count - 1)] : null;
                    navigation.selectOnDown = j < buttons.Count - 1 && buttons[j + 1].Count > 0 ? buttons[j + 1][Mathf.Min(k, buttons[j + 1].Count - 1)] : null;
                    buttons[j][k].navigation = navigation;
                }
            }
        }
        #endregion
    }
}
