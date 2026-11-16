using UnityEngine;

// Kaleng yang dilempar: saat pertama kali membentur sesuatu,
// memancarkan suara keras yang menarik penjaga untuk memeriksa.
public class ThrowableNoise : MonoBehaviour
{
    private float radius = 10f;
    private float strength = 2f;
    private bool emitted;

    public void Configure(float noiseRadius, float noiseStrength)
    {
        radius = noiseRadius;
        strength = noiseStrength;
    }

    private void Start()
    {
        Destroy(gameObject, 15f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (emitted || collision.relativeVelocity.magnitude < 1f)
            return;

        emitted = true;

        SoundManager.PlayAt("can_impact", transform.position, 1f, 40f, 0.1f);
        NoiseSystem.Emit(transform.position, radius, strength);
        NoisePulse.Spawn(transform.position, radius, new Color(1f, 0.85f, 0.2f));
    }
}
