using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using GameAssetPaths = RoR2BepInExPack.GameAssetPathsBetter;

namespace TweyenR2RMod.Survivors.Tweyen.Projectiles
{
    /// <summary>
    /// The glowing bolt Tweyen's arrows carry, like the fireball-like shots of her bow in Granblue Fantasy Relink (they
    /// make the arrows easy to see). Built from the Artificer's Flame Bolt ghost, recoloured to the arrows' violet:
    /// - its fire (two stretched spheres) is replaced by one thinner fire pyramid (a cone) whose point sits on the arrow's
    ///   tip and which widens forward to a rounded front, as if the tip were releasing it; it spins like the original;
    /// - its two spinning trails are kept, closer together and thinner (they replace the arrow's own trail);
    /// - its point light is switched on.
    /// Colours: recoloured with <see cref="TweyenEffectRecolour"/> (keeps the flame's brightness and shading).
    /// </summary>
    internal static class TweyenArrowBolt
    {
        /// <summary>Length of the fire pyramid ahead of the arrow's tip (m).</summary>
        public const float LENGTH = 1.8f;
        /// <summary>Its widest radius (m). The Flame Bolt's fire is 0.18 (but shorter).</summary>
        public const float RADIUS = 0.18f;
        /// <summary>Distance of the spinning trails from the arrow's axis (m), and their width factor.</summary>
        public const float TRAIL_OFFSET = 0.15f;
        public const float TRAIL_WIDTH = 0.9f;
        public const float LIGHT_RANGE = 4f;

        public static void Attach(GameObject ghost, Color colour)
        {
            GameObject flameBolt = Addressables.LoadAssetAsync<GameObject>(GameAssetPaths.RoR2_Base_Mage.MageFireboltGhost_prefab).WaitForCompletion();
            Transform fire = flameBolt ? flameBolt.transform.Find("Sphere") : null;
            if (!fire)
            {
                Log.Warning("Artificer's Flame Bolt ghost not found; arrows fly without their bolt");
                return;
            }
            Color.RGBToHSV(colour, out float hue, out _, out _);

            GameObject bolt = new GameObject("Bolt");
            bolt.transform.SetParent(ghost.transform, false); // the ghost's origin is the arrow's tip, +Z forward

            GameObject pyramid = Object.Instantiate(fire.gameObject, bolt.transform, false);
            pyramid.name = "FirePyramid";
            pyramid.transform.localPosition = Vector3.zero;
            pyramid.transform.localRotation = Quaternion.identity;
            pyramid.transform.localScale = Vector3.one;
            pyramid.GetComponent<MeshFilter>().sharedMesh = BuildPyramid(LENGTH, RADIUS);
            TweyenEffectRecolour.Recolour(pyramid.GetComponent<Renderer>(), hue);

            Transform spinner = flameBolt.transform.Find("Spinner");
            if (spinner)
            {
                GameObject trails = Object.Instantiate(spinner.gameObject, bolt.transform, false);
                trails.name = "Trails";
                foreach (TrailRenderer trail in trails.GetComponentsInChildren<TrailRenderer>(true))
                {
                    Vector3 p = trail.transform.localPosition;
                    trail.transform.localPosition = new Vector3(Mathf.Sign(p.x) * TRAIL_OFFSET, p.y, p.z);
                    trail.widthMultiplier *= TRAIL_WIDTH;
                    trail.colorGradient = TweyenEffectRecolour.Recolour(trail.colorGradient, hue);
                    TweyenEffectRecolour.Recolour(trail, hue);
                }
                trails.AddComponent<ClearTrailsOnEnable>();
            }

            Transform light = flameBolt.transform.Find("Point light");
            if (light)
            {
                GameObject glow = Object.Instantiate(light.gameObject, bolt.transform, false);
                glow.name = "Glow";
                glow.transform.localPosition = new Vector3(0f, 0f, LENGTH * 0.5f);
                glow.SetActive(true);
                Light l = glow.GetComponent<Light>();
                l.color = colour;
                l.range = LIGHT_RANGE;
            }
        }

        /// <summary>
        /// A cone along +Z: point at the origin, widening to <paramref name="radius"/> at 3/4 of its length, then a
        /// rounded front. UVs: u around, v along its length.
        /// </summary>
        private static Mesh BuildPyramid(float length, float radius)
        {
            const int rings = 24, segments = 24;
            const float shoulder = 0.75f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float t = (float)i / rings;
                float r = t < shoulder ? radius * t / shoulder : radius * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((t - shoulder) / (1f - shoulder), 2f)));
                for (int j = 0; j <= segments; j++)
                {
                    float a = 2f * Mathf.PI * j / segments;
                    Vector3 radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    vertices.Add(radial * r + Vector3.forward * (t * length));
                    normals.Add(radial);
                    uvs.Add(new Vector2((float)j / segments, t));
                }
            }
            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < segments; j++)
                {
                    int a = i * (segments + 1) + j, b = a + 1, c = a + segments + 1, d = c + 1;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            }
            var mesh = new Mesh { name = "TweyenArrowFirePyramid" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Ghosts are pooled: a reused one must not draw a trail from where its last arrow ended.</summary>
    internal class ClearTrailsOnEnable : MonoBehaviour
    {
        private TrailRenderer[] trails;

        private void OnEnable()
        {
            if (trails == null) trails = GetComponentsInChildren<TrailRenderer>(true);
            foreach (TrailRenderer trail in trails) trail.Clear();
        }
    }
}
