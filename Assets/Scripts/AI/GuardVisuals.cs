using UnityEngine;
using UnityEngine.AI;

// Visual penjaga: animasi jalan dari kecepatan NavMeshAgent
// dan warna senter mengikuti tingkat kewaspadaan.
public class GuardVisuals : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Light flashlight;

    [SerializeField] private Color unawareColor = new Color(1f, 0.95f, 0.8f);
    [SerializeField] private Color suspiciousColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] private Color searchingColor = new Color(1f, 0.5f, 0.1f);
    [SerializeField] private Color alertColor = new Color(1f, 0.15f, 0.15f);

    [Tooltip("Kecepatan (m/s) saat clip lari diputar dengan kecepatan normal.")]
    [SerializeField] private float animationRunSpeed = 4.5f;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");

    private NavMeshAgent agent;
    private EnemyBTController guard;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        guard = GetComponent<EnemyBTController>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (animator != null && agent != null)
        {
            float speed = agent.velocity.magnitude;
            animator.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);

            // Clip lari diperlambat saat patroli agar terlihat seperti jalan cepat.
            animator.SetFloat(MoveSpeedHash, Mathf.Clamp(speed / animationRunSpeed, 0.45f, 1.1f));
        }

        if (flashlight == null || guard == null)
            return;

        Color target;

        switch (guard.Awareness)
        {
            case AwarenessState.Suspicious: target = suspiciousColor; break;
            case AwarenessState.Searching: target = searchingColor; break;
            case AwarenessState.Alert: target = alertColor; break;
            default: target = unawareColor; break;
        }

        flashlight.color = Color.Lerp(flashlight.color, target, 8f * Time.deltaTime);
    }
}
