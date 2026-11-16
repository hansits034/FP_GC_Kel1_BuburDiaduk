using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Screenshot ventilasi (normal / beracun / laser) dari kamera game, untuk verifikasi visual.
//   Unity.exe -batchmode -projectPath . -executeMethod VentShotTest.Run   (env VENT_SHOTS = folder)
[InitializeOnLoad]
public static class VentShotTest
{
    private const string ActiveKey = "VentShot.Active";
    private const string FrameKey = "VentShot.Frame";
    private const string FoundKey = "VentShot.Found";
    private const string TriesKey = "VentShot.Tries";

    static VentShotTest()
    {
        if (SessionState.GetBool(ActiveKey, false))
            EditorApplication.update += Tick;
    }

    public static void Run()
    {
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetInt(FrameKey, 0);
        SessionState.SetInt(FoundKey, 0);
        SessionState.SetInt(TriesKey, 0);
        DifficultyDirector.ResetAll();
        DifficultyDirector.SetDifficulty(0.9f, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Levels/Level_Adaptive.unity");
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
            return;

        int frame = SessionState.GetInt(FrameKey, 0) + 1;
        SessionState.SetInt(FrameKey, frame);
        ProceduralLevelBuilder builder = ProceduralLevelBuilder.Instance;

        if (frame < 20 || builder == null)
            return;

        int found = SessionState.GetInt(FoundKey, 0); // bit 1 = normal, 2 = toxic, 4 = laser

        if (frame == 20)
        {
            StealthGameManager.Instance.EndBriefing();
            foreach (EnemyBTController g in builder.Guards) g.gameObject.SetActive(false);

            foreach (LayoutVent v in builder.Layout.vents)
            {
                int bit = v.hazard == VentHazardType.None ? 1 : v.hazard == VentHazardType.Toxic ? 2 : 4;
                if ((found & bit) != 0) continue;

                // Taruh pemain di dalam saluran, dekat lubang masuk -> kamera mengikuti.
                PlayerController player = Object.FindFirstObjectByType<PlayerController>();
                CharacterController cc = player.GetComponent<CharacterController>();
                cc.enabled = false;
                Vector3 outward = new Vector3(v.outward.x, 0f, v.outward.y);
                player.transform.position = (LevelLayout.CellToWorld(v.cellA) + LevelLayout.CellToWorld(v.cellB)) * 0.5f + outward * 2.1f + Vector3.up * 0.6f;
                cc.enabled = true;

                SessionState.SetInt("VentShot.Pending", bit);
                SessionState.SetString("VentShot.Name", v.hazard + "_" + builder.Seed);
                break;
            }
        }

        if (frame == 60 && SessionState.GetInt("VentShot.Pending", 0) != 0)
        {
            Camera cam = Camera.main;
            PlayerController p = Object.FindFirstObjectByType<PlayerController>();
            cam.transform.position = p.transform.position - cam.transform.forward * 15f;
            Capture(cam, SessionState.GetString("VentShot.Name", "vent"));
            SessionState.SetInt(FoundKey, found | SessionState.GetInt("VentShot.Pending", 0));
            SessionState.SetInt("VentShot.Pending", 0);
            found = SessionState.GetInt(FoundKey, 0);
        }

        if (frame < 70)
            return;

        int tries = SessionState.GetInt(TriesKey, 0) + 1;
        SessionState.SetInt(TriesKey, tries);

        if (found == 7 || tries > 40)
        {
            Debug.Log("[VENTSHOT] selesai, ditemukan mask " + found + " setelah " + tries + " layout");
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;
            DifficultyDirector.ResetAll();
            EditorApplication.ExitPlaymode();
            if (Application.isBatchMode) EditorApplication.Exit(0);
            return;
        }

        // Variasikan difficulty agar dapat ventilasi normal (rendah) & berbahaya (tinggi).
        DifficultyDirector.SetDifficulty((found & 1) == 0 && tries % 2 == 0 ? 0.1f : 0.9f, true);
        SessionState.SetInt(FrameKey, 0);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private static void Capture(Camera cam, string name)
    {
        string folder = System.Environment.GetEnvironmentVariable("VENT_SHOTS");
        if (string.IsNullOrEmpty(folder) || cam == null) return;

        RenderTexture rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
