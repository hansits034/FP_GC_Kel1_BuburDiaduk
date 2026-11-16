using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.AI;

// Informasi pencarian yang dibutuhkan policy (diisi EnemyBTController di game, atau arena saat training).
public interface ISearchContext
{
    Vector3 LastKnownPosition { get; }
    Vector3 LastKnownVelocity { get; }
    float SearchElapsed { get; }
    bool IsSearching { get; }
}

// REINFORCEMENT LEARNING (Unity ML-Agents): policy pencarian penjaga.
//
//   Observation -> Policy -> Action -> Reward -> Learning
//
// Observation (17 nilai):
//   - arah & jarak ke posisi terakhir pemain (frame lokal penjaga)         [3]
//   - arah gerak terakhir pemain (frame lokal)                             [2]
//   - lama mencari (dinormalisasi)                                         [1]
//   - kecepatan penjaga (frame lokal)                                      [2]
//   - 8 raycast jarak dinding/obstacle di sekeliling                       [8]
//   - apakah sel saat ini sudah pernah dikunjungi                          [1]
// Action (continuous 2): arah gerak yang diinginkan (x, z lokal).
// Reward (saat training, diberikan GuardSearchArena):
//   +1   menemukan target (masuk kerucut pandang & tidak terhalang)
//   +0.02 setiap masuk sel 2x2 m baru (eksplorasi)
//   -0.0005 per step (mendorong pencarian cepat), -0.01 menabrak dinding
// Tujuan: menebak tempat persembunyian pemain dari posisi & arah terakhir, lebih efisien
//         dibanding titik acak. Dipakai pada difficulty tinggi (DifficultyProfile.useLearnedSearch).
[RequireComponent(typeof(BehaviorParameters))]
public class GuardSearchAgent : Agent
{
    public const string BehaviorNameConst = "GuardSearch";
    public const int ObservationSize = 17;
    public const int RayCount = 8;

    [SerializeField] private float rayLength = 8f;
    [SerializeField] private LayerMask obstacleMask = 1 << 6;
    [SerializeField] private float moveSpeed = 3f;

    [Tooltip("Training: agent menggerakkan NavMeshAgent sendiri. Di game: EnemyBTController yang bergerak.")]
    [SerializeField] private bool driveMovement;

    private ISearchContext context;
    private NavMeshAgent navAgent;
    private BehaviorParameters behaviorParameters;
    private readonly System.Collections.Generic.HashSet<Vector2Int> visited = new System.Collections.Generic.HashSet<Vector2Int>();

    public Vector3 DesiredDirection { get; private set; }
    public int NewCellsThisStep { get; private set; }
    public bool HitWallThisStep { get; private set; }

    // Siap dipakai di game jika ada model hasil training.
    public bool IsReady =>
        isActiveAndEnabled && behaviorParameters != null &&
        (behaviorParameters.Model != null || behaviorParameters.BehaviorType == BehaviorType.HeuristicOnly);

    public bool DriveMovement { get => driveMovement; set => driveMovement = value; }

    public void SetContext(ISearchContext searchContext)
    {
        context = searchContext;
    }

    public override void Initialize()
    {
        navAgent = GetComponent<NavMeshAgent>();
        behaviorParameters = GetComponent<BehaviorParameters>();

        if (context == null)
            context = GetComponent<ISearchContext>();
    }

    public override void OnEpisodeBegin()
    {
        visited.Clear();
        DesiredDirection = Vector3.zero;

        if (context is GuardSearchArena arena)
            arena.ResetEpisode();
    }

    public void ResetVisited()
    {
        visited.Clear();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 toTarget = Vector3.zero;
        Vector3 lastVelocity = Vector3.zero;
        float elapsed = 0f;

        if (context != null)
        {
            toTarget = context.LastKnownPosition - transform.position;
            toTarget.y = 0f;
            lastVelocity = context.LastKnownVelocity;
            lastVelocity.y = 0f;
            elapsed = context.SearchElapsed;
        }

        Vector3 localTarget = transform.InverseTransformDirection(toTarget);
        Vector3 localVelocity = transform.InverseTransformDirection(lastVelocity.normalized);
        Vector3 ownVelocity = navAgent != null ? transform.InverseTransformDirection(navAgent.velocity) : Vector3.zero;

        sensor.AddObservation(localTarget.x / 20f);
        sensor.AddObservation(localTarget.z / 20f);
        sensor.AddObservation(Mathf.Clamp01(toTarget.magnitude / 20f));
        sensor.AddObservation(localVelocity.x);
        sensor.AddObservation(localVelocity.z);
        sensor.AddObservation(Mathf.Clamp01(elapsed / 30f));
        sensor.AddObservation(ownVelocity.x / moveSpeed);
        sensor.AddObservation(ownVelocity.z / moveSpeed);

        Vector3 origin = transform.position + Vector3.up * 0.5f;

        for (int i = 0; i < RayCount; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 360f / RayCount, 0f) * transform.forward;
            float distance = Physics.Raycast(origin, dir, out RaycastHit hit, rayLength, obstacleMask) ? hit.distance : rayLength;
            sensor.AddObservation(distance / rayLength);
        }

        sensor.AddObservation(visited.Contains(CurrentCell()) ? 1f : 0f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float x = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float z = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);

        Vector3 local = new Vector3(x, 0f, z);
        DesiredDirection = local.sqrMagnitude > 0.0001f ? transform.TransformDirection(local) : Vector3.zero;

        NewCellsThisStep = visited.Add(CurrentCell()) ? 1 : 0;
        HitWallThisStep = Physics.Raycast(transform.position + Vector3.up * 0.5f, DesiredDirection.normalized, 0.6f, obstacleMask);

        if (driveMovement && navAgent != null && navAgent.isOnNavMesh && DesiredDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 step = DesiredDirection.normalized * moveSpeed * Time.fixedDeltaTime;
            navAgent.Move(step);

            Quaternion look = Quaternion.LookRotation(DesiredDirection.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 0.5f);
        }

        if (context is GuardSearchArena arena)
            arena.OnAgentStep(this);
    }

    // Heuristic: kontrol manual (WASD/panah) untuk menguji arena tanpa model.
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<float> continuous = actionsOut.ContinuousActions;
        UnityEngine.InputSystem.Keyboard k = UnityEngine.InputSystem.Keyboard.current;

        if (k == null)
            return;

        Vector3 world = new Vector3(
            (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f),
            0f,
            (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f));

        Vector3 local = transform.InverseTransformDirection(world);
        continuous[0] = local.x;
        continuous[1] = local.z;
    }

    private Vector2Int CurrentCell()
    {
        return new Vector2Int(Mathf.FloorToInt(transform.position.x / 2f), Mathf.FloorToInt(transform.position.z / 2f));
    }
}
