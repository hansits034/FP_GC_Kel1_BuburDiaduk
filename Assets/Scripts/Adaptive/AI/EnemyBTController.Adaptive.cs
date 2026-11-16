using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Bagian adaptif dari EnemyBTController (Game AI yang dipengaruhi DDA).
//
// DDA tidak hanya mengubah angka (jarak pandang, kecepatan, cooldown), tetapi juga PERILAKU:
//  - Raise Alarm      : saat Alert, penjaga memilih (Utility AI) antara mengejar langsung atau
//                       lari ke panel alarm untuk memanggil semua penjaga.
//  - Coordinated Search: penjaga yang mencari membagi sektor pencarian (tidak menumpuk).
//  - Guard Extraction : satu penjaga memblokir titik ekstraksi saat pemain sedang dicari.
//  - Check Cover      : titik pencarian diprioritaskan di balik cover (tempat pemain bersembunyi).
//  - Learned Search   : fase Search memakai policy Reinforcement Learning (ML-Agents) jika model ada.
//  - Defensif         : pada difficulty rendah penjaga lebih ragu mendekati sumber curiga.
public partial class EnemyBTController : ISearchContext
{
    public static event System.Action AlarmRaised;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetAdaptiveStatics()
    {
        AlarmRaised = null;
    }

    [Header("Adaptive Behaviours (DDA)")]
    [SerializeField] private bool canRaiseAlarm;
    [SerializeField] private bool coordinatedSearch;
    [SerializeField] private bool guardExtraction;
    [SerializeField] private bool checkCover;
    [SerializeField] private bool useLearnedSearch;
    [SerializeField] private bool followDifficulty = true;

    private bool runningToAlarm;
    private AlarmPanel targetPanel;
    private float nextAlarmDecisionTime;
    private string searchPlanLabel = "";
    private float searchStartTime;
    private GuardSearchAgent learnedSearch;
    private Light flashlight;

    public float AlarmUtility { get; private set; }
    public float ChaseUtility { get; private set; }
    public string SearchPlanLabel => searchPlanLabel;
    public bool RunningToAlarm => runningToAlarm;
    public bool LearnedSearchActive => useLearnedSearch && learnedSearch != null && learnedSearch.IsReady;
    public string BehaviourSummary =>
        (canRaiseAlarm ? "Alarm " : "") + (coordinatedSearch ? "Koordinasi " : "") +
        (guardExtraction ? "JagaEkstraksi " : "") + (checkCover ? "CekCover " : "") +
        (LearnedSearchActive ? "RL-Search" : "");

    // ISearchContext (dipakai GuardSearchAgent)
    public Vector3 LastKnownPosition => blackboard.lastKnownPosition;
    public Vector3 LastKnownVelocity => blackboard.lastKnownVelocity;
    public float SearchElapsed => Time.time - searchStartTime;
    public bool IsSearching => blackboard.awareness == AwarenessState.Searching;

    // ==================================================
    // KONFIGURASI (PCG & DDA)
    // ==================================================

    public void Configure(Transform playerTransform, Transform[] points, float waitTime, bool post)
    {
        player = playerTransform;
        patrolPoints = points;
        patrolWaitTime = waitTime;
        useWaypointRotation = post;
        currentPatrolIndex = 0;
    }

    public void ApplyDifficulty(DifficultyProfile p)
    {
        if (p == null)
            return;

        visionRange = p.visionRange;
        visionAngle = p.visionAngle;
        sightGainRate = p.sightGainRate;
        hearingSensitivity = p.hearingSensitivity;
        memoryDuration = p.memoryDuration;
        searchPointCount = p.searchPointCount;
        searchSpeed = p.searchSpeed;
        chaseSpeed = p.chaseSpeed;
        patrolSpeed = p.patrolSpeed;
        attackCooldown = p.attackCooldown;
        attackDamage = p.attackDamage;
        suspicionDecayRate = p.suspicionDecay;
        investigateMoveThreshold = Mathf.Lerp(0.8f, 0.5f, p.difficulty); // rendah = lebih ragu/defensif
        searchRadius = Mathf.Lerp(5f, 8f, p.difficulty);

        canRaiseAlarm = p.raiseAlarm;
        coordinatedSearch = p.coordinatedSearch;
        guardExtraction = p.guardExtraction;
        checkCover = p.checkCover;
        useLearnedSearch = p.useLearnedSearch;

        if (flashlight == null)
        {
            Transform t = transform.Find("Flashlight");
            if (t != null) flashlight = t.GetComponent<Light>();
        }

        if (flashlight != null)
        {
            flashlight.spotAngle = Mathf.Min(visionAngle - 5f, 170f);
            flashlight.innerSpotAngle = flashlight.spotAngle * 0.6f;
            flashlight.range = visionRange + 1f;
        }

        // Cooldown serangan dibangun di dalam pohon -> bangun ulang jika sudah ada.
        if (rootNode != null)
            BuildBehaviorTree();
    }

    private void AdaptiveEnable()
    {
        learnedSearch = GetComponent<GuardSearchAgent>();
        DifficultyDirector.ProfileChanged += OnProfileChanged;
    }

    private void AdaptiveDisable()
    {
        DifficultyDirector.ProfileChanged -= OnProfileChanged;
    }

    private void OnProfileChanged()
    {
        if (followDifficulty)
            ApplyDifficulty(DifficultyDirector.LiveProfile);
    }

    // ==================================================
    // RAISE ALARM (Utility AI: alarm vs kejar)
    // ==================================================

    private BTNode BuildAlarmBranch()
    {
        return new SequenceNode(
            new List<BTNode>
            {
                new ConditionNode(ShouldRaiseAlarm),
                new ActionNode(RaiseAlarm)
            });
    }

    private bool ShouldRaiseAlarm()
    {
        if (runningToAlarm)
        {
            if (targetPanel != null && targetPanel.Usable && blackboard.distanceToPlayer > attackRange * 1.5f)
                return true;

            runningToAlarm = false; // panel disabotase / pemain terlalu dekat -> batal
            targetPanel = null;
        }

        if (!canRaiseAlarm || blackboard.awareness != AwarenessState.Alert || Time.time < nextAlarmDecisionTime)
            return false;

        nextAlarmDecisionTime = Time.time + 0.5f;

        if (AlertManager.Current == null || AlertManager.Current.AlarmOnCooldown)
        {
            AlarmUtility = 0f;
            return false;
        }

        AlarmPanel panel = AlarmPanel.Nearest(transform.position);

        if (panel == null)
        {
            AlarmUtility = 0f;
            return false;
        }

        float panelDistance = Vector3.Distance(transform.position, panel.StandPoint);
        int allies = AlertManager.Current.Guards.Count - 1;

        // Utility: panel dekat + ada rekan + pemain masih jauh = layak memanggil bantuan.
        AlarmUtility =
            Mathf.Clamp01(1f - panelDistance / 18f)
            * (allies > 0 ? 1f : 0.15f)
            * Mathf.Clamp01((blackboard.distanceToPlayer - attackRange) / 6f);

        // Utility mengejar: pemain dekat & terlihat jelas.
        ChaseUtility =
            Mathf.Clamp01(1f - blackboard.distanceToPlayer / Mathf.Max(visionRange, 1f))
            + (blackboard.canSeePlayer ? 0.1f : 0f);

        if (AlarmUtility <= ChaseUtility)
            return false;

        runningToAlarm = true;
        targetPanel = panel;
        Debug.Log(name + " memilih RAISE ALARM (utility " + AlarmUtility.ToString("0.00") + " > kejar " + ChaseUtility.ToString("0.00") + ")");
        return true;
    }

    private NodeState RaiseAlarm()
    {
        if (targetPanel == null)
        {
            runningToAlarm = false;
            return NodeState.Failure;
        }

        currentAction = "RAISE ALARM";
        MoveTo(targetPanel.StandPoint, chaseSpeed, 0.3f);

        if (Vector3.Distance(Flat(transform.position), targetPanel.StandPoint) > 1.2f)
            return NodeState.Running;

        StopMoving();
        targetPanel.Trigger();
        runningToAlarm = false;
        targetPanel = null;

        if (AlertManager.Current != null)
            AlertManager.Current.RaiseFacilityAlarm(blackboard.lastKnownPosition, this);

        AlarmRaised?.Invoke();
        return NodeState.Success;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    // ==================================================
    // ADAPTIVE SEARCH PLAN
    // ==================================================

    private bool BuildAdaptiveSearchPlan(Vector3 origin)
    {
        searchStartTime = Time.time;
        searchPlanLabel = "";

        if (!coordinatedSearch && !guardExtraction && !checkCover)
            return false;

        List<EnemyBTController> searchers = new List<EnemyBTController>();

        if (AlertManager.Current != null)
        {
            foreach (EnemyBTController guard in AlertManager.Current.Guards)
            {
                if (guard == this || guard.Awareness == AwarenessState.Searching)
                    searchers.Add(guard);
            }
        }
        else
        {
            searchers.Add(this);
        }

        searchers.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        int index = Mathf.Max(0, searchers.IndexOf(this));
        int count = Mathf.Max(1, searchers.Count);

        // --- Jaga ekstraksi: penjaga terdekat ke titik ekstraksi memblokirnya ---
        if (guardExtraction && count >= 2 && ExtractionZone.Active != null)
        {
            Vector3 exit = ExtractionZone.Active.transform.position;
            EnemyBTController closest = null;
            float best = float.MaxValue;

            foreach (EnemyBTController guard in searchers)
            {
                float d = (guard.transform.position - exit).sqrMagnitude;
                if (d < best) { best = d; closest = guard; }
            }

            if (closest == this)
            {
                searchPlanLabel = "(jaga ekstraksi)";
                AddSearchPoint(exit + new Vector3(1.5f, 0f, 0f));
                AddSearchPoint(exit + new Vector3(-1.5f, 0f, 1f));
                AddSearchPoint(exit + new Vector3(0f, 0f, -1.5f));
                return searchPoints.Count > 0;
            }
        }

        Vector3 velocity = blackboard.lastKnownVelocity;
        velocity.y = 0f;
        bool hasDirection = velocity.sqrMagnitude > 0.25f;
        Vector3 forward = hasDirection ? velocity.normalized : transform.forward;
        Vector3 center = hasDirection ? origin + forward * predictionDistance : origin;

        if (index == 0 || !coordinatedSearch)
            AddSearchPoint(origin);

        if (hasDirection && (index == 0 || !coordinatedSearch))
            AddSearchPoint(center);

        // --- Kandidat titik: sektor masing-masing jika terkoordinasi ---
        float sectorStart, sectorSize;

        if (coordinatedSearch && count > 1)
        {
            sectorSize = 360f / count;
            sectorStart = index * sectorSize;
            searchPlanLabel = "(sektor " + (index + 1) + "/" + count + ")";
        }
        else
        {
            sectorSize = hasDirection ? 150f : 360f;
            sectorStart = -sectorSize * 0.5f;
        }

        List<(Vector3 point, float score)> candidates = new List<(Vector3, float)>();
        Vector3 eye = origin + Vector3.up * 1.2f;

        for (int i = 0; i < 14; i++)
        {
            float angle = sectorStart + Random.Range(0f, sectorSize);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * forward;
            Vector3 point = center + direction * Random.Range(searchRadius * 0.4f, searchRadius * 1.2f);
            float score = Random.value * 0.3f;

            if (checkCover)
            {
                // Tempat sembunyi: tidak terlihat dari posisi terakhir & dekat obstacle.
                if (Physics.Linecast(eye, point + Vector3.up * 0.5f, obstacleMask))
                    score += 1f;

                if (Physics.CheckSphere(point + Vector3.up * 0.5f, 1.3f, obstacleMask))
                    score += 0.5f;
            }

            candidates.Add((point, score));
        }

        if (checkCover)
            searchPlanLabel = (searchPlanLabel + " (cek cover)").Trim();

        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        int wanted = searchPoints.Count + searchPointCount;

        foreach ((Vector3 point, float _) in candidates)
        {
            if (searchPoints.Count >= wanted)
                break;

            AddSearchPoint(point);
        }

        return searchPoints.Count > 0;
    }

    // ==================================================
    // LEARNED SEARCH (Reinforcement Learning)
    // ==================================================

    private bool TryLearnedSearch()
    {
        if (!string.IsNullOrEmpty(searchPlanLabel))
            currentAction = "SEARCH " + searchPlanLabel;

        if (!LearnedSearchActive)
            return false;

        // Rencana tetap dibuat dulu: penjaga yang ditugasi memblokir ekstraksi tidak memakai RL.
        if (plannedMemoryVersion != blackboard.memoryVersion)
        {
            BuildSearchPlan();
            searchStartTime = Time.time;
        }

        if (searchPlanLabel.Contains("ekstraksi"))
            return false;

        currentAction = "SEARCH (RL)";
        Vector3 direction = learnedSearch.DesiredDirection;

        if (direction.sqrMagnitude < 0.01f)
        {
            transform.Rotate(0f, 90f * Time.deltaTime, 0f);
            return true;
        }

        Vector3 target = transform.position + direction.normalized * 2.5f;

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
            MoveTo(hit.position, searchSpeed, 0.1f);

        // Batas waktu pencarian RL sama dengan durasi memori.
        if (SearchElapsed > memoryDuration)
            EndSearch();

        return true;
    }
}
