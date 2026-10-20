using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TweyenR2RMod.Modules
{
    internal static class Skins
    {
        /// <summary>
        /// creates a SkinDef with its SkinDefParams (where the game now reads renderers, meshes and activations from).
        /// <para>edit the returned skin through <c>skinDef.skinDefParams</c>, e.g. <c>skinDef.skinDefParams.meshReplacements</c></para>
        /// </summary>
        internal static SkinDef CreateSkinDef(string skinName, Sprite skinIcon, CharacterModel.RendererInfo[] defaultRendererInfos, GameObject root, UnlockableDef unlockableDef = null)
        {
            SkinDefParams skinDefParams = ScriptableObject.CreateInstance<SkinDefParams>();
            skinDefParams.name = skinName + "_Params";
            skinDefParams.rendererInfos = new CharacterModel.RendererInfo[defaultRendererInfos.Length];
            defaultRendererInfos.CopyTo(skinDefParams.rendererInfos, 0);

            SkinDef skinDef = ScriptableObject.CreateInstance<SkinDef>();
            skinDef.baseSkins = Array.Empty<SkinDef>();
            skinDef.icon = skinIcon;
            skinDef.unlockableDef = unlockableDef;
            skinDef.rootObject = root;
            skinDef.skinDefParams = skinDefParams;
            skinDef.nameToken = skinName;
            skinDef.name = skinName;

            return skinDef;
        }

        private static CharacterModel.RendererInfo[] getRendererMaterials(CharacterModel.RendererInfo[] defaultRenderers, params Material[] materials)
        {
            CharacterModel.RendererInfo[] newRendererInfos = new CharacterModel.RendererInfo[defaultRenderers.Length];
            defaultRenderers.CopyTo(newRendererInfos, 0);

            for (int i = 0; i < newRendererInfos.Length; i++)
            {
                try
                {
                    newRendererInfos[i].defaultMaterial = materials[i];
                }
                catch
                {
                    Log.Error("error adding skin rendererinfo material. make sure you're not passing in too many");
                }
            }

            return newRendererInfos;
        }
        /// <summary>
        /// pass in strings for mesh assets in your bundle. pass the same amount and order based on your rendererinfos, filling with null as needed
        /// <code>
        /// myskindef.skinDefParams.meshReplacements = Modules.Skins.getMeshReplacements(assetBundle, defaultRenderers,
        ///    "meshHenrySword",
        ///    null,
        ///    "meshHenry");
        /// </code>
        /// </summary>
        /// <param name="assetBundle">the assetbundle to load the meshes from</param>
        /// <param name="defaultRendererInfos">your skindef's rendererinfos to access the renderers</param>
        /// <param name="meshes">name of the mesh assets in your project</param>
        /// <returns></returns>
        internal static SkinDefParams.MeshReplacement[] getMeshReplacements(AssetBundle assetBundle, CharacterModel.RendererInfo[] defaultRendererInfos, params string[] meshes)
        {

            List<SkinDefParams.MeshReplacement> meshReplacements = new List<SkinDefParams.MeshReplacement>();

            for (int i = 0; i < defaultRendererInfos.Length; i++)
            {
                if (string.IsNullOrEmpty(meshes[i]))
                    continue;

                meshReplacements.Add(
                new SkinDefParams.MeshReplacement
                {
                    renderer = defaultRendererInfos[i].renderer,
                    mesh = assetBundle.LoadAsset<Mesh>(meshes[i])
                });
            }

            return meshReplacements.ToArray();
        }
    }
}
