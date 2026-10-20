using System.Collections.Generic;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.Projectiles
{
    /// <summary>
    /// Recolours borrowed RoR2 effects to Tweyen's violet. Hues are replaced and saturation and brightness kept, so an
    /// effect keeps its look (white-hot cores stay white). Most RoR2 effect materials take their colours from a colour
    /// ramp (_RemapTex), which is recoloured too.
    /// </summary>
    internal static class TweyenEffectRecolour
    {
        private static readonly Dictionary<Texture, Texture2D> recolouredRamps = new Dictionary<Texture, Texture2D>();

        /// <summary>Recolours every renderer, line, trail, particle colour and light of an effect (its own copies).</summary>
        public static void RecolourEffect(GameObject effect, Color colour)
        {
            Color.RGBToHSV(colour, out float hue, out _, out _);
            foreach (Renderer renderer in effect.GetComponentsInChildren<Renderer>(true))
            {
                Recolour(renderer, hue);
                if (renderer is LineRenderer line) line.colorGradient = Recolour(line.colorGradient, hue);
                if (renderer is TrailRenderer trail) trail.colorGradient = Recolour(trail.colorGradient, hue);
            }
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particles.main;
                main.startColor = Recolour(main.startColor, hue);
                ParticleSystem.ColorOverLifetimeModule overLifetime = particles.colorOverLifetime;
                if (overLifetime.enabled) overLifetime.color = Recolour(overLifetime.color, hue);
            }
            foreach (Light light in effect.GetComponentsInChildren<Light>(true))
            {
                light.color = Recolour(light.color, hue);
            }
        }

        /// <summary>Gives the renderer its own copies of its materials with their colour ramp and tints turned to <paramref name="hue"/>.</summary>
        public static void Recolour(Renderer renderer, float hue)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (!materials[i]) continue;
                Material m = new Material(materials[i]);
                if (m.HasProperty("_RemapTex") && m.GetTexture("_RemapTex"))
                {
                    m.SetTexture("_RemapTex", RecolourRamp(m.GetTexture("_RemapTex"), hue));
                }
                foreach (string tint in new[] { "_TintColor", "_Color", "_EmColor" })
                {
                    if (m.HasProperty(tint)) m.SetColor(tint, Recolour(m.GetColor(tint), hue));
                }
                materials[i] = m;
            }
            renderer.sharedMaterials = materials;
        }

        public static ParticleSystem.MinMaxGradient Recolour(ParticleSystem.MinMaxGradient g, float hue)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(Recolour(g.color, hue));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(Recolour(g.colorMin, hue), Recolour(g.colorMax, hue));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(Recolour(g.gradient, hue));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(Recolour(g.gradientMin, hue), Recolour(g.gradientMax, hue));
                case ParticleSystemGradientMode.RandomColor:
                    return new ParticleSystem.MinMaxGradient(Recolour(g.gradient, hue)) { mode = ParticleSystemGradientMode.RandomColor };
                default:
                    return g;
            }
        }

        public static Gradient Recolour(Gradient gradient, float hue)
        {
            if (gradient == null) return null;
            GradientColorKey[] keys = gradient.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = Recolour(keys[i].color, hue);
            var result = new Gradient { mode = gradient.mode };
            result.SetKeys(keys, gradient.alphaKeys);
            return result;
        }

        public static Color Recolour(Color c, float hue)
        {
            Color.RGBToHSV(c, out _, out float s, out float v);
            Color result = Color.HSVToRGB(hue, s, v, true);
            result.a = c.a;
            return result;
        }

        /// <summary>
        /// Copies a colour ramp with every texel's hue set to <paramref name="hue"/> (saturation and brightness kept).
        /// The game's textures aren't readable, so it is copied through the GPU first.
        /// </summary>
        public static Texture2D RecolourRamp(Texture source, float hue)
        {
            if (recolouredRamps.TryGetValue(source, out Texture2D cached) && cached) return cached;

            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var ramp = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
            ramp.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            Color[] pixels = ramp.GetPixels();
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Recolour(pixels[i], hue);
            ramp.SetPixels(pixels);
            ramp.wrapMode = source.wrapMode;
            ramp.filterMode = source.filterMode;
            ramp.name = source.name + "Tweyen";
            ramp.Apply(false, true);
            recolouredRamps[source] = ramp;
            return ramp;
        }
    }
}
