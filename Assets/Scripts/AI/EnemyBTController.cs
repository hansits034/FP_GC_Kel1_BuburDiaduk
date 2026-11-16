using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public partial class EnemyBTController : MonoBehaviour, INoiseListener
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private Transform safePoint;

    [Header("Perception - Vision")]
    [SerializeField] private float visionRange = 10f;
    [SerializeField] private float visionAngle = 90f;
    [SerializeField] private float eyeHeight = 1.5f;
    [Tooltip("Player sedekat ini tetap 'terasa' walau di luar FOV (misal di belakang).")]
    [SerializeField] private float proximitySenseRadius = 1.5f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Perception - Hearing")]
    [SerializeField] private float hearingSensitivity = 1f;
    [Tooltip("Pengali radius suara jika terhalang dinding.")]
    [Range(0f, 1f)]
    [SerializeField] private float wallOcclusion = 0.5f;

    [Header("Suspicion")]
    [SerializeField] private float sightGainRate = 1f;       // per detik (jarak dekat & tengah FOV)
    [SerializeField] private float proximityGainRate = 1.5f; // per detik
    [SerializeField] private float hearingGain = 0.5f;       // per suara (dikali kerasnya)
    [SerializeField] private float movingVisibilityMultiplier = 1.5f; // Player lari / dash lebih mencolok
    [SerializeField] private float crouchVisibilityMultiplier = 0.55f; // Player jongkok lebih sulit dikenali
    [SerializeField] private float instantDetectDistance = 2f;
    [SerializeField] private float suspicionDecayRate = 0.15f;
    [SerializeField] private float suspicionHoldTime = 2f;
    [Range(0f, 1f)]
    [SerializeField] private float suspiciousThreshold = 0.3f;
    [Range(0f, 1f)]
    [SerializeField] private float investigateMoveThreshold = 0.6f;

    [Header("Memory")]
    [Tooltip("Setelah selama ini tanpa info baru, penjaga menyerah mencari.")]
    [SerializeField] private float memoryDuration = 25f;
    [Tooltip("Setelah pernah Alert, penjaga lebih waspada selama durasi ini.")]
    [SerializeField] private float vigilanceDuration = 60f;
    [SerializeField] private float vigilanceMultiplier = 1.5f;

    [Header("Combat")]
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private int attackDamage = 10;

    [Header("Health")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private int lowHealthThreshold = 30;

    [Header("Movement")]
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float investigateSpeed = 1.5f;
    [SerializeField] private float searchSpeed = 3f;
    [SerializeField] private float chaseSpeed = 4f;
    [SerializeField] private float fleeSpeed = 5f;

    [Header("Patrol")]
    [Tooltip("Lama berhenti di tiap waypoint. 0 = langsung lanjut.")]
    [SerializeField] private float patrolWaitTime = 0f;
    [Tooltip("Saat berhenti, menghadap arah rotasi waypoint (untuk pos jaga).")]
    [SerializeField] private bool useWaypointRotation = false;
    [SerializeField] private float patrolLookSweep = 35f;

    [Header("Search")]
    [SerializeField] private float searchDuration = 3f;   // lama melihat-lihat di tiap titik
    [SerializeField] private float searchLookAngle = 70f; // sudut menoleh kiri-kanan
    [SerializeField] private float searchRadius = 6f;
    [SerializeField] private int searchPointCount = 3;
    [SerializeField] private float predictionDistance = 4f; // tebakan arah kabur Player
    [SerializeField] private float searchPointTimeout = 10f;

    [Header("Shared Alert")]
    [SerializeField] private float reportInterval = 1f;
    [Range(0f, 1f)]
    [SerializeField] private float allyAlertSuspicion = 0.85f;

    [Header("Debug")]
    [SerializeField] private string currentAction = "None";
    [SerializeField] private AwarenessState debugAwareness;
    [Range(0f, 1f)]
    [SerializeField] private float debugSuspicion;
    [SerializeField] private bool showStateLabel = true;
    [SerializeField] private bool showActionName = true;

    private NavMeshAgent agent;
    private BTNode rootNode;
    private PlayerController playerController;

    private int currentHealth;
    private int currentPatrolIndex = 0;
    private float patrolWaitTimer;

    private EnemyBlackboard blackboard = new EnemyBlackboard();

    // Perception
    private Vector3 playerVelocity;
    private Vector3 previousPlayerPosition;
    private bool hasPreviousPlayerPosition;
    private float lastStimulusTime = -Mathf.Infinity;
    private float nextReportTime;

    // Batasi bunyi "!" agar tidak beruntun saat banyak penjaga melihat bersamaan.
    private static float lastAlertSoundTime = -Mathf.Infinity;

    // Movement
    private Vector3 lastDestination;
    private bool hasDestination;

    // Search
    private readonly List<Vector3> searchPoints = new List<Vector3>();
    private int searchPointIndex;
    private int plannedMemoryVersion = -1;
    private bool lookingAround;
    private float lookTimer;
    private float lookBaseYaw;
    private float pointTimer;

    public AwarenessState Awareness => blackboard.awareness;
    public float Suspicion => blackboard.suspicion;

    // Untuk EnemyDebugMenu
    public string CurrentAction => currentAction;
    public EnemyBlackboard Blackboard => blackboard;
    public IReadOnlyList<Vector3> SearchPoints => searchPoints;
    public int SearchPointIndex => searchPointIndex;
    public float VisionRange => visionRange;
    public float VisionAngle => visionAngle;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        currentHealth = maxHealth;
    }

    private void OnEnable()
    {
        NoiseSystem.Register(this);
        AlertManager.Instance.Register(this);
        AdaptiveEnable();
    }

    private void OnDisable()
    {
        NoiseSystem.Unregister(this);
        AdaptiveDisable();

        if (AlertManager.Current != null)
            AlertManager.Current.Unregister(this);
    }

    private void Start()
    {
        if (player == null)
        {
            PlayerController found = FindFirstObjectByType<PlayerController>();
            if (found != null)
                player = found.transform;
        }

        if (player != null)
            playerController = player.GetComponent<PlayerController>();

        BuildBehaviorTree();
    }

    private void Update()
    {
        if (EnemyDebugMenu.FreezeAI)
        {
            agent.isStopped = true;
            return;
        }

        UpdatePerception();
        UpdateSuspicion();

        debugAwareness = blackboard.awareness;
        debugSuspicion = blackboard.suspicion;

        if (rootNode != null)
            rootNode.Tick();
    }

    // ==================================================
    // PERCEPTION
    // ==================================================

    private float lastSightAngle;

    private void UpdatePerception()
    {
        blackboard.player = player;
        UpdatePlayerVelocity();

        blackboard.canSeePlayer = CanSeePlayer(out lastSightAngle);
        blackboard.canSensePlayer = !blackboard.canSeePlayer && CanSensePlayerNearby();
        blackboard.distanceToPlayer = player != null
            ? Vector3.Distance(transform.position, player.position)
            : Mathf.Infinity;
        blackboard.health = currentHealth;
        blackboard.healthLow = currentHealth <= lowHealthThreshold;
    }

    private void UpdatePlayerVelocity()
    {
        if (player == null || Time.deltaTime <= 0f)
            return;

        Vector3 position = player.position;

        if (hasPreviousPlayerPosition)
        {
            Vector3 rawVelocity = (position - previousPlayerPosition) / Time.deltaTime;
            playerVelocity = Vector3.Lerp(playerVelocity, rawVelocity, 0.2f);
        }

        previousPlayerPosition = position;
        hasPreviousPlayerPosition = true;
    }

    public Vector3 EyePosition => transform.position + Vector3.up * eyeHeight;

    // Titik tubuh Player yang dicek (lebih rendah saat jongkok -> bisa bersembunyi di balik dinding rendah).
    private Vector3 PlayerTargetPoint =>
        playerController != null
            ? playerController.VisibilityPoint
            : player.position + Vector3.up * 1f;

    private bool CanSeePlayer(out float angle)
    {
        angle = 180f;

        if (player == null || EnemyDebugMenu.PlayerHidden)
            return false;

        Vector3 targetPosition = PlayerTargetPoint;
        Vector3 directionToPlayer = targetPosition - EyePosition;
        float distanceToPlayer = directionToPlayer.magnitude;

        // Player terlalu jauh (di area gelap jarak pandang menyusut).
        if (distanceToPlayer > visionRange * LightZone.GetVisibility(targetPosition))
            return false;

        // Player berada di luar Field of View.
        angle = Vector3.Angle(transform.forward, directionToPlayer);

        if (angle > visionAngle * 0.5f)
            return false;

        // Periksa apakah ada obstacle.
        return !Physics.Raycast(
            EyePosition,
            directionToPlayer.normalized,
            distanceToPlayer,
            obstacleMask
        );
    }

    private bool CanSensePlayerNearby()
    {
        if (player == null || EnemyDebugMenu.PlayerHidden)
            return false;

        Vector3 offset = player.position - transform.position;
        offset.y = 0f;

        if (offset.magnitude > proximitySenseRadius)
            return false;

        return !Physics.Linecast(EyePosition, PlayerTargetPoint, obstacleMask);
    }

    // Dipanggil NoiseSystem setiap ada suara (langkah kaki, lari, dash).
    public void HearNoise(Vector3 position, float radius, float strength)
    {
        if (!isActiveAndEnabled || blackboard.canSeePlayer || EnemyDebugMenu.FreezeAI)
            return;

        float effectiveRadius = radius * hearingSensitivity;
        float distance = Vector3.Distance(transform.position, position);

        if (distance > effectiveRadius)
            return;

        // Suara teredam dinding.
        if (Physics.Linecast(EyePosition, position + Vector3.up * 0.5f, obstacleMask))
        {
            effectiveRadius *= wallOcclusion;

            if (distance > effectiveRadius)
                return;
        }

        float loudness = 1f - distance / effectiveRadius;

        blackboard.suspicion = Mathf.Clamp01(
            blackboard.suspicion + loudness * hearingGain * strength * GetAwarenessMultiplier());

        RegisterStimulus(position, Vector3.zero, StimulusSource.Noise);
    }

    // ==================================================
    // SUSPICION
    // ==================================================

    private void UpdateSuspicion()
    {
        float dt = Time.deltaTime;

        if (blackboard.canSeePlayer)
        {
            if (blackboard.distanceToPlayer <= instantDetectDistance)
                blackboard.suspicion = 1f;
            else
                blackboard.suspicion += ComputeSightGain(lastSightAngle) * dt;

            RegisterStimulus(player.position, playerVelocity, StimulusSource.Sight);
        }
        else if (blackboard.canSensePlayer)
        {
            blackboard.suspicion += proximityGainRate * GetAwarenessMultiplier() * dt;
            RegisterStimulus(player.position, Vector3.zero, StimulusSource.Noise);
        }

        blackboard.suspicion = Mathf.Clamp01(blackboard.suspicion);

        switch (blackboard.awareness)
        {
            case AwarenessState.Alert:
                if (!blackboard.canSeePlayer)
                    SetAwareness(AwarenessState.Searching); // kehilangan kontak visual
                else if (Time.time >= nextReportTime)
                    ReportSighting();
                break;

            case AwarenessState.Searching:
                if (blackboard.canSeePlayer && blackboard.suspicion >= 1f)
                    EnterAlert();
                else if (blackboard.MemoryAge > memoryDuration)
                    EndSearch(); // ingatan sudah terlalu lama
                break;

            default: // Unaware / Suspicious
                if (blackboard.suspicion >= 1f)
                {
                    if (blackboard.canSeePlayer)
                        EnterAlert();
                    else
                        SetAwareness(AwarenessState.Searching); // yakin ada sesuatu, tapi tidak terlihat
                    break;
                }

                if (Time.time - lastStimulusTime > suspicionHoldTime)
                    blackboard.suspicion = Mathf.Max(0f, blackboard.suspicion - suspicionDecayRate * dt);

                if (blackboard.suspicion >= suspiciousThreshold)
                {
                    SetAwareness(AwarenessState.Suspicious);
                }
                else if (blackboard.awareness == AwarenessState.Suspicious &&
                         blackboard.suspicion < suspiciousThreshold * 0.5f)
                {
                    SetAwareness(AwarenessState.Unaware);
                    blackboard.Forget(); // "mungkin cuma perasaan"
                }
                else if (blackboard.suspicion <= 0f)
                {
                    blackboard.Forget();
                }
                break;
        }
    }

    private float ComputeSightGain(float angle)
    {
        // Lebih cepat curiga jika Player dekat dan berada di tengah pandangan.
        float effectiveRange = visionRange * LightZone.GetVisibility(PlayerTargetPoint);
        float distanceFactor = 1f - Mathf.Clamp01(blackboard.distanceToPlayer / effectiveRange);
        float angleFactor = 1f - Mathf.Clamp01(angle / (visionAngle * 0.5f));

        float gain =
            sightGainRate
            * Mathf.Lerp(0.25f, 1f, distanceFactor)
            * Mathf.Lerp(0.5f, 1f, angleFactor);

        if (playerController != null && (playerController.IsRunning || playerController.IsDashing))
            gain *= movingVisibilityMultiplier;
        else if (playerController != null && playerController.IsCrouching)
            gain *= crouchVisibilityMultiplier;

        // Penjaga yang sudah curiga / sedang mencari lebih cepat mengenali.
        if (blackboard.awareness != AwarenessState.Unaware)
            gain *= 1.5f;

        return gain * GetAwarenessMultiplier();
    }

    private float GetAwarenessMultiplier()
    {
        float multiplier = AlertManager.Current != null
            ? AlertManager.Current.VisionGainMultiplier
            : 1f;

        if (Time.time - blackboard.lastAlertTime < vigilanceDuration)
            multiplier *= vigilanceMultiplier;

        return multiplier;
    }

    private void RegisterStimulus(Vector3 position, Vector3 velocity, StimulusSource source)
    {
        blackboard.Remember(position, velocity, source);
        lastStimulusTime = Time.time;
    }

    private void SetAwareness(AwarenessState newState)
    {
        if (blackboard.awareness == newState)
            return;

        AwarenessState previous = blackboard.awareness;
        blackboard.awareness = newState;

        if (newState == AwarenessState.Searching)
        {
            plannedMemoryVersion = -1;

            if (previous != AwarenessState.Alert &&
                blackboard.memorySource != StimulusSource.Ally &&
                AlertManager.Current != null)
            {
                AlertManager.Current.ReportDisturbance(blackboard.lastKnownPosition);
            }
        }

        PlayAwarenessSound(previous, newState);

        Debug.Log(name + " awareness: " + previous + " -> " + newState);
    }

    private void PlayAwarenessSound(AwarenessState previous, AwarenessState newState)
    {
        if (newState == AwarenessState.Suspicious && previous == AwarenessState.Unaware)
        {
            SoundManager.PlayAt("guard_suspicious", transform.position, 0.8f, 20f);
        }
        else if (newState == AwarenessState.Alert && Time.time - lastAlertSoundTime > 3f)
        {
            lastAlertSoundTime = Time.time;
            SoundManager.Play("guard_alert", 0.9f, 0f);
        }
    }

    private void EnterAlert()
    {
        blackboard.suspicion = 1f;
        blackboard.lastAlertTime = Time.time;
        SetAwareness(AwarenessState.Alert);
        ReportSighting();
    }

    private void EndSearch()
    {
        Debug.Log(name + " selesai mencari, kembali patrol");
        searchPlanLabel = "";

        blackboard.Forget();
        blackboard.suspicion = suspiciousThreshold * 0.5f;
        plannedMemoryVersion = -1;
        lookingAround = false;
        SetAwareness(AwarenessState.Unaware);
    }

    // Dipakai EnemyDebugMenu: kembali patrol, tanpa curiga & tanpa sisa waspada.
    public void ResetAwareness()
    {
        EndSearch();
        blackboard.suspicion = 0f;
        blackboard.lastAlertTime = -Mathf.Infinity;
    }

    // ==================================================
    // SHARED ALERT
    // ==================================================

    private void ReportSighting()
    {
        nextReportTime = Time.time + reportInterval;

        if (AlertManager.Current != null && player != null)
            AlertManager.Current.ReportSighting(this, player.position, playerVelocity);
    }

    // Dipanggil AlertManager ketika penjaga lain melihat Player.
    public void ReceiveAllyAlert(Vector3 playerPosition, Vector3 velocity)
    {
        if (blackboard.canSeePlayer)
            return;

        blackboard.suspicion = Mathf.Max(blackboard.suspicion, allyAlertSuspicion);
        blackboard.lastAlertTime = Time.time;
        RegisterStimulus(playerPosition, velocity, StimulusSource.Ally);
        SetAwareness(AwarenessState.Searching);
    }

    // ==================================================
    // BEHAVIOR TREE
    // ==================================================

    private bool IsAlertAndSeeing()
    {
        return blackboard.awareness == AwarenessState.Alert && blackboard.canSeePlayer;
    }

    private void BuildBehaviorTree()
    {
        // ------------------------------
        // FLEE
        // ------------------------------

        BTNode fleeSequence =
            new SequenceNode(
                new List<BTNode>
                {
                    new ConditionNode(() => blackboard.healthLow),
                    new ActionNode(Flee)
                }
            );

        // ------------------------------
        // ATTACK
        // ------------------------------

        BTNode attackSequence =
            new SequenceNode(
                new List<BTNode>
                {
                    new ConditionNode(IsAlertAndSeeing),
                    new ConditionNode(() => blackboard.distanceToPlayer <= attackRange),
                    new CooldownDecorator(new ActionNode(AttackPlayer), attackCooldown)
                }
            );

        // ------------------------------
        // CHASE
        // ------------------------------

        BTNode chaseSequence =
            new SequenceNode(
                new List<BTNode>
                {
                    new ConditionNode(IsAlertAndSeeing),
                    new ActionNode(ChasePlayer)
                }
            );

        // ------------------------------
        // SEARCH
        // ------------------------------

        BTNode searchSequence =
            new SequenceNode(
                new List<BTNode>
                {
                    new ConditionNode(() =>
                        blackboard.awareness == AwarenessState.Searching && blackboard.hasMemory),
                    new ActionNode(Search)
                }
            );

        // ------------------------------
        // INVESTIGATE
        // ------------------------------

        BTNode investigateSequence =
            new SequenceNode(
                new List<BTNode>
                {
                    new ConditionNode(() =>
                        blackboard.awareness == AwarenessState.Suspicious && blackboard.hasMemory),
                    new ActionNode(Investigate)
                }
            );

        // ------------------------------
        // ROOT SELECTOR
        // ------------------------------

        rootNode =
            new SelectorNode(
                new List<BTNode>
                {
                    fleeSequence,
                    attackSequence,
                    BuildAlarmBranch(),
                    chaseSequence,
                    searchSequence,
                    investigateSequence,
                    new ActionNode(Patrol)
                }
            );
    }

    // ==================================================
    // ACTIONS
    // ==================================================

    private NodeState Patrol()
    {
        currentAction = "PATROL";

        if (patrolPoints == null || patrolPoints.Length == 0)
            return NodeState.Failure;

        float speedMultiplier = AlertManager.Current != null
            ? AlertManager.Current.PatrolSpeedMultiplier
            : 1f;

        Transform target = patrolPoints[currentPatrolIndex];

        MoveTo(target.position, patrolSpeed * speedMultiplier, 0.2f);

        if (!HasArrived(0.5f))
        {
            patrolWaitTimer = 0f;
            return NodeState.Running;
        }

        if (patrolWaitTimer < patrolWaitTime)
        {
            patrolWaitTimer += Time.deltaTime;

            if (useWaypointRotation)
            {
                // Berjaga: menghadap arah waypoint sambil menoleh pelan.
                float yaw =
                    target.eulerAngles.y
                    + Mathf.Sin(patrolWaitTimer * 1.2f) * patrolLookSweep;

                transform.rotation =
                    Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.Euler(0f, yaw, 0f),
                        4f * Time.deltaTime
                    );
            }

            return NodeState.Running;
        }

        patrolWaitTimer = 0f;
        currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;

        return NodeState.Running;
    }

    // Suspicious: menoleh ke sumber, lalu (jika makin curiga) mendekat pelan-pelan.
    private NodeState Investigate()
    {
        currentAction = "INVESTIGATE";

        Vector3 target = blackboard.lastKnownPosition;

        if (blackboard.suspicion < investigateMoveThreshold)
        {
            StopMoving();
            FaceTowards(target);
            return NodeState.Running;
        }

        MoveTo(target, investigateSpeed, 0.5f);

        if (HasArrived(0.7f))
        {
            StopMoving();
            transform.Rotate(0f, 60f * Time.deltaTime, 0f);
        }

        return NodeState.Running;
    }

    // Searching: datangi posisi terakhir, posisi prediksi, lalu beberapa titik di sekitarnya.
    private NodeState Search()
    {
        currentAction = "SEARCH";

        if (TryLearnedSearch())
            return NodeState.Running;

        if (plannedMemoryVersion != blackboard.memoryVersion)
            BuildSearchPlan();

        if (searchPointIndex >= searchPoints.Count)
        {
            EndSearch();
            return NodeState.Success;
        }

        Vector3 target = searchPoints[searchPointIndex];

        if (!lookingAround)
        {
            MoveTo(target, searchSpeed, 0.2f);
            pointTimer += Time.deltaTime;

            if (HasArrived(0.5f) || pointTimer > searchPointTimeout)
            {
                StopMoving();
                lookingAround = true;
                lookTimer = 0f;
                lookBaseYaw = transform.eulerAngles.y;
            }

            return NodeState.Running;
        }

        // Melihat-lihat: menoleh kiri-kanan.
        lookTimer += Time.deltaTime;

        float yaw =
            lookBaseYaw
            + Mathf.Sin(lookTimer / searchDuration * Mathf.PI * 2f) * searchLookAngle;

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        if (lookTimer >= searchDuration)
        {
            lookingAround = false;
            pointTimer = 0f;
            searchPointIndex++;
        }

        return NodeState.Running;
    }

    private void BuildSearchPlan()
    {
        plannedMemoryVersion = blackboard.memoryVersion;
        searchPoints.Clear();
        searchPointIndex = 0;
        lookingAround = false;
        pointTimer = 0f;

        Vector3 origin = blackboard.lastKnownPosition;

        // Game AI adaptif: pencarian terkoordinasi / jaga ekstraksi / periksa cover.
        if (BuildAdaptiveSearchPlan(origin))
            return;

        AddSearchPoint(origin);

        Vector3 velocity = blackboard.lastKnownVelocity;
        velocity.y = 0f;

        bool hasDirection = velocity.sqrMagnitude > 0.25f;
        Vector3 forward = hasDirection ? velocity.normalized : Vector3.zero;
        Vector3 center = origin;

        // Prediksi: Player kemungkinan terus bergerak ke arah terakhirnya.
        if (hasDirection)
        {
            center = origin + forward * predictionDistance;
            AddSearchPoint(center);
        }

        for (int i = 0; i < searchPointCount; i++)
        {
            float angle = hasDirection ? Random.Range(-75f, 75f) : Random.Range(0f, 360f);
            Vector3 baseDirection = hasDirection ? forward : Vector3.forward;
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * baseDirection;

            AddSearchPoint(center + direction * Random.Range(searchRadius * 0.4f, searchRadius));
        }
    }

    private void AddSearchPoint(Vector3 candidate)
    {
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            return;

        NavMeshPath path = new NavMeshPath();

        if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
            return;

        searchPoints.Add(hit.position);
    }

    private NodeState ChasePlayer()
    {
        if (player == null)
            return NodeState.Failure;

        currentAction = "CHASE";

        MoveTo(player.position, chaseSpeed, attackRange * 0.8f);

        return NodeState.Running;
    }

    private NodeState AttackPlayer()
    {
        if (player == null)
            return NodeState.Failure;

        currentAction = "ATTACK";

        StopMoving();
        FaceTowards(player.position);

        Debug.Log(name + " attacks Player! Damage = " + attackDamage);

        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();

        if (playerHealth != null)
            playerHealth.TakeDamage(attackDamage);

        return NodeState.Success;
    }

    private NodeState Flee()
    {
        if (safePoint == null)
            return NodeState.Failure;

        currentAction = "FLEE";

        MoveTo(safePoint.position, fleeSpeed, 0.5f);

        if (HasArrived(0.7f))
        {
            StopMoving();
            return NodeState.Success;
        }

        return NodeState.Running;
    }

    // ==================================================
    // MOVEMENT HELPERS
    // ==================================================

    private void MoveTo(Vector3 target, float speed, float stoppingDistance)
    {
        agent.isStopped = false;
        agent.speed = speed;
        agent.stoppingDistance = stoppingDistance;

        if (!hasDestination || (lastDestination - target).sqrMagnitude > 0.04f)
        {
            agent.SetDestination(target);
            lastDestination = target;
            hasDestination = true;
        }
    }

    private bool HasArrived(float tolerance)
    {
        return !agent.pathPending && agent.remainingDistance <= tolerance;
    }

    private void StopMoving()
    {
        agent.isStopped = true;
    }

    private void FaceTowards(Vector3 position)
    {
        Vector3 direction = position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return;

        transform.rotation =
            Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(direction),
                5f * Time.deltaTime
            );
    }

    // ==================================================
    // HEALTH
    // ==================================================

    public void TakeDamage(int damage)
    {
        currentHealth = Mathf.Clamp(currentHealth - damage, 0, maxHealth);

        Debug.Log(name + " Health = " + currentHealth);
    }

    [ContextMenu("Test Damage 25")]
    private void TestDamage25()
    {
        TakeDamage(25);
    }

    [ContextMenu("Reset Health")]
    private void ResetHealth()
    {
        currentHealth = maxHealth;
    }

    // ==================================================
    // DEBUG
    // ==================================================

    public Color GetStateColor()
    {
        switch (blackboard.awareness)
        {
            case AwarenessState.Suspicious: return Color.yellow;
            case AwarenessState.Searching: return new Color(1f, 0.5f, 0f);
            case AwarenessState.Alert: return Color.red;
            default: return Color.white;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = GetStateColor();

        // Vision Range
        Gizmos.DrawWireSphere(transform.position, visionRange);

        // FOV direction
        Vector3 leftDirection = Quaternion.Euler(0, -visionAngle * 0.5f, 0) * transform.forward;
        Vector3 rightDirection = Quaternion.Euler(0, visionAngle * 0.5f, 0) * transform.forward;

        Gizmos.DrawRay(transform.position, leftDirection * visionRange);
        Gizmos.DrawRay(transform.position, rightDirection * visionRange);

        // Attack & proximity
        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.DrawWireSphere(transform.position, proximitySenseRadius);

        // Memory: posisi terakhir yang diketahui
        if (blackboard.hasMemory)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(blackboard.lastKnownPosition, 0.5f);
            Gizmos.DrawLine(transform.position, blackboard.lastKnownPosition);
        }

        // Search plan
        Gizmos.color = Color.cyan;
        for (int i = searchPointIndex; i < searchPoints.Count; i++)
            Gizmos.DrawWireCube(searchPoints[i], Vector3.one * 0.4f);
    }

    private void OnGUI()
    {
        if (!(showStateLabel || EnemyDebugMenu.ShowLabels) || Camera.main == null || !StealthGameManager.IsPlaying)
            return;

        Vector3 screen = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2.4f);

        if (screen.z < 0f)
            return;

        float x = screen.x - 25f;
        float y = Screen.height - screen.y;

        // Suspicion bar
        GUI.color = Color.black * 0.6f;
        GUI.DrawTexture(new Rect(x, y, 50f, 6f), Texture2D.whiteTexture);
        GUI.color = GetStateColor();
        GUI.DrawTexture(new Rect(x, y, 50f * blackboard.suspicion, 6f), Texture2D.whiteTexture);

        string icon =
            blackboard.awareness == AwarenessState.Alert ? "!" :
            blackboard.awareness == AwarenessState.Searching ? "??" :
            blackboard.awareness == AwarenessState.Suspicious ? "?" : "";

        string label = showActionName || EnemyDebugMenu.ShowLabels ? icon + " " + currentAction : icon;

        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(x, y - 24f, 120f, 24f), label, style);
        GUI.color = Color.white;
    }
}
