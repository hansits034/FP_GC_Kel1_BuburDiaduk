using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// DYNAMIC DIFFICULTY ADJUSTMENT
//
//   Player Performance -> DDA -> Parameter PCG + Game AI -> Adaptive Challenge
//
// Dua lapis adaptasi:
//  1. Antar-run  : setelah menang/kalah, difficulty run berikutnya dihitung dari
//                  (a) aturan berbasis skor performa dan (b) model ML SkillPredictor.
//                  Hasilnya mengubah PCG (layout, jumlah penjaga, CCTV, cover, resource)
//                  dan Game AI (angka + perilaku).
//  2. Dalam-run  : PlayerPerformanceTracker memberi offset kecil secara live sehingga
//                  penjaga langsung melunak/menajam tanpa menunggu level berikutnya.
//
// State disimpan ke JSON (persistentDataPath) agar bertahan antar scene & sesi.
public static class DifficultyDirector
{
    [Serializable]
    private class SaveData
    {
        public float difficulty = 0.4f;
        public List<RunRecord> history = new List<RunRecord>();
        public SkillPredictor predictor = new SkillPredictor();
        public string lastDecision = "";
        public int runCounter;
        public bool locked;          // true = difficulty dipilih manual, DDA antar-run tidak mengubahnya
    }

    public const float StartDifficulty = 0.4f;
    public const float MaxStepPerRun = 0.2f;
    public const float MaxLiveOffset = 0.2f;

    private static SaveData data;
    private static float[] pendingFeatures;
    private static float liveOffset;
    private static float? forcedDifficulty;

    public static event Action ProfileChanged;

    public static float Difficulty { get { Load(); return data.difficulty; } }
    public static float LiveOffset => liveOffset;
    public static float EffectiveDifficulty => Mathf.Clamp01(Difficulty + liveOffset);
    public static IReadOnlyList<RunRecord> History { get { Load(); return data.history; } }
    public static SkillPredictor Predictor { get { Load(); return data.predictor; } }
    public static string LastDecision { get { Load(); return data.lastDecision; } }
    public static int RunCounter { get { Load(); return data.runCounter; } }
    public static bool Locked { get { Load(); return data.locked; } }

    // Profil saat level dibuat (PCG). Tidak berubah selama run.
    public static DifficultyProfile LevelProfile { get; private set; }

    // Profil untuk Game AI = difficulty level + offset live.
    public static DifficultyProfile LiveProfile { get; private set; }

    public static float PredictedWinThisRun { get; private set; }

    private static string SavePath => Path.Combine(Application.persistentDataPath, "adaptive_state.json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        data = null;
        pendingFeatures = null;
        liveOffset = 0f;
        forcedDifficulty = null;
        ProfileChanged = null;
        LevelProfile = null;
        LiveProfile = null;
    }

    private static void Load()
    {
        if (data != null)
            return;

        try
        {
            if (File.Exists(SavePath))
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DDA] Gagal membaca state: " + e.Message);
        }

        if (data == null)
            data = new SaveData();

        if (data.predictor == null || data.predictor.weights == null || data.predictor.weights.Length != SkillPredictor.FeatureCount)
            data.predictor = new SkillPredictor();

        if (data.history == null)
            data.history = new List<RunRecord>();
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DDA] Gagal menyimpan state: " + e.Message);
        }
    }

    // Dipanggil generator sebelum membuat level.
    public static DifficultyProfile BeginRun()
    {
        Load();

        if (forcedDifficulty.HasValue)
        {
            data.difficulty = forcedDifficulty.Value;
            data.lastDecision = "Override manual (debug): " + data.difficulty.ToString("0.00");
            forcedDifficulty = null;
            Save();
        }

        liveOffset = 0f;
        pendingFeatures = SkillPredictor.BuildFeatures(data.history, data.difficulty);
        PredictedWinThisRun = data.predictor.Predict(pendingFeatures);

        LevelProfile = DifficultyProfile.FromDifficulty(data.difficulty);
        LiveProfile = LevelProfile;
        return LevelProfile;
    }

    // Dipanggil saat menang/kalah.
    public static void EndRun(RunRecord record)
    {
        Load();

        float d = data.difficulty;
        record.difficulty = d;
        float perf = record.ComputePerformance();

        // (1) Latih model ML dengan hasil sebenarnya dari run ini.
        if (pendingFeatures != null)
            data.predictor.Train(pendingFeatures, record.won);

        data.history.Add(record);

        if (data.history.Count > 40)
            data.history.RemoveAt(0);

        data.runCounter++;

        // (2) Aturan: performa di atas 0.55 menaikkan difficulty, di bawahnya menurunkan.
        int lossStreak = 0;

        for (int i = data.history.Count - 1; i >= 0 && !data.history[i].won; i--)
            lossStreak++;

        float ruleNext = d + 0.45f * (perf - 0.55f) - 0.04f * Mathf.Max(0, lossStreak - 1);

        // (3) ML: difficulty dengan prediksi peluang menang ~65%.
        float mlNext = data.predictor.FindDifficulty(data.history, out float predictedWin);
        float confidence = data.predictor.Confidence;

        float blended = Mathf.Lerp(ruleNext, mlNext, confidence);
        float next = Mathf.Clamp01(Mathf.Clamp(blended, d - MaxStepPerRun, d + MaxStepPerRun));

        // Mode manual (dikunci): data tetap direkam & model tetap belajar, tapi difficulty tidak berubah.
        if (data.locked)
            next = d;

        data.difficulty = next;
        data.lastDecision =
            "Performa " + perf.ToString("0.00") + (record.won ? " (menang)" : " (gagal)") +
            " | Aturan -> " + ruleNext.ToString("0.00") +
            " | ML -> " + mlNext.ToString("0.00") + " (P menang " + (predictedWin * 100f).ToString("0") + "%)" +
            " | bobot ML " + (confidence * 100f).ToString("0") + "%" +
            " => " + d.ToString("0.00") + " -> " + next.ToString("0.00") +
            (data.locked ? "  [DIKUNCI manual, DDA hanya merekam]" : "");

        Debug.Log("[DDA] " + data.lastDecision);
        pendingFeatures = null;
        Save();
    }

    // Adaptasi dalam-run (hanya Game AI).
    public static void NudgeLive(float delta, string reason)
    {
        if (Locked)
            return; // mode manual: tingkat kesulitan tidak diubah sama sekali

        float before = liveOffset;
        liveOffset = Mathf.Clamp(liveOffset + delta, -MaxLiveOffset, MaxLiveOffset * 0.75f);

        if (Mathf.Approximately(before, liveOffset) || LevelProfile == null)
            return;

        LiveProfile = DifficultyProfile.FromDifficulty(EffectiveDifficulty);
        Debug.Log("[DDA-live] " + reason + " offset " + before.ToString("0.00") + " -> " + liveOffset.ToString("0.00"));
        ProfileChanged?.Invoke();
    }

    // ---------- Pilihan manual (briefing / panel) ----------

    // Set difficulty untuk run berikutnya. lockIt = true -> tetap di level ini (uji Hard dsb.).
    public static void SetDifficulty(float d, bool lockIt)
    {
        Load();
        data.difficulty = Mathf.Clamp01(d);
        data.locked = lockIt;
        data.lastDecision = (lockIt ? "Dipilih manual & dikunci: " : "Mulai mode adaptif dari: ") + data.difficulty.ToString("0.00");
        forcedDifficulty = null;
        Save();
    }

    public static void Unlock()
    {
        Load();
        data.locked = false;
        data.lastDecision = "Mode adaptif (DDA aktif) mulai dari " + data.difficulty.ToString("0.00");
        Save();
    }

    // ---------- Debug / demo ----------

    public static void ForceNextDifficulty(float d)
    {
        forcedDifficulty = Mathf.Clamp01(d);
    }

    public static void ResetAll()
    {
        data = new SaveData();
        liveOffset = 0f;
        Save();
    }
}
