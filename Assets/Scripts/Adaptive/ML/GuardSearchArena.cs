using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Satu arena training RL untuk GuardSearchAgent.
// Layout dibuat oleh LayoutGenerator (PCG yang sama dengan game) agar policy tergeneralisasi.
//
// Skenario tiap episode (meniru pemain yang kabur lalu bersembunyi):
//   - "Posisi terakhir terlihat" = sel acak; target (pemain) bersembunyi 2-6 sel dari situ,
//     sebisa mungkin di balik cover (tidak terlihat dari posisi terakhir).
//   - Arah gerak terakhir = arah dari posisi terakhir menuju tempat sembunyi (+ noise).
//   - Agent muncul 4-10 sel dari posisi terakhir dan harus menemukan target.
public class GuardSearchArena : MonoBehaviour, ISearchContext
{
    [SerializeField] private GuardSearchAgent agent;
    [SerializeField] private Transform target;
    [SerializeField] private int seed = 1;
    [SerializeField] private float viewRange = 9f;
    [SerializeField] private float viewAngle = 100f;
    [SerializeField] private LayerMask obstacleMask = 1 << 6;

    [Header("Reward")]
    [SerializeField] private float findReward = 1f;
    [SerializeField] private float stepPenalty = -0.0005f;
    [SerializeField] private float newCellReward = 0.02f;
    [SerializeField] private float wallPenalty = -0.01f;

    private LevelLayout layout;
    private Vector3 origin;
    private readonly List<Vector2Int> freeCells = new List<Vector2Int>();
    private Vector3 lastKnown;
    private Vector3 lastVelocity;
    private float episodeStart;

    public int Episodes { get; private set; }
    public int Finds { get; private set; }

    // ISearchContext
    public Vector3 LastKnownPosition => lastKnown;
    public Vector3 LastKnownVelocity => lastVelocity;
    public float SearchElapsed => Time.time - episodeStart;
    public bool IsSearching => true;

    public void Build(ProceduralLevelBuilder palette, Vector3 worldOrigin)
    {
        origin = worldOrigin;

        DifficultyProfile profile = DifficultyProfile.FromDifficulty(0.5f);
        profile.roomCount = 4 + seed % 3;
        profile.guardCount = 0;
        profile.cameraCount = 0;
        profile.raiseAlarm = false;
        profile.coverDensity = 0.8f;
        profile.extraLoops = 2;
        profile.ventCount = 0; // lingkungan training tetap sama dengan model yang sudah dilatih

        layout = LayoutGenerator.Generate(seed * 101 + 7, profile);
        palette.BuildGeometry(layout, origin, transform);

        freeCells.Clear();
        for (int x = 0; x < layout.width; x++)
            for (int y = 0; y < layout.height; y++)
                if (!layout.blocked[x, y]) freeCells.Add(new Vector2Int(x, y));
    }

    public void Activate()
    {
        if (agent == null)
            return;

        agent.SetContext(this);
        agent.DriveMovement = true;
        agent.gameObject.SetActive(true);
    }

    // Dipanggil GuardSearchAgent.OnEpisodeBegin (lewat konteks).
    public void ResetEpisode()
    {
        if (layout == null || freeCells.Count == 0)
            return;

        Episodes++;
        episodeStart = Time.time;

        Vector2Int lastCell = freeCells[Random.Range(0, freeCells.Count)];
        int[,] dist = Distances(lastCell);

        // Tempat sembunyi 2-6 sel, utamakan yang tidak terlihat dari posisi terakhir.
        Vector2Int hide = lastCell;
        float bestScore = float.MinValue;

        for (int i = 0; i < 30; i++)
        {
            Vector2Int c = freeCells[Random.Range(0, freeCells.Count)];
            int d = dist[c.x, c.y];

            if (d < 2 || d > 6)
                continue;

            float score = (LayoutGenerator.HasLineOfSight(layout, lastCell, c) ? 0f : 2f) + Random.value;

            if (score > bestScore)
            {
                bestScore = score;
                hide = c;
            }
        }

        // Spawn agent 4-10 sel dari posisi terakhir.
        Vector2Int spawn = lastCell;

        for (int i = 0; i < 40; i++)
        {
            Vector2Int c = freeCells[Random.Range(0, freeCells.Count)];
            int d = dist[c.x, c.y];

            if (d >= 4 && d <= 10 && c != hide)
            {
                spawn = c;
                break;
            }
        }

        lastKnown = origin + LevelLayout.CellToWorld(lastCell);
        Vector3 hideWorld = origin + LevelLayout.CellToWorld(hide);
        Vector3 flee = hideWorld - lastKnown;
        flee.y = 0f;
        lastVelocity = (Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * flee.normalized) * 3f;

        if (target != null)
            target.position = hideWorld + Vector3.up * 0.9f;

        NavMeshAgent nav = agent.GetComponent<NavMeshAgent>();
        Vector3 spawnWorld = origin + LevelLayout.CellToWorld(spawn);

        if (nav != null && NavMesh.SamplePosition(spawnWorld, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            nav.Warp(hit.position);

        agent.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        agent.ResetVisited();
    }

    // Dipanggil GuardSearchAgent.OnActionReceived (lewat konteks).
    public void OnAgentStep(GuardSearchAgent searcher)
    {
        searcher.AddReward(stepPenalty);

        if (searcher.NewCellsThisStep > 0)
            searcher.AddReward(newCellReward);

        if (searcher.HitWallThisStep)
            searcher.AddReward(wallPenalty);

        if (CanSeeTarget(searcher.transform))
        {
            Finds++;
            searcher.AddReward(findReward);
            searcher.EndEpisode();
        }
    }

    private bool CanSeeTarget(Transform searcher)
    {
        if (target == null)
            return false;

        Vector3 eye = searcher.position + Vector3.up * 0.6f;
        Vector3 to = target.position - eye;

        if (to.magnitude > viewRange)
            return false;

        Vector3 flat = new Vector3(to.x, 0f, to.z);

        if (Vector3.Angle(searcher.forward, flat) > viewAngle * 0.5f && to.magnitude > 1.5f)
            return false;

        return !Physics.Raycast(eye, to.normalized, to.magnitude, obstacleMask);
    }

    private int[,] Distances(Vector2Int from)
    {
        int[,] dist = new int[layout.width, layout.height];

        for (int x = 0; x < layout.width; x++)
            for (int y = 0; y < layout.height; y++)
                dist[x, y] = -1;

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        dist[from.x, from.y] = 0;
        queue.Enqueue(from);
        Vector2Int[] dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();

            foreach (Vector2Int d in dirs)
            {
                Vector2Int n = c + d;

                if (!LayoutGenerator.CanStep(layout, c, n, true) || dist[n.x, n.y] >= 0)
                    continue;

                dist[n.x, n.y] = dist[c.x, c.y] + 1;
                queue.Enqueue(n);
            }
        }

        return dist;
    }
}
