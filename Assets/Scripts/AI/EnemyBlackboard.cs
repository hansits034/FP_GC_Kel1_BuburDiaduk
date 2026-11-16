using UnityEngine;

public enum AwarenessState
{
    Unaware,    // patrol biasa
    Suspicious, // ada yang aneh: menoleh / mendekati sumber
    Alert,      // Player terlihat pasti: kejar
    Searching   // kehilangan jejak / dapat laporan: cari
}

public enum StimulusSource
{
    None,
    Sight,
    Noise,
    Ally
}

public class EnemyBlackboard
{
    // ---------- Perception ----------
    public Transform player;

    public bool canSeePlayer;

    public bool canSensePlayer; // sangat dekat (tanpa harus dalam FOV)

    public float distanceToPlayer;

    // ---------- Suspicion ----------
    public float suspicion; // 0..1

    public AwarenessState awareness = AwarenessState.Unaware;

    // ---------- Memory ----------
    public bool hasMemory;

    public Vector3 lastKnownPosition;

    public Vector3 lastKnownVelocity; // arah gerak Player saat terakhir diketahui

    public float lastKnownTime;

    public StimulusSource memorySource;

    public int memoryVersion; // naik setiap ada info baru (dipakai Search untuk re-plan)

    public float lastAlertTime = -Mathf.Infinity; // ingatan jangka panjang: "pernah ada penyusup"

    // ---------- Health ----------
    public int health;

    public bool healthLow;

    public float MemoryAge => Time.time - lastKnownTime;

    public void Remember(Vector3 position, Vector3 velocity, StimulusSource source)
    {
        hasMemory = true;
        lastKnownPosition = position;
        lastKnownVelocity = velocity;
        lastKnownTime = Time.time;
        memorySource = source;
        memoryVersion++;
    }

    public void Forget()
    {
        hasMemory = false;
        memorySource = StimulusSource.None;
        lastKnownVelocity = Vector3.zero;
    }
}
