using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sanity checks for the generated Tweyen assets: every animation curve must bind to a transform in mdlTweyen,
/// and a few poses are rendered to PNG so the result can be inspected without opening the game.
///   Unity.exe -batchmode -quit -projectPath TweyenUnityProject -executeMethod TweyenValidator.Run
/// (no -nographics: rendering needs a graphics device)
/// </summary>
public static class TweyenValidator
{
    const string Root = "Assets/TweyenAssets/TweyenBundle";

    [MenuItem("Tweyen/Validate")]
    public static void Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/mdlTweyen.prefab");
        var model = Object.Instantiate(prefab);
        // one bow (and its matching crystals) at a time, like in game: TWEYEN_WEAPON=wp23xx (default Bow of Dismissal)
        string weapon = System.Environment.GetEnvironmentVariable("TWEYEN_WEAPON");
        if (string.IsNullOrEmpty(weapon)) weapon = "wp2300";
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var tag = System.Text.RegularExpressions.Regex.Match(r.name, @"_(wp\d{4})_");
            if (tag.Success) r.gameObject.SetActive(tag.Groups[1].Value == weapon);
        }
        // the Animator would keep re-applying its default state over the sampled poses
        Object.DestroyImmediate(model.GetComponent<Animator>());
        // several renders happen within one editor frame: re-skin on every render
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;
        // red marker on ArrowTip (projectile spawn point)
        var tipT = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ArrowTip");
        if (tipT)
        {
            var tipMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(tipMarker.GetComponent<Collider>());
            tipMarker.transform.SetParent(tipT, false);
            tipMarker.transform.localScale = Vector3.one * 0.12f;
            tipMarker.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
        }
        int bad = 0, total = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { Root + "/Animation" }))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                total++;
                if (!model.transform.Find(b.path)) { bad++; if (bad < 10) Debug.LogError($"[TweyenValidator] {clip.name}: unbound path {b.path}"); }
            }
        }
        Debug.Log($"[TweyenValidator] curve bindings: {total - bad}/{total} resolve");

        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../ValidationRenders"));
        Directory.CreateDirectory(outDir);
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.16f, 0.19f);
        cam.fieldOfView = 30f;
        var light = new GameObject("light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

        var rt = new RenderTexture(512, 768, 24);
        cam.targetTexture = rt;
        var shots = new (string clip, float t, float yaw)[]
        {
            ("animIdle", 0f, 180f), ("animIdle", 0f, 135f), ("animRunForward", 0.2f, 90f),
            ("animWheelOfDeath", 0.25f, 160f), ("animWheelOfDeath", 0.5f, 160f), ("animWheelOfDeath", 0.75f, 160f), ("animWheelOfDeath", 1.0f, 160f),
        };
        var tex = new Texture2D(512, 768, TextureFormat.RGB24, false);
        var sheet = new Texture2D(512 * shots.Length, 768, TextureFormat.RGB24, false);
        for (int i = 0; i < shots.Length; i++)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Root}/Animation/{shots[i].clip}.anim");
            clip.SampleAnimation(model, shots[i].t);
            if (tipT && shots[i].clip == "animStellarArrow")
            {
                Transform bowRoot = model.transform.Find("BowArmature/_000");
                Transform nock = tipT.parent;
                Debug.Log($"[TweyenValidator] {shots[i].clip} t={shots[i].t}: tip {tipT.position} nock {nock.position} bow grip {(bowRoot ? bowRoot.position : Vector3.zero)} tip-nock dir {(tipT.position - nock.position).normalized} model forward {model.transform.forward}");
            }
            // camera orbits the character at chest height; yaw 180 looks at her face (she faces +Z)
            Quaternion rot = Quaternion.Euler(5f, shots[i].yaw, 0f);
            cam.transform.position = new Vector3(0f, 1.0f, 0f) + rot * new Vector3(0f, 0f, -6.5f);
            cam.transform.LookAt(new Vector3(0f, 1.0f, 0f));
            light.transform.rotation = cam.transform.rotation * Quaternion.Euler(20f, -25f, 0f); // key light from over the camera
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 512, 768), 0, 0);
            tex.Apply();
            sheet.SetPixels(512 * i, 0, 512, 768, tex.GetPixels());
        }
        RenderTexture.active = null;
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(outDir, "poses.png"), sheet.EncodeToPNG());

        var smrs = model.GetComponentsInChildren<SkinnedMeshRenderer>();
        Debug.Log($"[TweyenValidator] renderers {smrs.Length}, missing materials {smrs.Count(s => !s.sharedMaterial)}, missing meshes {smrs.Count(s => !s.sharedMesh)}");

        // face close-up (idle, front) to check the eyes
        AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Root}/Animation/animIdle.anim").SampleAnimation(model, 0f);
        Transform head = model.GetComponentsInChildren<Transform>().First(t => t.name == "HeadHurtbox");
        Vector3 focus = head.position + new Vector3(0f, -0.05f, 0f);
        var faceRt = new RenderTexture(768, 768, 24);
        cam.targetTexture = faceRt;
        cam.fieldOfView = 12f;
        cam.transform.position = focus + new Vector3(0f, 0.02f, 1.6f);
        cam.transform.LookAt(focus);
        light.transform.rotation = cam.transform.rotation * Quaternion.Euler(20f, -25f, 0f);
        cam.Render();
        RenderTexture.active = faceRt;
        var face = new Texture2D(768, 768, TextureFormat.RGB24, false);
        face.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
        face.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(outDir, "face.png"), face.EncodeToPNG());

        // three-quarter view of the head (checks the face mask's shape against the rest of the head)
        cam.transform.position = focus + Quaternion.Euler(0f, 50f, 0f) * new Vector3(0f, 0.02f, 1.6f);
        cam.transform.LookAt(focus);
        light.transform.rotation = cam.transform.rotation * Quaternion.Euler(20f, -25f, 0f);
        cam.Render();
        RenderTexture.active = faceRt;
        face.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
        face.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(outDir, "face34.png"), face.EncodeToPNG());

        // arrow ghost seen from the side: tip should touch the green marker (origin) and point right (+Z)
        Object.DestroyImmediate(model);
        model = null;
        var arrow = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/TweyenArrowGhost.prefab"));
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.transform.localScale = Vector3.one * 0.04f;
        var markerMat = new Material(Shader.Find("Unlit/Color")) { color = Color.green };
        marker.GetComponent<Renderer>().sharedMaterial = markerMat;
        var arrowRt = new RenderTexture(1024, 384, 24);
        cam.targetTexture = arrowRt;
        cam.orthographic = true;
        cam.orthographicSize = 0.45f;
        cam.transform.position = new Vector3(-3f, 0f, -0.85f);
        cam.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
        light.transform.rotation = cam.transform.rotation * Quaternion.Euler(20f, -25f, 0f);
        cam.Render();
        RenderTexture.active = arrowRt;
        var arrowTex = new Texture2D(1024, 384, TextureFormat.RGB24, false);
        arrowTex.ReadPixels(new Rect(0, 0, 1024, 384), 0, 0);
        arrowTex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(outDir, "arrow.png"), arrowTex.EncodeToPNG());
        Object.DestroyImmediate(arrow);
        Object.DestroyImmediate(marker);

        Debug.Log($"[TweyenValidator] renders -> {outDir}");
        Object.DestroyImmediate(cam.gameObject);
        Object.DestroyImmediate(light.gameObject);
    }
}
