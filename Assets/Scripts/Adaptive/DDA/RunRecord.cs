using UnityEngine;

// Ringkasan performa pemain dalam satu run (satu level prosedural).
[System.Serializable]
public class RunRecord
{
    public int seed;
    public float difficulty;
    public bool won;
    public float time;
    public float parTime;
    public int detections;      // berapa kali penjaga/CCTV melihat pasti (Alert)
    public int alarms;          // berapa kali alarm fasilitas dibunyikan
    public float exposure;      // detik saat ada penjaga yang curiga (suspicion > 0.35)
    public int damageTaken;
    public int noiseEvents;     // suara yang dibuat pemain (lari, dash, benturan)
    public int distractions;    // kaleng yang dilempar
    public float objectiveRatio;// proporsi misi wajib yang selesai
    public float performance;   // skor 0..1 (dihitung saat run berakhir)

    // 1 = tidak pernah ketahuan, 0 = sangat sering terlihat.
    public float Stealth =>
        1f - Mathf.Clamp01(detections * 0.22f + exposure / 60f * 0.5f + damageTaken / 100f * 0.3f + alarms * 0.15f);

    // 1 = selesai sebelum par time.
    public float Speed => Mathf.Clamp01(parTime / Mathf.Max(time, 1f));

    public float ComputePerformance()
    {
        performance = won
            ? 0.45f + 0.3f * Stealth + 0.25f * Speed
            : 0.08f + 0.27f * objectiveRatio * (0.5f + 0.5f * Stealth);

        performance = Mathf.Clamp01(performance);
        return performance;
    }
}
