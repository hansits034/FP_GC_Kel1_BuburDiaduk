using UnityEngine;

// Lingkaran yang mengembang di lantai untuk menunjukkan jangkauan suara.
[RequireComponent(typeof(LineRenderer))]
public class NoisePulse : MonoBehaviour
{
    private const int Segments = 48;
    private const float Duration = 0.6f;

    private static Material sharedMaterial;

    private LineRenderer line;
    private float radius;
    private Color color;
    private float timer;

    public static void Spawn(Vector3 position, float radius, Color color)
    {
        if (radius <= 0f)
            return;

        GameObject pulse = new GameObject("NoisePulse");
        pulse.transform.position = position + Vector3.up * 0.05f;

        NoisePulse component = pulse.AddComponent<NoisePulse>();
        component.radius = radius;
        component.color = color;
    }

    private void Awake()
    {
        line = GetComponent<LineRenderer>();

        if (sharedMaterial == null)
            sharedMaterial = new Material(Shader.Find("Sprites/Default"));

        line.sharedMaterial = sharedMaterial;
        line.loop = true;
        line.useWorldSpace = false;
        line.positionCount = Segments;
        line.widthMultiplier = 0.08f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
    }

    private void Update()
    {
        timer += Time.deltaTime;

        float t = Mathf.Clamp01(timer / Duration);
        float currentRadius = Mathf.Lerp(0.2f, radius, 1f - (1f - t) * (1f - t));

        for (int i = 0; i < Segments; i++)
        {
            float angle = i / (float)Segments * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * currentRadius);
        }

        Color faded = color;
        faded.a = color.a * (1f - t);
        line.startColor = faded;
        line.endColor = faded;

        if (t >= 1f)
            Destroy(gameObject);
    }
}
