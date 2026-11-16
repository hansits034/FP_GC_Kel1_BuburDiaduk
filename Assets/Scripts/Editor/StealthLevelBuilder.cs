using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Membangun menu utama + tutorial + 2 level stealth secara otomatis (menu: Stealth > Build Semua Level).
// Collider tiap prop berupa kotak (untuk NavMesh & garis pandang), visualnya model 3D yang di-fit ke kotak itu.
public static partial class StealthLevelBuilder
{
    private const string SceneFolder = "Assets/Scenes/Levels";
    private const string MaterialFolder = "Assets/Materials/Stealth";
    private const string TextureFolder = "Assets/Textures/Environment";
    private const string TutorialPath = SceneFolder + "/Level0_Tutorial.unity";
    private const string Level1Path = SceneFolder + "/Level1_SecurityCheckpoint.unity";
    private const string Level2Path = SceneFolder + "/Level2_ResearchLab.unity";
    private const string MainMenuPath = "Assets/Scenes/MainMenu.unity";
    private const string PostFxPath = "Assets/Settings/StealthPostFX.asset";
    private const string GuardControllerPath = "Assets/Animations/GuardSoldier.controller";

    private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    private const string SoldierPath = "Assets/Imported Models/ToonShooter/Characters/Character_Soldier.fbx";

    private const string TS = "Assets/Imported Models/ToonShooter/Environment/";
    private const string KF = "Assets/Imported Models/KayKit_Furniture/";
    private const string KP = "Assets/Imported Models/KayKit_Prototype/";

    private const int ObstacleLayer = 6;
    private const int PlayerLayer = 7;
    private const float WallHeight = 3f;
    private const float WallThickness = 0.4f;
    private const float LowWallHeight = 1f;

    private static readonly Dictionary<string, string> Models = new Dictionary<string, string>
    {
        { "crate", TS + "Crate.obj" },
        { "cardboard1", TS + "CardboardBoxes_1.obj" },
        { "cardboard2", TS + "CardboardBoxes_2.obj" },
        { "cardboard3", TS + "CardboardBoxes_3.obj" },
        { "cardboard4", TS + "CardboardBoxes_4.obj" },
        { "barrel_red", TS + "ExplodingBarrel.obj" },
        { "gastank", TS + "GasTank.obj" },
        { "pallet", TS + "Pallet.obj" },
        { "planks", TS + "WoodPlanks.obj" },
        { "sandbag", TS + "SackTrench.obj" },
        { "sandbag_small", TS + "SackTrench_Small.obj" },
        { "barrier", TS + "Barrier_Single.obj" },
        { "barrier_large", TS + "Barrier_Large.obj" },
        { "container", TS + "Container_Long.obj" },
        { "watertank", TS + "WaterTank_Floor.obj" },
        { "fence", TS + "MetalFence.obj" },
        { "pipes", TS + "Pipes.obj" },
        { "sign", TS + "Sign.obj" },
        { "cone", TS + "TrafficCone.obj" },
        { "papers1", TS + "Debris_Papers_1.obj" },
        { "papers2", TS + "Debris_Papers_2.obj" },
        { "table_long", KF + "table_medium_long.obj" },
        { "table", KF + "table_medium.obj" },
        { "table_small", KF + "table_small.obj" },
        { "chair_a", KF + "chair_A.obj" },
        { "chair_b", KF + "chair_B.obj" },
        { "cabinet", KF + "cabinet_medium_decorated.obj" },
        { "shelf_wall", KF + "shelf_B_large_decorated.obj" },
        { "books", KF + "book_set.obj" },
        { "lamp", KF + "lamp_table.obj" },
        { "rug", KF + "rug_rectangle_stripes_A.obj" },
        { "couch", KF + "couch.obj" },
        { "cactus", KF + "cactus_medium_A.obj" },
        { "barrel", KP + "Barrel_A.obj" },
        { "can", KP + "Can_A.obj" }
    };

    private static readonly string[] SoldierWeapons =
    {
        "GrenadeLauncher", "Knife_1", "Knife_2", "Pistol", "Revolver", "Revolver_Small", "RocketLauncher",
        "ShortCannon", "Shotgun", "Shovel", "SMG", "Sniper", "Sniper_2"
    };

    // Parent & state untuk scene yang sedang dibangun.
    private static Transform environmentRoot;
    private static Transform propsRoot;
    private static Transform lightsRoot;
    private static Transform gameplayRoot;
    private static Transform guardsRoot;
    private static Transform playerTransform;

    private static string meshAssetPath;
    private static bool meshAssetCreated;
    private static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();
    private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    private struct MissionDef
    {
        public string key;
        public string description;
        public Transform target;
        public bool optional;

        public MissionDef(string key, string description, Transform target, bool optional = false)
        {
            this.key = key;
            this.description = description;
            this.target = target;
            this.optional = optional;
        }
    }

    private struct GuardTuning
    {
        public float visionRange;
        public float visionAngle;
        public float sightGainRate;
        public float hearingSensitivity;

        public GuardTuning(float visionRange, float visionAngle, float sightGainRate, float hearingSensitivity)
        {
            this.visionRange = visionRange;
            this.visionAngle = visionAngle;
            this.sightGainRate = sightGainRate;
            this.hearingSensitivity = hearingSensitivity;
        }

        public static GuardTuning Default => new GuardTuning(10f, 100f, 1f, 1f);
    }

    // ==================================================
    // MENU
    // ==================================================

    [MenuItem("Stealth/Build Semua Level")]
    public static void BuildAllMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        BuildAll();
        EditorSceneManager.OpenScene(MainMenuPath);
    }

    [MenuItem("Stealth/Build Tutorial Saja")]
    public static void BuildTutorialMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        BuildTutorialOnly();
        EditorSceneManager.OpenScene(TutorialPath);
    }

    // Command line: -executeMethod StealthLevelBuilder.BuildTutorialOnly
    // Jangan panggil CreateGuardController/CreatePostFxProfile di sini: keduanya membuat ulang aset
    // (GUID baru) yang dipakai Menu, Level 1 & Level 2.
    public static void BuildTutorialOnly()
    {
        materials.Clear();
        BuildTutorial();
        UpdateBuildSettings();
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Stealth/Ambil Screenshot Level")]
    public static void CaptureMenu()
    {
        CaptureScreenshots(Path.Combine(Directory.GetCurrentDirectory(), "Screenshots"));
    }

    // Dipanggil dari command line: -executeMethod StealthLevelBuilder.BuildAll
    public static void BuildAll()
    {
        EnsureFolder("Assets/Scenes", "Levels");
        EnsureFolder("Assets/Materials", "Stealth");
        materials.Clear();

        CreateGuardController();
        CreatePostFxProfile();

        BuildMainMenu();
        BuildTutorial();
        BuildLevel1();
        BuildLevel2();
        UpdateBuildSettings();

        AssetDatabase.SaveAssets();
        Debug.Log("[StealthLevelBuilder] Menu, Tutorial, Level 1 & 2 selesai dibuat.");
    }

    // Command line: -executeMethod StealthLevelBuilder.CaptureFromCommandLine (folder = env STEALTH_SHOTS)
    public static void CaptureFromCommandLine()
    {
        string folder = System.Environment.GetEnvironmentVariable("STEALTH_SHOTS");

        if (string.IsNullOrEmpty(folder))
            folder = Path.Combine(Directory.GetCurrentDirectory(), "Screenshots");

        CaptureScreenshots(folder);
    }

    private static void UpdateBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(MainMenuPath, true),
            new EditorBuildSettingsScene(TutorialPath, true),
            new EditorBuildSettingsScene(Level1Path, true),
            new EditorBuildSettingsScene(Level2Path, true)
        };

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(AdaptivePath) != null)
            scenes.Add(new EditorBuildSettingsScene(AdaptivePath, true));

        foreach (string extra in new[] { "Assets/Scenes/MainScene.unity", "Assets/Scenes/P06_BehaviorTree.unity" })
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(extra) != null)
                scenes.Add(new EditorBuildSettingsScene(extra, false));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ==================================================
    // MATERIAL PALETTE
    // ==================================================

    private static Material MFloorTile => TexMat("Floor_Tile", "floor_tile", new Color(0.85f, 0.88f, 0.92f), new Vector2(2f, 2f));
    private static Material MFloorDark => TexMat("Floor_TileDark", "floor_tile", new Color(0.42f, 0.46f, 0.55f), new Vector2(2f, 2f));
    private static Material MFloorConcrete => TexMat("Floor_Concrete", "floor_concrete", new Color(0.8f, 0.8f, 0.82f), new Vector2(3f, 3f), 0.15f);
    private static Material MFloorLab => TexMat("Floor_Lab", "floor_lab", Color.white, new Vector2(2f, 2f), 0.45f);
    private static Material MFloorWood => TexMat("Floor_Wood", "floor_wood", Color.white, new Vector2(2f, 2f), 0.3f);
    private static Material MWall => TexMat("Wall_Panel", "wall_panel", new Color(0.82f, 0.85f, 0.9f), new Vector2(2f, 3f), 0.35f);
    private static Material MWallLab => TexMat("Wall_PanelLab", "wall_panel", new Color(1f, 1f, 1f), new Vector2(2f, 3f), 0.4f);
    private static Material MConcrete => TexMat("Pillar_Concrete", "floor_concrete", new Color(0.7f, 0.7f, 0.72f), new Vector2(1.5f, 1.5f), 0.1f);
    private static Material MHazard => TexMat("Hazard", "hazard", Color.white, new Vector2(1f, 1f), 0.3f);
    private static Material MVent => TexMat("Vent_Metal", "vent_metal", Color.white, new Vector2(1f, 1f), 0.5f);
    private static Material MServer => TexMat("Server_Rack", "server_rack", Color.white, new Vector2(1f, 2.2f), 0.5f, 1f, "server_rack_e", 1.5f);

    // ==================================================
    // MAIN MENU (diorama 3D)
    // ==================================================

    private static void BuildMainMenu()
    {
        BeginScene("MainMenu");
        SetupLighting(0.4f, new Color(0.2f, 0.22f, 0.28f), 0.02f);

        Floor("Floor", -10, -6, 10, 8, MFloorConcrete);
        Box("BackWall", new Vector3(0f, 2f, 7f), new Vector3(20f, 4f, 0.4f), MWall, environmentRoot, ObstacleLayer);
        Box("SideWall", new Vector3(-9f, 2f, 1f), new Vector3(0.4f, 4f, 12f), MWall, environmentRoot, ObstacleLayer);

        Decor("container", new Vector3(-3.5f, 0f, 5.2f), 0f, 1f);
        Decor("sandbag", new Vector3(2.6f, 0f, -0.2f), 0f, 1f);
        Decor("sandbag_small", new Vector3(5.2f, 0f, 0.6f), 60f, 1f);
        Decor("crate", new Vector3(-1.2f, 0f, 1.6f), 15f, 1.3f);
        Decor("crate", new Vector3(-1.1f, 1.03f, 1.6f), -10f, 1.1f);
        Decor("crate", new Vector3(-2.3f, 0f, 2.2f), 40f, 1.2f);
        Decor("barrel_red", new Vector3(5.6f, 0f, 3.6f), 0f, 1f);
        Decor("barrel_red", new Vector3(6.5f, 0f, 3.2f), 30f, 1f);
        Decor("gastank", new Vector3(7.8f, 0f, 5.2f), 0f, 1f);
        Decor("pallet", new Vector3(0.2f, 0f, 4.6f), 10f, 1f);
        Decor("cardboard4", new Vector3(0.3f, 0.19f, 4.6f), 0f, 1f);
        Decor("fence", new Vector3(8.5f, 0f, 1.5f), 90f, 1f);
        Decor("cone", new Vector3(4.2f, 0f, -2.2f), 0f, 1f);
        Decor("papers1", new Vector3(1.6f, 0.01f, -1.6f), 20f, 1f);

        // Prajurit berjaga di bawah lampu sorot.
        GameObject soldier = CreateSoldierModel(environmentRoot, out _);
        soldier.transform.position = new Vector3(4.4f, 0f, -0.9f);
        soldier.transform.rotation = Quaternion.Euler(0f, 215f, 0f);

        Light spot = new GameObject("Spotlight").AddComponent<Light>();
        spot.transform.SetParent(lightsRoot);
        spot.transform.SetPositionAndRotation(new Vector3(4.2f, 6f, -1.6f), Quaternion.Euler(78f, 10f, 0f));
        spot.type = LightType.Spot;
        spot.spotAngle = 55f;
        spot.innerSpotAngle = 30f;
        spot.range = 12f;
        spot.intensity = 90f;
        spot.color = new Color(1f, 0.85f, 0.6f);
        spot.shadows = LightShadows.Soft;

        PointLight(new Vector3(-4f, 2.6f, 3f), new Color(0.4f, 0.6f, 1f), 6f, 9f, false);
        PointLight(new Vector3(7f, 1.5f, 4f), new Color(1f, 0.35f, 0.2f), 4f, 6f, false);

        CreateSecurityCamera("SearchCam", new Vector3(-8.4f, 3.4f, 6.4f), 135f, 35f);

        Transform pivot = new GameObject("CameraPivot").transform;
        pivot.position = new Vector3(1.6f, 1.2f, 0.8f);

        GameObject camObject = CreateCamera(new Vector3(6.5f, 2.4f, -7.5f), Quaternion.identity);
        camObject.transform.LookAt(pivot);
        camObject.GetComponent<Camera>().fieldOfView = 42f;

        MenuCameraDrift drift = camObject.AddComponent<MenuCameraDrift>();
        SetProps(drift, ("pivot", pivot));

        CreatePostFxVolume();

        GameObject menu = new GameObject("MainMenu");
        MainMenuUI ui = menu.AddComponent<MainMenuUI>();

        object[,] levels =
        {
            {
                "Level0_Tutorial", "Simulasi Akademi Agen", 1,
                "Simulasi terakhir sebelum operasi pertamamu: keycard & pintu, obeng & ventilasi, dan menyelinap melewati penjaga latihan.\n\n1 penjaga latihan  -  1 kamera CCTV\nWajib bagi agen baru."
            },
            {
                "Level1_SecurityCheckpoint", "Pos Keamanan Sektor 7", 1,
                "Curi Keycard Merah, retas terminal server, lalu kabur lewat Gerbang Timur.\n\n3 prajurit  -  1 kamera CCTV\nCocok untuk belajar jongkok, lempar kaleng & panel listrik."
            },
            {
                "Level2_ResearchLab", "Laboratorium Riset Bawah Tanah", 2,
                "Cari obeng, merangkak lewat ventilasi, curi Prototipe X-7 dari brankas.\n\n5 prajurit  -  2 kamera CCTV\nKamera mengawasi jalur utama lab."
            },
            {
                AdaptiveSceneName, AdaptiveTitle, 3, AdaptiveDescription
            }
        };

        SerializedObject so = new SerializedObject(ui);
        SerializedProperty list = so.FindProperty("levels");
        list.arraySize = levels.GetLength(0);

        for (int i = 0; i < levels.GetLength(0); i++)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("sceneName").stringValue = (string)levels[i, 0];
            element.FindPropertyRelative("title").stringValue = (string)levels[i, 1];
            element.FindPropertyRelative("difficulty").intValue = (int)levels[i, 2];
            element.FindPropertyRelative("description").stringValue = (string)levels[i, 3];
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        SaveScene(MainMenuPath);
    }

    // ==================================================
    // LEVEL 0 - TUTORIAL (PELATIHAN AGEN)
    // ==================================================
    //
    //  z=32 +-----------------------+
    //       |  POS JAGA          X  |  X = titik ekstraksi
    //  <==  (lubang)  1 prajurit    |
    //  ^ z=20 +=====================+  dinding penuh: Pos Jaga hanya lewat ventilasi
    //  ^    |  GUDANG (obeng, kaleng)|
    //  <==  (lubang + penutup baut) |
    //  z=10 +-------------[pintu]---+
    //       |  AREA LATIHAN (start)  |
    //  z=0  +-----------------------+
    //      x=0                     x=18
    //
    // Gudang & Pos Jaga memakai koordinat yang sama dengan Gudang/Kepala Lab di Level 2,
    // sehingga BuildVentDuct() bisa dipakai tanpa diubah.

    private static void BuildTutorial()
    {
        BeginScene("Tutorial");

        // ---------- Lantai ----------
        Floor("Floor_Training", 0, 0, 18, 10, MFloorConcrete);
        Floor("Floor_Storage", 0, 10, 18, 20, MFloorTile);
        Floor("Floor_Post", 0, 20, 18, 32, MFloorDark);

        // ---------- Dinding ----------
        Material wall = MWall;
        Wall(0, 0, 18, 0, wall);
        Wall(0, 32, 18, 32, wall);
        Wall(18, 0, 18, 32, wall);
        Wall(0, 0, 0, 16.8f, wall);      // lubang ventilasi Gudang z 17.0..18.6
        Wall(0, 18.8f, 0, 25.8f, wall);  // lubang keluar Pos Jaga z 26.0..27.6
        Wall(0, 27.8f, 0, 32, wall);

        Wall(0, 10, 12, 10, wall);       // pintu 12..14: Area Latihan <-> Gudang
        Wall(14, 10, 18, 10, wall);
        Lintel(13, 10, true, wall);
        Wall(0, 20, 18, 20, wall);

        BuildVentDuct();

        // ---------- Area Latihan (start) ----------
        ModelProp("Sandbags", B(9f, 5f), new Vector3(4f, LowWallHeight, 0.6f), "sandbag");
        ModelProp("Desk", B(15.5f, 3f), new Vector3(2.5f, 0.9f, 1.2f), "table_long");
        Decor("crate", new Vector3(1.2f, 0f, 8.6f), 15f, 1.1f, true);
        Decor("cone", new Vector3(6.5f, 0f, 1f), 0f, 1f);
        Decor("sign", new Vector3(11.2f, 0f, 9.4f), 0f, 1f);

        // ---------- Gudang ----------
        ModelRow("Shelf", B(9f, 17f), new Vector3(5f, 2.2f, 0.8f), new[] { "cardboard4", "cardboard3" }, 4);
        ModelProp("Crate", B(15.5f, 18.5f), new Vector3(1.2f, 1f, 1.2f), "crate");
        Decor("pallet", new Vector3(16f, 0f, 11.4f), 0f, 1f);
        Decor("barrel_red", new Vector3(17.2f, 0f, 15f), 0f, 1f, true);

        // ---------- Pos Jaga: karung pasir melintang = tempat sembunyi & melempar kaleng ----------
        // Jongkok di baliknya (sisi barat) tertutup dari pandangan penjaga; lewat dari sini ke ekstraksi
        // terlihat penjaga, jadi lolos tanpa ketahuan butuh kaleng.
        ModelProp("Sandbags", B(8f, 27.5f), new Vector3(0.6f, LowWallHeight, 4f), "sandbag");
        Decor("barrier_large", new Vector3(17.4f, 0f, 27.5f), 90f, 0.85f);

        // ---------- Cahaya ----------
        SetupLighting(0.6f, new Color(0.3f, 0.32f, 0.38f), 0.008f);
        Color warm = new Color(1f, 0.9f, 0.75f);
        PointLight(new Vector3(9, 2.8f, 5), warm, 12f, 11f);
        PointLight(new Vector3(9, 2.8f, 15), new Color(1f, 0.85f, 0.6f), 8f, 10f);
        Light post1 = PointLight(new Vector3(5, 2.8f, 26), warm, 12f, 10f);
        PointLight(new Vector3(15, 2.8f, 27), warm, 12f, 7f);

        // Panel listrik hanya menggelapkan sisi barat; area penjaga tetap terang agar kaleng tetap dibutuhkan.
        LightZone postZone = Zone("LightZone_PosJaga", "Pos Jaga", new Vector3(5, 2, 26), new Vector3(10, 4, 12), post1);

        BakeNavMesh("Tutorial");

        // ---------- Objek gameplay ----------
        Transform door = CreateDoor("Door_Training", new Vector3(13, 0, 10), true,
            "keycard_red", "Keycard Merah", Mat("DoorRed", new Color(0.65f, 0.12f, 0.1f), 0f, 0.5f),
            "open:door_training", "Pintu Gudang");

        Transform keycard = CreatePickup("Keycard_Red", "keycard_red", "Keycard Merah", 1,
            new Vector3(15.5f, 0.9f, 3f), new Color(1f, 0.2f, 0.15f), PickupShape.Keycard, "pickup:keycard_red");

        Transform can = CreatePickup("Can", "can", "Kaleng", 2,
            new Vector3(8f, 0f, 12.5f), new Color(0.75f, 0.78f, 0.8f), PickupShape.Can, "pickup:can");

        Transform screwdriver = CreatePickup("Screwdriver", "screwdriver", "Obeng", 1,
            new Vector3(15.5f, 1f, 18.5f), new Color(1f, 0.85f, 0.1f), PickupShape.Screwdriver, "pickup:screwdriver");

        Transform vent = CreateVentCover("VentCover_Gudang", new Vector3(0.28f, 0f, 17.8f), 90f, "open:vent_tutorial");

        SecurityCamera postCam = CreateSecurityCamera("SecurityCam_PosJaga", new Vector3(17.6f, 2.8f, 20.4f), 315f, 35f);

        Transform powerBox = CreatePowerBox("PowerBox_PosJaga", new Vector3(0.45f, 1.3f, 23f), 90f,
            "Arena Ujian", new[] { postZone }, new[] { postCam }, 20f, "use:power_tutorial");

        Transform extraction = CreateExtraction("Extraction_Tutorial", new Vector3(16.3f, 1f, 30.3f));

        CreatePlayer(new Vector3(2f, 1f, 2f), 0f);

        // Pos jaga diam di samping ekstraksi, menghadap jalur dari ventilasi (barat) sambil menoleh pelan.
        // Pendengaran penuh agar kaleng yang jatuh ~4 m darinya cukup untuk memancingnya.
        CreateGuard("Guard_Latihan", new Vector3(15.5f, 0, 26.5f), 10.5f, true,
            new GuardTuning(8f, 90f, 0.7f, 1f),
            WP(15.5f, 26.5f, 270));

        // ---------- Instruksi pelatihan (sekali muncul, menjeda game) ----------
        // Area = kotak pemicu (y tengah 1 = tinggi transform Player). Area nol = cukup membawa item.
        CreateTip("Tip_GerakSenyap", new Vector3(9f, 1f, 3.5f), new Vector3(6f, 4f, 5f), null, "Gerak Senyap",
            "Berjalan menimbulkan suara kecil, berlari (" + K("SHIFT") + ") jauh lebih berisik - lingkaran putih di lantai menunjukkan jangkauan suaramu. " +
            "Tekan " + K("CTRL") + " untuk jongkok: langkahmu nyaris tanpa suara dan kamu bisa bersembunyi di balik cover rendah seperti karung pasir ini.");

        CreateTip("Tip_Keycard", Vector3.zero, Vector3.zero, "keycard_red", "Keycard",
            "Keycard Merah masuk ke inventory (kiri bawah). Keycard membuka pintu dengan lampu indikator merah.");

        CreateTip("Tip_Pintu", new Vector3(13f, 1f, 9.2f), new Vector3(2f, 4f, 1.2f), "keycard_red", "Pintu Berkunci",
            "Lampu merah di atas pintu berarti pintu ini butuh keycard. Kamu membawa Keycard Merah - berdiri di depan pintu dan tekan " + K("E") + ". " +
            "Hati-hati: pintu yang terbuka berbunyi pelan dan bisa terdengar penjaga di dekatnya.");

        CreateTip("Tip_Kaleng", Vector3.zero, Vector3.zero, "can", "Kaleng Pengalih",
            "Kaleng dipakai untuk membuat suara pengalih: penjaga yang mendengarnya akan datang memeriksa. " +
            "Simpan dulu, kamu bisa menggunakannya di Arena Ujian.");

        CreateTip("Tip_Obeng", Vector3.zero, Vector3.zero, "screwdriver", "Obeng",
            "Obeng membuka penutup ventilasi yang dibaut. Cari penutup ventilasi di dinding barat gudang ini.");

        CreateTip("Tip_Ventilasi", new Vector3(0.8f, 1f, 17.8f), new Vector3(1.2f, 4f, 1.6f), "screwdriver", "Ventilasi",
            K("TAHAN E") + " sampai bar terisi penuh dan baut terlepas. " +
            "Saluran ventilasi terlalu rendah untuk berdiri: tekan " + K("CTRL") + " untuk jongkok, lalu coba masuk.");

        CreateTip("Tip_DalamVentilasi", new Vector3(-1.1f, 1f, 22.3f), new Vector3(1.6f, 4f, 8f), null, "Di Dalam Ventilasi",
            "Dinding saluran menyembunyikanmu, dan penjaga tidak bisa mengikutimu ke sini. Kamu baru bisa berdiri setelah keluar. " +
            "Di ujung saluran ada Arena Ujian, tempat penjaga latihan berjaga.");

        CreateTip("Tip_Penjaga", new Vector3(1.8f, 1f, 26.8f), new Vector3(3.6f, 4f, 3.4f), null, "Penjaga Latihan",
            "Senter penjaga menunjukkan arah pandangnya, bar di atas kepalanya adalah tingkat curiga: " +
            "<color=#ffd36a>?</color> curiga & memeriksa, <color=#ff9426>??</color> mencarimu, <color=#ff4d47>!</color> melihatmu & mengejar. " +
            "Penjaga ini berjaga di samping titik ekstraksi dan mengawasi jalurmu. Ia juga mendengar langkahmu. Tertangkap = simulasi gagal.");

        CreateTip("Tip_LemparKaleng", new Vector3(10f, 1f, 27.5f), new Vector3(12f, 4f, 9f), "can", "Melempar Kaleng",
            "Sembunyi di balik karung pasir, lalu " + K("KLIK KIRI") + " untuk melempar kaleng ke arah kursor mouse. " +
            "Lempar ke dekat penjaga tapi jauh dari jalurmu: ia akan menoleh atau datang memeriksa bunyinya. Saat itulah menyelinap ke titik ekstraksi. " +
            "Lolos tanpa sekali pun ketahuan = peringkat HANTU.");

        CreateTip("Tip_PanelListrik", new Vector3(1.5f, 1f, 23f), new Vector3(3f, 4f, 2.6f), null, "Panel Listrik & CCTV",
            "Kamera CCTV di sudut arena memanggil penjaga jika melihatmu. Tekan " + K("E") + " pada panel ini untuk memadamkan kamera & lampu sisi barat arena selama 20 detik. " +
            "Di tempat gelap kamu sulit terlihat, tapi area di dekat penjaga tetap terang - untuk melewatinya kamu tetap butuh kaleng.");

        CreateGameSystems(
            "PELATIHAN - Simulasi Akademi Agen",
            "Agen baru, ini simulasi terakhir sebelum operasi pertamamu. Instruktur menyiapkan pintu berkunci, ventilasi, dan penjaga latihan.\n" +
            "Perhatikan senter penjaga: kuning = curiga, oranye = mencari, merah = kamu ketahuan!",
            "Level1_SecurityCheckpoint",
            new MissionDef("pickup:keycard_red", "Latihan 1: Ambil Keycard Merah dari meja instruktur (E)", keycard),
            new MissionDef("open:door_training", "Latihan 1: Buka pintu gudang memakai keycard", door),
            new MissionDef("pickup:can", "Latihan 2: Ambil kaleng - klik kiri untuk melempar pengalih", can),
            new MissionDef("pickup:screwdriver", "Latihan 2: Ambil obeng dari peti perlengkapan", screwdriver),
            new MissionDef("open:vent_tutorial", "Latihan 3: Buka baut ventilasi (tahan E)", vent),
            new MissionDef(MissionManager.ExtractKey, "Ujian akhir: jongkok (Ctrl) lewat ventilasi, lolos dari penjaga, capai titik ekstraksi", extraction),
            new MissionDef("use:power_tutorial", "Bonus: padamkan lampu & kamera dari panel listrik", powerBox, true));

        SaveScene(TutorialPath);
    }

    // ==================================================
    // LEVEL 1 - POS KEAMANAN SEKTOR 7
    // ==================================================
    //
    //  z=28 +-----------+-----------+-----------+
    //       | RUANG JAGA|  GUDANG   |  SERVER   |
    //  z=16 +--  ---+---+--  -------+--[pintu]--+
    //       | DOK   |        AULA                |
    //  z=0  +-------+---------------------------+  (Gerbang Timur di pojok kanan bawah)
    //      x=0     x=10                        x=36

    private static void BuildLevel1()
    {
        BeginScene("Level1");

        // ---------- Lantai ----------
        Floor("Floor_Dock", 0, 0, 10, 16, MFloorConcrete);
        Floor("Floor_Hall", 10, 0, 36, 16, MFloorTile);
        Floor("Floor_Office", 0, 16, 12, 28, MFloorWood);
        Floor("Floor_Storage", 12, 16, 24, 28, MFloorConcrete);
        Floor("Floor_Server", 24, 16, 36, 28, MFloorDark);

        // ---------- Dinding ----------
        Material wall = MWall;
        Wall(0, 0, 36, 0, wall);
        Wall(0, 28, 36, 28, wall);
        Wall(0, 0, 0, 28, wall);
        Wall(36, 0, 36, 28, wall);

        Wall(0, 16, 4, 16, wall);       // celah 4..6  : Dok <-> Ruang Jaga
        Wall(6, 16, 16, 16, wall);      // celah 16..18: Aula <-> Gudang
        Wall(18, 16, 30, 16, wall);     // pintu 30..32: Aula <-> Server
        Wall(32, 16, 36, 16, wall);
        Lintel(31, 16, true, wall);

        Wall(10, 0, 10, 6, wall);       // celah 6..8: Dok <-> Aula
        Wall(10, 8, 10, 16, wall);
        Wall(12, 16, 12, 22, wall);     // celah 22..24: Ruang Jaga <-> Gudang
        Wall(12, 24, 12, 28, wall);
        Wall(24, 16, 24, 28, wall);

        // ---------- Dok (start) ----------
        ModelProp("Crate", B(7.5f, 3f), new Vector3(1.5f, 1.2f, 1.5f), "crate");
        ModelProp("Crate", B(2.5f, 8f), new Vector3(1.5f, 1.2f, 1.5f), "crate");
        ModelProp("Boxes", B(7f, 11.5f), new Vector3(2f, 1.2f, 1.5f), "cardboard2");
        ModelProp("Crate", B(3f, 13.5f), new Vector3(1.5f, 1.2f, 1.5f), "crate");
        Decor("barrel_red", new Vector3(0.75f, 0f, 4.6f), 0f, 1f, true);
        Decor("barrel_red", new Vector3(0.75f, 0f, 5.5f), 40f, 1f, true);
        Decor("gastank", new Vector3(9.2f, 0f, 14.9f), 0f, 1f, true);
        Decor("pallet", new Vector3(4.8f, 0f, 0.95f), 0f, 1f);
        Decor("planks", new Vector3(4.8f, 0.19f, 0.95f), 90f, 1f);
        Decor("papers2", new Vector3(5.5f, 0.01f, 6.5f), 30f, 1f);

        // ---------- Aula: karung pasir (cover rendah) & pilar ----------
        ModelProp("Sandbags", B(15f, 5f), new Vector3(4f, LowWallHeight, 0.6f), "sandbag");
        ModelProp("Sandbags", B(20f, 10.5f), new Vector3(0.6f, LowWallHeight, 4f), "sandbag");
        ModelProp("Sandbags", B(25.5f, 4f), new Vector3(4f, LowWallHeight, 0.6f), "sandbag");
        ModelProp("Sandbags", B(30f, 11f), new Vector3(3f, LowWallHeight, 0.6f), "sandbag_small");
        ModelProp("Sandbags", B(31.5f, 4.5f), new Vector3(0.6f, LowWallHeight, 3f), "sandbag_small");
        ModelProp("Crate", B(14f, 12f), new Vector3(1.2f, 1.2f, 1.2f), "crate");
        ModelProp("Crate", B(34.2f, 14.2f), new Vector3(1.2f, 1.2f, 1.2f), "crate");
        Solid("Pillar", B(17.5f, 8.5f), new Vector3(1f, WallHeight, 1f), MConcrete);
        Solid("Pillar", B(27.5f, 8.5f), new Vector3(1f, WallHeight, 1f), MConcrete);
        Decor("barrel_red", new Vector3(10.9f, 0f, 0.9f), 0f, 1f, true);
        Decor("barrel_red", new Vector3(10.9f, 0f, 15.1f), 0f, 1f, true);
        Decor("cone", new Vector3(33.4f, 0f, 0.8f), 0f, 1f);
        Decor("cone", new Vector3(35.2f, 0f, 3.8f), 0f, 1f);
        Decor("sign", new Vector3(34.9f, 0f, 4.6f), 270f, 1f);
        Decor("barrier_large", new Vector3(35.6f, 0f, 2f), 90f, 0.85f);
        Decor("papers1", new Vector3(23f, 0.01f, 8f), 60f, 1f);

        // ---------- Ruang Jaga ----------
        ModelProp("Desk", B(3f, 25f), new Vector3(3f, 0.9f, 1.2f), "table_long");
        ModelProp("Desk", B(8.5f, 20f), new Vector3(2.5f, 0.9f, 1.2f), "table_long");
        ModelProp("Lockers", B(0.6f, 19f), new Vector3(0.8f, 2.2f, 2f), "cabinet");
        ModelProp("Lockers", B(0.6f, 21f), new Vector3(0.8f, 2.2f, 2f), "cabinet");
        ModelProp("Shelf", B(10.5f, 26.8f), new Vector3(2f, 2.2f, 0.8f), "cabinet");
        Decor("rug", new Vector3(6f, 0.005f, 22.6f), 0f, 0.9f);
        Decor("chair_a", new Vector3(3f, 0f, 23.9f), 180f, 0.8f);
        Decor("chair_b", new Vector3(8.5f, 0f, 18.9f), 180f, 0.8f);
        Decor("lamp", new Vector3(4.1f, 0.9f, 25.1f), 0f, 0.35f);
        Decor("books", new Vector3(1.9f, 0.9f, 25f), 20f, 0.6f);
        Decor("cactus", new Vector3(11.3f, 0f, 17f), 0f, 0.8f);

        // ---------- Gudang ----------
        ModelRow("Shelf", B(15.5f, 21f), new Vector3(0.8f, 2.2f, 5f), new[] { "cardboard3", "cardboard4" }, 4);
        ModelRow("Shelf", B(18.5f, 23f), new Vector3(0.8f, 2.2f, 5f), new[] { "cardboard4", "cardboard3" }, 4);
        ModelRow("Shelf", B(21.5f, 21f), new Vector3(0.8f, 2.2f, 5f), new[] { "cardboard3", "cardboard4" }, 4);
        Decor("pallet", new Vector3(13.6f, 0f, 19.5f), 0f, 1f);
        Decor("barrel", new Vector3(23.2f, 0f, 17f), 0f, 0.8f, true);
        Decor("gastank", new Vector3(13.2f, 0f, 17.2f), 0f, 1f, true);

        // ---------- Ruang Server ----------
        Solid("ServerRack", B(27f, 23f), new Vector3(0.8f, 2.2f, 6f), MServer);
        Solid("ServerRack", B(34.5f, 21.5f), new Vector3(0.8f, 2.2f, 5f), MServer);
        Decor("cardboard1", new Vector3(25f, 0f, 27.2f), 0f, 1f, true);

        // ---------- Cahaya ----------
        SetupLighting(0.6f, new Color(0.3f, 0.32f, 0.38f), 0.008f);
        Color warm = new Color(1f, 0.9f, 0.75f);
        Light hall1 = PointLight(new Vector3(16, 2.8f, 4), warm, 12f, 11f);
        Light hall2 = PointLight(new Vector3(16, 2.8f, 12), warm, 12f, 11f);
        Light hall3 = PointLight(new Vector3(29, 2.8f, 4), warm, 12f, 11f);
        Light hall4 = PointLight(new Vector3(29, 2.8f, 12), warm, 12f, 11f);
        Light office = PointLight(new Vector3(6, 2.8f, 22), warm, 12f, 10f);
        PointLight(new Vector3(5, 2.8f, 8), new Color(0.8f, 0.85f, 1f), 8f, 10f);
        PointLight(new Vector3(18, 2.8f, 22), new Color(1f, 0.85f, 0.6f), 6.4f, 9f);
        PointLight(new Vector3(30, 2.8f, 22), new Color(0.5f, 0.7f, 1f), 10f, 10f);

        LightZone hallZone = Zone("LightZone_Aula", "Aula", new Vector3(23, 2, 8), new Vector3(26, 4, 16), hall1, hall2, hall3, hall4);
        LightZone officeZone = Zone("LightZone_RuangJaga", "Ruang Jaga", new Vector3(6, 2, 22), new Vector3(12, 4, 12), office);

        // Terminal punya meja (collider) -> dibuat sebelum bake agar penjaga memutarinya.
        Transform terminal = CreateTerminal("Terminal_Server", new Vector3(31f, 0, 27f), 180f, "Unduh data patroli", 3f, "use:terminal_server");

        BakeNavMesh("Level1");

        // ---------- Objek gameplay ----------
        Transform doorServer = CreateDoor("Door_Server", new Vector3(31, 0, 16), true,
            "keycard_red", "Keycard Merah", Mat("DoorRed", new Color(0.65f, 0.12f, 0.1f), 0f, 0.5f),
            "open:door_server", "Pintu Ruang Server");

        Transform keycard = CreatePickup("Keycard_Red", "keycard_red", "Keycard Merah", 1,
            new Vector3(3f, 0.9f, 25f), new Color(1f, 0.2f, 0.15f), PickupShape.Keycard, "pickup:keycard_red");

        Transform can1 = CreatePickup("Can", "can", "Kaleng", 2,
            new Vector3(8.5f, 0f, 1.2f), new Color(0.75f, 0.78f, 0.8f), PickupShape.Can, "pickup:can");
        CreatePickup("Can", "can", "Kaleng", 2,
            new Vector3(13.5f, 0f, 26.8f), new Color(0.75f, 0.78f, 0.8f), PickupShape.Can, "pickup:can");

        SecurityCamera hallCam = CreateSecurityCamera("SecurityCam_Aula", new Vector3(35.3f, 2.8f, 15.3f), 225f, 45f);

        Transform powerBox = CreatePowerBox("PowerBox_Aula", new Vector3(20f, 1.3f, 27.65f), 180f,
            "Aula & Ruang Jaga", new[] { hallZone, officeZone }, new[] { hallCam }, 25f, "use:power_hall");

        Transform extraction = CreateExtraction("Extraction_GerbangTimur", new Vector3(34.3f, 1f, 1.8f));

        CreatePlayer(new Vector3(2f, 1f, 2f), 0f);

        CreateGuard("Guard_Aula", new Vector3(12.5f, 0, 13.5f), 1f, false,
            WP(12.5f, 13.5f, 90), WP(33f, 13f, 180), WP(33f, 2.5f, 270), WP(22f, 2f, 270), WP(12.5f, 2.5f, 0));
        CreateGuard("Guard_RuangJaga", new Vector3(6f, 0, 19.5f), 3f, true,
            WP(6f, 19.5f, 0), WP(8f, 25.5f, 270));
        CreateGuard("Guard_Gerbang", new Vector3(29f, 0, 7f), 3f, true,
            WP(29f, 7f, 90), WP(34f, 9.5f, 0));

        CreateGameSystems(
            "LEVEL 1 - Pos Keamanan Sektor 7",
            "Susupi pos keamanan militer, curi data patroli dari Ruang Server, lalu kabur lewat Gerbang Timur.\n" +
            "Jongkok (Ctrl) di balik karung pasir, lempar kaleng untuk mengalihkan prajurit. Jangan sampai tertangkap!",
            "Level2_ResearchLab",
            new MissionDef("pickup:keycard_red", "Ambil Keycard Merah di Ruang Jaga", keycard),
            new MissionDef("open:door_server", "Buka pintu Ruang Server", doorServer),
            new MissionDef("use:terminal_server", "Unduh data patroli di terminal (tahan E)", terminal),
            new MissionDef(MissionManager.ExtractKey, "Kabur ke Gerbang Timur", extraction),
            new MissionDef("pickup:can", "Ambil kaleng untuk pengalih perhatian", can1, true),
            new MissionDef("use:power_hall", "Matikan listrik Aula dari panel di Gudang", powerBox, true));

        SaveScene(Level1Path);
    }

    // ==================================================
    // LEVEL 2 - LABORATORIUM RISET BAWAH TANAH
    // ==================================================
    //
    //       z=32 +-----------+---------------+-----------+
    //            | KEPALA LAB|   LAB UTAMA   |  BRANKAS  |
    //   +--+  <==  (lubang)  [pintu]        [pintu]      |
    //   |^ |     |           |               |           |
    //   |^ | z=20+-----------+---  ----------+-----------+
    //   |^ |     | GUDANG    |   KORIDOR                 |
    //   +--+  <==  (lubang + penutup baut)               |
    //  vent U    |--  ---    +                           |
    //  (di luar  | LOBI      |                         X |  X = Lift Darurat
    //   map) z=0 +-----------+---------------------------+
    //           x=0         x=14                        x=40

    private static void BuildLevel2()
    {
        BeginScene("Level2");

        // ---------- Lantai ----------
        Floor("Floor_Lobby", 0, 0, 14, 10, MFloorTile);
        Floor("Floor_Storage", 0, 10, 14, 20, MFloorConcrete);
        Floor("Floor_Corridor", 14, 0, 40, 20, MFloorLab);
        Floor("Floor_HeadLab", 0, 20, 12, 32, MFloorWood);
        Floor("Floor_MainLab", 12, 20, 28, 32, MFloorLab);
        Floor("Floor_Vault", 28, 20, 40, 32, MFloorDark);

        // ---------- Dinding ----------
        Material wall = MWallLab;
        Wall(0, 0, 40, 0, wall);
        Wall(0, 32, 40, 32, wall);
        Wall(0, 0, 0, 16.8f, wall);      // dinding kiri: lubang ventilasi Gudang z 17.0..18.6
        Wall(0, 18.8f, 0, 25.8f, wall);  //               lubang ventilasi Kepala Lab z 26.0..27.6
        Wall(0, 27.8f, 0, 32, wall);
        Wall(40, 0, 40, 32, wall);

        Wall(0, 10, 6, 10, wall);        // celah 6..8: Lobi <-> Gudang
        Wall(8, 10, 14, 10, wall);
        Wall(14, 0, 14, 4, wall);        // celah 4..6  : Lobi <-> Koridor
        Wall(14, 6, 14, 14, wall);       // celah 14..16: Gudang <-> Koridor
        Wall(14, 16, 14, 20, wall);

        Wall(0, 20, 18, 20, wall);
        Wall(20, 20, 40, 20, wall);      // celah 18..20: Koridor <-> Lab Utama

        Wall(12, 20, 12, 26, wall);      // pintu 26..28: Kepala Lab <-> Lab Utama
        Wall(12, 28, 12, 32, wall);
        Lintel(27, 12, false, wall);
        Wall(28, 20, 28, 24, wall);      // pintu 24..26: Lab Utama <-> Brankas
        Wall(28, 26, 28, 32, wall);
        Lintel(25, 28, false, wall);

        BuildVentDuct();

        // ---------- Lobi ----------
        ModelProp("Sandbags", B(4f, 3.5f), new Vector3(3f, LowWallHeight, 0.6f), "sandbag");
        ModelProp("Reception", B(10f, 6f), new Vector3(4f, LowWallHeight, 1f), "table_long");
        Solid("Lift", B(3f, 0.3f), new Vector3(2.4f, 2.6f, 0.2f), Mat("Metal", new Color(0.6f, 0.62f, 0.65f), 0f, 0.7f), false);
        Decor("couch", new Vector3(8.5f, 0f, 9.1f), 180f, 0.7f, true);
        Decor("cactus", new Vector3(12.9f, 0f, 9.1f), 0f, 0.8f);
        Decor("rug", new Vector3(8.5f, 0.005f, 7.9f), 0f, 0.9f);
        Decor("sign", new Vector3(5.3f, 0f, 0.7f), 0f, 1f);

        // ---------- Gudang ----------
        ModelRow("Shelf", B(5f, 14f), new Vector3(5f, 2.2f, 0.8f), new[] { "cardboard4", "cardboard3" }, 4);
        ModelRow("Shelf", B(9.5f, 17f), new Vector3(0.8f, 2.2f, 4f), new[] { "cardboard3", "cardboard4" }, 3);
        ModelProp("Crate", B(11.5f, 18.5f), new Vector3(1.2f, 1f, 1.2f), "crate");
        ModelProp("Crate", B(12.8f, 11.2f), new Vector3(1.2f, 1.2f, 1.2f), "crate");
        Decor("barrel_red", new Vector3(13.2f, 0f, 13f), 0f, 1f, true);
        Decor("gastank", new Vector3(7.6f, 0f, 19.2f), 0f, 0.9f, true);

        // ---------- Koridor ----------
        ModelProp("LabBench", B(20f, 6f), new Vector3(4f, LowWallHeight, 1f), "table_long");
        ModelProp("LabBench", B(26f, 12f), new Vector3(1f, LowWallHeight, 4f), "table_long");
        ModelProp("LabBench", B(32f, 6f), new Vector3(4f, LowWallHeight, 1f), "table_long");
        ModelProp("LabBench", B(33f, 15f), new Vector3(4f, LowWallHeight, 1f), "table_long");
        ModelProp("LabBench", B(21f, 15.5f), new Vector3(3f, LowWallHeight, 1f), "table_long");
        Solid("Pillar", B(24f, 8f), new Vector3(1f, WallHeight, 1f), MConcrete);
        Solid("Pillar", B(36f, 10f), new Vector3(1f, WallHeight, 1f), MConcrete);
        Solid("Elevator", B(39.75f, 1.8f), new Vector3(0.15f, 2.6f, 2.6f), MHazard, false);
        Decor("pipes", new Vector3(30f, 0f, 19.3f), 90f, 0.9f, true);
        Decor("gastank", new Vector3(39.2f, 0f, 19.2f), 0f, 0.9f, true);
        Decor("cone", new Vector3(37.4f, 0f, 0.8f), 0f, 1f);
        Decor("papers2", new Vector3(28f, 0.01f, 3f), 0f, 1f);

        // ---------- Kepala Lab ----------
        ModelProp("Desk", B(7f, 29f), new Vector3(3f, 0.9f, 1.2f), "table_long");
        ModelProp("Bookshelf", B(11.2f, 23f), new Vector3(0.8f, 2.2f, 3f), "cabinet");
        ModelProp("Bookshelf", B(4f, 31.2f), new Vector3(3f, 2.2f, 0.8f), "cabinet");
        Decor("chair_a", new Vector3(7f, 0f, 30.4f), 0f, 0.8f);
        Decor("lamp", new Vector3(8.1f, 0.9f, 29.1f), 0f, 0.35f);
        Decor("books", new Vector3(5.9f, 0.9f, 29f), -15f, 0.6f);
        Decor("rug", new Vector3(9f, 0.005f, 23.5f), 90f, 0.7f);

        // ---------- Lab Utama ----------
        ModelProp("LabTable", B(16f, 24f), new Vector3(3f, LowWallHeight, 1.2f), "table_long");
        ModelProp("LabTable", B(16f, 29f), new Vector3(3f, LowWallHeight, 1.2f), "table_long");
        ModelProp("LabTable", B(22f, 24f), new Vector3(3f, LowWallHeight, 1.2f), "table_long");
        ModelProp("LabTable", B(22f, 29f), new Vector3(3f, LowWallHeight, 1.2f), "table_long");
        CreateSpecimenTank(new Vector3(19f, 0f, 26.5f));
        Decor("gastank", new Vector3(27.2f, 0f, 31.2f), 0f, 0.9f, true);

        // ---------- Brankas ----------
        Solid("Pedestal", B(34f, 27f), new Vector3(1f, 1f, 1f), Mat("Metal", Color.white));
        Solid("Safe", B(39.2f, 23f), new Vector3(1f, 2f, 3f), MServer);
        Solid("Safe", B(39.2f, 29f), new Vector3(1f, 2f, 3f), MServer);
        Decor("container", new Vector3(33f, 0f, 30.8f), 0f, 0.9f, true);
        Decor("crate", new Vector3(29.2f, 0f, 21f), 0f, 1.1f, true);

        // ---------- Cahaya ----------
        SetupLighting(0.55f, new Color(0.3f, 0.33f, 0.4f), 0.008f);
        Color cold = new Color(0.85f, 0.92f, 1f);
        Light b1 = PointLight(new Vector3(19, 2.8f, 5), cold, 12f, 11f);
        Light b2 = PointLight(new Vector3(19, 2.8f, 15), cold, 12f, 11f);
        Light b3 = PointLight(new Vector3(32, 2.8f, 5), cold, 12f, 11f);
        Light b4 = PointLight(new Vector3(32, 2.8f, 15), cold, 12f, 11f);
        Light e1 = PointLight(new Vector3(16, 2.8f, 26), cold, 12f, 10f);
        Light e2 = PointLight(new Vector3(24, 2.8f, 26), cold, 12f, 10f);
        PointLight(new Vector3(7, 2.8f, 5), new Color(1f, 0.92f, 0.8f), 10f, 10f);
        PointLight(new Vector3(7, 2.8f, 15), new Color(1f, 0.85f, 0.6f), 6f, 9f);
        PointLight(new Vector3(8, 2.8f, 27), new Color(1f, 0.8f, 0.55f), 7f, 9f);
        PointLight(new Vector3(34, 2.8f, 26), new Color(0.4f, 0.9f, 1f), 10f, 10f);

        LightZone corridorZone = Zone("LightZone_Koridor", "Koridor", new Vector3(27, 2, 10), new Vector3(26, 4, 20), b1, b2, b3, b4);
        LightZone labZone = Zone("LightZone_LabUtama", "Lab Utama", new Vector3(20, 2, 26), new Vector3(16, 4, 12), e1, e2);

        BakeNavMesh("Level2");

        // ---------- Objek gameplay ----------
        Material blueDoor = Mat("DoorBlue", new Color(0.12f, 0.28f, 0.7f), 0f, 0.5f);

        CreateDoor("Door_HeadLab", new Vector3(12, 0, 27), false,
            "keycard_blue", "Keycard Biru", blueDoor, "open:door_headlab", "Pintu Kepala Lab");

        Transform vaultDoor = CreateDoor("Door_Vault", new Vector3(28, 0, 25), false,
            "keycard_blue", "Keycard Biru", blueDoor, "open:door_vault", "Pintu Brankas");

        Transform vent = CreateVentCover("VentCover_Gudang", new Vector3(0.28f, 0f, 17.8f), 90f, "open:vent_storage");

        Transform screwdriver = CreatePickup("Screwdriver", "screwdriver", "Obeng", 1,
            new Vector3(11.5f, 1f, 18.5f), new Color(1f, 0.85f, 0.1f), PickupShape.Screwdriver, "pickup:screwdriver");

        Transform keycard = CreatePickup("Keycard_Blue", "keycard_blue", "Keycard Biru", 1,
            new Vector3(7f, 0.9f, 29f), new Color(0.2f, 0.5f, 1f), PickupShape.Keycard, "pickup:keycard_blue");

        Transform prototype = CreatePickup("Prototype", "prototype", "Prototipe X-7", 1,
            new Vector3(34f, 1f, 27f), new Color(0.2f, 1f, 0.9f), PickupShape.Prototype, "pickup:prototype");

        Transform can1 = CreatePickup("Can", "can", "Kaleng", 2,
            new Vector3(12.5f, 0f, 1.5f), new Color(0.75f, 0.78f, 0.8f), PickupShape.Can, "pickup:can");
        CreatePickup("Can", "can", "Kaleng", 1,
            new Vector3(2f, 0f, 11.5f), new Color(0.75f, 0.78f, 0.8f), PickupShape.Can, "pickup:can");

        SecurityCamera corridorCam = CreateSecurityCamera("SecurityCam_Koridor", new Vector3(21.5f, 2.8f, 19.4f), 180f, 50f);
        SecurityCamera labCam = CreateSecurityCamera("SecurityCam_Lab", new Vector3(12.7f, 2.8f, 31.3f), 135f, 40f);

        Transform powerBox = CreatePowerBox("PowerBox_Lab", new Vector3(0.45f, 1.3f, 12.5f), 90f,
            "Koridor & Lab", new[] { corridorZone, labZone }, new[] { corridorCam, labCam }, 20f, "use:power_lab");

        Transform extraction = CreateExtraction("Extraction_LiftDarurat", new Vector3(38.3f, 1f, 1.8f));

        CreatePlayer(new Vector3(2f, 1f, 1.8f), 0f);

        CreateGuard("Guard_Lobi", new Vector3(7f, 0, 7f), 1.5f, false,
            WP(7f, 7f, 0), WP(17f, 5f, 0), WP(17f, 15f, 270), WP(8f, 15f, 180));
        CreateGuard("Guard_Koridor", new Vector3(17.5f, 0, 17.5f), 1f, false,
            WP(17.5f, 17.5f, 90), WP(37.5f, 17.5f, 180), WP(37.5f, 9.5f, 270), WP(17.5f, 9.5f, 0));
        CreateGuard("Guard_Selatan", new Vector3(17f, 0, 2.5f), 2.5f, true,
            WP(17f, 2.5f, 90), WP(35f, 2.5f, 270));
        CreateGuard("Guard_LabUtama", new Vector3(14f, 0, 21.5f), 1f, false,
            WP(14f, 21.5f, 90), WP(25.5f, 21.5f, 0), WP(25.5f, 30.5f, 270), WP(14f, 30.5f, 180));

        // Penjaga brankas: pandangan lebih pendek & lambat curiga, patroli sampai koridor.
        CreateGuard("Guard_Brankas", new Vector3(26.5f, 0, 25f), 2.5f, true,
            new GuardTuning(7f, 75f, 0.55f, 0.75f),
            WP(26.5f, 25f, 270), WP(19f, 17.5f, 180));

        CreateGameSystems(
            "LEVEL 2 - Laboratorium Riset Bawah Tanah",
            "Prototipe X-7 disimpan di Brankas. Keycard Biru ada di Ruang Kepala Lab yang hanya bisa dicapai lewat ventilasi.\n" +
            "Cari obeng, merangkak lewat ventilasi, curi prototipe, lalu kabur lewat Lift Darurat. Awas kamera keamanan!",
            "",
            new MissionDef("pickup:screwdriver", "Cari obeng di Gudang", screwdriver),
            new MissionDef("open:vent_storage", "Buka baut ventilasi di Gudang (tahan E)", vent),
            new MissionDef("pickup:keycard_blue", "Merangkak lewat ventilasi (Ctrl) & ambil Keycard Biru", keycard),
            new MissionDef("open:door_vault", "Buka Pintu Brankas di ujung Lab Utama", vaultDoor),
            new MissionDef("pickup:prototype", "Curi Prototipe X-7", prototype),
            new MissionDef(MissionManager.ExtractKey, "Kabur lewat Lift Darurat", extraction),
            new MissionDef("use:power_lab", "Matikan lampu & kamera dari panel listrik Gudang", powerBox, true),
            new MissionDef("pickup:can", "Kumpulkan kaleng untuk pengalih perhatian", can1, true));

        SaveScene(Level2Path);
    }

    // Saluran ventilasi berbentuk U di LUAR dinding kiri map:
    // lubang di dinding kiri Gudang (z 17..18.6) -> keluar ke barat -> maju ke utara di luar map
    // -> masuk lagi lewat lubang di dinding kiri Ruang Kepala Lab (z 26..27.6).
    private static void BuildVentDuct()
    {
        const float ductH = 1.5f;
        const float zStart = 16.8f;
        const float zEnd = 27.8f;
        Material metal = MVent;
        Material wall = MWallLab;

        // Lintel di atas kedua lubang (lubang setinggi 1.3 m).
        Box("Vent_Lintel", new Vector3(0f, 2.15f, 17.8f), new Vector3(WallThickness, 1.7f, 1.6f), wall, environmentRoot, ObstacleLayer);
        Box("Vent_Lintel", new Vector3(0f, 2.15f, 26.8f), new Vector3(WallThickness, 1.7f, 1.6f), wall, environmentRoot, ObstacleLayer);

        // Lantai, dinding luar, dan tutup ujung saluran (x -2.0 .. -0.2).
        // Lantai & atap tidak ikut NavMesh agar penjaga tidak mencari jalan lewat ventilasi.
        Box("Duct_Floor", new Vector3(-1.1f, -0.1f, (zStart + zEnd) * 0.5f), new Vector3(2.2f, 0.2f, zEnd - zStart), metal, environmentRoot, 0)
            .AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        Box("Duct_Wall", new Vector3(-1.9f, ductH * 0.5f, (zStart + zEnd) * 0.5f), new Vector3(0.2f, ductH, zEnd - zStart), metal, environmentRoot, ObstacleLayer);
        Box("Duct_Wall", new Vector3(-1.1f, ductH * 0.5f, zStart + 0.1f), new Vector3(1.8f, ductH, 0.2f), metal, environmentRoot, ObstacleLayer);
        Box("Duct_Wall", new Vector3(-1.1f, ductH * 0.5f, zEnd - 0.1f), new Vector3(1.8f, ductH, 0.2f), metal, environmentRoot, ObstacleLayer);

        // Atap rendah semi-transparan (Player tetap terlihat dari kamera atas, tapi harus jongkok).
        Material glass = TransparentMat("Vent_Ceiling", new Color(0.55f, 0.62f, 0.7f, 0.35f));
        CeilingSlab(new Vector3(-1.1f, 1.4f, (zStart + zEnd) * 0.5f), new Vector3(2.0f, 0.2f, zEnd - zStart), glass);

        // Bingkai lubang keluar di Ruang Kepala Lab.
        Material frame = Mat("Metal", Color.white);
        Box("Vent_ExitFrame", new Vector3(0.22f, 1.35f, 26.8f), new Vector3(0.06f, 0.1f, 1.8f), frame, environmentRoot, 0, false);
        Box("Vent_ExitFrame", new Vector3(0.22f, 0.65f, 25.95f), new Vector3(0.06f, 1.3f, 0.1f), frame, environmentRoot, 0, false);
        Box("Vent_ExitFrame", new Vector3(0.22f, 0.65f, 27.65f), new Vector3(0.06f, 1.3f, 0.1f), frame, environmentRoot, 0, false);

        // Lampu redup di dalam saluran.
        PointLight(new Vector3(-1.1f, 1.1f, 22.3f), new Color(0.45f, 0.65f, 1f), 3f, 6f, false);
    }

    private static void CeilingSlab(Vector3 center, Vector3 size, Material material)
    {
        GameObject slab = Box("Duct_Ceiling", center, size, material, environmentRoot, ObstacleLayer);
        slab.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        slab.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
    }

    // ==================================================
    // SCENE
    // ==================================================

    private static void BeginScene(string levelName)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        environmentRoot = new GameObject("Environment").transform;
        propsRoot = new GameObject("Props").transform;
        lightsRoot = new GameObject("Lights").transform;
        gameplayRoot = new GameObject("Gameplay").transform;
        guardsRoot = new GameObject("Guards").transform;
        playerTransform = null;

        meshAssetPath = SceneFolder + "/Meshes_" + levelName + ".asset";
        AssetDatabase.DeleteAsset(meshAssetPath);
        meshAssetCreated = false;
        meshCache.Clear();
    }

    private static void SaveScene(string path)
    {
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);
        AssetDatabase.SaveAssets();
        Debug.Log("[StealthLevelBuilder] Scene disimpan: " + path);
    }

    private static void SetupLighting(float moonIntensity, Color ambient, float fogDensity)
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambient;
        RenderSettings.fog = fogDensity > 0f;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.fogColor = new Color(0.03f, 0.04f, 0.06f);

        GameObject moon = new GameObject("Moonlight");
        moon.transform.SetParent(lightsRoot);
        moon.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

        Light light = moon.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.55f, 0.65f, 0.9f);
        light.intensity = moonIntensity;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.8f;
    }

    private static void BakeNavMesh(string levelName)
    {
        GameObject navigation = new GameObject("Navigation");
        NavMeshSurface surface = navigation.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~((1 << PlayerLayer) | (1 << 2));
        surface.BuildNavMesh();

        string path = SceneFolder + "/NavMesh_" + levelName + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(surface.navMeshData, path);
    }

    private static void CreateGameSystems(string title, string briefing, string nextScene, params MissionDef[] steps)
    {
        GameObject systems = new GameObject("GameSystems");

        StealthGameManager game = systems.AddComponent<StealthGameManager>();
        SetProps(game, ("nextSceneName", nextScene));

        AlertManager alert = systems.AddComponent<AlertManager>();
        SetProps(alert, ("showHud", false), ("broadcastRadius", 25f));

        systems.AddComponent<StealthHUD>();

        MissionManager mission = systems.AddComponent<MissionManager>();
        SerializedObject so = new SerializedObject(mission);
        so.FindProperty("levelTitle").stringValue = title;
        so.FindProperty("briefing").stringValue = briefing;

        SerializedProperty list = so.FindProperty("steps");
        list.arraySize = steps.Length;

        for (int i = 0; i < steps.Length; i++)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("key").stringValue = steps[i].key;
            element.FindPropertyRelative("description").stringValue = steps[i].description;
            element.FindPropertyRelative("target").objectReferenceValue = steps[i].target;
            element.FindPropertyRelative("optional").boolValue = steps[i].optional;
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        CreatePostFxVolume();
    }

    // ==================================================
    // POST PROCESSING
    // ==================================================

    private static void CreatePostFxProfile()
    {
        AssetDatabase.DeleteAsset(PostFxPath);

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, PostFxPath);

        Tonemapping tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.ACES);

        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.95f);
        bloom.intensity.Override(0.8f);
        bloom.scatter.Override(0.7f);

        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.3f);
        vignette.smoothness.Override(0.45f);

        ColorAdjustments color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.35f);
        color.contrast.Override(14f);
        color.saturation.Override(12f);

        foreach (VolumeComponent component in profile.components)
        {
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
    }

    private static void CreatePostFxVolume()
    {
        GameObject go = new GameObject("PostProcessing");
        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostFxPath);
    }

    private static GameObject CreateCamera(Vector3 position, Quaternion rotation)
    {
        GameObject camObject = new GameObject("Main Camera");
        camObject.tag = "MainCamera";
        camObject.transform.SetPositionAndRotation(position, rotation);

        Camera cam = camObject.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.025f, 0.035f);
        cam.fieldOfView = 50f;
        cam.farClipPlane = 200f;
        camObject.AddComponent<AudioListener>();

        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        return camObject;
    }

    // ==================================================
    // PLAYER & GUARDS
    // ==================================================

    private static void CreatePlayer(Vector3 position, float yaw)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        player.tag = "Player";

        if (player.GetComponent<PlayerInventory>() == null)
            player.AddComponent<PlayerInventory>();

        if (player.GetComponent<PlayerInteractor>() == null)
            player.AddComponent<PlayerInteractor>();

        playerTransform = player.transform;

        Quaternion rotation = Quaternion.Euler(68f, 0f, 0f);
        GameObject camObject = CreateCamera(position - rotation * Vector3.forward * 19f, rotation);

        CameraFollow follow = camObject.AddComponent<CameraFollow>();
        SetProps(follow, ("target", player.transform), ("pitch", 68f), ("yaw", 0f), ("distance", 19f));
    }

    private static Vector4 WP(float x, float z, float yaw)
    {
        return new Vector4(x, 0f, z, yaw);
    }

    private static GameObject CreateGuard(string name, Vector3 position, float waitTime, bool isPost, params Vector4[] waypoints)
    {
        return CreateGuard(name, position, waitTime, isPost, GuardTuning.Default, waypoints);
    }

    private static GameObject CreateGuard(string name, Vector3 position, float waitTime, bool isPost, GuardTuning tuning, params Vector4[] waypoints)
    {
        GameObject guard = new GameObject(name);
        guard.transform.SetParent(guardsRoot);
        guard.transform.position = position + Vector3.up;
        guard.transform.rotation = Quaternion.Euler(0f, waypoints.Length > 0 ? waypoints[0].w : 0f, 0f);

        CapsuleCollider capsule = guard.AddComponent<CapsuleCollider>();
        capsule.height = 2f;
        capsule.radius = 0.4f;

        NavMeshAgent agent = guard.AddComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 2f;
        agent.baseOffset = 1f;
        agent.speed = 2f;
        agent.angularSpeed = 360f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 0.2f;

        // Model prajurit (Toon Shooter) dengan AK.
        GameObject model = CreateSoldierModel(guard.transform, out Animator animator);
        model.transform.localPosition = new Vector3(0f, -1f, 0f);
        model.transform.localRotation = Quaternion.identity;

        // Senter = penanda arah pandang.
        GameObject flash = new GameObject("Flashlight");
        flash.transform.SetParent(guard.transform, false);
        flash.transform.localPosition = new Vector3(0f, 0.5f, 0.35f);
        flash.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);

        Light flashlight = flash.AddComponent<Light>();
        flashlight.type = LightType.Spot;
        flashlight.spotAngle = Mathf.Min(tuning.visionAngle - 5f, 170f);
        flashlight.innerSpotAngle = flashlight.spotAngle * 0.6f;
        flashlight.range = tuning.visionRange + 1f;
        flashlight.intensity = 8f;
        flashlight.color = new Color(1f, 0.95f, 0.8f);
        flashlight.shadows = LightShadows.Soft;
        flashlight.shadowNearPlane = 0.3f;

        Transform waypointRoot = new GameObject("Waypoints_" + name).transform;
        waypointRoot.SetParent(guardsRoot);

        Transform[] points = new Transform[waypoints.Length];

        for (int i = 0; i < waypoints.Length; i++)
        {
            Transform point = new GameObject("WP" + i).transform;
            point.SetParent(waypointRoot);
            point.position = new Vector3(waypoints[i].x, 0f, waypoints[i].z);
            point.rotation = Quaternion.Euler(0f, waypoints[i].w, 0f);
            points[i] = point;
        }

        EnemyBTController controller = guard.AddComponent<EnemyBTController>();
        SetProps(controller,
            ("player", playerTransform),
            ("patrolPoints", points),
            ("visionRange", tuning.visionRange),
            ("visionAngle", tuning.visionAngle),
            ("sightGainRate", tuning.sightGainRate),
            ("hearingSensitivity", tuning.hearingSensitivity),
            ("eyeHeight", 0.6f),
            ("proximitySenseRadius", 1.5f),
            ("obstacleMask", 1 << ObstacleLayer),
            ("attackRange", 1.3f),
            ("attackCooldown", 1f),
            ("attackDamage", 100),
            ("lowHealthThreshold", 0),
            ("patrolSpeed", 1.8f),
            ("investigateSpeed", 1.5f),
            ("searchSpeed", 2.8f),
            ("chaseSpeed", 4.2f),
            ("patrolWaitTime", waitTime),
            ("useWaypointRotation", isPost),
            ("showActionName", false));

        GuardVisuals visuals = guard.AddComponent<GuardVisuals>();
        SetProps(visuals, ("animator", animator), ("flashlight", flashlight));
        return guard;
    }

    // Model prajurit: hanya AK yang aktif, tinggi ~1.85 m, telapak kaki di y = 0 (lokal).
    private static GameObject CreateSoldierModel(Transform parent, out Animator animator)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SoldierPath);
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        model.name = "Soldier";

        foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
        {
            if (System.Array.IndexOf(SoldierWeapons, child.name) >= 0)
                child.gameObject.SetActive(false);
        }

        // Ukur di origin, lalu skala ke tinggi 1.85 m dengan kaki di y = 0.
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        model.transform.localScale = Vector3.one;

        Bounds body = BodyBounds(model);
        float scale = body.size.y > 0.01f ? 1.85f / body.size.y : 1f;

        Transform holder = new GameObject("SoldierRig").transform;
        model.transform.SetParent(holder, false);
        model.transform.localScale = Vector3.one * scale;
        model.transform.localPosition = new Vector3(0f, -body.min.y * scale, 0f);
        holder.SetParent(parent, false);

        animator = model.GetComponent<Animator>();
        if (animator == null)
            animator = model.AddComponent<Animator>();

        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(GuardControllerPath);
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            smr.updateWhenOffscreen = true;

        return holder.gameObject;
    }

    private static Bounds BodyBounds(GameObject model)
    {
        Bounds bounds = new Bounds();
        bool first = true;

        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.gameObject.activeInHierarchy || r.name == "AK")
                continue;

            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }

        return bounds;
    }

    private static void CreateGuardController()
    {
        AssetDatabase.DeleteAsset(GuardControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(GuardControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = "MoveSpeed",
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 1f
        });

        AnimationClip idle = null;
        AnimationClip run = null;

        foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(SoldierPath))
        {
            if (!(sub is AnimationClip clip) || clip.name.StartsWith("__preview"))
                continue;

            if (clip.name == "Idle") idle = clip;
            if (clip.name == "Run_Gun") run = clip;
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState idleState = machine.AddState("Idle");
        idleState.motion = idle;

        AnimatorState moveState = machine.AddState("Move");
        moveState.motion = run;
        moveState.speedParameterActive = true;
        moveState.speedParameter = "MoveSpeed";

        machine.defaultState = idleState;

        AnimatorStateTransition toMove = idleState.AddTransition(moveState);
        toMove.hasExitTime = false;
        toMove.duration = 0.15f;
        toMove.AddCondition(AnimatorConditionMode.Greater, 0.2f, "Speed");

        AnimatorStateTransition toIdle = moveState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.2f;
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.12f, "Speed");

        EditorUtility.SetDirty(controller);

        if (idle == null || run == null)
            Debug.LogError("[StealthLevelBuilder] Clip Idle / Run_Gun tidak ditemukan di " + SoldierPath);
    }

    // ==================================================
    // GAMEPLAY OBJECTS
    // ==================================================

    private enum PickupShape
    {
        Keycard,
        Can,
        Screwdriver,
        Prototype
    }

    private static Transform CreatePickup(string name, string itemId, string displayName, int amount,
        Vector3 surfacePosition, Color color, PickupShape shape, string missionKey)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(gameplayRoot);
        root.transform.position = surfacePosition;

        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        visual.localPosition = Vector3.up * 0.3f;

        Material mat = Mat("Item_" + itemId, color, shape == PickupShape.Can ? 0f : 1.4f, 0.6f);

        switch (shape)
        {
            case PickupShape.Keycard:
                Primitive(PrimitiveType.Cube, visual, Vector3.zero, new Vector3(0.35f, 0.03f, 0.22f), mat, Quaternion.Euler(0f, 0f, 20f));
                break;

            case PickupShape.Can:
                for (int i = 0; i < amount; i++)
                {
                    GameObject can = SpawnModel("can", visual);
                    if (can == null) continue;
                    can.transform.localScale = Vector3.one * 0.45f;
                    can.transform.localPosition = new Vector3(i * 0.18f - (amount - 1) * 0.09f, -0.3f, 0f);
                }
                break;

            case PickupShape.Screwdriver:
                Primitive(PrimitiveType.Cylinder, visual, new Vector3(0f, 0f, -0.1f), new Vector3(0.07f, 0.09f, 0.07f), mat, Quaternion.Euler(90f, 0f, 0f));
                Primitive(PrimitiveType.Cylinder, visual, new Vector3(0f, 0f, 0.12f), new Vector3(0.025f, 0.13f, 0.025f), Mat("Metal", Color.white), Quaternion.Euler(90f, 0f, 0f));
                break;

            case PickupShape.Prototype:
                Primitive(PrimitiveType.Cube, visual, Vector3.zero, new Vector3(0.3f, 0.3f, 0.3f), mat, Quaternion.Euler(45f, 0f, 45f));
                break;
        }

        Light glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(root.transform, false);
        glow.transform.localPosition = Vector3.up * 0.6f;
        glow.type = LightType.Point;
        glow.color = color;
        glow.intensity = 1.5f;
        glow.range = 2.5f;
        glow.shadows = LightShadows.None;

        PickupItem pickup = root.AddComponent<PickupItem>();
        SetProps(pickup,
            ("missionKey", missionKey),
            ("itemId", itemId),
            ("displayName", displayName),
            ("amount", amount),
            ("color", color),
            ("visual", visual));

        return root.transform;
    }

    private static Transform CreateDoor(string name, Vector3 center, bool alongX, string keyId, string keyName,
        Material material, string missionKey, string doorName)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(gameplayRoot);
        root.transform.position = center;

        Vector3 size = alongX ? new Vector3(2f, 2.8f, 0.25f) : new Vector3(0.25f, 2.8f, 2f);
        GameObject panel = Box("Panel", center + Vector3.up * 1.4f, size, material, root.transform, ObstacleLayer, false);

        NavMeshObstacle obstacle = panel.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = size;
        obstacle.carving = true;

        // Garis hazard di bawah pintu (tanpa collider).
        Vector3 stripeSize = alongX ? new Vector3(2f, 0.25f, 0.27f) : new Vector3(0.27f, 0.25f, 2f);
        GameObject stripe = Box("Stripe", panel.transform.position + Vector3.down * 1.1f, stripeSize, MHazard, panel.transform, 0, false);
        Object.DestroyImmediate(stripe.GetComponent<BoxCollider>());

        GameObject indicator = Primitive(PrimitiveType.Cube, panel.transform, new Vector3(0f, 0.95f, 0f),
            alongX ? new Vector3(0.4f, 0.15f, 0.3f) : new Vector3(0.3f, 0.15f, 0.4f), Mat("Indicator", Color.white, 1.5f), Quaternion.identity);

        Door door = root.AddComponent<Door>();
        SetProps(door,
            ("missionKey", missionKey),
            ("doorName", doorName),
            ("requiredItemId", keyId),
            ("requiredItemName", keyName),
            ("panel", panel.transform),
            ("openOffset", alongX ? new Vector3(1.9f, 0f, 0f) : new Vector3(0f, 0f, 1.9f)),
            ("indicator", indicator.GetComponent<Renderer>()));

        return root.transform;
    }

    // yaw = arah hadap penutup (forward menghadap ke dalam ruangan / keluar dari lubang).
    private static Transform CreateVentCover(string name, Vector3 position, float yaw, string missionKey)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(gameplayRoot);
        root.transform.position = position + Vector3.up * 0.65f;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        GameObject cover = Box("Grate", root.transform.position, new Vector3(1.6f, 1.3f, 0.08f), MVent, root.transform, 0, false);
        cover.transform.localRotation = Quaternion.identity;

        Material frame = Mat("Metal", Color.white);
        Primitive(PrimitiveType.Cube, cover.transform, new Vector3(0f, 0.62f, 0.02f), new Vector3(1.7f, 0.08f, 0.1f), frame, Quaternion.identity);
        Primitive(PrimitiveType.Cube, cover.transform, new Vector3(0f, -0.62f, 0.02f), new Vector3(1.7f, 0.08f, 0.1f), frame, Quaternion.identity);
        Primitive(PrimitiveType.Cube, cover.transform, new Vector3(0.8f, 0f, 0.02f), new Vector3(0.08f, 1.3f, 0.1f), frame, Quaternion.identity);
        Primitive(PrimitiveType.Cube, cover.transform, new Vector3(-0.8f, 0f, 0.02f), new Vector3(0.08f, 1.3f, 0.1f), frame, Quaternion.identity);

        VentCover vent = root.AddComponent<VentCover>();
        SetProps(vent, ("missionKey", missionKey), ("cover", cover.transform));

        return root.transform;
    }

    private static Transform CreateTerminal(string name, Vector3 position, float yaw, string actionName, float duration, string missionKey)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(gameplayRoot);
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

        GameObject desk = ModelProp("Desk", new Vector3(position.x, 0f, position.z), new Vector3(1.6f, 0.9f, 0.8f), "table_small");
        desk.transform.SetParent(root.transform, true);

        GameObject screen = Primitive(PrimitiveType.Cube, root.transform, new Vector3(0f, 1.25f, 0.1f), new Vector3(1f, 0.6f, 0.08f), Mat("Screen", Color.white, 1.5f), Quaternion.identity);
        Primitive(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.97f, 0.15f), new Vector3(0.12f, 0.15f, 0.05f), Mat("Metal", Color.white), Quaternion.identity);

        HackTerminal terminal = root.AddComponent<HackTerminal>();
        SetProps(terminal,
            ("missionKey", missionKey),
            ("actionName", actionName),
            ("hackDuration", duration),
            ("screen", screen.GetComponent<Renderer>()));

        return root.transform;
    }

    private static Transform CreatePowerBox(string name, Vector3 position, float yaw, string areaName,
        LightZone[] zones, SecurityCamera[] cameras, float duration, string missionKey)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(gameplayRoot);
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

        Primitive(PrimitiveType.Cube, root.transform, Vector3.zero, new Vector3(0.7f, 1f, 0.25f), Mat("PowerBox", new Color(0.85f, 0.65f, 0.1f), 0f, 0.5f), Quaternion.identity);
        Primitive(PrimitiveType.Cube, root.transform, new Vector3(0f, -0.25f, 0.13f), new Vector3(0.5f, 0.12f, 0.02f), MHazard, Quaternion.identity);
        GameObject indicator = Primitive(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.3f, 0.14f), new Vector3(0.15f, 0.15f, 0.05f), Mat("Indicator", Color.white, 1.5f), Quaternion.identity);

        PowerBox box = root.AddComponent<PowerBox>();
        SetProps(box,
            ("missionKey", missionKey),
            ("areaName", areaName),
            ("zones", zones),
            ("cameras", cameras),
            ("outageDuration", duration),
            ("indicator", indicator.GetComponent<Renderer>()));

        return root.transform;
    }

    private static SecurityCamera CreateSecurityCamera(string name, Vector3 position, float yaw, float sweep)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(gameplayRoot);
        root.transform.position = position;

        Material camMat = Mat("CameraBody", new Color(0.12f, 0.12f, 0.14f), 0f, 0.6f);
        Primitive(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(0.15f, 0.3f, 0.15f), camMat, Quaternion.identity);

        Transform head = new GameObject("Head").transform;
        head.SetParent(root.transform, false);
        head.localRotation = Quaternion.Euler(35f, yaw, 0f);

        Primitive(PrimitiveType.Cube, head, Vector3.zero, new Vector3(0.25f, 0.25f, 0.45f), camMat, Quaternion.identity);
        Primitive(PrimitiveType.Cylinder, head, new Vector3(0f, 0f, 0.24f), new Vector3(0.14f, 0.02f, 0.14f), Mat("Indicator", Color.white, 1.5f), Quaternion.Euler(90f, 0f, 0f));

        Light spot = new GameObject("ViewCone").AddComponent<Light>();
        spot.transform.SetParent(head, false);
        spot.type = LightType.Spot;
        spot.spotAngle = 50f;
        spot.innerSpotAngle = 35f;
        spot.range = 13f;
        spot.intensity = 20f;
        spot.color = new Color(0.4f, 1f, 0.5f);
        spot.shadows = LightShadows.None;

        SecurityCamera cam = root.AddComponent<SecurityCamera>();
        SetProps(cam,
            ("head", head),
            ("spotLight", spot),
            ("sweepAngle", sweep),
            ("pitch", 35f),
            ("viewRange", 11f),
            ("viewAngle", 50f),
            ("obstacleMask", 1 << ObstacleLayer));

        return cam;
    }

    // areaSize nol = muncul saat item didapat, di mana pun Player berada.
    private static void CreateTip(string name, Vector3 center, Vector3 areaSize, string requiredItemId, string title, string body)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(gameplayRoot);
        root.transform.position = center;

        TrainingTip tip = root.AddComponent<TrainingTip>();
        SetProps(tip,
            ("title", title),
            ("body", body),
            ("requiredItemId", requiredItemId ?? ""),
            ("areaSize", areaSize));
    }

    // Nama tombol berwarna di teks instruksi.
    private static string K(string key)
    {
        return "<color=#ffd36a>" + key + "</color>";
    }

    private static Transform CreateExtraction(string name, Vector3 position)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(gameplayRoot);
        root.transform.position = position;

        GameObject disc = Primitive(PrimitiveType.Cylinder, root.transform, new Vector3(0f, -0.97f, 0f), new Vector3(2.6f, 0.02f, 2.6f), Mat("Extraction", new Color(0.2f, 1f, 0.4f), 1.5f), Quaternion.identity);
        Primitive(PrimitiveType.Cylinder, root.transform, new Vector3(0f, -0.955f, 0f), new Vector3(2.1f, 0.02f, 2.1f), Mat("ExtractionInner", new Color(0.05f, 0.12f, 0.08f)), Quaternion.identity);

        Light light = new GameObject("Beacon").AddComponent<Light>();
        light.transform.SetParent(root.transform, false);
        light.transform.localPosition = new Vector3(0f, 1f, 0f);
        light.type = LightType.Point;
        light.range = 5f;
        light.intensity = 2f;
        light.shadows = LightShadows.None;

        ExtractionZone zone = root.AddComponent<ExtractionZone>();
        SetProps(zone,
            ("size", new Vector3(2.8f, 4f, 2.8f)),
            ("beacon", disc.GetComponent<Renderer>()),
            ("beaconLight", light));

        return root.transform;
    }

    private static void CreateSpecimenTank(Vector3 basePosition)
    {
        GameObject root = new GameObject("SpecimenTank");
        root.transform.SetParent(propsRoot);
        root.transform.position = basePosition;
        root.layer = ObstacleLayer;

        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 1.25f, 0f);
        collider.size = new Vector3(1.5f, 2.5f, 1.5f);

        Material metal = Mat("Metal", Color.white);
        Primitive(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(1.5f, 0.15f, 1.5f), metal, Quaternion.identity);
        Primitive(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 2.4f, 0f), new Vector3(1.5f, 0.1f, 1.5f), metal, Quaternion.identity);
        Primitive(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 1.25f, 0f), new Vector3(1.3f, 1f, 1.3f), TransparentMat("TankGlass", new Color(0.3f, 0.9f, 0.85f, 0.45f), 0.8f), Quaternion.identity);
        Primitive(PrimitiveType.Capsule, root.transform, new Vector3(0f, 1.2f, 0f), new Vector3(0.45f, 0.5f, 0.45f), Mat("Specimen", new Color(0.4f, 1f, 0.6f), 1.2f), Quaternion.identity);

        Light glow = new GameObject("TankGlow").AddComponent<Light>();
        glow.transform.SetParent(root.transform, false);
        glow.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        glow.type = LightType.Point;
        glow.color = new Color(0.3f, 1f, 0.8f);
        glow.intensity = 3f;
        glow.range = 4f;
        glow.shadows = LightShadows.None;
    }

    // ==================================================
    // GEOMETRY HELPERS
    // ==================================================

    private static Vector3 B(float x, float z)
    {
        return new Vector3(x, 0f, z);
    }

    private static void Floor(string name, float x1, float z1, float x2, float z2, Material material)
    {
        Box(name, new Vector3((x1 + x2) * 0.5f, -0.1f, (z1 + z2) * 0.5f), new Vector3(x2 - x1, 0.2f, z2 - z1), material, environmentRoot, 0);
    }

    // Dinding tinggi lurus (sejajar sumbu X atau Z).
    private static void Wall(float x1, float z1, float x2, float z2, Material material)
    {
        Vector3 center = new Vector3((x1 + x2) * 0.5f, WallHeight * 0.5f, (z1 + z2) * 0.5f);
        Vector3 size = new Vector3(Mathf.Abs(x2 - x1) + WallThickness, WallHeight, Mathf.Abs(z2 - z1) + WallThickness);

        if (Mathf.Abs(x2 - x1) < 0.01f) size.x = WallThickness;
        if (Mathf.Abs(z2 - z1) < 0.01f) size.z = WallThickness;

        Box("Wall", center, size, material, environmentRoot, ObstacleLayer);
    }

    // Balok di atas pintu (2.8 m .. 3 m).
    private static void Lintel(float along, float fixedCoord, bool alongX, Material material)
    {
        Vector3 center = alongX ? new Vector3(along, 2.9f, fixedCoord) : new Vector3(fixedCoord, 2.9f, along);
        Vector3 size = alongX ? new Vector3(2f, 0.2f, WallThickness) : new Vector3(WallThickness, 0.2f, 2f);
        Box("Lintel", center, size, material, environmentRoot, ObstacleLayer);
    }

    // Kotak bertekstur yang berdiri di lantai.
    private static GameObject Solid(string name, Vector3 basePosition, Vector3 size, Material material, bool obstacle = true)
    {
        Vector3 center = new Vector3(basePosition.x, size.y * 0.5f, basePosition.z);
        return Box(name, center, size, material, propsRoot, obstacle ? ObstacleLayer : 0);
    }

    // Collider kotak + model 3D yang di-fit ke kotak tersebut (yaw < 0 = otomatis).
    private static GameObject ModelProp(string name, Vector3 basePosition, Vector3 size, string modelKey, float yaw = -1f)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(propsRoot);
        root.transform.position = new Vector3(basePosition.x, size.y * 0.5f, basePosition.z);
        root.layer = ObstacleLayer;

        root.AddComponent<BoxCollider>().size = size;
        GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic);

        GameObject model = SpawnModel(modelKey, null);

        if (model != null)
        {
            FitModel(model, new Vector3(basePosition.x, 0f, basePosition.z), size, yaw);
            model.transform.SetParent(root.transform, true);
        }

        return root;
    }

    // Satu collider panjang berisi beberapa model berjajar (misal rak tumpukan kardus).
    private static GameObject ModelRow(string name, Vector3 basePosition, Vector3 size, string[] keys, int count)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(propsRoot);
        root.transform.position = new Vector3(basePosition.x, size.y * 0.5f, basePosition.z);
        root.layer = ObstacleLayer;
        root.AddComponent<BoxCollider>().size = size;

        bool alongX = size.x >= size.z;
        float length = alongX ? size.x : size.z;
        float segment = length / count;

        for (int i = 0; i < count; i++)
        {
            float offset = -length * 0.5f + segment * (i + 0.5f);
            Vector3 center = basePosition + (alongX ? new Vector3(offset, 0f, 0f) : new Vector3(0f, 0f, offset));
            Vector3 segmentSize = alongX ? new Vector3(segment * 0.95f, size.y, size.z) : new Vector3(size.x, size.y, segment * 0.95f);

            GameObject model = SpawnModel(keys[i % keys.Length], null);

            if (model == null)
                continue;

            FitModel(model, center, segmentSize, -1f);
            model.transform.SetParent(root.transform, true);
        }

        return root;
    }

    // Model hiasan dengan skala seragam. collide = tambah collider kotak (dibuat sebelum bake NavMesh).
    private static GameObject Decor(string modelKey, Vector3 basePosition, float yaw, float scale, bool collide = false)
    {
        GameObject model = SpawnModel(modelKey, null);

        if (model == null)
            return null;

        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
        model.transform.localScale = Vector3.one * scale;

        Bounds bounds = RendererBounds(model);
        model.transform.position = new Vector3(basePosition.x - bounds.center.x, basePosition.y - bounds.min.y, basePosition.z - bounds.center.z);

        if (!collide)
        {
            model.transform.SetParent(propsRoot, true);
            return model;
        }

        Bounds placed = RendererBounds(model);
        GameObject root = new GameObject(modelKey);
        root.transform.SetParent(propsRoot);
        root.transform.position = placed.center;
        root.layer = ObstacleLayer;
        root.AddComponent<BoxCollider>().size = placed.size;
        model.transform.SetParent(root.transform, true);
        return root;
    }

    private static GameObject SpawnModel(string key, Transform parent)
    {
        if (!Models.TryGetValue(key, out string path))
        {
            Debug.LogError("[StealthLevelBuilder] Model tidak terdaftar: " + key);
            return null;
        }

        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (asset == null)
        {
            Debug.LogError("[StealthLevelBuilder] Model tidak ditemukan: " + path);
            return null;
        }

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(asset);

        if (parent != null)
            model.transform.SetParent(parent, false);

        return model;
    }

    // Skala & posisikan model agar mengisi kotak (alas di basePosition, ukuran = size).
    private static void FitModel(GameObject model, Vector3 basePosition, Vector3 size, float yaw)
    {
        Transform t = model.transform;
        t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        t.localScale = Vector3.one;

        Vector3 raw = RendererBounds(model).size;
        raw = new Vector3(Mathf.Max(raw.x, 0.01f), Mathf.Max(raw.y, 0.01f), Mathf.Max(raw.z, 0.01f));

        if (yaw < 0f)
        {
            // Samakan sisi terpanjang model dengan sisi terpanjang kotak.
            bool modelLongX = raw.x >= raw.z;
            bool boxLongX = size.x >= size.z;
            yaw = modelLongX == boxLongX ? 0f : 90f;
        }

        bool rotated = Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(yaw, 270f)) < 1f;
        Vector3 worldExtent = rotated ? new Vector3(raw.z, raw.y, raw.x) : raw;
        Vector3 worldScale = new Vector3(size.x / worldExtent.x, size.y / worldExtent.y, size.z / worldExtent.z);

        t.localScale = rotated ? new Vector3(worldScale.z, worldScale.y, worldScale.x) : worldScale;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);

        Bounds placed = RendererBounds(model);
        t.position = new Vector3(basePosition.x - placed.center.x, basePosition.y - placed.min.y, basePosition.z - placed.center.z);
    }

    private static Bounds RendererBounds(GameObject model)
    {
        Bounds bounds = new Bounds(model.transform.position, Vector3.zero);
        bool first = true;

        foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
        {
            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }

        return bounds;
    }

    private static GameObject Box(string name, Vector3 center, Vector3 size, Material material, Transform parent, int layer, bool isStatic = true)
    {
        GameObject box = new GameObject(name);
        box.layer = layer;
        box.transform.SetParent(parent, false);
        box.transform.position = center;

        box.AddComponent<MeshFilter>().sharedMesh = GetBoxMesh(size);
        box.AddComponent<MeshRenderer>().sharedMaterial = material;
        box.AddComponent<BoxCollider>().size = size;

        if (isStatic)
            GameObjectUtility.SetStaticEditorFlags(box, StaticEditorFlags.BatchingStatic);

        return box;
    }

    // Objek primitive tanpa collider (hiasan kecil).
    private static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, Quaternion localRotation)
    {
        GameObject decor = GameObject.CreatePrimitive(type);
        decor.name = type.ToString();
        Object.DestroyImmediate(decor.GetComponent<Collider>());
        decor.transform.SetParent(parent, false);
        decor.transform.localPosition = localPosition;
        decor.transform.localRotation = localRotation;
        decor.transform.localScale = localScale;
        decor.GetComponent<Renderer>().sharedMaterial = material;
        return decor;
    }

    private static Light PointLight(Vector3 position, Color color, float intensity, float range, bool fixture = true)
    {
        GameObject lamp = new GameObject("Lamp");
        lamp.transform.SetParent(lightsRoot);
        lamp.transform.position = position;

        if (fixture)
            Primitive(PrimitiveType.Cube, lamp.transform, new Vector3(0f, 0.12f, 0f), new Vector3(0.9f, 0.05f, 0.25f), Mat("LampFixture", new Color(1f, 0.95f, 0.85f), 2f), Quaternion.identity);

        Light light = lamp.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        return light;
    }

    private static LightZone Zone(string name, string zoneName, Vector3 center, Vector3 size, params Light[] lights)
    {
        GameObject zoneObject = new GameObject(name);
        zoneObject.transform.SetParent(lightsRoot);
        zoneObject.transform.position = center;

        LightZone zone = zoneObject.AddComponent<LightZone>();
        SetProps(zone, ("zoneName", zoneName), ("size", size), ("lights", lights));
        return zone;
    }

    // ==================================================
    // MESH (kotak dengan UV dalam meter -> tekstur tidak melar)
    // ==================================================

    private static Mesh GetBoxMesh(Vector3 size)
    {
        string key = size.x.ToString("F3") + "_" + size.y.ToString("F3") + "_" + size.z.ToString("F3");

        if (meshCache.TryGetValue(key, out Mesh cached))
            return cached;

        Mesh mesh = BuildBoxMesh(size);
        mesh.name = "Box_" + key;

        if (!meshAssetCreated)
        {
            AssetDatabase.CreateAsset(mesh, meshAssetPath);
            meshAssetCreated = true;
        }
        else
        {
            AssetDatabase.AddObjectToAsset(mesh, meshAssetPath);
        }

        meshCache[key] = mesh;
        return mesh;
    }

    private static Mesh BuildBoxMesh(Vector3 s)
    {
        Vector3 h = s * 0.5f;
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        void Face(Vector3 origin, Vector3 uDir, Vector3 vDir, float uLen, float vLen, Vector3 normal)
        {
            int i = vertices.Count;
            vertices.Add(origin);
            vertices.Add(origin + uDir * uLen);
            vertices.Add(origin + uDir * uLen + vDir * vLen);
            vertices.Add(origin + vDir * vLen);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(uLen, 0f));
            uvs.Add(new Vector2(uLen, vLen));
            uvs.Add(new Vector2(0f, vLen));

            // Unity: normal segitiga (a,b,c) = Cross(b-a, c-a); segitiga (i,i+1,i+2) bernormal Cross(u,v).
            if (Vector3.Dot(Vector3.Cross(uDir, vDir), normal) > 0f)
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else
                triangles.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        Face(new Vector3(h.x, -h.y, h.z), Vector3.back, Vector3.up, s.z, s.y, Vector3.right);
        Face(new Vector3(-h.x, -h.y, -h.z), Vector3.forward, Vector3.up, s.z, s.y, Vector3.left);
        Face(new Vector3(h.x, -h.y, h.z), Vector3.left, Vector3.up, s.x, s.y, Vector3.forward);
        Face(new Vector3(-h.x, -h.y, -h.z), Vector3.right, Vector3.up, s.x, s.y, Vector3.back);
        Face(new Vector3(-h.x, h.y, -h.z), Vector3.right, Vector3.forward, s.x, s.z, Vector3.up);
        Face(new Vector3(-h.x, -h.y, h.z), Vector3.right, Vector3.back, s.x, s.z, Vector3.down);

        Mesh mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ==================================================
    // MATERIALS
    // ==================================================

    private static Material TexMat(string name, string texture, Color tint, Vector2 metersPerTile, float smoothness = 0.25f,
        float bumpScale = 1f, string emissionTexture = null, float emission = 0f)
    {
        if (materials.TryGetValue(name, out Material cached) && cached != null)
            return cached;

        Material material = LoadOrCreateMaterial(name);

        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + texture + ".png"));
        material.SetTextureScale("_BaseMap", new Vector2(1f / metersPerTile.x, 1f / metersPerTile.y));
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Smoothness", smoothness);

        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + texture + "_n.png");
        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", bumpScale);
            material.EnableKeyword("_NORMALMAP");
        }

        if (!string.IsNullOrEmpty(emissionTexture))
        {
            material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + emissionTexture + ".png"));
            material.SetColor("_EmissionColor", Color.white * emission);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        EditorUtility.SetDirty(material);
        materials[name] = material;
        return material;
    }

    // Material warna polos. Warna putih tanpa emission = pakai material yang sudah ada tanpa diubah.
    private static Material Mat(string name, Color color, float emission = 0f, float smoothness = 0.25f)
    {
        if (materials.TryGetValue(name, out Material cached) && cached != null)
            return cached;

        bool exists = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + name + ".mat") != null;
        Material material = LoadOrCreateMaterial(name);

        if (exists && color == Color.white && emission <= 0f)
        {
            materials[name] = material;
            return material;
        }

        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);

        if (emission > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        EditorUtility.SetDirty(material);
        materials[name] = material;
        return material;
    }

    private static Material TransparentMat(string name, Color color, float smoothness = 0.6f)
    {
        if (materials.TryGetValue(name, out Material cached) && cached != null)
            return cached;

        Material material = LoadOrCreateMaterial(name);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);

        EditorUtility.SetDirty(material);
        materials[name] = material;
        return material;
    }

    private static Material LoadOrCreateMaterial(string name)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        return material;
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static void SetProps(Object target, params (string name, object value)[] properties)
    {
        SerializedObject so = new SerializedObject(target);

        foreach ((string name, object value) in properties)
        {
            SerializedProperty property = so.FindProperty(name);

            if (property == null)
            {
                Debug.LogError("[StealthLevelBuilder] Property '" + name + "' tidak ada di " + target.GetType().Name);
                continue;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Float:
                    property.floatValue = System.Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    property.intValue = System.Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = (bool)value;
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = (string)value;
                    break;
                case SerializedPropertyType.Color:
                    property.colorValue = (Color)value;
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = (Vector3)value;
                    break;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = value as Object;
                    break;
                default:
                    if (property.isArray && value is Object[] array)
                    {
                        property.arraySize = array.Length;
                        for (int i = 0; i < array.Length; i++)
                            property.GetArrayElementAtIndex(i).objectReferenceValue = array[i];
                    }
                    else
                    {
                        Debug.LogError("[StealthLevelBuilder] Tipe property '" + name + "' belum didukung: " + property.propertyType);
                    }
                    break;
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ==================================================
    // SCREENSHOT (verifikasi visual)
    // ==================================================

    private static void CaptureScreenshots(string folder)
    {
        Directory.CreateDirectory(folder);

        (string scene, string label, Vector3 position, Vector3 lookAt, float fov)[] shots =
        {
            (MainMenuPath, "menu", Vector3.zero, Vector3.zero, 0f),
            (Level1Path, "level1_overview", new Vector3(18f, 42f, -8f), new Vector3(18f, 0f, 13f), 50f),
            (Level1Path, "level1_hall", new Vector3(14f, 9f, -3f), new Vector3(24f, 0f, 9f), 55f),
            (Level1Path, "level1_office", new Vector3(6f, 12f, 12f), new Vector3(6f, 0f, 23f), 55f),
            (Level1Path, "level1_gameplay", new Vector3(2f, 18.6f, -5.1f), new Vector3(2f, 1f, 2f), 50f),
            (Level2Path, "level2_overview", new Vector3(20f, 46f, -9f), new Vector3(20f, 0f, 15f), 50f),
            (Level2Path, "level2_vent", new Vector3(6f, 13f, 12f), new Vector3(0f, 0f, 22f), 60f),
            (Level2Path, "level2_lab", new Vector3(20f, 10f, 15f), new Vector3(21f, 0f, 27f), 60f)
        };

        foreach (var shot in shots)
        {
            EditorSceneManager.OpenScene(shot.scene, OpenSceneMode.Single);

            Camera cam = Camera.main;

            if (cam == null)
                continue;

            foreach (MonoBehaviour behaviour in cam.GetComponents<MonoBehaviour>())
                behaviour.enabled = false;

            if (shot.fov > 0f)
            {
                cam.transform.position = shot.position;
                cam.transform.LookAt(shot.lookAt);
                cam.fieldOfView = shot.fov;
            }

            RenderTexture rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();

            File.WriteAllBytes(Path.Combine(folder, shot.label + ".png"), image.EncodeToPNG());

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(image);

            Debug.Log("[StealthLevelBuilder] Screenshot: " + shot.label);
        }
    }
}
