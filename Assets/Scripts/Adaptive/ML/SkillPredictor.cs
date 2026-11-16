using System.Collections.Generic;
using UnityEngine;

// MACHINE LEARNING: Logistic Regression yang dilatih online dari data gameplay.
//
//   Gameplay Data -> Feature Extraction -> Model -> P(menang | skill, difficulty) -> DDA
//
// Input  : fitur skill pemain dari riwayat run + difficulty yang akan dicoba.
// Target : apakah pemain menang (1) atau gagal (0) pada run tersebut.
// Tujuan : DDA memilih difficulty yang memberi peluang menang ~TargetWinRate (zona "flow").
// Update : setiap run selesai, satu langkah SGD dengan label hasil sebenarnya.
[System.Serializable]
public class SkillPredictor
{
    public const int FeatureCount = 6;
    public const float TargetWinRate = 0.65f;

    public static readonly string[] FeatureNames =
    {
        "bias", "difficulty", "avg performa", "win rate", "deteksi", "stealth"
    };

    // Bobot awal (prior) supaya model sudah masuk akal sebelum ada data.
    public float[] weights = { 0.3f, -3f, 2.5f, 1f, -1f, 0.5f };
    public int samples;
    public float learningRate = 0.35f;
    public float l2 = 0.002f;
    public float lastLoss;

    public float Confidence => samples / (samples + 6f);

    public static float[] BuildFeatures(IReadOnlyList<RunRecord> history, float difficulty)
    {
        float perf = 0.5f, winRate = 0.5f, detections = 0.25f, stealth = 0.5f;

        int n = history.Count;

        if (n > 0)
        {
            perf = 0f; detections = 0f; stealth = 0f;
            int recent = Mathf.Min(3, n);

            for (int i = n - recent; i < n; i++)
            {
                perf += history[i].performance;
                detections += Mathf.Clamp01(history[i].detections / 4f);
                stealth += history[i].Stealth;
            }

            perf /= recent; detections /= recent; stealth /= recent;

            int window = Mathf.Min(5, n);
            int wins = 0;

            for (int i = n - window; i < n; i++)
                if (history[i].won) wins++;

            winRate = wins / (float)window;
        }

        return new[] { 1f, difficulty, perf, winRate, detections, stealth };
    }

    public float Predict(float[] x)
    {
        float z = 0f;

        for (int i = 0; i < FeatureCount; i++)
            z += weights[i] * x[i];

        return 1f / (1f + Mathf.Exp(-z));
    }

    public void Train(float[] x, bool won)
    {
        float y = won ? 1f : 0f;
        float p = Predict(x);
        float error = p - y;

        lastLoss = -(y * Mathf.Log(Mathf.Max(p, 1e-5f)) + (1f - y) * Mathf.Log(Mathf.Max(1f - p, 1e-5f)));

        float lr = learningRate / (1f + samples * 0.05f);

        for (int i = 0; i < FeatureCount; i++)
        {
            float reg = i == 0 ? 0f : l2 * weights[i];
            weights[i] -= lr * (error * x[i] + reg);
        }

        // Difficulty harus selalu menurunkan peluang menang (monoton) agar pencarian difficulty valid.
        weights[1] = Mathf.Min(weights[1], -0.5f);
        samples++;
    }

    // Cari difficulty yang membuat prediksi peluang menang paling dekat dengan target.
    public float FindDifficulty(IReadOnlyList<RunRecord> history, out float predictedWin)
    {
        float best = 0.5f;
        float bestError = float.MaxValue;
        predictedWin = 0.5f;

        for (float d = 0f; d <= 1.0001f; d += 0.02f)
        {
            float p = Predict(BuildFeatures(history, d));
            float error = Mathf.Abs(p - TargetWinRate);

            if (error < bestError)
            {
                bestError = error;
                best = d;
                predictedWin = p;
            }
        }

        return best;
    }

    public static string SkillLabel(float avgPerformance)
    {
        if (avgPerformance < 0.35f) return "Pemula";
        if (avgPerformance < 0.62f) return "Menengah";
        return "Mahir";
    }
}
