using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds Tweyen's prefabs, materials, animation clips and animator controllers from the data exported by
/// Tools/export_unity.py (TweyenUnityProject/TweyenSource), then builds the "tweyenbundle" AssetBundle.
/// Run from the menu (Tweyen > Build All) or headless:
///   Unity.exe -batchmode -quit -projectPath TweyenUnityProject -executeMethod TweyenBuilder.BuildAll
/// Everything under Assets/TweyenAssets/TweyenBundle is regenerated on each run.
/// </summary>
public static class TweyenBuilder
{
    const string BundleName = "tweyenbundle";
    /// <summary>Uniform model scale (GBFR proportions read small next to RoR2 survivors).</summary>
    const float ModelScale = 1.1f;
    /// <summary>Scale of the arrow model on Tweyen's projectiles (the game's arrow is 1.85 m long).</summary>
    const float ArrowScale = 1.0f;
    /// <summary>
    /// The game's arrow is a flat blade (17 cm wide, 3 cm thick). Its thin axis is scaled by this, and a second
    /// copy rotated 90 degrees around its length makes it cross-shaped, so it reads as solid from every angle.
    /// </summary>
    const float ArrowThickness = 3.0f;
    const string Root = "Assets/TweyenAssets/TweyenBundle";
    static string Source => Path.GetFullPath(Path.Combine(Application.dataPath, "../TweyenSource"));

    #region data
    [Serializable] class BoneData { public string name; public int parent; public float[] t; public float[] r; public float[] s; }
    [Serializable] class VariantData { public string albedo; public string normal; }
    [Serializable] class RendererData { public string name; public string group; public string weapon; public long offset; public int vertexCount; public int indexCount; public int[] bones; public VariantData[] variants; }
    [Serializable] public class AnimEvent { public string name; public float time; public float yaw; }
    [Serializable] class ClipInfo { public string name; public string motionId; public bool loop; public float length; public AnimEvent[] events; }
    [Serializable] class ClipSpeed { public string clip; public float speed; }
    [Serializable] class PropData { public string name; public string group; public long offset; public int vertexCount; public int indexCount; public string albedo; public string normal; }
    [Serializable] class AttachmentData { public string name; public string parentPath; public float[] position; }
    [Serializable] class Manifest { public BoneData[] bones; public string[] bonePaths; public int headBone; public RendererData[] renderers; public int variantCount; public ClipInfo[] clips; public ClipSpeed[] clipSpeeds; public PropData[] props; public AttachmentData[] attachments; }
    [Serializable] class CurveData { public string path; public string prop; public float[] times; public float[] values; }
    [Serializable] class ClipData { public string name; public string motionId; public float length; public bool loop; public CurveData[] curves; public AnimEvent[] events; }

    /// <summary>Written into the bundle (TweyenAnimEvents) so skill code can time things to the animations.</summary>
    [Serializable] public class AnimEventTable { public ClipEvents[] clips; }
    [Serializable] public class ClipEvents { public string clip; public float length; public AnimEvent[] events; }

    /// <summary>Written into the bundle so the mod knows which material each renderer uses in each skin.</summary>
    [Serializable] public class SkinTable { public SkinRenderer[] renderers; }
    [Serializable] public class SkinRenderer { public string renderer; public string group; public string weapon; public string[] materials; }
    #endregion

    [MenuItem("Tweyen/Build All")]
    public static void BuildAll()
    {
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(Source, "manifest.json")));

        if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
        foreach (string sub in new[] { "Textures", "Icons", "Materials", "Meshes", "Animation" })
            Directory.CreateDirectory(Path.Combine(Root, sub));
        AssetDatabase.Refresh();

        ImportTextures();
        var materials = BuildMaterials(manifest);
        var clips = BuildClips(manifest);
        var controller = BuildBodyController(manifest, clips);
        var menuController = BuildMenuController(clips);
        BuildEventTable(manifest);
        BuildPrefabs(manifest, materials, controller, menuController);
        BuildArrowGhost(manifest);
        AssignBundle();
        BuildBundle();
        Debug.Log("[TweyenBuilder] done");
    }

    #region textures and materials
    static void ImportTextures()
    {
        foreach (string f in Directory.GetFiles(Path.Combine(Source, "textures"), "*.png"))
            File.Copy(f, Path.Combine(Root, "Textures", Path.GetFileName(f)), true);
        foreach (string f in Directory.GetFiles(Path.Combine(Source, "icons"), "*.png"))
            File.Copy(f, Path.Combine(Root, "Icons", Path.GetFileName(f)), true);
        // hand-made icons from <workspace>/Art/Icons (any subfolder); same-named files replace generated placeholders.
        // folders named "temp" hold reference images and are skipped.
        string art = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Icons"));
        if (Directory.Exists(art))
            foreach (string f in Directory.GetFiles(art, "*.png", SearchOption.AllDirectories))
            {
                bool inTemp = Path.GetRelativePath(art, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => part.Equals("temp", StringComparison.OrdinalIgnoreCase));
                if (!inTemp) File.Copy(f, Path.Combine(Root, "Icons", Path.GetFileName(f)), true);
            }
        AssetDatabase.Refresh();

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                bool normal = path.Contains("_nrml");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal;
                ti.maxTextureSize = 1024;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Icons" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
    }

    static Texture2D Tex(string file) =>
        string.IsNullOrEmpty(file) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{file}");

    /// <summary>One material per distinct albedo/normal pair. Returns [renderer][variant] -> material.</summary>
    static Material[][] BuildMaterials(Manifest m)
    {
        var shader = Shader.Find("Standard");
        var cache = new Dictionary<string, Material>();
        var result = new Material[m.renderers.Length][];
        for (int r = 0; r < m.renderers.Length; r++)
        {
            var rd = m.renderers[r];
            result[r] = new Material[m.variantCount];
            for (int v = 0; v < m.variantCount; v++)
            {
                var vd = rd.variants[v];
                string key = vd.albedo + "|" + vd.normal;
                if (!cache.TryGetValue(key, out var mat))
                {
                    string name = "mat" + Path.GetFileNameWithoutExtension(vd.albedo).Replace("_lod0", "").Replace("_albd", "");
                    if (cache.Values.Any(x => x.name == name)) name += "_" + cache.Count;
                    mat = new Material(shader) { name = name };
                    mat.SetTexture("_MainTex", Tex(vd.albedo));
                    var nrm = Tex(vd.normal);
                    if (nrm)
                    {
                        mat.SetTexture("_BumpMap", nrm);
                        mat.EnableKeyword("_NORMALMAP");
                    }
                    mat.SetFloat("_Glossiness", 0.2f);
                    mat.SetFloat("_Metallic", 0f);
                    AssetDatabase.CreateAsset(mat, $"{Root}/Materials/{name}.mat");
                    cache[key] = mat;
                }
                result[r][v] = mat;
            }
        }
        return result;
    }
    #endregion

    #region animation
    static Dictionary<string, AnimationClip> BuildClips(Manifest m)
    {
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var info in m.clips)
        {
            var data = JsonUtility.FromJson<ClipData>(File.ReadAllText(Path.Combine(Source, "clips", info.name + ".json")));
            var clip = new AnimationClip { name = "anim" + info.name, frameRate = 60f };
            foreach (var c in data.curves)
            {
                int n = c.times.Length;
                var keys = new Keyframe[n];
                for (int i = 0; i < n; i++)
                {
                    float inT = i > 0 ? (c.values[i] - c.values[i - 1]) / Mathf.Max(c.times[i] - c.times[i - 1], 1e-5f) : 0f;
                    float outT = i < n - 1 ? (c.values[i + 1] - c.values[i]) / Mathf.Max(c.times[i + 1] - c.times[i], 1e-5f) : 0f;
                    if (i == 0) inT = outT;
                    if (i == n - 1) outT = inT;
                    keys[i] = new Keyframe(c.times[i], c.values[i], inT, outT);
                }
                clip.SetCurve(c.path, typeof(Transform), c.prop, new AnimationCurve(keys));
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = info.loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, $"{Root}/Animation/{clip.name}.anim");
            clips[info.name] = clip;
        }
        return clips;
    }

    static void BuildEventTable(Manifest m)
    {
        var table = new AnimEventTable
        {
            clips = m.clips.Select(c => new ClipEvents { clip = c.name, length = c.length, events = c.events ?? Array.Empty<AnimEvent>() }).ToArray(),
        };
        AssetDatabase.CreateAsset(new TextAsset(JsonUtility.ToJson(table, true)), $"{Root}/TweyenAnimEvents.asset");
    }

    static float Speed(Manifest m, string clip) => m.clipSpeeds.First(s => s.clip == clip).speed;

    static AnimatorStateTransition Go(AnimatorState from, AnimatorState to, float duration, float? exitTime = null)
    {
        var t = from.AddTransition(to);
        t.duration = duration;
        t.hasFixedDuration = true;
        t.hasExitTime = exitTime.HasValue;
        if (exitTime.HasValue) t.exitTime = exitTime.Value;
        return t;
    }

    /// <summary>
    /// Body layer driven by the parameters RoR2's BaseCharacterMain sets. Jumps are entered by name from code
    /// ("Jump" / "BonusJump" on the "Body" layer). Locomotion playback scales with walkSpeed (m/s) divided by
    /// each clip's native travel speed, so feet roughly match the ground.
    /// </summary>
    static AnimatorController BuildBodyController(Manifest m, Dictionary<string, AnimationClip> clips)
    {
        var ac = AnimatorController.CreateAnimatorControllerAtPath($"{Root}/Animation/animTweyen.controller");
        foreach (string b in new[] { "isMoving", "isSprinting", "isGrounded" })
            ac.AddParameter(b, AnimatorControllerParameterType.Bool);
        foreach (string f in new[] { "forwardSpeed", "rightSpeed", "upSpeed", "walkSpeed", "turnAngle", "aimWeight", "mainRootPlaybackRate", "aimPitchCycle", "aimYawCycle" })
            ac.AddParameter(f, AnimatorControllerParameterType.Float);
        // isGrounded must start true so the character doesn't begin in the fall state
        var ps = ac.parameters;
        ps.First(p => p.name == "isGrounded").defaultBool = true;
        ps.First(p => p.name == "walkSpeed").defaultFloat = 7f;
        ac.parameters = ps;

        var layers = ac.layers;
        layers[0].name = "Body";
        ac.layers = layers;
        var sm = ac.layers[0].stateMachine;

        var idle = sm.AddState("Idle");
        idle.motion = clips["Idle"];
        sm.defaultState = idle;

        float run = Speed(m, "RunForward");
        var runState = ac.CreateBlendTreeInController("Run", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = "rightSpeed";
        tree.blendParameterY = "forwardSpeed";
        tree.AddChild(clips["RunForward"], new Vector2(0, 1));
        tree.AddChild(clips["WalkBackward"], new Vector2(0, -1));
        tree.AddChild(clips["WalkRight"], new Vector2(1, 0));
        tree.AddChild(clips["WalkLeft"], new Vector2(-1, 0));
        var children = tree.children;
        // the strafe/backpedal clips are slow walks; speed them up to the run's ground speed
        children[1].timeScale = run / Speed(m, "WalkBackward");
        children[2].timeScale = run / Speed(m, "WalkRight");
        children[3].timeScale = run / Speed(m, "WalkLeft");
        tree.children = children;
        // scaled-up legs cover more ground per cycle, so playback slows by the model scale
        runState.speedParameterActive = true;
        runState.speedParameter = "walkSpeed";
        runState.speed = 1f / (run * ModelScale);

        var sprint = sm.AddState("Sprint");
        sprint.motion = clips["Sprint"];
        sprint.speedParameterActive = true;
        sprint.speedParameter = "walkSpeed";
        sprint.speed = 1f / (Speed(m, "Sprint") * ModelScale);

        var jump = sm.AddState("Jump");
        jump.motion = clips["JumpStart"];
        var ascend = sm.AddState("Ascend");
        ascend.motion = clips["JumpAscend"];
        var bonusJump = sm.AddState("BonusJump");
        bonusJump.motion = clips["BonusJumpStart"];
        var bonusAscend = sm.AddState("BonusAscend");
        bonusAscend.motion = clips["BonusJumpAscend"];
        var fall = sm.AddState("Fall");
        fall.motion = clips["Fall"];
        var land = sm.AddState("Land");
        land.motion = clips["Land"];

        // locomotion
        Go(idle, runState, 0.15f).AddCondition(AnimatorConditionMode.If, 0, "isMoving");
        Go(runState, idle, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "isMoving");
        Go(runState, sprint, 0.2f).AddCondition(AnimatorConditionMode.If, 0, "isSprinting");
        Go(sprint, runState, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "isSprinting");
        Go(sprint, idle, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "isMoving");

        // walking off a ledge
        foreach (var s in new[] { idle, runState, sprint })
        {
            var t = Go(s, fall, 0.2f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "isGrounded");
            t.AddCondition(AnimatorConditionMode.Less, -3f, "upSpeed");
        }

        // jumps (entered from code), rising, falling, landing
        Go(jump, ascend, 0.05f, 1f);
        Go(bonusJump, bonusAscend, 0.05f, 1f);
        foreach (var rise in new[] { ascend, bonusAscend })
        {
            Go(rise, fall, 0.2f).AddCondition(AnimatorConditionMode.Less, -1f, "upSpeed");
            Go(rise, fall, 0.2f, 1f);
            Go(rise, land, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "isGrounded");
        }
        Go(fall, land, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "isGrounded");
        Go(land, idle, 0.3f, 0.35f);
        Go(land, runState, 0.15f).AddCondition(AnimatorConditionMode.If, 0, "isMoving");

        BuildFullBodyLayer(ac, clips);
        return ac;
    }

    /// <summary>
    /// "FullBody, Override": whole-body skill animations played from code (EntityState.PlayAnimation), on top of the
    /// locomotion layer. Idles in an empty state that contributes nothing.
    ///   StellarArrow: draw and shoot (GBFR 3600). Playback rate driven by "StellarArrow.playbackRate".
    ///   StellarArrowHold: aimed follow-through (GBFR 3630), held briefly then faded back to locomotion.
    /// </summary>
    static void BuildFullBodyLayer(AnimatorController ac, Dictionary<string, AnimationClip> clips)
    {
        ac.AddParameter("StellarArrow.playbackRate", AnimatorControllerParameterType.Float);
        var ps = ac.parameters;
        ps.First(p => p.name == "StellarArrow.playbackRate").defaultFloat = 1f;
        ac.parameters = ps;

        ac.AddLayer("FullBody, Override");
        var layers = ac.layers;
        layers[layers.Length - 1].defaultWeight = 1f;
        layers[layers.Length - 1].blendingMode = AnimatorLayerBlendingMode.Override;
        ac.layers = layers;
        var sm = ac.layers[layers.Length - 1].stateMachine;

        var empty = sm.AddState("BufferEmpty");
        sm.defaultState = empty;

        var shoot = sm.AddState("StellarArrow");
        shoot.motion = clips["StellarArrow"];
        shoot.speedParameterActive = true;
        shoot.speedParameter = "StellarArrow.playbackRate";

        var hold = sm.AddState("StellarArrowHold");
        hold.motion = clips["StellarArrowHold"];

        Go(shoot, hold, 0.05f, 1f);
        Go(hold, empty, 0.25f, 0.16f); // ~0.4 s of the 2.5 s follow-through, then back to locomotion

        // Wheel of Death: the 720 degree spin (GBFR 3440, pause removed). Playback rate set by the skill to fit its duration.
        ac.AddParameter("WheelOfDeath.playbackRate", AnimatorControllerParameterType.Float);
        var ps2 = ac.parameters;
        ps2.First(p => p.name == "WheelOfDeath.playbackRate").defaultFloat = 1f;
        ac.parameters = ps2;
        var wheel = sm.AddState("WheelOfDeath");
        wheel.motion = clips["WheelOfDeath"];
        wheel.speedParameterActive = true;
        wheel.speedParameter = "WheelOfDeath.playbackRate";
        Go(wheel, empty, 0.2f, 1f);

        // Clincher: draw, shot and recovery (GBFR 3420, first 3 s); the skill fires at 1.4 s and can be cancelled after
        var clincher = sm.AddState("Clincher");
        clincher.motion = clips["Clincher"];
        Go(clincher, empty, 0.25f, 1f);

        // Two-Crown's Strife: leap back, draw in the air, shot (GBFR 3460, up to the fall)
        var twoCrown = sm.AddState("TwoCrownStrife");
        twoCrown.motion = clips["TwoCrownStrife"];
        Go(twoCrown, empty, 0.2f, 1f);

        // Asteroid Jaeger: raise the bow and hold the aim (looping) while marking; Multilock Hail shot
        var multilockStart = sm.AddState("MultilockStart");
        multilockStart.motion = clips["MultilockStart"];
        var multilockHold = sm.AddState("MultilockHold");
        multilockHold.motion = clips["MultilockHold"];
        Go(multilockStart, multilockHold, 0.1f, 1f);
        var multilockFire = sm.AddState("MultilockFire");
        multilockFire.motion = clips["MultilockFire"];
        Go(multilockFire, empty, 0.25f, 1f);

        // Valiant Weave: the forward dash shot (GBFR 310b, dash only, 2.5x speed)
        var valiantWeave = sm.AddState("ValiantWeave");
        valiantWeave.motion = clips["ValiantWeave"];
        Go(valiantWeave, empty, 0.15f, 1f);
    }

    /// <summary>Character select: Merculight once, then idle.</summary>
    static AnimatorController BuildMenuController(Dictionary<string, AnimationClip> clips)
    {
        var ac = AnimatorController.CreateAnimatorControllerAtPath($"{Root}/Animation/animTweyenMenu.controller");
        var sm = ac.layers[0].stateMachine;
        var merc = sm.AddState("Merculight");
        merc.motion = clips["Merculight"];
        var idle = sm.AddState("Idle");
        idle.motion = clips["Idle"];
        sm.defaultState = merc;
        Go(merc, idle, 0.3f, 0.94f);
        return ac;
    }
    #endregion

    #region prefabs
    static void BuildPrefabs(Manifest m, Material[][] materials, AnimatorController body, AnimatorController menu)
    {
        var root = new GameObject("mdlTweyen");
        var bones = new Transform[m.bones.Length];
        for (int i = 0; i < m.bones.Length; i++)
        {
            var b = m.bones[i];
            var t = new GameObject(b.name).transform;
            t.SetParent(b.parent < 0 ? root.transform : bones[b.parent], false);
            t.localPosition = new Vector3(b.t[0], b.t[1], b.t[2]);
            t.localRotation = new Quaternion(b.r[0], b.r[1], b.r[2], b.r[3]);
            t.localScale = new Vector3(b.s[0], b.s[1], b.s[2]);
            bones[i] = t;
        }
        Transform armature = root.transform.Find("Armature");
        Transform bowArmature = root.transform.Find("BowArmature");

        // markers on animated bones, e.g. ArrowTip: where the (unrendered) drawn arrow's point is; projectiles spawn there
        foreach (var a in m.attachments ?? Array.Empty<AttachmentData>())
        {
            var marker = new GameObject(a.name).transform;
            marker.SetParent(root.transform.Find(a.parentPath), false);
            marker.localPosition = new Vector3(a.position[0], a.position[1], a.position[2]);
        }

        var skinTable = new SkinTable { renderers = new SkinRenderer[m.renderers.Length] };
        using (var reader = new BinaryReader(File.OpenRead(Path.Combine(Source, "meshes.bin"))))
        {
            for (int r = 0; r < m.renderers.Length; r++)
            {
                var rd = m.renderers[r];
                var mesh = ReadMesh(reader, rd);
                var go = new GameObject(rd.name);
                go.transform.SetParent(root.transform, false);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                var rbones = rd.bones.Select(i => bones[i]).ToArray();
                mesh.bindposes = rbones.Select(b => b.worldToLocalMatrix * root.transform.localToWorldMatrix).ToArray();
                AssetDatabase.CreateAsset(mesh, $"{Root}/Meshes/mesh_{rd.name}.asset");
                smr.sharedMesh = mesh;
                smr.bones = rbones;
                smr.rootBone = rd.group == "bow" ? bowArmature : armature;
                smr.sharedMaterial = materials[r][0];
                smr.localBounds = new Bounds(new Vector3(0, 1f, 0), new Vector3(4f, 4f, 4f));
                smr.updateWhenOffscreen = false;
                skinTable.renderers[r] = new SkinRenderer { renderer = rd.name, group = rd.group, weapon = rd.weapon, materials = materials[r].Select(x => x.name).ToArray() };
            }
        }

        var table = new TextAsset(JsonUtility.ToJson(skinTable, true));
        AssetDatabase.CreateAsset(table, $"{Root}/TweyenSkins.asset");

        // display: identical hierarchy (so skins apply by transform path), menu animations, no hurtboxes
        var display = UnityEngine.Object.Instantiate(root);
        display.name = "TweyenDisplay";

        // hurtboxes (RoR2 HurtBox components are added by the mod at runtime)
        var main = new GameObject("MainHurtbox");
        main.transform.SetParent(root.transform, false);
        var capsule = main.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.85f, 0f);
        capsule.radius = 0.32f;
        capsule.height = 1.7f;
        capsule.direction = 1;
        var head = new GameObject("HeadHurtbox");
        head.transform.SetParent(bones[m.headBone], false);
        head.transform.position = bones[m.headBone].position + new Vector3(0f, 0.1f, 0f);
        head.AddComponent<SphereCollider>().radius = 0.16f;

        // scale last: bind poses and hurtbox placement above are computed at scale 1, then everything scales together
        root.transform.localScale = Vector3.one * ModelScale;
        display.transform.localScale = Vector3.one * ModelScale;

        SetupAnimator(root, body);
        SetupAnimator(display, menu);
        PrefabUtility.SaveAsPrefabAsset(root, $"{Root}/mdlTweyen.prefab");
        PrefabUtility.SaveAsPrefabAsset(display, $"{Root}/TweyenDisplay.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        UnityEngine.Object.DestroyImmediate(display);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Projectile ghost for Tweyen's arrows: the arrow mesh with its tip on the ghost's origin pointing forward (+Z).
    /// ProjectileGhostController is added by the mod at load time.
    /// </summary>
    static void BuildArrowGhost(Manifest m)
    {
        var shader = Shader.Find("Standard");
        var ghost = new GameObject("TweyenArrowGhost");
        var model = new GameObject("ArrowModel");
        model.transform.SetParent(ghost.transform, false);
        model.transform.localScale = Vector3.one * ArrowScale;
        using (var reader = new BinaryReader(File.OpenRead(Path.Combine(Source, "props.bin"))))
        {
            foreach (var pd in m.props.Where(p => p.group == "arrow"))
            {
                var mesh = ReadStaticMesh(reader, pd);
                AssetDatabase.CreateAsset(mesh, $"{Root}/Meshes/mesh_{pd.name}.asset");
                var mat = new Material(shader) { name = "matTweyenArrow" };
                var albedo = Tex(pd.albedo);
                mat.SetTexture("_MainTex", albedo);
                var nrm = Tex(pd.normal);
                if (nrm) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }
                // the arrow is a glowing, magical one: its own colours double as emission
                mat.EnableKeyword("_EMISSION");
                mat.SetTexture("_EmissionMap", albedo);
                mat.SetColor("_EmissionColor", Color.white * 0.8f);
                mat.SetFloat("_Glossiness", 0.4f);
                AssetDatabase.CreateAsset(mat, $"{Root}/Materials/{mat.name}.mat");
                // mesh axes: X = blade width, Y = thin side, Z = length (tip at origin)
                foreach (float roll in new[] { 0f, 90f })
                {
                    var part = new GameObject(roll == 0f ? pd.name : pd.name + "_cross");
                    part.transform.SetParent(model.transform, false);
                    part.transform.localRotation = Quaternion.Euler(0f, 0f, roll);
                    part.transform.localScale = new Vector3(1f, ArrowThickness, 1f);
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = part.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }
        PrefabUtility.SaveAsPrefabAsset(ghost, $"{Root}/TweyenArrowGhost.prefab");
        UnityEngine.Object.DestroyImmediate(ghost);
        AssetDatabase.SaveAssets();
    }

    static Mesh ReadStaticMesh(BinaryReader r, PropData pd)
    {
        r.BaseStream.Position = pd.offset;
        int n = pd.vertexCount;
        Vector3[] V3() { var a = new Vector3[n]; for (int i = 0; i < n; i++) a[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); return a; }
        var pos = V3();
        var nrm = V3();
        var tan = new Vector4[n];
        for (int i = 0; i < n; i++) tan[i] = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        var uv = new Vector2[n];
        for (int i = 0; i < n; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
        var idx = new int[pd.indexCount];
        for (int i = 0; i < pd.indexCount; i++) idx[i] = r.ReadInt32();
        var mesh = new Mesh { name = pd.name, vertices = pos, normals = nrm, tangents = tan, uv = uv, triangles = idx };
        mesh.RecalculateBounds();
        return mesh;
    }

    static void SetupAnimator(GameObject go, RuntimeAnimatorController controller)
    {
        var anim = go.AddComponent<Animator>();
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    static Mesh ReadMesh(BinaryReader r, RendererData rd)
    {
        r.BaseStream.Position = rd.offset;
        int n = rd.vertexCount;
        Vector3[] V3() { var a = new Vector3[n]; for (int i = 0; i < n; i++) a[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); return a; }
        var pos = V3();
        var nrm = V3();
        var tan = new Vector4[n];
        for (int i = 0; i < n; i++) tan[i] = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        var uv = new Vector2[n];
        for (int i = 0; i < n; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
        var bi = new int[n * 4];
        for (int i = 0; i < n * 4; i++) bi[i] = r.ReadInt32();
        var bw = new float[n * 4];
        for (int i = 0; i < n * 4; i++) bw[i] = r.ReadSingle();
        var idx = new int[rd.indexCount];
        for (int i = 0; i < rd.indexCount; i++) idx[i] = r.ReadInt32();

        var mesh = new Mesh { name = rd.name };
        if (n > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = pos;
        mesh.normals = nrm;
        mesh.tangents = tan;
        mesh.uv = uv;
        mesh.triangles = idx;
        var weights = new BoneWeight[n];
        for (int i = 0; i < n; i++)
        {
            weights[i] = new BoneWeight
            {
                boneIndex0 = bi[i * 4], weight0 = bw[i * 4],
                boneIndex1 = bi[i * 4 + 1], weight1 = bw[i * 4 + 1],
                boneIndex2 = bi[i * 4 + 2], weight2 = bw[i * 4 + 2],
                boneIndex3 = bi[i * 4 + 3], weight3 = bw[i * 4 + 3],
            };
        }
        mesh.boneWeights = weights;
        mesh.RecalculateBounds();
        return mesh;
    }
    #endregion

    #region bundle
    static void AssignBundle()
    {
        // only Tweyen's folder goes into the bundle; the template's Henry assets stay out of it
        var henry = AssetImporter.GetAtPath("Assets/HenryAssets/HenryBundle");
        if (henry != null && !string.IsNullOrEmpty(henry.assetBundleName)) henry.assetBundleName = "";
        AssetImporter.GetAtPath(Root).assetBundleName = BundleName;
        AssetDatabase.RemoveUnusedAssetBundleNames();
        AssetDatabase.SaveAssets();
    }

    static void BuildBundle()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../AssetBundles"));
        Directory.CreateDirectory(outDir);
        var build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = AssetDatabase.GetAssetPathsFromAssetBundle(BundleName),
        };
        var manifest = BuildPipeline.BuildAssetBundles(outDir, new[] { build }, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new Exception("AssetBundle build failed");
        Debug.Log($"[TweyenBuilder] built {BundleName} with {build.assetNames.Length} assets -> {outDir}");
    }
    #endregion
}
