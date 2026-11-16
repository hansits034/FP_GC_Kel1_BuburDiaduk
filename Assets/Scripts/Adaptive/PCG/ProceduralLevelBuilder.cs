using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.AI;

// Membangun level dari LevelLayout saat runtime:
//   DDA (DifficultyDirector) -> DifficultyProfile -> LayoutGenerator (PCG) -> GameObject + NavMesh.
// Template objek (penjaga, pintu, terminal, prop, dll.) disiapkan StealthLevelBuilder di scene
// dalam keadaan nonaktif, lalu di-instantiate & dikonfigurasi di sini.
[DefaultExecutionOrder(-1000)]
public class ProceduralLevelBuilder : MonoBehaviour
{
    [System.Serializable]
    public class PropTemplate
    {
        public PropKind kind;
        public GameObject template;
    }

    [Header("Mode")]
    [Tooltip("false = hanya dipakai sebagai palet (misal arena training RL).")]
    [SerializeField] private bool generateOnAwake = true;
    [Tooltip("0 = seed acak tiap run.")]
    [SerializeField] private int fixedSeed;

    [Header("Templates (nonaktif di scene)")]
    [SerializeField] private GameObject guardTemplate;
    [SerializeField] private GameObject doorTemplate;
    [SerializeField] private GameObject keycardTemplate;
    [SerializeField] private GameObject canTemplate;
    [SerializeField] private GameObject terminalTemplate;
    [SerializeField] private GameObject powerBoxTemplate;
    [SerializeField] private GameObject cameraTemplate;
    [SerializeField] private GameObject extractionTemplate;
    [SerializeField] private GameObject alarmTemplate;
    [SerializeField] private GameObject ventCoverTemplate;
    [SerializeField] private GameObject screwdriverTemplate;
    [SerializeField] private PropTemplate[] propTemplates;

    [Header("Materials")]
    [SerializeField] private Material wallMaterial;
    [Tooltip("Urutan sesuai RoomTheme: Dock, Hall, Office, Storage, Server.")]
    [SerializeField] private Material[] floorMaterials;
    [SerializeField] private Material lampMaterial;
    [SerializeField] private Material ventMaterial;
    [SerializeField] private Material ventCeilingMaterial;
    [SerializeField] private Material gasMaterial;
    [SerializeField] private Material laserMaterial;

    [Header("Scene")]
    [SerializeField] private Transform player;
    [SerializeField] private NavMeshSurface navSurface;
    [SerializeField] private MissionManager missionManager;
    [SerializeField] private PlayerPerformanceTracker tracker;

    [Header("Reinforcement Learning")]
    [Tooltip("Model ONNX hasil training GuardSearch (opsional). Kosong = pencarian scripted.")]
    [SerializeField] private Unity.InferenceEngine.ModelAsset guardSearchModel;

    public const int ObstacleLayer = 6;
    private const float WallHeight = 3f;
    private const float WallThickness = 0.4f;

    // Seed yang dipaksa untuk run berikutnya (debug overlay: "ulang layout yang sama").
    public static int? NextSeedOverride;

    public static ProceduralLevelBuilder Instance { get; private set; }

    public LevelLayout Layout { get; private set; }
    public DifficultyProfile Profile { get; private set; }
    public int Seed { get; private set; }
    public float GenerationMs { get; private set; }
    public bool HasSearchModel => guardSearchModel != null;
    public IReadOnlyList<EnemyBTController> Guards => guards;
    public IReadOnlyList<SecurityCamera> Cameras => cameras;

    private readonly List<EnemyBTController> guards = new List<EnemyBTController>();
    private readonly List<SecurityCamera> cameras = new List<SecurityCamera>();
    private readonly Dictionary<int, LightZone> zones = new Dictionary<int, LightZone>();
    private readonly List<VentHazardZone> hazards = new List<VentHazardZone>();
    private Transform root, environmentRoot, propsRoot, lightsRoot, gameplayRoot, guardsRoot;
    private Vector3 origin;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        NextSeedOverride = null;
        Instance = null;
    }

    private void Awake()
    {
        if (!generateOnAwake)
            return;

        Instance = this;

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        Profile = DifficultyDirector.BeginRun();
        Seed = NextSeedOverride ?? (fixedSeed != 0 ? fixedSeed : System.Environment.TickCount & 0x7fffffff);
        NextSeedOverride = null;

        Layout = LayoutGenerator.Generate(Seed, Profile);

        if (Layout == null)
        {
            Debug.LogError("[PCG] Gagal membuat layout.");
            return;
        }

        BuildLevel(Layout, Profile, Vector3.zero, transform);
        GenerationMs = (float)watch.Elapsed.TotalMilliseconds;

        if (tracker != null)
            tracker.BeginRun(Seed, Layout.parTime);

        Debug.Log("[PCG] Seed " + Seed + " | difficulty " + Profile.difficulty.ToString("0.00") +
                  " | " + Layout.rooms.Count + " ruangan, " + Layout.guards.Count + " penjaga, " +
                  Layout.cameras.Count + " CCTV | eksposur " + Layout.exposure + "/" + Layout.exposureBudget +
                  " | " + Layout.layoutAttempts + " layout dicoba | " + GenerationMs.ToString("0") + " ms");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ==================================================
    // LEVEL LENGKAP (game)
    // ==================================================

    public void BuildLevel(LevelLayout layout, DifficultyProfile profile, Vector3 worldOrigin, Transform parent)
    {
        BuildGeometry(layout, worldOrigin, parent);

        // Terminal punya meja (collider) -> sebelum bake agar penjaga memutarinya.
        Transform terminal = SpawnWallMounted(terminalTemplate, layout.terminal, 0.45f, 0f, gameplayRoot);

        Physics.SyncTransforms();

        if (navSurface != null)
            navSurface.BuildNavMesh();

        // ---------- Pintu terkunci (NavMeshObstacle carve) ----------
        LayoutDoor locked = layout.LockedDoor;
        Transform door = null;

        if (locked != null && doorTemplate != null)
        {
            Vector3 center = (W(locked.cellA) + W(locked.cellB)) * 0.5f;
            door = Spawn(doorTemplate, center, Quaternion.Euler(0f, locked.AlongX ? 0f : 90f, 0f), gameplayRoot).transform;
        }

        // ---------- Pickup ----------
        Transform keycard = keycardTemplate != null
            ? Spawn(keycardTemplate, W(layout.keycard), Quaternion.identity, gameplayRoot).transform
            : null;

        Transform firstCan = null;

        foreach (Vector2Int cell in layout.cans)
        {
            GameObject can = Spawn(canTemplate, W(cell), Quaternion.identity, gameplayRoot);
            if (firstCan == null && can != null) firstCan = can.transform;
        }

        // ---------- Ventilasi: penutup dibaut di lubang masuk + obeng ----------
        Transform firstVent = null;
        Transform screwdriver = null;

        foreach (LayoutVent vent in layout.vents)
        {
            if (ventCoverTemplate == null)
                break;

            Vector3 inward = new Vector3(-vent.outward.x, 0f, -vent.outward.y);
            Vector3 hole = W(vent.cellA) - inward * (LevelLayout.CellSize * 0.5f);
            float yaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
            GameObject cover = Spawn(ventCoverTemplate, hole + inward * 0.28f + Vector3.up * 0.65f, Quaternion.Euler(0f, yaw, 0f), gameplayRoot);
            if (firstVent == null && cover != null) firstVent = cover.transform;
        }

        if (layout.vents.Count > 0 && screwdriverTemplate != null)
            screwdriver = Spawn(screwdriverTemplate, W(layout.screwdriver), Quaternion.identity, gameplayRoot).transform;

        // ---------- CCTV ----------
        cameras.Clear();

        foreach (LayoutCamera cam in layout.cameras)
        {
            Vector3 position = W(cam.cell) + new Vector3(cam.cornerOffset.x, 0f, cam.cornerOffset.y) * LevelLayout.CellSize;
            position.y = 2.8f;
            GameObject go = Spawn(cameraTemplate, position, Quaternion.Euler(0f, cam.yaw, 0f), gameplayRoot);

            if (go != null && go.TryGetComponent(out SecurityCamera securityCamera))
            {
                securityCamera.SetDetectTime(profile.cameraDetectTime);
                cameras.Add(securityCamera);
            }
        }

        // ---------- Panel listrik: lampu ruangan + CCTV di ruangan tersebut ----------
        Transform powerBox = null;

        if (layout.powerBox != null && powerBoxTemplate != null)
        {
            powerBox = SpawnWallMounted(powerBoxTemplate, layout.powerBox, 0.675f, 1.3f, gameplayRoot, false);

            List<LightZone> powered = new List<LightZone>();
            List<SecurityCamera> poweredCams = new List<SecurityCamera>();
            List<string> names = new List<string>();

            foreach (int room in layout.poweredRooms)
            {
                if (zones.TryGetValue(room, out LightZone zone)) powered.Add(zone);
                names.Add(layout.rooms[room].name);

                for (int i = 0; i < layout.cameras.Count; i++)
                    if (layout.cameras[i].room == room && i < cameras.Count) poweredCams.Add(cameras[i]);
            }

            if (powerBox.TryGetComponent(out PowerBox box))
                box.Configure(string.Join(" & ", names), powered.ToArray(), poweredCams.ToArray(), profile.powerOutage);

            powerBox.gameObject.SetActive(true);

            // Gas & laser di ventilasi ikut padam selama listrik dimatikan.
            foreach (VentHazardZone hazard in hazards)
                hazard.SetPowerSource(box);
        }

        // ---------- Panel alarm ----------
        Transform firstAlarm = null;

        foreach (WallMount mount in layout.alarms)
        {
            Transform alarm = SpawnWallMounted(alarmTemplate, mount, 0.68f, 1.4f, gameplayRoot);
            if (firstAlarm == null) firstAlarm = alarm;
        }

        // ---------- Ekstraksi ----------
        Transform extraction = extractionTemplate != null
            ? Spawn(extractionTemplate, W(layout.extraction) + Vector3.up, Quaternion.identity, gameplayRoot).transform
            : null;

        // ---------- Player ----------
        if (player != null)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            Vector3 start = W(layout.playerStart) + Vector3.up;
            Vector2 toCenter = layout.rooms[layout.startRoom].Center - (Vector2)layout.playerStart;
            player.SetPositionAndRotation(start, Quaternion.Euler(0f, Mathf.Atan2(toCenter.x, toCenter.y) * Mathf.Rad2Deg, 0f));

            if (cc != null) cc.enabled = true;
        }

        // ---------- Penjaga ----------
        guards.Clear();

        for (int i = 0; i < layout.guards.Count; i++)
            SpawnGuard(layout.guards[i], i, profile);

        // ---------- Misi ----------
        if (missionManager != null)
            ConfigureMission(layout, profile, keycard, door, terminal, extraction, firstCan, powerBox, firstAlarm, screwdriver, firstVent);
    }

    // ==================================================
    // GEOMETRI (dipakai juga arena training RL)
    // ==================================================

    public void BuildGeometry(LevelLayout layout, Vector3 worldOrigin, Transform parent)
    {
        origin = worldOrigin;
        zones.Clear();

        root = new GameObject("Generated_" + layout.seed).transform;
        root.SetParent(parent, false);
        environmentRoot = Child("Environment");
        propsRoot = Child("Props");
        lightsRoot = Child("Lights");
        gameplayRoot = Child("Gameplay");
        guardsRoot = Child("Guards");

        float cs = LevelLayout.CellSize;

        // ---------- Lantai per ruangan ----------
        foreach (LayoutRoom room in layout.rooms)
        {
            Vector3 center = origin + new Vector3((room.rect.x + room.rect.width * 0.5f) * cs, -0.1f, (room.rect.y + room.rect.height * 0.5f) * cs);
            Vector3 size = new Vector3(room.rect.width * cs, 0.2f, room.rect.height * cs);
            RuntimeBox.Create("Floor_" + room.name, center, size, FloorMaterial(room.theme), environmentRoot, 0);
        }

        // ---------- Dinding: garis batas antar ruangan, dikurangi pintu ----------
        HashSet<(Vector2Int, Vector2Int)> doorEdges = new HashSet<(Vector2Int, Vector2Int)>();

        foreach (LayoutDoor d in layout.doors)
        {
            doorEdges.Add((d.cellA, d.cellB));
            doorEdges.Add((d.cellB, d.cellA));
        }

        // Lubang ventilasi di dinding batas.
        foreach (LayoutVent v in layout.vents)
        {
            doorEdges.Add((v.cellA, v.cellA + v.outward));
            doorEdges.Add((v.cellA + v.outward, v.cellA));
            doorEdges.Add((v.cellB, v.cellB + v.outward));
            doorEdges.Add((v.cellB + v.outward, v.cellB));
        }

        // Garis horizontal (z = k).
        for (int k = 0; k <= layout.height; k++)
        {
            int runStart = -1;

            for (int x = 0; x <= layout.width; x++)
            {
                bool wall = x < layout.width && IsWallEdge(layout, new Vector2Int(x, k - 1), new Vector2Int(x, k), doorEdges);

                if (wall && runStart < 0) runStart = x;

                if (!wall && runStart >= 0)
                {
                    WallSegment(new Vector2(runStart, k), new Vector2(x, k));
                    runStart = -1;
                }
            }
        }

        // Garis vertikal (x = k).
        for (int k = 0; k <= layout.width; k++)
        {
            int runStart = -1;

            for (int y = 0; y <= layout.height; y++)
            {
                bool wall = y < layout.height && IsWallEdge(layout, new Vector2Int(k - 1, y), new Vector2Int(k, y), doorEdges);

                if (wall && runStart < 0) runStart = y;

                if (!wall && runStart >= 0)
                {
                    WallSegment(new Vector2(k, runStart), new Vector2(k, y));
                    runStart = -1;
                }
            }
        }

        // ---------- Prop ----------
        foreach (LayoutProp prop in layout.props)
        {
            GameObject template = PropTemplateFor(prop.kind);

            if (template == null)
                continue;

            Vector3 center = origin + new Vector3(prop.cells.center.x * cs, template.transform.position.y, prop.cells.center.y * cs);
            Spawn(template, center, Quaternion.Euler(0f, prop.rotated ? 90f : 0f, 0f), propsRoot, true);
        }

        // ---------- Saluran ventilasi ----------
        hazards.Clear();

        for (int i = 0; i < layout.vents.Count; i++)
            BuildVentDuct(layout.vents[i], i);

        // ---------- Lampu & LightZone per ruangan ----------
        foreach (LayoutRoom room in layout.rooms)
            BuildRoomLights(room, layout);
    }

    // Saluran di luar dinding batas (lantai diabaikan NavMesh -> penjaga tidak bisa masuk),
    // atap rendah transparan (pemain harus jongkok, tetap terlihat dari kamera atas).
    private void BuildVentDuct(LayoutVent vent, int index)
    {
        Material metal = ventMaterial != null ? ventMaterial : wallMaterial;
        Material ceiling = ventCeilingMaterial != null ? ventCeilingMaterial : metal;
        Vector3 outward = new Vector3(vent.outward.x, 0f, vent.outward.y);
        bool alongX = vent.outward.x == 0;
        Vector3 axis = alongX ? Vector3.right : Vector3.forward;
        Transform ductRoot = new GameObject("VentDuct_" + index).transform;
        ductRoot.SetParent(environmentRoot, false);

        Vector3 holeA = W(vent.cellA) + outward * (LevelLayout.CellSize * 0.5f);
        Vector3 holeB = W(vent.cellB) + outward * (LevelLayout.CellSize * 0.5f);

        // Lubang 1.6 x 1.3 m: balok di atas + pengisi kiri-kanan.
        foreach (Vector3 hole in new[] { holeA, holeB })
        {
            RuntimeBox.Create("Vent_Lintel", hole + Vector3.up * 2.15f, Oriented(alongX, 2f, 1.7f, WallThickness), wallMaterial, ductRoot, ObstacleLayer);
            RuntimeBox.Create("Vent_Side", hole + axis * 0.9f + Vector3.up * 0.65f, Oriented(alongX, 0.2f, 1.3f, WallThickness), wallMaterial, ductRoot, ObstacleLayer);
            RuntimeBox.Create("Vent_Side", hole - axis * 0.9f + Vector3.up * 0.65f, Oriented(alongX, 0.2f, 1.3f, WallThickness), wallMaterial, ductRoot, ObstacleLayer);
        }

        float a0 = Mathf.Min(Vector3.Dot(holeA, axis), Vector3.Dot(holeB, axis)) - 1f;
        float a1 = Mathf.Max(Vector3.Dot(holeA, axis), Vector3.Dot(holeB, axis)) + 1f;
        float length = a1 - a0;
        Vector3 boundary = holeA - axis * Vector3.Dot(holeA, axis);           // komponen tegak lurus sumbu saluran
        Vector3 Along(float t) => boundary + axis * t;

        GameObject floor = RuntimeBox.Create("Duct_Floor", Along((a0 + a1) * 0.5f) + outward * 1.1f + Vector3.down * 0.1f,
            Oriented(alongX, length, 0.2f, 2.2f), metal, ductRoot, 0);
        floor.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        RuntimeBox.Create("Duct_Wall", Along((a0 + a1) * 0.5f) + outward * 2.0f + Vector3.up * 0.75f,
            Oriented(alongX, length, 1.5f, 0.2f), metal, ductRoot, ObstacleLayer);
        RuntimeBox.Create("Duct_Cap", Along(a0 + 0.1f) + outward * 1.1f + Vector3.up * 0.75f, Oriented(alongX, 0.2f, 1.5f, 1.8f), metal, ductRoot, ObstacleLayer);
        RuntimeBox.Create("Duct_Cap", Along(a1 - 0.1f) + outward * 1.1f + Vector3.up * 0.75f, Oriented(alongX, 0.2f, 1.5f, 1.8f), metal, ductRoot, ObstacleLayer);

        GameObject roof = RuntimeBox.Create("Duct_Ceiling", Along((a0 + a1) * 0.5f) + outward * 1.1f + Vector3.up * 1.4f,
            Oriented(alongX, length, 0.2f, 2.0f), ceiling, ductRoot, 0);
        roof.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        roof.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        GameObject lampObject = new GameObject("Duct_Light");
        lampObject.transform.SetParent(ductRoot, false);
        lampObject.transform.position = Along((a0 + a1) * 0.5f) + outward * 1.1f + Vector3.up * 1.1f;
        Light lamp = lampObject.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = new Color(0.45f, 0.65f, 1f);
        lamp.intensity = 3f;
        lamp.range = Mathf.Max(6f, length * 0.6f);
        lamp.shadows = LightShadows.None;

        if (vent.hazard == VentHazardType.None)
            return;

        // ---------- Bahaya ----------
        Vector3 inside = Along((a0 + a1) * 0.5f) + outward * 1.1f + Vector3.up * 0.7f;
        Bounds area = new Bounds(inside, Oriented(alongX, length - 0.4f, 1.4f, 1.8f));
        VentHazardZone zone = new GameObject("VentHazard_" + vent.hazard).AddComponent<VentHazardZone>();
        zone.transform.SetParent(ductRoot, false);
        zone.Configure(vent.hazard, area, lamp);
        hazards.Add(zone);

        if (vent.hazard == VentHazardType.Toxic)
        {
            // Lapisan gas hijau di dalam saluran (tanpa collider).
            for (int i = 0; i < 3; i++)
            {
                GameObject gas = RuntimeBox.Create("ToxicGas", inside + Vector3.up * (i * 0.3f - 0.3f),
                    Oriented(alongX, length - 0.6f, 0.25f, 1.6f - i * 0.2f), gasMaterial != null ? gasMaterial : ceiling, zone.transform, 2);
                DestroyImmediate(gas.GetComponent<Collider>());
                gas.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                zone.AddGasVisual(gas);
            }
        }
        else
        {
            // Berkas laser melintang, berkedip bergantian (bisa dilewati dengan timing).
            int count = Mathf.Max(2, Mathf.RoundToInt((length - 3f) / 3.5f));

            for (int i = 0; i < count; i++)
            {
                float t = Mathf.Lerp(a0 + 2f, a1 - 2f, count == 1 ? 0.5f : i / (float)(count - 1));
                Vector3 c = Along(t) + outward * 1.1f;
                GameObject beam = new GameObject("Laser_" + i);
                beam.transform.SetParent(zone.transform, false);
                beam.transform.position = c;

                foreach (float y in new[] { 0.3f, 0.65f, 1.0f })
                {
                    GameObject line = RuntimeBox.Create("Beam", c + Vector3.up * y, Oriented(alongX, 0.04f, 0.04f, 1.8f),
                        laserMaterial != null ? laserMaterial : wallMaterial, beam.transform, 2);
                    DestroyImmediate(line.GetComponent<Collider>());
                    line.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                Bounds hit = new Bounds(c + Vector3.up * 0.7f, Oriented(alongX, 0.5f, 1.4f, 1.8f));
                zone.AddBeam(beam, hit, i * 0.9f);
            }
        }
    }

    // Ukuran kotak: "along" = sepanjang sumbu saluran, "across" = tegak lurus (ke luar peta).
    private static Vector3 Oriented(bool alongX, float along, float height, float across)
    {
        return alongX ? new Vector3(along, height, across) : new Vector3(across, height, along);
    }

    private Transform Child(string name)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(root, false);
        return t;
    }

    private static bool IsWallEdge(LevelLayout layout, Vector2Int a, Vector2Int b, HashSet<(Vector2Int, Vector2Int)> doorEdges)
    {
        bool aIn = layout.InBounds(a);
        bool bIn = layout.InBounds(b);

        if (!aIn && !bIn)
            return false;

        if (!aIn || !bIn)
            return !doorEdges.Contains((a, b));

        if (layout.roomOf[a.x, a.y] == layout.roomOf[b.x, b.y])
            return false;

        return !doorEdges.Contains((a, b));
    }

    private void WallSegment(Vector2 fromCell, Vector2 toCell)
    {
        float cs = LevelLayout.CellSize;
        Vector3 a = origin + new Vector3(fromCell.x * cs, 0f, fromCell.y * cs);
        Vector3 b = origin + new Vector3(toCell.x * cs, 0f, toCell.y * cs);
        Vector3 center = (a + b) * 0.5f + Vector3.up * WallHeight * 0.5f;
        bool alongX = Mathf.Abs(b.x - a.x) > 0.01f;
        Vector3 size = alongX
            ? new Vector3(Mathf.Abs(b.x - a.x) + WallThickness, WallHeight, WallThickness)
            : new Vector3(WallThickness, WallHeight, Mathf.Abs(b.z - a.z) + WallThickness);

        RuntimeBox.Create("Wall", center, size, wallMaterial, environmentRoot, ObstacleLayer);
    }

    private void BuildRoomLights(LayoutRoom room, LevelLayout layout)
    {
        float cs = LevelLayout.CellSize;
        int nx = Mathf.Clamp(Mathf.RoundToInt(room.rect.width / 5f), 1, 3);
        int ny = Mathf.Clamp(Mathf.RoundToInt(room.rect.height / 5f), 1, 3);

        Color color =
            room.theme == RoomTheme.Server ? new Color(0.5f, 0.7f, 1f) :
            room.theme == RoomTheme.Dock ? new Color(0.8f, 0.85f, 1f) :
            new Color(1f, 0.9f, 0.75f);

        List<Light> lights = new List<Light>();

        for (int ix = 0; ix < nx; ix++)
        {
            for (int iy = 0; iy < ny; iy++)
            {
                float x = room.rect.x + room.rect.width * (ix + 0.5f) / nx;
                float z = room.rect.y + room.rect.height * (iy + 0.5f) / ny;

                GameObject lamp = new GameObject("Lamp");
                lamp.transform.SetParent(lightsRoot, false);
                lamp.transform.position = origin + new Vector3(x * cs, 2.8f, z * cs);

                if (lampMaterial != null)
                {
                    GameObject fixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    DestroyImmediate(fixture.GetComponent<Collider>());
                    fixture.transform.SetParent(lamp.transform, false);
                    fixture.transform.localPosition = new Vector3(0f, 0.12f, 0f);
                    fixture.transform.localScale = new Vector3(0.9f, 0.05f, 0.25f);
                    fixture.GetComponent<Renderer>().sharedMaterial = lampMaterial;
                }

                Light light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.intensity = 10f;
                light.range = Mathf.Max(room.rect.width, room.rect.height) * cs / Mathf.Max(nx, ny) + 3f;
                light.shadows = LightShadows.None;
                lights.Add(light);
            }
        }

        GameObject zoneObject = new GameObject("LightZone_" + room.name);
        zoneObject.SetActive(false);
        zoneObject.transform.SetParent(lightsRoot, false);
        zoneObject.transform.position = origin + new Vector3((room.rect.x + room.rect.width * 0.5f) * cs, 2f, (room.rect.y + room.rect.height * 0.5f) * cs);

        LightZone zone = zoneObject.AddComponent<LightZone>();
        zone.Configure(room.name, new Vector3(room.rect.width * cs, 4f, room.rect.height * cs), lights.ToArray());
        zoneObject.SetActive(true);
        zones[room.id] = zone;
    }

    // ==================================================
    // PENJAGA
    // ==================================================

    private void SpawnGuard(LayoutGuard data, int index, DifficultyProfile profile)
    {
        if (guardTemplate == null || data.route.Count == 0)
            return;

        Transform waypointRoot = new GameObject("Waypoints_Guard" + index).transform;
        waypointRoot.SetParent(guardsRoot, false);
        Transform[] points = new Transform[data.route.Count];

        for (int i = 0; i < data.route.Count; i++)
        {
            Vector2Int cell = data.route[i];
            Vector2Int next = data.route[(i + 1) % data.route.Count];
            Vector2 dir = data.post
                ? Layout.rooms[data.room].Center - (Vector2)cell
                : (Vector2)(next - cell);

            Transform point = new GameObject("WP" + i).transform;
            point.SetParent(waypointRoot, false);
            point.SetPositionAndRotation(W(cell), Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, 0f));
            points[i] = point;
        }

        Vector3 spawn = W(data.route[0]) + Vector3.up;
        GameObject guard = Instantiate(guardTemplate, spawn, points[0].rotation, guardsRoot);
        guard.name = (data.post ? "Guard_Post_" : "Guard_") + index;

        EnemyBTController controller = guard.GetComponent<EnemyBTController>();
        controller.Configure(player, points, data.waitTime, data.post);
        controller.ApplyDifficulty(profile);

        if (guardSearchModel != null)
            AttachSearchAgent(guard);

        guard.SetActive(true);

        NavMeshAgent agent = guard.GetComponent<NavMeshAgent>();
        if (agent != null && NavMesh.SamplePosition(spawn, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            agent.Warp(hit.position);

        guards.Add(controller);
    }

    // Komponen ML-Agents ditambahkan hanya jika ada model hasil training.
    private void AttachSearchAgent(GameObject guard)
    {
        BehaviorParameters parameters = guard.AddComponent<BehaviorParameters>();
        parameters.BehaviorName = GuardSearchAgent.BehaviorNameConst;
        parameters.BrainParameters.VectorObservationSize = GuardSearchAgent.ObservationSize;
        parameters.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(2);
        parameters.Model = guardSearchModel;
        parameters.BehaviorType = BehaviorType.InferenceOnly;

        guard.AddComponent<GuardSearchAgent>();
        DecisionRequester requester = guard.AddComponent<DecisionRequester>();
        requester.DecisionPeriod = 5;
        requester.TakeActionsBetweenDecisions = true;
    }

    // ==================================================
    // MISI
    // ==================================================

    private void ConfigureMission(LevelLayout layout, DifficultyProfile profile, Transform keycard, Transform door,
        Transform terminal, Transform extraction, Transform can, Transform powerBox, Transform alarm,
        Transform screwdriver = null, Transform vent = null)
    {
        string keyRoom = layout.rooms[layout.keyRoom].name;
        string vault = layout.rooms[layout.vaultRoom].name;
        string exitRoom = layout.rooms[layout.extractionRoom].name;

        List<MissionManager.MissionStep> steps = new List<MissionManager.MissionStep>
        {
            Step("pickup:keycard_red", "Ambil Keycard Merah di " + keyRoom, keycard),
            Step("open:door_vault", "Buka pintu " + vault, door),
            Step("use:terminal_vault", "Unduh data di terminal " + vault + " (tahan E)", terminal),
            Step(MissionManager.ExtractKey, "Kabur ke titik ekstraksi (" + exitRoom + ")", extraction)
        };

        if (can != null)
            steps.Add(Step("pickup:can", "Ambil kaleng untuk pengalih perhatian", can, true));

        if (powerBox != null)
            steps.Add(Step("use:power", "Matikan listrik dari panel di " + layout.rooms[layout.powerRoom].name, powerBox, true));

        if (screwdriver != null && vent != null)
        {
            LayoutVent v = layout.vents[0];
            steps.Add(Step("pickup:screwdriver", "Ambil obeng di " + layout.rooms[RoomAt(layout, layout.screwdriver)].name, screwdriver, true));
            steps.Add(Step("open:vent", "Buka ventilasi di " + layout.rooms[v.roomA].name + " (jalur pintas ke " + layout.rooms[v.roomB].name +
                (v.hazard == VentHazardType.Toxic ? ", BERACUN" : v.hazard == VentHazardType.Laser ? ", LASER" : "") + ")", vent, true));
        }

        if (alarm != null)
            steps.Add(Step("sabotage:alarm", "Sabotase panel alarm agar penjaga tidak bisa memanggil bantuan", alarm, true));

        string briefing =
            "Fasilitas ini dibangun ulang secara prosedural (seed " + layout.seed + ").\n" +
            "Tingkat ancaman: <b>" + profile.Label + "</b> (" + profile.difficulty.ToString("0.00") + ")  -  " +
            layout.rooms.Count + " ruangan, " + layout.guards.Count + " prajurit, " + layout.cameras.Count + " CCTV, " +
            layout.cans.Count + " kaleng" + VentBriefing(layout) + ".\n" +
            "Curi data dari " + vault + ", lalu kabur. Tingkat ancaman berikutnya menyesuaikan performamu.";

        missionManager.Configure("OPERASI ADAPTIF #" + (DifficultyDirector.RunCounter + 1), briefing, steps);
    }

    private static int RoomAt(LevelLayout layout, Vector2Int cell) => layout.roomOf[cell.x, cell.y];

    private static string VentBriefing(LevelLayout layout)
    {
        if (layout.vents.Count == 0)
            return "";

        int toxic = layout.vents.FindAll(v => v.hazard == VentHazardType.Toxic).Count;
        int laser = layout.vents.FindAll(v => v.hazard == VentHazardType.Laser).Count;
        string text = ", " + layout.vents.Count + " ventilasi";

        if (toxic > 0) text += " (" + toxic + " beracun)";
        if (laser > 0) text += " (" + laser + " laser)";
        return text;
    }

    private static MissionManager.MissionStep Step(string key, string description, Transform target, bool optional = false)
    {
        return new MissionManager.MissionStep { key = key, description = description, target = target, optional = optional };
    }

    // ==================================================
    // HELPERS
    // ==================================================

    private Vector3 W(Vector2Int cell)
    {
        return origin + LevelLayout.CellToWorld(cell);
    }

    private Material FloorMaterial(RoomTheme theme)
    {
        int i = (int)theme;
        return floorMaterials != null && i < floorMaterials.Length && floorMaterials[i] != null
            ? floorMaterials[i]
            : wallMaterial;
    }

    private GameObject PropTemplateFor(PropKind kind)
    {
        if (propTemplates == null)
            return null;

        foreach (PropTemplate p in propTemplates)
            if (p.kind == kind) return p.template;

        return null;
    }

    private GameObject Spawn(GameObject template, Vector3 position, Quaternion rotation, Transform parent, bool activate = true)
    {
        if (template == null)
            return null;

        GameObject go = Instantiate(template, position, rotation, parent);
        go.name = template.name.Replace("Template_", "");

        if (activate)
            go.SetActive(true);

        return go;
    }

    // Objek menempel dinding: digeser ke arah dinding, menghadap ke dalam ruangan.
    private Transform SpawnWallMounted(GameObject template, WallMount mount, float offset, float height, Transform parent, bool activate = true)
    {
        if (template == null || mount == null)
            return null;

        Vector3 toWall = new Vector3(mount.toWall.x, 0f, mount.toWall.y);
        Vector3 position = W(mount.cell) + toWall * offset + Vector3.up * height;
        float yaw = Mathf.Atan2(-toWall.x, -toWall.z) * Mathf.Rad2Deg;

        return Spawn(template, position, Quaternion.Euler(0f, yaw, 0f), parent, activate).transform;
    }
}
