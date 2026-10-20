using System;
using System.Collections.Generic;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// Timing data exported with the animations (TweyenAnimEvents in the asset bundle, written by the Unity builder from
    /// Tools/export_unity.py), e.g. when Wheel of Death's spin reaches each 60 degree step and which way she faces.
    /// </summary>
    public static class TweyenAnimEvents
    {
        [Serializable] public class AnimEvent { public string name; public float time; public float yaw; }
        [Serializable] public class ClipEvents { public string clip; public float length; public AnimEvent[] events; }
        [Serializable] private class Table { public ClipEvents[] clips = new ClipEvents[0]; }

        private static readonly Dictionary<string, ClipEvents> byClip = new Dictionary<string, ClipEvents>();

        public static void Init(AssetBundle assetBundle)
        {
            TextAsset asset = assetBundle.LoadAsset<TextAsset>("TweyenAnimEvents");
            if (!asset)
            {
                Log.Error("TweyenAnimEvents missing from the asset bundle");
                return;
            }
            foreach (ClipEvents clip in JsonUtility.FromJson<Table>(asset.text).clips)
            {
                byClip[clip.clip] = clip;
            }
        }

        public static ClipEvents Get(string clip)
        {
            return byClip.TryGetValue(clip, out ClipEvents events) ? events : null;
        }
    }
}
