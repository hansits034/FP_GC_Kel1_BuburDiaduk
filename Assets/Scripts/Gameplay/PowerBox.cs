using UnityEngine;

// Panel listrik: mematikan lampu area & kamera untuk sementara.
// Bunyi korsleting menarik penjaga terdekat untuk memeriksa (distraksi).
public class PowerBox : Interactable
{
    [SerializeField] private string areaName = "Aula";
    [SerializeField] private LightZone[] zones;
    [SerializeField] private SecurityCamera[] cameras;
    [SerializeField] private VentHazard[] ventHazards;
    [SerializeField] private float outageDuration = 25f;
    [SerializeField] private float noiseRadius = 12f;
    [SerializeField] private Renderer indicator;

    private float restoreTime;

    public bool IsPowered { get; private set; } = true;
    public float RemainingOutage => IsPowered ? 0f : Mathf.Max(0f, restoreTime - Time.time);

    public override bool IsAvailable => base.IsAvailable && IsPowered;

    // Dipakai ProceduralLevelBuilder (PCG) saat runtime.
    public void Configure(string area, LightZone[] lightZones, SecurityCamera[] securityCameras, float duration)
    {
        areaName = area;
        zones = lightZones;
        cameras = securityCameras;
        outageDuration = duration;
    }

    private void Start()
    {
        SetIndicator(Color.green);
    }

    private void Update()
    {
        if (!IsPowered && Time.time >= restoreTime)
            SetPower(true);
    }

    public override string GetPrompt(PlayerInventory inventory)
    {
        return "[E] Matikan listrik " + areaName + " (" + Mathf.RoundToInt(outageDuration) + " dtk)";
    }

    public override void Interact(PlayerInteractor interactor)
    {
        SetPower(false);
        restoreTime = Time.time + outageDuration;

        SoundManager.Play("power_off", 1f, 0f);
        NoiseSystem.Emit(transform.position, noiseRadius, 1.2f);
        NoisePulse.Spawn(transform.position, noiseRadius, new Color(0.6f, 0.6f, 1f));
        StealthHUD.Toast("Listrik " + areaName + " padam! Lampu & kamera mati");

        NotifyMission();
    }

    private void SetPower(bool powered)
    {
        IsPowered = powered;

        if (zones != null)
        {
            foreach (LightZone zone in zones)
            {
                if (zone != null)
                    zone.SetDark(!powered);
            }
        }

        if (cameras != null)
        {
            foreach (SecurityCamera cam in cameras)
            {
                if (cam != null)
                    cam.SetPowered(powered);
            }
        }

        if (ventHazards != null)
        {
            foreach (VentHazard vent in ventHazards)
            {
                if (vent != null)
                    vent.SetHazard(powered);
            }
        }

        SetIndicator(powered ? Color.green : Color.red);

        if (powered && Time.timeSinceLevelLoad > 1f)
        {
            StealthHUD.Toast("Listrik " + areaName + " menyala kembali");
            SoundManager.Play("power_on", 0.7f, 0f);
        }
    }

    private void SetIndicator(Color color)
    {
        if (indicator == null)
            return;

        indicator.material.color = color;
        indicator.material.SetColor("_EmissionColor", color * 2f);
    }
}
