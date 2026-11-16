using UnityEngine;

// Semua parameter gameplay yang diatur oleh DDA, diturunkan dari satu nilai difficulty (0..1).
// PCG membaca bagian "Level", Game AI membaca bagian "Guard AI".
[System.Serializable]
public class DifficultyProfile
{
    public float difficulty;

    // ---------- PCG: Level ----------
    public int roomCount;            // jumlah ruangan (ukuran fasilitas)
    public int guardCount;           // jumlah penjaga
    public int cameraCount;          // jumlah CCTV
    public int canCount;             // resource pengalih (kaleng)
    public float coverDensity;       // 0..1 kepadatan cover/prop
    public int extraLoops;           // jalur alternatif antar ruangan (lebih banyak = lebih mudah)
    public int keycardDepth;         // jarak (ruangan) keycard dari start
    public bool extractionFar;       // ekstraksi jauh dari start
    public float powerOutage;        // durasi listrik padam dari panel (detik)
    public int maxExposedCells;      // batas sel "terawasi" pada jalur stealth (validasi PCG)
    public int patrolRoomsPerGuard;  // rute patroli mencakup berapa ruangan
    public bool vaultPost;           // penjaga statis di depan pintu brankas
    public int ventCount;            // saluran ventilasi (jalur pintas, hanya pemain)
    public int screwdriverDepth;     // jarak (ruangan) obeng dari start

    // ---------- Game AI: angka ----------
    public float visionRange;
    public float visionAngle;
    public float sightGainRate;
    public float hearingSensitivity;
    public float memoryDuration;
    public int searchPointCount;
    public float searchSpeed;
    public float chaseSpeed;
    public float patrolSpeed;
    public float attackCooldown;
    public int attackDamage;
    public float suspicionDecay;
    public float cameraDetectTime;

    // ---------- Game AI: perilaku ----------
    public bool raiseAlarm;          // penjaga bisa lari ke panel alarm (memanggil semua penjaga)
    public bool coordinatedSearch;   // penjaga membagi sektor pencarian
    public bool guardExtraction;     // satu penjaga memblokir titik ekstraksi saat pencarian
    public bool checkCover;          // pencarian memprioritaskan titik di balik cover
    public bool useLearnedSearch;    // fase Search memakai policy RL (jika model tersedia)

    public string Label
    {
        get
        {
            if (difficulty < 0.25f) return "SANGAT MUDAH";
            if (difficulty < 0.45f) return "MUDAH";
            if (difficulty < 0.65f) return "NORMAL";
            if (difficulty < 0.82f) return "SULIT";
            return "SANGAT SULIT";
        }
    }

    public static DifficultyProfile FromDifficulty(float d)
    {
        d = Mathf.Clamp01(d);

        DifficultyProfile p = new DifficultyProfile { difficulty = d };

        p.roomCount = Mathf.RoundToInt(Mathf.Lerp(5f, 10f, d));
        p.guardCount = Mathf.RoundToInt(Mathf.Lerp(2f, 7f, d));
        p.cameraCount = Mathf.RoundToInt(Mathf.Lerp(0f, 3.4f, d));
        p.canCount = Mathf.RoundToInt(Mathf.Lerp(5f, 1f, d));
        p.coverDensity = Mathf.Lerp(0.9f, 0.35f, d);
        p.extraLoops = Mathf.RoundToInt(Mathf.Lerp(3f, 0f, d));
        p.keycardDepth = Mathf.RoundToInt(Mathf.Lerp(1f, 4f, d));
        p.extractionFar = d >= 0.45f;
        p.powerOutage = Mathf.Lerp(35f, 12f, d);
        p.maxExposedCells = Mathf.RoundToInt(Mathf.Lerp(3f, 16f, d));
        p.patrolRoomsPerGuard = d < 0.4f ? 1 : (d < 0.75f ? 2 : 3);
        p.vaultPost = d >= 0.7f;
        p.ventCount = d < 0.3f ? 2 : 1;
        p.screwdriverDepth = Mathf.RoundToInt(Mathf.Lerp(0f, 3f, d));

        p.visionRange = Mathf.Lerp(7f, 12.5f, d);
        p.visionAngle = Mathf.Lerp(80f, 115f, d);
        p.sightGainRate = Mathf.Lerp(0.55f, 1.45f, d);
        p.hearingSensitivity = Mathf.Lerp(0.7f, 1.3f, d);
        p.memoryDuration = Mathf.Lerp(12f, 35f, d);
        p.searchPointCount = Mathf.RoundToInt(Mathf.Lerp(2f, 5f, d));
        p.searchSpeed = Mathf.Lerp(2.3f, 3.3f, d);
        p.chaseSpeed = Mathf.Lerp(3.6f, 4.7f, d);
        p.patrolSpeed = Mathf.Lerp(1.5f, 2.1f, d);
        p.attackCooldown = Mathf.Lerp(1.8f, 0.7f, d);
        p.attackDamage = d < 0.35f ? 34 : (d < 0.7f ? 50 : 100);
        p.suspicionDecay = Mathf.Lerp(0.3f, 0.1f, d);
        p.cameraDetectTime = Mathf.Lerp(2f, 0.9f, d);

        p.raiseAlarm = d >= 0.3f;
        p.coordinatedSearch = d >= 0.45f;
        p.checkCover = d >= 0.55f;
        p.guardExtraction = d >= 0.72f;
        p.useLearnedSearch = d >= 0.6f;

        return p;
    }
}
