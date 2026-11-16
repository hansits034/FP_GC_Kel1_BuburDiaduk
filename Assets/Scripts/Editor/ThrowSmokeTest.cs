using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Uji melempar kaleng di Level Adaptif (difficulty tinggi) - reproduksi bug freeze.
//   Unity.exe -batchmode -projectPath . -executeMethod ThrowSmokeTest.Run
[InitializeOnLoad]
public static class ThrowSmokeTest
{
    private const string ActiveKey = "ThrowSmoke.Active";
    private const string FrameKey = "ThrowSmoke.Frame";

    static ThrowSmokeTest()
    {
        if (SessionState.GetBool(ActiveKey, false))
            EditorApplication.update += Tick;
    }

    [MenuItem("Stealth/FP - Smoke Test Lempar Kaleng")]
    public static void Run()
    {
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetInt(FrameKey, 0);
        DifficultyDirector.ResetAll();
        DifficultyDirector.SetDifficulty(0.8f, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Levels/Level_Adaptive.unity");
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
            return;

        int frame = SessionState.GetInt(FrameKey, 0) + 1;
        SessionState.SetInt(FrameKey, frame);

        if (frame == 30)
        {
            StealthGameManager.Instance.EndBriefing();
            ProceduralLevelBuilder builder = ProceduralLevelBuilder.Instance;
            Debug.Log("[THROW] difficulty " + builder.Profile.difficulty + " guards " + builder.Guards.Count);

            PlayerInteractor interactor = Object.FindFirstObjectByType<PlayerInteractor>();
            PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
            inventory.Add("can", "Kaleng", 3, Color.gray);

            // Lempar ke arah penjaga terdekat agar penjaga mendengar.
            EnemyBTController guard = builder.Guards[0];
            interactor.transform.position = guard.transform.position + new Vector3(4f, 0f, 0f);

            Debug.Log("[THROW] mulai lempar");
            typeof(PlayerInteractor).GetMethod("TryThrow", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(interactor, null);
            Debug.Log("[THROW] lempar selesai");
        }

        if (frame > 30 && frame % 100 == 0)
        {
            string states = "";
            foreach (EnemyBTController g in ProceduralLevelBuilder.Instance.Guards)
                states += g.name + ":" + g.Awareness + "/" + g.CurrentAction + "  ";
            Debug.Log("[THROW] frame " + frame + " " + states);
        }

        // Paksa semua penjaga mencari (memicu pencarian adaptif: sektor/cover/ekstraksi).
        if (frame == 200)
        {
            Debug.Log("[THROW] paksa search");
            AlertManager.Current.ReportSighting(Vector3.zero, LevelLayout.CellToWorld(ProceduralLevelBuilder.Instance.Layout.keycard), Vector3.forward);
            Debug.Log("[THROW] search dipicu");
        }

        if (frame >= 1200)
        {
            Debug.Log("[THROW] PASS - tidak freeze");
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;
            DifficultyDirector.ResetAll();
            EditorApplication.ExitPlaymode();

            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
    }
}
