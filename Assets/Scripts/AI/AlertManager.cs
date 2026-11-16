using System;
using System.Collections.Generic;
using UnityEngine;

public enum FacilityAlertLevel
{
    Calm,
    Caution,
    Alert
}

// Shared Alert: penjaga yang melihat Player melapor ke sini,
// lalu informasi posisi dibagikan ke penjaga lain di sekitarnya.
// Jika belum ada di scene, objek ini dibuat otomatis.
public class AlertManager : MonoBehaviour
{
    [Header("Broadcast")]
    [Tooltip("Radius penjaga lain yang menerima laporan. <= 0 berarti semua penjaga (radio).")]
    [SerializeField] private float broadcastRadius = 30f;

    [Header("Facility Alert Level")]
    [SerializeField] private float alertDuration = 20f;   // Alert -> Caution jika Player tidak terlihat lagi
    [SerializeField] private float cautionDuration = 30f; // Caution -> Calm

    [Header("Global Modifiers")]
    [SerializeField] private float cautionGainMultiplier = 1.3f;
    [SerializeField] private float alertGainMultiplier = 1.6f;
    [SerializeField] private float cautionPatrolSpeedMultiplier = 1.25f;

    [Header("Debug")]
    [SerializeField] private bool showHud = true;
    [SerializeField] private FacilityAlertLevel level = FacilityAlertLevel.Calm;

    private static AlertManager instance;

    private readonly List<EnemyBTController> guards = new List<EnemyBTController>();

    private float lastEventTime = -Mathf.Infinity;
    private float lastSightingTime = -Mathf.Infinity;

    public event Action<FacilityAlertLevel> LevelChanged;
    public event Action PlayerSpotted;

    // Berapa kali Player ketahuan (dipakai untuk rating akhir misi).
    public int DetectionCount { get; private set; }

    public FacilityAlertLevel Level => level;
    public Vector3 LastReportedPosition { get; private set; }
    public IReadOnlyList<EnemyBTController> Guards => guards;

    // Akses tanpa membuat instance baru (aman dipanggil saat scene di-unload).
    public static AlertManager Current => instance;

    public static AlertManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<AlertManager>();

                if (instance == null)
                    instance = new GameObject("AlertManager").AddComponent<AlertManager>();
            }

            return instance;
        }
    }

    public float VisionGainMultiplier =>
        level == FacilityAlertLevel.Alert ? alertGainMultiplier :
        level == FacilityAlertLevel.Caution ? cautionGainMultiplier : 1f;

    public float PatrolSpeedMultiplier =>
        level == FacilityAlertLevel.Calm ? 1f : cautionPatrolSpeedMultiplier;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        float sinceLastEvent = Time.time - lastEventTime;

        if (level == FacilityAlertLevel.Alert && sinceLastEvent > alertDuration)
        {
            SetLevel(FacilityAlertLevel.Caution);
            lastEventTime = Time.time;
        }
        else if (level == FacilityAlertLevel.Caution && sinceLastEvent > cautionDuration)
        {
            SetLevel(FacilityAlertLevel.Calm);
        }
    }

    public void Register(EnemyBTController guard)
    {
        if (!guards.Contains(guard))
            guards.Add(guard);
    }

    public void Unregister(EnemyBTController guard)
    {
        guards.Remove(guard);
    }

    // Dipanggil penjaga yang melihat Player secara pasti (state Alert).
    public void ReportSighting(EnemyBTController reporter, Vector3 playerPosition, Vector3 playerVelocity)
    {
        ReportSighting(reporter.transform.position, playerPosition, playerVelocity, reporter);
    }

    // Versi umum: bisa dipanggil penjaga maupun kamera keamanan (reporter = null).
    public void ReportSighting(Vector3 origin, Vector3 playerPosition, Vector3 playerVelocity, EnemyBTController reporter = null)
    {
        // Episode ketahuan baru jika sudah beberapa detik tidak ada laporan.
        if (Time.time - lastSightingTime > 3f)
        {
            DetectionCount++;
            PlayerSpotted?.Invoke();
        }

        lastSightingTime = Time.time;
        LastReportedPosition = playerPosition;
        lastEventTime = Time.time;
        SetLevel(FacilityAlertLevel.Alert);

        foreach (EnemyBTController guard in guards)
        {
            if (guard == reporter)
                continue;

            if (broadcastRadius > 0f &&
                Vector3.Distance(guard.transform.position, origin) > broadcastRadius)
                continue;

            guard.ReceiveAllyAlert(playerPosition, playerVelocity);
        }
    }

    // ---------- Alarm fasilitas (perilaku Raise Alarm) ----------
    public const float AlarmCooldown = 25f;
    public float LastAlarmTime { get; private set; } = -Mathf.Infinity;
    public bool AlarmOnCooldown => Time.time - LastAlarmTime < AlarmCooldown;

    // Panel alarm ditekan penjaga: SEMUA penjaga (tanpa batas radius) mencari ke posisi itu.
    public void RaiseFacilityAlarm(Vector3 playerPosition, EnemyBTController reporter)
    {
        LastAlarmTime = Time.time;
        lastEventTime = Time.time;
        LastReportedPosition = playerPosition;
        SetLevel(FacilityAlertLevel.Alert);

        foreach (EnemyBTController guard in guards)
        {
            if (guard != reporter)
                guard.ReceiveAllyAlert(playerPosition, Vector3.zero);
        }

        SoundManager.Play("camera_alarm", 1f, 0f);
        StealthHUD.Toast("ALARM FASILITAS! Semua penjaga menuju posisimu");
    }

    // Dipanggil saat penjaga mulai mencari karena suara / hal mencurigakan.
    public void ReportDisturbance(Vector3 position)
    {
        LastReportedPosition = position;
        lastEventTime = Time.time;

        if (level == FacilityAlertLevel.Calm)
            SetLevel(FacilityAlertLevel.Caution);
    }

    private void SetLevel(FacilityAlertLevel newLevel)
    {
        if (level == newLevel)
            return;

        level = newLevel;
        Debug.Log("Facility alert level = " + level);
        LevelChanged?.Invoke(level);
    }

    private void OnGUI()
    {
        if (!showHud)
            return;

        Color color =
            level == FacilityAlertLevel.Alert ? Color.red :
            level == FacilityAlertLevel.Caution ? new Color(1f, 0.6f, 0f) : Color.green;

        GUI.color = Color.black * 0.6f;
        GUI.DrawTexture(new Rect(10, 10, 220, 28), Texture2D.whiteTexture);

        GUI.color = color;
        GUI.Label(new Rect(18, 14, 210, 22), "FACILITY ALERT: " + level.ToString().ToUpper());
        GUI.color = Color.white;
    }
}
