using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Simulasi pemain BERJALAN (CharacterController, bukan teleport) dari start ke setiap target misi:
// keycard -> pintu brankas (dibuka) -> terminal -> ekstraksi. Mendeteksi celah yang terlalu sempit
// untuk badan pemain atau target yang tidak menampilkan prompt E.
//   Unity.exe -batchmode -projectPath . -executeMethod WalkSmokeTest.Run   (env WALK_SEED opsional)
[InitializeOnLoad]
public static class WalkSmokeTest
{
    private const string ActiveKey = "WalkSmoke.Active";
    private const string FrameKey = "WalkSmoke.Frame";
    private const string RunKey = "WalkSmoke.Run";
    private const string ReportKey = "WalkSmoke.Report";
    private const string FailKey = "WalkSmoke.Fails";
    private const string SeedKey = "WalkSmoke.Seed";
    private const int Runs = 10;

    private static List<Interactable> targets;
    private static int targetIndex;
    private static Vector3[] corners;
    private static int corner;
    private static float bestDistance;
    private static int stuckFrames;
    private static int holdFrames;
    private static float lastInteractTime;

    static WalkSmokeTest()
    {
        if (SessionState.GetBool(ActiveKey, false))
            EditorApplication.update += Tick;
    }

    [MenuItem("Stealth/FP - Smoke Test Jalan ke Target Misi")]
    public static void Run()
    {
        string seed = System.Environment.GetEnvironmentVariable("WALK_SEED");
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetInt(FrameKey, 0);
        SessionState.SetInt(RunKey, 0);
        SessionState.SetInt(FailKey, 0);
        SessionState.SetString(ReportKey, "");
        SessionState.SetInt(SeedKey, string.IsNullOrEmpty(seed) ? 0 : int.Parse(seed));
        DifficultyDirector.ResetAll();
        DifficultyDirector.SetDifficulty(0.4f, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Levels/Level_Adaptive.unity");
        EditorApplication.EnterPlaymode();
    }

    private static void Report(string s) => SessionState.SetString(ReportKey, SessionState.GetString(ReportKey, "") + s + "\n");

    private static void Fail(string s)
    {
        SessionState.SetInt(FailKey, SessionState.GetInt(FailKey, 0) + 1);
        Report("  FAIL " + s);
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

        PlayerController player = Object.FindFirstObjectByType<PlayerController>();
        PlayerInteractor interactor = player.GetComponent<PlayerInteractor>();

        if (targets == null)
        {
            int force = SessionState.GetInt(SeedKey, 0);
            if (force != 0 && builder.Seed != force)
            {
                ProceduralLevelBuilder.NextSeedOverride = force;
                SessionState.SetInt(FrameKey, 0);
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }

            StealthGameManager.Instance.EndBriefing();
            foreach (EnemyBTController g in builder.Guards) g.gameObject.SetActive(false);
            foreach (SecurityCamera c in Object.FindObjectsByType<SecurityCamera>(FindObjectsSortMode.None)) c.enabled = false;

            targets = new List<Interactable>();
            foreach (MissionManager.MissionStep step in MissionManager.Instance.Steps)
            {
                if (step.optional || step.target == null) continue;
                Interactable it = step.target.GetComponent<Interactable>();
                if (it != null) targets.Add(it);
            }

            targetIndex = 0;
            corners = null;
            holdFrames = 0;
            Report("== seed " + builder.Seed + " d=" + builder.Profile.difficulty.ToString("0.00") + " | target: " + targets.Count);
            return;
        }

        if (targetIndex >= targets.Count)
        {
            NextRun();
            return;
        }

        Interactable target = targets[targetIndex];

        if (corners == null)
        {
            // Jeda setelah interaksi sebelumnya (pintu butuh waktu bergeser & NavMesh carve diperbarui).
            if (holdFrames == 0) { lastInteractTime = Time.time; holdFrames = 1; }
            if (Time.time - lastInteractTime < 2f)
                return;

            Vector3 goal = target.InteractionPoint;
            goal.y = 0f;
            NavMesh.SamplePosition(goal, out NavMeshHit goalHit, 3f, NavMesh.AllAreas);
            NavMesh.SamplePosition(player.transform.position, out NavMeshHit startHit, 3f, NavMesh.AllAreas);
            NavMeshPath path = new NavMeshPath();
            NavMesh.CalculatePath(startHit.position, goalHit.position, NavMesh.AllAreas, path);

            if (path.status == NavMeshPathStatus.PathInvalid || path.corners.Length == 0)
            {
                Fail(target.name + ": tidak ada jalur NavMesh (" + path.status + ")");
                targetIndex++;
                return;
            }

            corners = path.corners;
            corner = 1;
            bestDistance = float.MaxValue;
            stuckFrames = 0;
            holdFrames = -1000;
            return;
        }

        Vector3 flat = target.InteractionPoint - player.transform.position;
        flat.y = 0f;

        // Sampai: prompt E harus muncul untuk target ini, lalu tahan/tekan E (disimulasikan).
        if (interactor.Current == target)
        {
            if (holdFrames < -997) { holdFrames++; return; }

            if (!target.CanInteract(interactor.Inventory, out string reason))
                Fail(target.name + ": prompt muncul tapi tidak bisa dipakai: " + reason);
            else
                target.Interact(interactor);

            Capture(target.GetType().Name + "_" + ProceduralLevelBuilder.Instance.Seed);
            Report("  ok   " + target.GetType().Name + " '" + target.name + "' (jarak " + flat.magnitude.ToString("0.00") + " m)");
            targetIndex++;
            corners = null;
            holdFrames = 0;
            return;
        }

        if (corner >= corners.Length)
        {
            Fail(target.name + ": sampai ujung jalur tapi prompt E tidak muncul (jarak " + flat.magnitude.ToString("0.00") +
                 " m, terpilih: " + (interactor.Current != null ? interactor.Current.name : "-") + ")");
            targetIndex++;
            corners = null;
            return;
        }

        // Jalan menuju corner berikutnya dengan CharacterController (fisik sama seperti pemain).
        Vector3 to = corners[corner] - player.transform.position;
        to.y = 0f;

        if (to.magnitude < 0.25f)
        {
            corner++;
            return;
        }

        CharacterController cc = player.GetComponent<CharacterController>();
        Vector3 move = to.normalized * Mathf.Min(0.08f, to.magnitude);
        cc.Move(move + Vector3.down * 0.05f);
        player.transform.rotation = Quaternion.LookRotation(to.normalized);

        float d = flat.magnitude;
        if (d < bestDistance - 0.01f) { bestDistance = d; stuckFrames = 0; }
        else if (++stuckFrames > 300)
        {
            string hits = "";
            foreach (Collider c in Physics.OverlapSphere(player.transform.position, 0.9f))
                if (c.transform.root != player.transform)
                    hits += c.name + "[" + LayerMask.LayerToName(c.gameObject.layer) + ", root " + c.transform.root.name + ", " + c.bounds.center.ToString("F1") + " size " + c.bounds.size.ToString("F1") + "]  ";
            Report("       menyentuh: " + hits + " | corner tujuan " + corners[corner].ToString("F1"));
            Fail(target.name + ": pemain TERSANGKUT di " + player.transform.position.ToString("F1") +
                 " (sisa jarak " + d.ToString("0.00") + " m, corner " + corner + "/" + corners.Length + ")");
            targetIndex++;
            corners = null;
        }
    }

    // Screenshot dari kamera game (hanya jika ada grafis, yaitu batchmode tanpa -nographics).
    private static void Capture(string name)
    {
        string folder = System.Environment.GetEnvironmentVariable("WALK_SHOTS");
        Camera cam = Camera.main;

        if (string.IsNullOrEmpty(folder) || cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;

        CameraFollow follow = cam.GetComponent<CameraFollow>();
        if (follow != null) follow.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);

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

    private static void NextRun()
    {
        targets = null;
        int run = SessionState.GetInt(RunKey, 0) + 1;
        SessionState.SetInt(RunKey, run);

        if (run >= Runs || SessionState.GetInt(SeedKey, 0) != 0)
        {
            int fails = SessionState.GetInt(FailKey, 0);
            Debug.Log("[WALK] " + (fails == 0 ? "PASS" : "FAIL (" + fails + ")") + "\n" + SessionState.GetString(ReportKey, ""));
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;
            DifficultyDirector.ResetAll();
            EditorApplication.ExitPlaymode();
            if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
            return;
        }

        DifficultyDirector.SetDifficulty(run / (float)(Runs - 1), true);
        SessionState.SetInt(FrameKey, 0);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
