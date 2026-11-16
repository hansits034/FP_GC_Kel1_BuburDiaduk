using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Smoke test scene training RL (tanpa trainer Python -> agent memakai Heuristic):
//   Unity.exe -batchmode -projectPath . -executeMethod TrainingSmokeTest.Run
// Memeriksa arena terbangun, NavMesh ter-bake, agent aktif, episode berjalan & reset.
[InitializeOnLoad]
public static class TrainingSmokeTest
{
    private const string ActiveKey = "TrainingSmoke.Active";
    private const string FrameKey = "TrainingSmoke.Frame";
    private const string ErrorsKey = "TrainingSmoke.Errors";
    private const string LogKey = "TrainingSmoke.Log";

    static TrainingSmokeTest()
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;

        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Stealth/FP - Smoke Test Arena RL")]
    public static void Run()
    {
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetInt(FrameKey, 0);
        SessionState.SetInt(ErrorsKey, 0);
        SessionState.SetString(LogKey, "");
        EditorSceneManager.OpenScene("Assets/Scenes/Training/RL_GuardSearch.unity");
        EditorApplication.EnterPlaymode();
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception) &&
            !condition.Contains("Licensing") && !stackTrace.Contains("UnityEditor.Search"))
        {
            SessionState.SetInt(ErrorsKey, SessionState.GetInt(ErrorsKey, 0) + 1);
            SessionState.SetString(LogKey, SessionState.GetString(LogKey, "") + "ERROR: " + condition + "\n" + stackTrace + "\n");
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
            return;

        int frame = SessionState.GetInt(FrameKey, 0) + 1;
        SessionState.SetInt(FrameKey, frame);

        if (frame < 600)
            return;

        GuardSearchArena[] arenas = Object.FindObjectsByType<GuardSearchArena>(FindObjectsSortMode.None);
        GuardSearchAgent[] agents = Object.FindObjectsByType<GuardSearchAgent>(FindObjectsSortMode.None);
        int episodes = 0, onMesh = 0;

        foreach (GuardSearchArena arena in arenas)
            episodes += arena.Episodes;

        foreach (GuardSearchAgent agent in agents)
            if (agent.GetComponent<UnityEngine.AI.NavMeshAgent>().isOnNavMesh) onMesh++;

        int errors = SessionState.GetInt(ErrorsKey, 0);
        bool pass = errors == 0 && arenas.Length > 0 && agents.Length == arenas.Length && onMesh == agents.Length && episodes >= arenas.Length;

        Debug.Log("[TRAINING SMOKE] " + (pass ? "PASS" : "FAIL") + " | arena " + arenas.Length + " | agent aktif " + agents.Length +
                  " | di NavMesh " + onMesh + " | episode " + episodes + " | error " + errors + "\n" + SessionState.GetString(LogKey, ""));

        SessionState.SetBool(ActiveKey, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        EditorApplication.ExitPlaymode();

        if (Application.isBatchMode)
            EditorApplication.Exit(pass ? 0 : 1);
    }
}
