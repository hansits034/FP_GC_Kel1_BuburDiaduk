using System;
using System.Collections.Generic;
using UnityEngine;

// Daftar langkah misi per level. Objek interaktif memanggil Notify("kunci")
// dan langkah dengan kunci tersebut ditandai selesai.
public class MissionManager : MonoBehaviour
{
    [Serializable]
    public class MissionStep
    {
        public string key;
        public string description;
        public Transform target;
        public bool optional;

        [NonSerialized] public bool completed;
    }

    public const string ExtractKey = "extract";

    [SerializeField] private string levelTitle = "Level";
    [TextArea(2, 5)]
    [SerializeField] private string briefing;
    [SerializeField] private List<MissionStep> steps = new List<MissionStep>();

    public static MissionManager Instance { get; private set; }

    public event Action<MissionStep> StepCompleted;

    public string LevelTitle => levelTitle;
    public string Briefing => briefing;
    public IReadOnlyList<MissionStep> Steps => steps;

    // Langkah wajib pertama yang belum selesai (ditampilkan sebagai tujuan sekarang).
    public MissionStep CurrentStep
    {
        get
        {
            foreach (MissionStep step in steps)
            {
                if (!step.optional && !step.completed)
                    return step;
            }

            return null;
        }
    }

    public bool IsReadyForExtraction
    {
        get
        {
            foreach (MissionStep step in steps)
            {
                if (!step.optional && !step.completed && step.key != ExtractKey)
                    return false;
            }

            return true;
        }
    }

    // Dipakai ProceduralLevelBuilder (PCG): misi dibuat sesuai layout.
    public void Configure(string title, string briefingText, List<MissionStep> newSteps)
    {
        levelTitle = title;
        briefing = briefingText;
        steps = newSteps;
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static void Notify(string key)
    {
        if (Instance != null)
            Instance.Complete(key);
    }

    public void Complete(string key)
    {
        foreach (MissionStep step in steps)
        {
            if (step.completed || step.key != key)
                continue;

            step.completed = true;
            Debug.Log("Misi selesai: " + step.description);

            if (key != ExtractKey)
            {
                StealthHUD.Toast("Selesai: " + step.description);
                SoundManager.Play("objective", 0.8f, 0f);
            }

            StepCompleted?.Invoke(step);
        }
    }
}
