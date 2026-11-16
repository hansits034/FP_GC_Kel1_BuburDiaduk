using UnityEngine;

// Jendela instruksi di level pelatihan. Muncul sekali saat syaratnya terpenuhi:
// Player membawa item tertentu dan/atau berada di dalam area (kotak di posisi objek ini).
public class TrainingTip : MonoBehaviour
{
    [SerializeField] private string title = "Instruksi";
    [TextArea(3, 8)]
    [SerializeField] private string body;

    [Tooltip("Kosong = tidak perlu item.")]
    [SerializeField] private string requiredItemId;

    [Tooltip("Ukuran area pemicu. Nol = tidak perlu area (cukup membawa item).")]
    [SerializeField] private Vector3 areaSize;

    private PlayerController player;
    private PlayerInventory inventory;
    private bool shown;

    private void Start()
    {
        player = FindFirstObjectByType<PlayerController>();

        if (player != null)
            inventory = player.GetComponent<PlayerInventory>();
    }

    private void Update()
    {
        // Tip lain yang sedang terbuka membuat IsPlaying false, jadi tip berikutnya menunggu giliran.
        if (shown || player == null || !StealthGameManager.IsPlaying)
            return;

        if (!string.IsNullOrEmpty(requiredItemId) && (inventory == null || !inventory.Has(requiredItemId)))
            return;

        if (areaSize != Vector3.zero && !new Bounds(transform.position, areaSize).Contains(player.transform.position))
            return;

        shown = true;
        StealthHUD.ShowTip(title, body);
    }

    private void OnDrawGizmos()
    {
        if (areaSize == Vector3.zero)
            return;

        Gizmos.color = new Color(0.45f, 0.82f, 1f, 0.6f);
        Gizmos.DrawWireCube(transform.position, areaSize);
    }
}
