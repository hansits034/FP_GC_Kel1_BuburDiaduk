using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Smoke test otomatis Level Adaptif di Play Mode (bisa dari command line):
//   Unity.exe -batchmode -projectPath . -executeMethod AdaptivePlaymodeSmokeTest.Run
// Memeriksa: layout valid, penjaga di NavMesh, jalur misi ada di NavMesh, AI berjalan,
// adaptasi live DDA, akhir run -> DDA + ML terupdate, lalu run kedua memakai difficulty baru.
[InitializeOnLoad]
public static class AdaptivePlaymodeSmokeTest
{
    private const string ActiveKey = "AdaptiveSmoke.Active";
    private const string FrameKey = "AdaptiveSmoke.Frame";
    private const string PhaseKey = "AdaptiveSmoke.Phase";
    private const string ErrorsKey = "AdaptiveSmoke.Errors";
    private const string ReportKey = "AdaptiveSmoke.Report";
    private const string ScenePath = "Assets/Scenes/Levels/Level_Adaptive.unity";

    static AdaptivePlaymodeSmokeTest()
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;

        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Stealth/FP - Smoke Test Level Adaptif")]
    public static void Run()
    {
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetInt(FrameKey, 0);
        SessionState.SetInt(PhaseKey, 0);
        SessionState.SetInt(ErrorsKey, 0);
        SessionState.SetString(ReportKey, "");

        DifficultyDirector.ResetAll();
        EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.EnterPlaymode();
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return;

        if (condition.Contains("Licensing") || condition.Contains("Access token") || stackTrace.Contains("UnityEditor.Search"))
            return;

        SessionState.SetInt(ErrorsKey, SessionState.GetInt(ErrorsKey, 0) + 1);
        Report("ERROR: " + condition + "\n" + stackTrace);
    }

    private static void Report(string line)
    {
        SessionState.SetString(ReportKey, SessionState.GetString(ReportKey, "") + line + "\n");
    }

    private static void Check(bool condition, string what)
    {
        Report((condition ? "  OK   " : "  FAIL ") + what);

        if (!condition)
            SessionState.SetInt(ErrorsKey, SessionState.GetInt(ErrorsKey, 0) + 1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
            return;

        int frame = SessionState.GetInt(FrameKey, 0) + 1;
        SessionState.SetInt(FrameKey, frame);
        int phase = SessionState.GetInt(PhaseKey, 0);

        if (phase == 4 && EditorApplication.timeSinceStartup - SessionState.GetFloat("AdaptiveSmoke.ReloadTime", 0f) > 45f)
        {
            Check(false, "scene di-reload setelah kalah (timeout 45 dtk)");
            Finish();
            return;
        }

        if (frame > 200000)
        {
            Check(false, "TIMEOUT");
            Finish();
            return;
        }

        switch (phase)
        {
            case 0 when frame >= 30: CheckGeneration("RUN 1"); Next(); break;
            case 1 when frame >= 330: CheckAIRunning(); Next(); break;
            case 2 when frame >= 340: EndRun(); Next(); break;
            case 3 when frame >= 400: Reload(); Next(); break;
            case 4 when SceneReloaded(): SessionState.SetInt(FrameKey, 0); Next(); break;
            case 5 when frame >= 30: CheckGeneration("RUN 2 (setelah DDA)"); Next(); break;
            case 6 when frame >= 60: Finish(); break;
        }
    }

    private static void Next()
    {
        SessionState.SetInt(PhaseKey, SessionState.GetInt(PhaseKey, 0) + 1);
    }

    private static void CheckGeneration(string label)
    {
        Report("== " + label);
        ProceduralLevelBuilder builder = ProceduralLevelBuilder.Instance;
        Check(builder != null && builder.Layout != null, "generator & layout ada");

        if (builder == null || builder.Layout == null)
            return;

        LevelLayout layout = builder.Layout;
        Report("  seed " + builder.Seed + " | difficulty " + builder.Profile.difficulty.ToString("0.00") + " (" + builder.Profile.Label + ")" +
               " | ruangan " + layout.rooms.Count + " | penjaga " + layout.guards.Count + " | CCTV " + layout.cameras.Count +
               " | prop " + layout.props.Count + " | eksposur " + layout.exposure + "/" + layout.exposureBudget +
               " | " + builder.GenerationMs.ToString("0") + " ms");

        Check(layout.valid, "layout lolos validasi PCG");

        if (label.StartsWith("RUN 2"))
            Check(Mathf.Abs(builder.Profile.difficulty - DifficultyDirector.Difficulty) < 0.001f && builder.Profile.difficulty < 0.39f,
                "run kedua memakai difficulty hasil DDA (" + builder.Profile.difficulty.ToString("0.00") + ")");
        Check(builder.Guards.Count == layout.guards.Count, "semua penjaga ter-spawn (" + builder.Guards.Count + ")");

        int onMesh = 0;
        foreach (EnemyBTController guard in builder.Guards)
            if (guard != null && guard.GetComponent<NavMeshAgent>().isOnNavMesh) onMesh++;
        Check(onMesh == builder.Guards.Count, "penjaga berada di NavMesh (" + onMesh + "/" + builder.Guards.Count + ")");

        PlayerController player = Object.FindFirstObjectByType<PlayerController>();
        Check(player != null && Vector3.Distance(player.transform.position, LevelLayout.CellToWorld(layout.playerStart) + Vector3.up) < 1.5f,
            "player dipindah ke titik start");

        if (player != null)
        {
            Check(NavPath(player.transform.position, LevelLayout.CellToWorld(layout.keycard)), "NavMesh: start -> keycard");
            Check(NavPath(LevelLayout.CellToWorld(layout.keycard), LevelLayout.CellToWorld(layout.extraction)), "NavMesh: keycard -> ekstraksi");
        }

        MissionManager mission = MissionManager.Instance;
        Check(mission != null && mission.Steps.Count >= 4, "misi dikonfigurasi (" + (mission != null ? mission.Steps.Count : 0) + " langkah)");

        if (mission != null)
            foreach (MissionManager.MissionStep step in mission.Steps)
                Check(step.target != null, "target misi '" + step.key + "' ada");

        Check(Object.FindObjectsByType<Door>(FindObjectsSortMode.None).Length == 1, "pintu brankas terkunci ada");
        Check(Object.FindObjectsByType<SecurityCamera>(FindObjectsSortMode.None).Length == layout.cameras.Count, "CCTV ter-spawn");
        Check(Object.FindObjectsByType<AlarmPanel>(FindObjectsSortMode.None).Length == layout.alarms.Count, "panel alarm ter-spawn (" + layout.alarms.Count + ")");

        CheckVents(layout, player);

        if (StealthGameManager.Instance != null)
            StealthGameManager.Instance.EndBriefing();
    }

    // Kapsul seukuran pemain JONGKOK harus bisa melewati ventilasi dari ruangan A ke B.
    private static void CheckVents(LevelLayout layout, PlayerController player)
    {
        Report("  ventilasi: " + layout.vents.Count + (layout.routeUsesVent ? " (rute stealth lewat vent)" : ""));

        if (player == null)
            return;

        CharacterController cc = player.GetComponent<CharacterController>();
        float radius = cc != null ? cc.radius : 0.4f;
        int mask = ~((1 << 7) | (1 << 2)); // abaikan Player & IgnoreRaycast

        foreach (LayoutVent v in layout.vents)
        {
            Vector3 outward = new Vector3(v.outward.x, 0f, v.outward.y);
            Vector3 frontA = LevelLayout.CellToWorld(v.cellA - v.outward);
            Vector3 frontB = LevelLayout.CellToWorld(v.cellB - v.outward);
            Vector3 inA = LevelLayout.CellToWorld(v.cellA) + outward * 2.1f;
            Vector3 inB = LevelLayout.CellToWorld(v.cellB) + outward * 2.1f;
            Vector3[] route = { frontA, inA, inB, frontB };
            bool clear = true;
            string blocker = "";

            // Penutup ventilasi sudah dibuka (simulasi).
            foreach (VentCover cover in Object.FindObjectsByType<VentCover>(FindObjectsSortMode.None))
                foreach (Collider c in cover.GetComponentsInChildren<Collider>()) c.enabled = false;

            for (int i = 0; i < route.Length - 1 && clear; i++)
            {
                Vector3 p1 = route[i] + Vector3.up * (radius + 0.05f);
                Vector3 p2 = route[i] + Vector3.up * (1.1f - radius);
                Vector3 delta = route[i + 1] - route[i];

                if (Physics.CapsuleCast(p1, p2, radius * 0.95f, delta.normalized, out RaycastHit hit, delta.magnitude, mask, QueryTriggerInteraction.Ignore))
                {
                    clear = false;
                    blocker = hit.collider.name + " @" + hit.point.ToString("F1");
                }
            }

            Check(clear, "ventilasi " + layout.rooms[v.roomA].name + " -> " + layout.rooms[v.roomB].name + " (" + v.hazard + ") bisa dilalui jongkok" +
                         (clear ? "" : " - terhalang " + blocker));
        }
    }

    private static void CheckAIRunning()
    {
        Report("== AI & DDA live");
        ProceduralLevelBuilder builder = ProceduralLevelBuilder.Instance;

        if (builder == null)
            return;

        int patrolling = 0;
        foreach (EnemyBTController guard in builder.Guards)
            if (guard != null && guard.CurrentAction != "None") patrolling++;
        Check(patrolling == builder.Guards.Count, "behavior tree berjalan (" + patrolling + " penjaga punya aksi)");

        float before = builder.Guards.Count > 0 ? builder.Guards[0].VisionRange : 0f;
        DifficultyDirector.NudgeLive(-0.15f, "smoke test");
        float after = builder.Guards.Count > 0 ? builder.Guards[0].VisionRange : 0f;
        Check(after < before, "DDA live menurunkan jarak pandang penjaga (" + before.ToString("0.0") + " -> " + after.ToString("0.0") + ")");

        // Paksa satu penjaga melihat pemain melalui laporan -> penjaga lain mencari.
        if (AlertManager.Current != null && builder.Guards.Count > 1)
        {
            AlertManager.Current.ReportSighting(builder.Guards[0], LevelLayout.CellToWorld(builder.Layout.extraction), Vector3.forward);
            int searching = 0;
            foreach (EnemyBTController guard in builder.Guards)
                if (guard.Awareness == AwarenessState.Searching) searching++;
            Check(searching > 0, "shared alert: " + searching + " penjaga beralih ke Searching");
        }
    }

    private static void EndRun()
    {
        Report("== Akhir run (simulasi kalah)");
        int before = DifficultyDirector.History.Count;
        float d = DifficultyDirector.Difficulty;

        if (StealthGameManager.Instance != null)
            StealthGameManager.Instance.Lose("smoke test");

        Check(DifficultyDirector.History.Count == before + 1, "RunRecord tersimpan ke riwayat DDA");
        Check(DifficultyDirector.Predictor.samples == 1, "model ML dilatih 1 sampel");
        Check(DifficultyDirector.Difficulty < d, "kalah -> difficulty turun (" + d.ToString("0.00") + " -> " + DifficultyDirector.Difficulty.ToString("0.00") + ")");
        Report("  keputusan: " + DifficultyDirector.LastDecision);
    }

    private static void Reload()
    {
        SessionState.SetInt("AdaptiveSmoke.Seed", ProceduralLevelBuilder.Instance != null ? ProceduralLevelBuilder.Instance.Seed : 0);
        SessionState.SetFloat("AdaptiveSmoke.ReloadTime", (float)EditorApplication.timeSinceStartup);

        // SceneTransition (async + animasi) tidak selesai di batchmode -nographics -> load langsung.
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    private static bool SceneReloaded()
    {
        ProceduralLevelBuilder builder = ProceduralLevelBuilder.Instance;
        return builder != null && builder.Layout != null && builder.Seed != SessionState.GetInt("AdaptiveSmoke.Seed", 0);
    }

    private static bool NavPath(Vector3 from, Vector3 to)
    {
        if (!NavMesh.SamplePosition(from, out NavMeshHit a, 2f, NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(to, out NavMeshHit b, 2f, NavMesh.AllAreas))
            return false;

        NavMeshPath path = new NavMeshPath();
        return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
    }

    private static void Finish()
    {
        int errors = SessionState.GetInt(ErrorsKey, 0);
        string report = SessionState.GetString(ReportKey, "");

        SessionState.SetBool(ActiveKey, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;

        StringBuilder sb = new StringBuilder();
        sb.Append("[SMOKE] ").Append(errors == 0 ? "PASS" : "FAIL (" + errors + ")").Append("\n").Append(report);
        Debug.Log(sb.ToString());

        DifficultyDirector.ResetAll(); // jangan tinggalkan data uji
        EditorApplication.ExitPlaymode();

        if (Application.isBatchMode)
            EditorApplication.Exit(errors == 0 ? 0 : 1);
    }
}
