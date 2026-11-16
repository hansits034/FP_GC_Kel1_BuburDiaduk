using System.Collections.Generic;
using UnityEngine;

// Bahaya di dalam saluran ventilasi prosedural. Aktif selama listrik menyala (PowerBox).
//   Toxic : gas beracun, HP berkurang terus selama berada di dalam saluran.
//   Laser : beberapa berkas laser yang berkedip; menyentuh berkas yang menyala = damage besar
//           + bunyi alarm yang menarik penjaga. Bisa dilewati dengan timing, atau dimatikan via panel.
public class VentHazardZone : MonoBehaviour
{
    [SerializeField] private float toxicDamagePerSecond = 14f;
    [SerializeField] private int laserDamage = 60;
    [SerializeField] private float laserOnTime = 1.6f;
    [SerializeField] private float laserOffTime = 1.3f;

    private VentHazardType type;
    private Bounds area;
    private PowerBox power;
    private Light ductLight;
    private readonly List<GameObject> gasVisuals = new List<GameObject>();
    private readonly List<(GameObject beam, Bounds bounds, float offset)> beams = new List<(GameObject, Bounds, float)>();

    private PlayerController player;
    private PlayerHealth health;
    private float damageBuffer;
    private float nextLaserHit;
    private bool warned;

    public VentHazardType Type => type;
    public bool Active => power == null || power.IsPowered;

    public void Configure(VentHazardType hazardType, Bounds hazardArea, Light light)
    {
        type = hazardType;
        area = hazardArea;
        ductLight = light;
    }

    public void SetPowerSource(PowerBox powerBox)
    {
        power = powerBox;
    }

    public void AddGasVisual(GameObject visual)
    {
        gasVisuals.Add(visual);
    }

    public void AddBeam(GameObject beam, Bounds bounds, float phaseOffset)
    {
        beams.Add((beam, bounds, phaseOffset));
    }

    private void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        if (player != null) health = player.GetComponent<PlayerHealth>();
    }

    private void Update()
    {
        bool active = Active;

        if (ductLight != null)
            ductLight.color = !active ? new Color(0.45f, 0.65f, 1f) :
                type == VentHazardType.Toxic ? new Color(0.4f, 1f, 0.3f) : new Color(1f, 0.25f, 0.2f);

        if (type == VentHazardType.Toxic)
            UpdateToxic(active);
        else if (type == VentHazardType.Laser)
            UpdateLaser(active);
    }

    private bool PlayerInside(Bounds bounds)
    {
        return player != null && bounds.Contains(player.FeetPosition + Vector3.up * 0.4f);
    }

    private void UpdateToxic(bool active)
    {
        for (int i = 0; i < gasVisuals.Count; i++)
        {
            GameObject gas = gasVisuals[i];
            if (gas.activeSelf != active) gas.SetActive(active);

            if (active)
            {
                // Gas "bernapas" pelan.
                float s = 1f + Mathf.Sin(Time.time * 1.5f + i) * 0.05f;
                gas.transform.localScale = new Vector3(s, 1f, s);
            }
        }

        if (!active || health == null || !StealthGameManager.IsPlaying || !PlayerInside(area))
        {
            damageBuffer = 0f;
            return;
        }

        if (!warned)
        {
            warned = true;
            StealthHUD.Toast("GAS BERACUN! Keluar, atau padamkan listrik di panel dulu");
        }

        damageBuffer += toxicDamagePerSecond * Time.deltaTime;

        if (damageBuffer >= 5f)
        {
            int amount = Mathf.FloorToInt(damageBuffer);
            damageBuffer -= amount;
            health.TakeDamage(amount);
        }
    }

    private void UpdateLaser(bool active)
    {
        float cycle = laserOnTime + laserOffTime;

        foreach ((GameObject beam, Bounds bounds, float offset) in beams)
        {
            bool on = active && Mathf.Repeat(Time.time + offset, cycle) < laserOnTime;

            if (beam.activeSelf != on)
                beam.SetActive(on);

            if (!on || health == null || !StealthGameManager.IsPlaying || Time.time < nextLaserHit || !PlayerInside(bounds))
                continue;

            nextLaserHit = Time.time + 1f;
            SoundManager.PlayAt("camera_alarm", beam.transform.position, 1f, 30f, 0f);
            NoiseSystem.Emit(beam.transform.position, 12f, 1.5f);
            StealthHUD.Toast("Kena laser! Perhatikan ritme kedipnya, atau padamkan listrik");
            health.TakeDamage(laserDamage);
        }

        if (!warned && active && PlayerInside(area))
        {
            warned = true;
            StealthHUD.Toast("Laser di ventilasi - lewati saat padam, atau matikan listrik di panel");
        }
    }
}
