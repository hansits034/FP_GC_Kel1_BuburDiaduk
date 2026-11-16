using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Menu debug (F1): daftar guard, detail guard, overlay FOV / memori / rencana search, dan cheat
public class EnemyDebugMenu : MonoBehaviour
{
    public static bool PlayerHidden { get; private set; }
    public static bool FreezeAI { get; private set; }
    public static bool ShowLabels { get; private set; }
    public static bool IsPointerOverPanel { get; private set; }

    private static Material lineMaterial;

    private bool open;
    private bool showOverlay = true;
    private EnemyBTController selected;
    private Vector2 scroll;
    private Rect window = new Rect(20f, 140f, 400f, 0f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!Debug.isDebugBuild)
            return;

        PlayerHidden = false;
        FreezeAI = false;
        ShowLabels = false;

        GameObject go = new GameObject("EnemyDebugMenu");
        DontDestroyOnLoad(go);
        go.AddComponent<EnemyDebugMenu>();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            open = !open;
    }

    // ---------------- Panel ----------------

    private void OnGUI()
    {
        IsPointerOverPanel = false;

        if (!open || AlertManager.Current == null)
            return;

        window = GUILayout.Window(GetInstanceID(), window, DrawWindow, "AI DEBUG  (F1)");
        IsPointerOverPanel = window.Contains(Event.current.mousePosition);
    }

    private void DrawWindow(int id)
    {
        AlertManager alert = AlertManager.Current;
        IReadOnlyList<EnemyBTController> guards = alert.Guards;

        GUILayout.Label("Fasilitas: " + alert.Level + "   Terdeteksi: " + alert.DetectionCount + "x   Penjaga: " + guards.Count);

        showOverlay = GUILayout.Toggle(showOverlay, "Overlay FOV / memori / titik search");
        ShowLabels = GUILayout.Toggle(ShowLabels, "Label state + aksi di atas semua penjaga");
        PlayerHidden = GUILayout.Toggle(PlayerHidden, "Player tak terlihat (suara tetap terdengar)");
        FreezeAI = GUILayout.Toggle(FreezeAI, "Bekukan semua penjaga");

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Semua cari Player"))
        {
            foreach (EnemyBTController guard in guards)
                SendToPlayer(guard);
        }

        if (GUILayout.Button("Reset semua"))
        {
            foreach (EnemyBTController guard in guards)
                guard.ResetAwareness();
        }

        GUILayout.EndHorizontal();

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(150f));

        foreach (EnemyBTController guard in guards)
        {
            string row = guard.name + "   " + guard.Awareness + "   " + guard.Suspicion.ToString("0.00") + "   " + guard.CurrentAction;

            if (GUILayout.Toggle(guard == selected, row, GUI.skin.button))
                selected = guard;
        }

        GUILayout.EndScrollView();

        if (selected != null)
            DrawDetails(selected);

        GUI.DragWindow();
    }

    private void DrawDetails(EnemyBTController guard)
    {
        EnemyBlackboard bb = guard.Blackboard;

        GUILayout.Space(6f);
        GUILayout.Label("== " + guard.name + " ==");
        GUILayout.Label("State: " + bb.awareness + "   Aksi: " + guard.CurrentAction);
        GUILayout.Label("Lihat: " + bb.canSeePlayer + "   Rasa dekat: " + bb.canSensePlayer + "   Jarak: " + bb.distanceToPlayer.ToString("0.0") + " m");
        GUILayout.Label(bb.hasMemory
            ? "Memori: " + bb.memorySource + ", " + bb.MemoryAge.ToString("0.0") + " dtk lalu"
            : "Memori: kosong");
        GUILayout.Label("Search: titik " + guard.SearchPointIndex + " / " + guard.SearchPoints.Count);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Suspicion " + bb.suspicion.ToString("0.00"), GUILayout.Width(110f));

        // Hanya ditulis saat slider digeser, jadi tidak melawan perhitungan AI sendiri.
        float suspicion = GUILayout.HorizontalSlider(bb.suspicion, 0f, 1f);
        if (!Mathf.Approximately(suspicion, bb.suspicion))
            bb.suspicion = suspicion;

        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Kirim ke posisi Player"))
            SendToPlayer(guard);

        if (GUILayout.Button("Reset"))
            guard.ResetAwareness();

        GUILayout.EndHorizontal();
    }

    private static void SendToPlayer(EnemyBTController guard)
    {
        if (guard.Blackboard.player != null)
            guard.ReceiveAllyAlert(guard.Blackboard.player.position, Vector3.zero);
    }

    // ---------------- Overlay dunia ----------------

    private void OnRenderObject()
    {
        if (!open || !showOverlay || AlertManager.Current == null)
            return;

        if (lineMaterial == null)
        {
            lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        lineMaterial.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);
        GL.Begin(GL.LINES);

        foreach (EnemyBTController guard in AlertManager.Current.Guards)
            DrawGuard(guard);

        // Suara terakhir yang dipancarkan (langkah, kaleng, panel listrik).
        if (NoiseSystem.LastNoiseRadius > 0f)
            Circle(NoiseSystem.LastNoisePosition, NoiseSystem.LastNoiseRadius, Color.cyan);

        GL.End();
        GL.PopMatrix();
    }

    private void DrawGuard(EnemyBTController guard)
    {
        EnemyBlackboard bb = guard.Blackboard;
        Vector3 eye = guard.EyePosition;
        Color color = guard == selected ? Color.white : guard.GetStateColor();

        // FOV
        float half = guard.VisionAngle * 0.5f;
        Vector3 previous = eye;

        for (int i = 0; i <= 12; i++)
        {
            float angle = Mathf.Lerp(-half, half, i / 12f);
            Vector3 point = eye + Quaternion.Euler(0f, angle, 0f) * guard.transform.forward * guard.VisionRange;
            Line(previous, point, color);
            previous = point;
        }

        Line(previous, eye, color);

        if (bb.hasMemory)
        {
            Line(eye, bb.lastKnownPosition, Color.yellow);
            Cross(bb.lastKnownPosition, Color.yellow);
        }

        if (bb.awareness == AwarenessState.Searching)
        {
            for (int i = guard.SearchPointIndex; i < guard.SearchPoints.Count; i++)
                Cross(guard.SearchPoints[i], Color.magenta);
        }
    }

    private static void Line(Vector3 a, Vector3 b, Color color)
    {
        GL.Color(color);
        GL.Vertex(a);
        GL.Vertex(b);
    }

    private static void Cross(Vector3 p, Color color)
    {
        Line(p + new Vector3(-0.3f, 0.05f, 0f), p + new Vector3(0.3f, 0.05f, 0f), color);
        Line(p + new Vector3(0f, 0.05f, -0.3f), p + new Vector3(0f, 0.05f, 0.3f), color);
    }

    private static void Circle(Vector3 center, float radius, Color color)
    {
        const int segments = 32;
        center += Vector3.up * 0.05f;

        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            float b = (i + 1) / (float)segments * Mathf.PI * 2f;
            Line(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius,
                 center + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radius, color);
        }
    }
}
