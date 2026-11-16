using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.AI.Navigation;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

// Project EAS: scene Level Adaptif (PCG + DDA + Game AI + ML) dan scene training RL.
//   Menu: Stealth > FP - Build Level Adaptif
//         Stealth > FP - Build Arena Training RL
//         Stealth > FP - Uji PCG (validasi 200 layout)
public static partial class StealthLevelBuilder
{
    private const string AdaptiveSceneName = "Level_Adaptive";
    private const string AdaptivePath = SceneFolder + "/" + AdaptiveSceneName + ".unity";
    private const string TrainingPath = "Assets/Scenes/Training/RL_GuardSearch.unity";
    private const string SearchModelPath = "Assets/ML/Models/GuardSearch.onnx";
    private const string AdaptiveTitle = "Operasi Adaptif (Prosedural)";
    private const string AdaptiveDescription =
        "Fasilitas dibangun ulang setiap run (PCG). Jumlah prajurit, CCTV, cover & perilaku AI menyesuaikan performamu (DDA + ML).\n\n" +
        "Tanpa akhir - semakin jago, semakin sulit.\nF2 panel AI  -  F3 peta validasi.";

    // ==================================================
    // MENU
    // ==================================================

    [MenuItem("Stealth/FP - Build Level Adaptif")]
    public static void BuildAdaptiveMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        BuildAdaptiveLevel();
        EditorSceneManager.OpenScene(AdaptivePath);
    }

    [MenuItem("Stealth/FP - Build Arena Training RL")]
    public static void BuildTrainingMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        BuildTrainingScene();
        EditorSceneManager.OpenScene(TrainingPath);
    }

    [MenuItem("Stealth/FP - Uji PCG (validasi 200 layout)")]
    public static void PCGSelfTestMenu()
    {
        PCGSelfTest();
    }

    // Command line: -executeMethod StealthLevelBuilder.BuildAdaptiveLevel
    public static void BuildAdaptiveLevel()
    {
        EnsureFolder("Assets/Scenes", "Levels");
        EnsureFolder("Assets/Materials", "Stealth");
        materials.Clear();

        if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(GuardControllerPath) == null)
            CreateGuardController();

        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(PostFxPath) == null)
            CreatePostFxProfile();

        BeginScene("Adaptive");
        SetupLighting(0.6f, new Color(0.3f, 0.32f, 0.38f), 0.006f);

        CreatePlayer(new Vector3(2f, 1f, 2f), 0f);

        // ---------- Template (nonaktif) ----------
        Transform templates = new GameObject("PCG_Templates").transform;
        List<GameObject> all = new List<GameObject>();

        GameObject guard = CreateGuard("Template_Guard", Vector3.zero, 1f, false, WP(0f, 0f, 0f));
        Transform waypointRoot = guardsRoot.Find("Waypoints_Template_Guard");
        if (waypointRoot != null) Object.DestroyImmediate(waypointRoot.gameObject);
        all.Add(guard);

        GameObject door = CreateDoor("Template_Door", Vector3.zero, true, "keycard_red", "Keycard Merah",
            Mat("DoorRed", new Color(0.65f, 0.12f, 0.1f), 0f, 0.5f), "open:door_vault", "Pintu Brankas").gameObject;
        all.Add(door);

        GameObject keycard = CreatePickup("Template_Keycard", "keycard_red", "Keycard Merah", 1, Vector3.zero,
            new Color(1f, 0.2f, 0.15f), PickupShape.Keycard, "pickup:keycard_red").gameObject;
        all.Add(keycard);

        GameObject can = CreatePickup("Template_Can", "can", "Kaleng", 2, Vector3.zero,
            new Color(0.75f, 0.78f, 0.8f), PickupShape.Can, "pickup:can").gameObject;
        all.Add(can);

        GameObject terminal = CreateTerminal("Template_Terminal", Vector3.zero, 0f, "Unduh data brankas", 3f, "use:terminal_vault").gameObject;
        all.Add(terminal);

        GameObject power = CreatePowerBox("Template_PowerBox", new Vector3(0f, 1.3f, 0f), 0f, "Area",
            new LightZone[0], new SecurityCamera[0], 25f, "use:power").gameObject;
        all.Add(power);

        GameObject cam = CreateSecurityCamera("Template_Camera", new Vector3(0f, 2.8f, 0f), 0f, 45f).gameObject;
        all.Add(cam);

        GameObject extraction = CreateExtraction("Template_Extraction", new Vector3(0f, 1f, 0f)).gameObject;
        all.Add(extraction);

        GameObject alarm = CreateAlarmPanel("Template_Alarm", new Vector3(0f, 1.4f, 0f));
        all.Add(alarm);

        GameObject ventCover = CreateVentCover("Template_VentCover", Vector3.zero, 0f, "open:vent").gameObject;
        all.Add(ventCover);

        GameObject screwdriver = CreatePickup("Template_Screwdriver", "screwdriver", "Obeng", 1, Vector3.zero,
            new Color(1f, 0.85f, 0.1f), PickupShape.Screwdriver, "pickup:screwdriver").gameObject;
        all.Add(screwdriver);

        List<(PropKind, GameObject)> props = CreatePropTemplates();
        foreach ((PropKind _, GameObject go) in props) all.Add(go);

        foreach (GameObject go in all)
        {
            go.transform.SetParent(templates, true);
            go.SetActive(false);
        }

        // ---------- Sistem game ----------
        CreateGameSystems(
            "OPERASI ADAPTIF",
            "Fasilitas prosedural.",
            AdaptiveSceneName);

        GameObject systems = GameObject.Find("GameSystems");
        PlayerPerformanceTracker tracker = systems.AddComponent<PlayerPerformanceTracker>();
        systems.AddComponent<AdaptiveDebugOverlay>();
        MissionManager mission = systems.GetComponent<MissionManager>();

        NavMeshSurface surface = CreateNavSurface();

        // ---------- Generator ----------
        GameObject generatorObject = new GameObject("ProceduralLevel");
        ProceduralLevelBuilder generator = generatorObject.AddComponent<ProceduralLevelBuilder>();

        SetProps(generator,
            ("guardTemplate", guard),
            ("doorTemplate", door),
            ("keycardTemplate", keycard),
            ("canTemplate", can),
            ("terminalTemplate", terminal),
            ("powerBoxTemplate", power),
            ("cameraTemplate", cam),
            ("extractionTemplate", extraction),
            ("alarmTemplate", alarm),
            ("ventCoverTemplate", ventCover),
            ("screwdriverTemplate", screwdriver),
            ("player", playerTransform),
            ("navSurface", surface),
            ("missionManager", mission),
            ("tracker", tracker));

        ConfigurePalette(generator, props);

        Unity.InferenceEngine.ModelAsset model = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(SearchModelPath);
        bool hasModel = model != null; // dicatat sekarang: aset bisa ter-unload saat menu utama dibuka
        if (hasModel)
            SetProps(generator, ("guardSearchModel", model));

        SaveScene(AdaptivePath);
        UpdateBuildSettings();
        AddAdaptiveToMainMenu();
        AssetDatabase.SaveAssets();

        Debug.Log("[StealthLevelBuilder] Level Adaptif selesai: " + AdaptivePath +
                  (hasModel ? " (model RL terpasang)" : " (model RL belum ada: " + SearchModelPath + ")"));
    }

    // ==================================================
    // TEMPLATE & PALET
    // ==================================================

    private static List<(PropKind, GameObject)> CreatePropTemplates()
    {
        List<(PropKind, GameObject)> list = new List<(PropKind, GameObject)>();
        Vector3 o = Vector3.zero;

        list.Add((PropKind.Crate, ModelProp("Template_Crate", o, PropCatalog.WorldSize(PropKind.Crate), "crate")));
        list.Add((PropKind.Boxes, ModelProp("Template_Boxes", o, PropCatalog.WorldSize(PropKind.Boxes), "cardboard2")));
        list.Add((PropKind.Barrel, ModelProp("Template_Barrel", o, PropCatalog.WorldSize(PropKind.Barrel), "barrel_red")));
        list.Add((PropKind.Pillar, Solid("Template_Pillar", o, PropCatalog.WorldSize(PropKind.Pillar), MConcrete)));
        list.Add((PropKind.Locker, ModelProp("Template_Locker", o, PropCatalog.WorldSize(PropKind.Locker), "cabinet")));
        list.Add((PropKind.Sandbag, ModelProp("Template_Sandbag", o, PropCatalog.WorldSize(PropKind.Sandbag), "sandbag")));
        list.Add((PropKind.Desk, ModelProp("Template_Desk", o, PropCatalog.WorldSize(PropKind.Desk), "table_long")));
        list.Add((PropKind.Shelf, ModelRow("Template_Shelf", o, PropCatalog.WorldSize(PropKind.Shelf), new[] { "cardboard3", "cardboard4" }, 4)));
        list.Add((PropKind.ServerRack, Solid("Template_ServerRack", o, PropCatalog.WorldSize(PropKind.ServerRack), MServer)));

        return list;
    }

    private static void ConfigurePalette(ProceduralLevelBuilder generator, List<(PropKind, GameObject)> props)
    {
        SetProps(generator,
            ("wallMaterial", MWall),
            ("lampMaterial", Mat("LampFixture", new Color(1f, 0.95f, 0.85f), 2f)),
            ("ventMaterial", MVent),
            ("ventCeilingMaterial", TransparentMat("Vent_Ceiling", new Color(0.55f, 0.62f, 0.7f, 0.35f))),
            ("gasMaterial", TransparentMat("ToxicGas", new Color(0.35f, 1f, 0.25f, 0.3f), 0.2f)),
            ("laserMaterial", Mat("Laser", new Color(1f, 0.1f, 0.08f), 4f, 0.8f)),
            ("floorMaterials", new Object[] { MFloorConcrete, MFloorTile, MFloorWood, MFloorConcrete, MFloorDark }));

        SerializedObject so = new SerializedObject(generator);
        SerializedProperty list = so.FindProperty("propTemplates");
        list.arraySize = props.Count;

        for (int i = 0; i < props.Count; i++)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("kind").enumValueIndex = (int)props[i].Item1;
            element.FindPropertyRelative("template").objectReferenceValue = props[i].Item2;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static NavMeshSurface CreateNavSurface()
    {
        GameObject navigation = new GameObject("Navigation");
        NavMeshSurface surface = navigation.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~((1 << PlayerLayer) | (1 << 2));
        return surface;
    }

    private static GameObject CreateAlarmPanel(string name, Vector3 position)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(gameplayRoot);
        root.transform.position = position;

        Primitive(PrimitiveType.Cube, root.transform, Vector3.zero, new Vector3(0.55f, 0.7f, 0.18f),
            Mat("AlarmBody", new Color(0.55f, 0.08f, 0.06f), 0f, 0.5f), Quaternion.identity);
        Primitive(PrimitiveType.Cube, root.transform, new Vector3(0f, -0.22f, 0.1f), new Vector3(0.45f, 0.1f, 0.02f), MHazard, Quaternion.identity);
        GameObject indicator = Primitive(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.12f, 0.1f),
            new Vector3(0.22f, 0.03f, 0.22f), Mat("AlarmLamp", new Color(1f, 0.15f, 0.1f), 1.5f), Quaternion.Euler(90f, 0f, 0f));

        AlarmPanel panel = root.AddComponent<AlarmPanel>();
        SetProps(panel, ("missionKey", "sabotage:alarm"), ("indicator", indicator.GetComponent<Renderer>()));

        return root;
    }

    private static void AddAdaptiveToMainMenu()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuPath) == null)
            return;

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);
        MainMenuUI ui = Object.FindFirstObjectByType<MainMenuUI>();

        if (ui == null)
            return;

        SerializedObject so = new SerializedObject(ui);
        SerializedProperty list = so.FindProperty("levels");

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).FindPropertyRelative("sceneName").stringValue == AdaptiveSceneName)
                return;
        }

        list.arraySize++;
        SerializedProperty element = list.GetArrayElementAtIndex(list.arraySize - 1);
        element.FindPropertyRelative("sceneName").stringValue = AdaptiveSceneName;
        element.FindPropertyRelative("title").stringValue = AdaptiveTitle;
        element.FindPropertyRelative("difficulty").intValue = 3;
        element.FindPropertyRelative("description").stringValue = AdaptiveDescription;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[StealthLevelBuilder] Level Adaptif ditambahkan ke menu utama.");
    }

    // ==================================================
    // SCENE TRAINING RL (ML-Agents)
    // ==================================================

    // Command line: -executeMethod StealthLevelBuilder.BuildTrainingScene
    public static void BuildTrainingScene()
    {
        EnsureFolder("Assets/Scenes", "Training");
        materials.Clear();

        BeginScene("Training");
        SetupLighting(0.9f, new Color(0.45f, 0.47f, 0.52f), 0f);

        Transform templates = new GameObject("PCG_Templates").transform;
        List<(PropKind, GameObject)> props = CreatePropTemplates();

        foreach ((PropKind _, GameObject go) in props)
        {
            go.transform.SetParent(templates, true);
            go.SetActive(false);
        }

        GameObject paletteObject = new GameObject("Palette");
        ProceduralLevelBuilder palette = paletteObject.AddComponent<ProceduralLevelBuilder>();
        SetProps(palette, ("generateOnAwake", false));
        ConfigurePalette(palette, props);

        NavMeshSurface surface = CreateNavSurface();

        const int arenaCount = 8;
        Object[] arenas = new Object[arenaCount];
        Material targetMat = Mat("RLTarget", new Color(1f, 0.85f, 0.1f), 2f);
        Material agentMat = Mat("RLAgent", new Color(0.9f, 0.2f, 0.2f));

        for (int i = 0; i < arenaCount; i++)
        {
            GameObject arenaObject = new GameObject("Arena_" + i);
            GuardSearchArena arena = arenaObject.AddComponent<GuardSearchArena>();

            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            target.name = "HiddenTarget";
            Object.DestroyImmediate(target.GetComponent<Collider>());
            target.transform.SetParent(arenaObject.transform, false);
            target.transform.localScale = Vector3.one * 0.8f;
            target.GetComponent<Renderer>().sharedMaterial = targetMat;

            GameObject agentObject = CreateTrainingAgent(i, agentMat);
            agentObject.transform.SetParent(arenaObject.transform, false);

            SetProps(arena,
                ("agent", agentObject.GetComponent<GuardSearchAgent>()),
                ("target", target.transform),
                ("seed", i + 1),
                ("obstacleMask", 1 << ObstacleLayer));

            arenas[i] = arena;
        }

        GameObject managerObject = new GameObject("TrainingManager");
        GuardSearchTrainingManager manager = managerObject.AddComponent<GuardSearchTrainingManager>();
        SetProps(manager, ("palette", palette), ("navSurface", surface), ("arenas", arenas));

        CreateCamera(new Vector3(130f, 190f, 20f), Quaternion.Euler(75f, 0f, 0f));

        SaveScene(TrainingPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[StealthLevelBuilder] Scene training RL selesai: " + TrainingPath);
    }

    private static GameObject CreateTrainingAgent(int index, Material material)
    {
        GameObject agentObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        agentObject.name = "GuardSearchAgent_" + index;
        agentObject.layer = PlayerLayer; // tidak ikut bake NavMesh & tidak menghalangi raycast pandang
        agentObject.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(agentObject.GetComponent<Collider>());

        GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(nose.GetComponent<Collider>());
        nose.transform.SetParent(agentObject.transform, false);
        nose.transform.localPosition = new Vector3(0f, 0.5f, 0.45f);
        nose.transform.localScale = new Vector3(0.3f, 0.2f, 0.3f);

        NavMeshAgent nav = agentObject.AddComponent<NavMeshAgent>();
        nav.radius = 0.4f;
        nav.height = 2f;
        nav.baseOffset = 1f;
        nav.speed = 3f;
        nav.angularSpeed = 360f;
        nav.acceleration = 12f;

        BehaviorParameters parameters = agentObject.AddComponent<BehaviorParameters>();
        parameters.BehaviorName = GuardSearchAgent.BehaviorNameConst;
        parameters.BrainParameters.VectorObservationSize = GuardSearchAgent.ObservationSize;
        parameters.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(2);
        parameters.BehaviorType = BehaviorType.Default;

        GuardSearchAgent agent = agentObject.AddComponent<GuardSearchAgent>();
        agent.MaxStep = 1500;
        SetProps(agent, ("driveMovement", true), ("obstacleMask", 1 << ObstacleLayer));

        DecisionRequester requester = agentObject.AddComponent<DecisionRequester>();
        requester.DecisionPeriod = 5;
        requester.TakeActionsBetweenDecisions = true;

        agentObject.SetActive(false);
        return agentObject;
    }

    // ==================================================
    // UJI PCG: generate banyak layout tanpa scene, laporkan statistik validasi
    // ==================================================

    // Command line: -executeMethod StealthLevelBuilder.PCGSelfTest
    public static void PCGSelfTest()
    {
        StringBuilder report = new StringBuilder("[PCG Self-Test]\n");
        float[] levels = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        const int seeds = 40;

        foreach (float d in levels)
        {
            DifficultyProfile profile = DifficultyProfile.FromDifficulty(d);
            int valid = 0, failed = 0;
            float attempts = 0f, exposure = 0f, guards = 0f, rooms = 0f, ms = 0f, cams = 0f, path = 0f;
            int withVent = 0, ventRoute = 0, toxic = 0, laser = 0;
            int maxAttempts = 0;

            for (int s = 1; s <= seeds; s++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                LevelLayout layout = LayoutGenerator.Generate(s * 7919 + Mathf.RoundToInt(d * 100f), profile);
                ms += (float)watch.Elapsed.TotalMilliseconds;

                if (layout == null)
                {
                    failed++;
                    continue;
                }

                if (layout.valid) valid++;
                attempts += layout.layoutAttempts;
                maxAttempts = Mathf.Max(maxAttempts, layout.layoutAttempts);
                exposure += layout.exposure;
                guards += layout.guards.Count;
                cams += layout.cameras.Count;
                rooms += layout.rooms.Count;
                path += layout.pathLength;
                if (layout.vents.Count > 0) withVent++;
                if (layout.routeUsesVent) ventRoute++;
                toxic += layout.vents.FindAll(v => v.hazard == VentHazardType.Toxic).Count;
                laser += layout.vents.FindAll(v => v.hazard == VentHazardType.Laser).Count;

                // Cek ulang invariant penting secara independen.
                if (layout.valid && layout.watched[layout.playerStart.x, layout.playerStart.y])
                    report.Append("  ! seed ").Append(s).Append(": start terlihat\n");
            }

            int n = Mathf.Max(1, seeds - failed);
            report.Append("d=").Append(d.ToString("0.00"))
                .Append(" | valid ").Append(valid).Append("/").Append(seeds)
                .Append(" | gagal ").Append(failed)
                .Append(" | ruangan ").Append((rooms / n).ToString("0.0"))
                .Append(" | penjaga ").Append((guards / n).ToString("0.0"))
                .Append(" | CCTV ").Append((cams / n).ToString("0.0"))
                .Append(" | eksposur ").Append((exposure / n).ToString("0.0")).Append("/").Append(profile.maxExposedCells)
                .Append(" | jalur ").Append((path / n).ToString("0"))
                .Append(" | ber-vent ").Append(withVent).Append(" (rute stealth lewat vent ").Append(ventRoute)
                .Append(", gas ").Append(toxic).Append(", laser ").Append(laser).Append(")")
                .Append(" | layout dicoba avg ").Append((attempts / n).ToString("0.0")).Append(" max ").Append(maxAttempts)
                .Append(" | ").Append((ms / seeds).ToString("0.0")).Append(" ms\n");
        }

        Debug.Log(report.ToString());
    }
}
