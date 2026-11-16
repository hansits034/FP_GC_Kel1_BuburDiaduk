using System.Collections.Generic;
using UnityEngine;

// Area dengan lampu yang bisa dimatikan (lewat PowerBox).
// Di area gelap, jarak pandang penjaga & kamera menyusut.
public class LightZone : MonoBehaviour
{
    [SerializeField] private string zoneName = "Area";
    [SerializeField] private Vector3 size = new Vector3(10f, 4f, 10f);
    [SerializeField] private Light[] lights;
    [Range(0.1f, 1f)]
    [SerializeField] private float darkVisibility = 0.4f;

    private static readonly List<LightZone> zones = new List<LightZone>();

    private float[] litIntensities;

    public bool IsDark { get; private set; }
    public string ZoneName => zoneName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        zones.Clear();
    }

    // Dipakai ProceduralLevelBuilder (PCG). Panggil saat GameObject masih nonaktif.
    public void Configure(string name, Vector3 areaSize, Light[] zoneLights)
    {
        zoneName = name;
        size = areaSize;
        lights = zoneLights;
    }

    private void Awake()
    {
        litIntensities = new float[lights != null ? lights.Length : 0];

        for (int i = 0; i < litIntensities.Length; i++)
            litIntensities[i] = lights[i] != null ? lights[i].intensity : 0f;
    }

    private void OnEnable()
    {
        zones.Add(this);
    }

    private void OnDisable()
    {
        zones.Remove(this);
    }

    public bool Contains(Vector3 position)
    {
        return new Bounds(transform.position, size).Contains(position);
    }

    public void SetDark(bool dark)
    {
        IsDark = dark;

        for (int i = 0; i < litIntensities.Length; i++)
        {
            if (lights[i] != null)
                lights[i].intensity = dark ? litIntensities[i] * 0.08f : litIntensities[i];
        }
    }

    // 1 = terang, < 1 = gelap (pengali jarak pandang).
    public static float GetVisibility(Vector3 position)
    {
        float visibility = 1f;

        foreach (LightZone zone in zones)
        {
            if (zone.IsDark && zone.Contains(position))
                visibility = Mathf.Min(visibility, zone.darkVisibility);
        }

        return visibility;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = IsDark ? new Color(0.2f, 0.2f, 0.6f, 0.4f) : new Color(1f, 0.9f, 0.4f, 0.15f);
        Gizmos.DrawWireCube(transform.position, size);
    }
}
