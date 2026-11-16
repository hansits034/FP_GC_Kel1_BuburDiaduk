using System.Collections.Generic;
using UnityEngine;

// Mengukur performa pemain selama satu run (input untuk DDA).
// Juga melakukan adaptasi dalam-run: jika pemain kewalahan, penjaga melunak; jika terlalu mulus, menajam.
public class PlayerPerformanceTracker : MonoBehaviour
{
    [Header("Live DDA")]
    [SerializeField] private float evaluateInterval = 6f;
    [SerializeField] private float pressureWindow = 30f;

    public static PlayerPerformanceTracker Instance { get; private set; }

    public RunRecord Current { get; private set; } = new RunRecord();
    public float LiveScore { get; private set; } = 0.5f;
    public string LastLiveEvent { get; private set; } = "-";

    private readonly List<float> recentDetections = new List<float>();
    private readonly List<float> recentDamage = new List<float>();
    private float lastSuspicionTime;
    private float nextEvaluateTime;
    private bool finished;
    private int requiredSteps;
    private int completedSteps;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnEnable()
    {
        NoiseSystem.Emitted += OnNoise;
        PlayerHealth.Damaged += OnDamaged;
        PlayerInteractor.CanThrown += OnCanThrown;
        StealthGameManager.RunEnded += OnRunEnded;
        EnemyBTController.AlarmRaised += OnAlarm;
    }

    private void OnDisable()
    {
        NoiseSystem.Emitted -= OnNoise;
        PlayerHealth.Damaged -= OnDamaged;
        PlayerInteractor.CanThrown -= OnCanThrown;
        StealthGameManager.RunEnded -= OnRunEnded;
        EnemyBTController.AlarmRaised -= OnAlarm;

        if (AlertManager.Current != null)
            AlertManager.Current.PlayerSpotted -= OnSpotted;

        if (MissionManager.Instance != null)
            MissionManager.Instance.StepCompleted -= OnStep;
    }

    private void Start()
    {
        if (AlertManager.Instance != null)
            AlertManager.Instance.PlayerSpotted += OnSpotted;

        if (MissionManager.Instance != null)
        {
            MissionManager.Instance.StepCompleted += OnStep;

            foreach (MissionManager.MissionStep step in MissionManager.Instance.Steps)
                if (!step.optional && step.key != MissionManager.ExtractKey)
                    requiredSteps++;
        }

        lastSuspicionTime = Time.time;
        nextEvaluateTime = Time.time + evaluateInterval;
    }

    public void BeginRun(int seed, float parTime)
    {
        Current = new RunRecord { seed = seed, parTime = parTime };
        finished = false;
    }

    private void Update()
    {
        if (finished || !StealthGameManager.IsPlaying)
            return;

        Current.time += Time.deltaTime;

        float maxSuspicion = 0f;

        if (AlertManager.Current != null)
        {
            foreach (EnemyBTController guard in AlertManager.Current.Guards)
                if (guard != null)
                    maxSuspicion = Mathf.Max(maxSuspicion, guard.Suspicion);
        }

        if (maxSuspicion > 0.35f)
        {
            Current.exposure += Time.deltaTime;
            lastSuspicionTime = Time.time;
        }

        if (Time.time >= nextEvaluateTime)
        {
            nextEvaluateTime = Time.time + evaluateInterval;
            EvaluateLive();
        }
    }

    // Adaptasi dalam-run berdasarkan tekanan 30 detik terakhir.
    private void EvaluateLive()
    {
        float now = Time.time;
        recentDetections.RemoveAll(t => now - t > pressureWindow);
        recentDamage.RemoveAll(t => now - t > pressureWindow);

        float pressure = recentDetections.Count * 0.35f + recentDamage.Count * 0.4f;
        float calmTime = now - lastSuspicionTime;

        float progress = requiredSteps > 0 ? completedSteps / (float)requiredSteps : 0f;
        float expectedProgress = Current.parTime > 0f ? Mathf.Clamp01(Current.time / Current.parTime) : 0f;

        LiveScore = Mathf.Clamp01(0.5f - pressure * 0.5f + (progress - expectedProgress) * 0.4f + (calmTime > 40f ? 0.15f : 0f));

        if (pressure >= 0.7f)
        {
            LastLiveEvent = "Tertekan (" + recentDetections.Count + " deteksi/" + recentDamage.Count + " hit) -> penjaga melunak";
            DifficultyDirector.NudgeLive(-0.08f, LastLiveEvent);
        }
        else if (calmTime > 40f && progress >= expectedProgress)
        {
            LastLiveEvent = "Terlalu mulus (" + Mathf.RoundToInt(calmTime) + " dtk tanpa curiga) -> penjaga menajam";
            DifficultyDirector.NudgeLive(0.05f, LastLiveEvent);
            lastSuspicionTime = now - 20f; // jeda sebelum menaikkan lagi
        }
    }

    private void OnSpotted()
    {
        Current.detections++;
        recentDetections.Add(Time.time);
    }

    private void OnAlarm()
    {
        Current.alarms++;
        recentDetections.Add(Time.time);
    }

    private void OnDamaged(int amount)
    {
        Current.damageTaken += amount;
        recentDamage.Add(Time.time);
    }

    private void OnNoise(Vector3 position, float radius)
    {
        if (radius >= 4f)
            Current.noiseEvents++;
    }

    private void OnCanThrown()
    {
        Current.distractions++;
    }

    private void OnStep(MissionManager.MissionStep step)
    {
        if (!step.optional && step.key != MissionManager.ExtractKey)
            completedSteps++;
    }

    private void OnRunEnded(bool won)
    {
        if (finished)
            return;

        finished = true;
        Current.won = won;
        Current.objectiveRatio = won ? 1f : (requiredSteps > 0 ? completedSteps / (float)requiredSteps : 0f);
        DifficultyDirector.EndRun(Current);
    }
}
