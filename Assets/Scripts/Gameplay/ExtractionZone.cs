using UnityEngine;

// Titik ekstraksi: masuk ke area ini setelah semua misi wajib selesai = menang.
public class ExtractionZone : MonoBehaviour
{
    [SerializeField] private Vector3 size = new Vector3(3f, 3f, 3f);
    [SerializeField] private Renderer beacon;
    [SerializeField] private Light beaconLight;

    private PlayerController player;

    // Dipakai Game AI (penjaga memblokir ekstraksi pada difficulty tinggi).
    public static ExtractionZone Active { get; private set; }

    private void OnEnable() { Active = this; }

    private void OnDisable() { if (Active == this) Active = null; }
    private bool wasInside;
    private bool lastReady;

    private void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        UpdateBeacon(false);
    }

    private void Update()
    {
        bool ready = MissionManager.Instance == null || MissionManager.Instance.IsReadyForExtraction;

        if (ready != lastReady)
            UpdateBeacon(ready);

        if (beaconLight != null)
            beaconLight.intensity = ready ? 3f + Mathf.Sin(Time.time * 4f) * 1.5f : 0.6f;

        if (player == null || !StealthGameManager.IsPlaying)
            return;

        bool inside = new Bounds(transform.position, size).Contains(player.transform.position);

        if (inside && !wasInside)
        {
            if (ready)
            {
                MissionManager.Notify("extract");

                if (StealthGameManager.Instance != null)
                    StealthGameManager.Instance.Win();
            }
            else
            {
                StealthHUD.Toast("Belum bisa kabur - selesaikan misi dulu!");
            }
        }

        wasInside = inside;
    }

    private void UpdateBeacon(bool ready)
    {
        lastReady = ready;

        Color color = ready ? new Color(0.2f, 1f, 0.4f) : new Color(0.3f, 0.4f, 0.35f);

        if (beacon != null)
        {
            beacon.material.color = color;
            beacon.material.SetColor("_EmissionColor", color * (ready ? 2f : 0.2f));
        }

        if (beaconLight != null)
            beaconLight.color = color;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, size);
    }
}
