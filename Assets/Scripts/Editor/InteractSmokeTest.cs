using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Uji: setiap objek interaktif (terminal, pintu, keycard, kaleng, panel) bisa dijangkau tombol E
// dari posisi yang bisa dicapai di NavMesh. Diulang untuk beberapa seed & difficulty.
//   Unity.exe -batchmode -projectPath . -executeMethod InteractSmokeTest.Run
[InitializeOnLoad]
public static class InteractSmokeTest
{
    private const string ActiveKey = "InteractSmoke.Active";
    private const string FrameKey = "InteractSmoke.Frame";
    private const string RunKey = "InteractSmoke.Run";
    private const string ReportKey = "InteractSmoke.Report";
    private const string FailKey = "InteractSmoke.Fails";
    private const int Runs = 12;

    private static List<Interactable> queue;
    private static int index;
    private static int settle;

    static InteractSmokeTest()
    {
        if (SessionState.GetBool(ActiveKey, false))
            EditorApplication.update += Tick;
    }

    // Uji satu seed tertentu (difficulty 0.40), misal seed laporan bug.
    public static void RunSeed()
    {
        string seed = System.Environment.GetEnvironmentVariable("INTERACT_SEED");
        Run();
        SessionState.SetInt("InteractSmoke.ForceSeed", int.Parse(seed));
        SessionState.SetInt(RunKey, Runs - 1);
        DifficultyDirector.SetDifficulty(0.40f, true);
    }

    [MenuItem("Stealth/FP - Smoke Test Interaksi E")]
    public static void Run()
    {
        SessionState.SetInt("InteractSmoke.ForceSeed", 0);
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetInt(FrameKey, 0);
        SessionState.SetInt(RunKey, 0);
        SessionState.SetInt(FailKey, 0);
        SessionState.SetString(ReportKey, "");
        DifficultyDirector.ResetAll();
        DifficultyDirector.SetDifficulty(0.1f, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Levels/Level_Adaptive.unity");
        EditorApplication.EnterPlaymode();
    }

    private static void Report(string s)
    {
        SessionState.SetString(ReportKey, SessionState.GetString(ReportKey, "") + s + "\n");
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
            return;

        int frame = SessionState.GetInt(FrameKey, 0) + 1;
        SessionState.SetInt(FrameKey, frame);

        if (frame < 20 || ProceduralLevelBuilder.Instance == null)
            return;

        if (queue == null)
        {
            int force = SessionState.GetInt("InteractSmoke.ForceSeed", 0);
            if (force != 0 && ProceduralLevelBuilder.Instance.Seed != force)
            {
                ProceduralLevelBuilder.NextSeedOverride = force;
                SessionState.SetInt(FrameKey, 0);
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }

            StealthGameManager.Instance.EndBriefing();
            foreach (EnemyBTController g in ProceduralLevelBuilder.Instance.Guards) g.enabled = false; // penjaga tidak menangkap selama uji
            queue = new List<Interactable>(Interactable.All);
            index = 0;
            settle = 0;
            ProceduralLevelBuilder b = ProceduralLevelBuilder.Instance;
            Report("== seed " + b.Seed + " d=" + b.Profile.difficulty.ToString("0.00") + " | " + queue.Count + " objek interaktif");
            return;
        }

        PlayerInteractor interactor = Object.FindFirstObjectByType<PlayerInteractor>();

        if (index < queue.Count)
        {
            Interactable target = queue[index];

            if (settle == 0)
            {
                Place(interactor.transform, target);
                settle = 1;
                return;
            }

            if (settle < 3) { settle++; return; }

            Vector3 offset = target.InteractionPoint - interactor.transform.position;
            offset.y = 0f;
            bool ok = interactor.Current == target;

            if (!ok)
            {
                SessionState.SetInt(FailKey, SessionState.GetInt(FailKey, 0) + 1);
                Report("  FAIL " + target.GetType().Name + " '" + target.name + "' jarak " + offset.magnitude.ToString("0.00") +
                       " m | yang terpilih: " + (interactor.Current != null ? interactor.Current.name : "tidak ada") +
                       " | posisi objek " + target.InteractionPoint + " pemain " + interactor.transform.position);
            }

            index++;
            settle = 0;
            return;
        }

        int run = SessionState.GetInt(RunKey, 0) + 1;
        SessionState.SetInt(RunKey, run);
        queue = null;

        if (run >= Runs)
        {
            int fails = SessionState.GetInt(FailKey, 0);
            Debug.Log("[INTERACT] " + (fails == 0 ? "PASS" : "FAIL (" + fails + ")") + "\n" + SessionState.GetString(ReportKey, ""));
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

    // Titik NavMesh terdekat dari objek = tempat pemain bisa berdiri.
    private static void Place(Transform player, Interactable target)
    {
        Vector3 p = target.InteractionPoint;
        p.y = 0f;

        if (NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            p = hit.position;

        Vector3 toTarget = target.InteractionPoint - p;
        toTarget.y = 0f;

        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.position = p + Vector3.up;
        if (toTarget.sqrMagnitude > 0.01f) player.rotation = Quaternion.LookRotation(toTarget);
        if (cc != null) cc.enabled = true;
    }
}
