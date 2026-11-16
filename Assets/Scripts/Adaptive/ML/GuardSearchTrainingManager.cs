using Unity.AI.Navigation;
using UnityEngine;

// Scene training RL: membangun semua arena, bake NavMesh sekali, lalu mengaktifkan agent.
// Jalankan: mlagents-learn Tools/ml/guard_search.yaml --run-id=GuardSearch, lalu tekan Play.
[DefaultExecutionOrder(-900)]
public class GuardSearchTrainingManager : MonoBehaviour
{
    [SerializeField] private ProceduralLevelBuilder palette;
    [SerializeField] private NavMeshSurface navSurface;
    [SerializeField] private GuardSearchArena[] arenas;
    [SerializeField] private float arenaSpacing = 70f;
    [SerializeField] private int columns = 4;

    private void Awake()
    {
        for (int i = 0; i < arenas.Length; i++)
        {
            Vector3 origin = new Vector3((i % columns) * arenaSpacing, 0f, (i / columns) * arenaSpacing);
            arenas[i].Build(palette, origin);
        }

        Physics.SyncTransforms();

        if (navSurface != null)
            navSurface.BuildNavMesh();

        foreach (GuardSearchArena arena in arenas)
            arena.Activate();
    }

    private void OnGUI()
    {
        int episodes = 0, finds = 0;

        foreach (GuardSearchArena arena in arenas)
        {
            episodes += arena.Episodes;
            finds += arena.Finds;
        }

        GUI.Label(new Rect(10, 10, 600, 24),
            "RL GuardSearch  |  arena " + arenas.Length + "  |  episode " + episodes + "  |  target ditemukan " + finds +
            (episodes > 0 ? " (" + (finds * 100f / episodes).ToString("0") + "%)" : ""));
    }
}
