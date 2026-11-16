using UnityEngine;

// Kamera menu utama: bergerak pelan mengitari diorama agar latar terasa hidup.
public class MenuCameraDrift : MonoBehaviour
{
    [SerializeField] private Transform pivot;
    [SerializeField] private float yawAmplitude = 10f;
    [SerializeField] private float period = 24f;
    [SerializeField] private float bobAmplitude = 0.15f;

    private Vector3 offset;

    private void Start()
    {
        if (pivot != null)
            offset = transform.position - pivot.position;
    }

    private void LateUpdate()
    {
        if (pivot == null)
            return;

        float t = Time.unscaledTime / period * Mathf.PI * 2f;
        Quaternion yaw = Quaternion.Euler(0f, Mathf.Sin(t) * yawAmplitude, 0f);

        transform.position = pivot.position + yaw * offset + Vector3.up * (Mathf.Sin(t * 2f) * bobAmplitude);
        transform.LookAt(pivot.position);
    }
}
