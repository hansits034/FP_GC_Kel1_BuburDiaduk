using UnityEngine;

// Kamera CCTV: menyapu kiri-kanan, jika melihat Player cukup lama
// akan melapor ke AlertManager (Shared Alert) sehingga penjaga terdekat datang.
public class SecurityCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform head;
    [SerializeField] private Light spotLight;

    [Header("Sweep")]
    [SerializeField] private float sweepAngle = 45f;
    [SerializeField] private float sweepPeriod = 8f;
    [SerializeField] private float pitch = 35f;

    [Header("Detection")]
    [SerializeField] private float viewRange = 11f;
    [SerializeField] private float viewAngle = 50f;
    [SerializeField] private float detectTime = 1.2f;
    [SerializeField] private float reportInterval = 1.5f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Colors")]
    [SerializeField] private Color idleColor = new Color(0.4f, 1f, 0.5f);
    [SerializeField] private Color suspiciousColor = new Color(1f, 0.8f, 0.2f);
    [SerializeField] private Color alertColor = new Color(1f, 0.2f, 0.2f);

    private PlayerController player;
    private Vector3 previousPlayerPosition;
    private float baseYaw;
    private float detection;
    private float nextReportTime;
    private float sweepTime;
    private bool alarmSounded;

    public bool IsPowered { get; private set; } = true;
    public float Detection => detection;

    private void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        baseYaw = head != null ? head.localEulerAngles.y : 0f;

        if (player != null)
            previousPlayerPosition = player.transform.position;
    }

    // Dipakai DDA: makin sulit, makin cepat CCTV mengenali Player.
    public void SetDetectTime(float seconds)
    {
        detectTime = Mathf.Max(0.2f, seconds);
    }

    public void SetPowered(bool powered)
    {
        IsPowered = powered;
        detection = 0f;

        if (spotLight != null)
            spotLight.enabled = powered;
    }

    private void Update()
    {
        if (!IsPowered || head == null)
            return;

        sweepTime += Time.deltaTime;

        float yaw = baseYaw + Mathf.Sin(sweepTime / sweepPeriod * Mathf.PI * 2f) * sweepAngle;
        head.localRotation = Quaternion.Euler(pitch, yaw, 0f);

        bool seen = CanSeePlayer();

        if (seen)
        {
            float rate = player.IsCrouching ? 0.6f : 1f;
            detection = Mathf.Min(1f, detection + Time.deltaTime / detectTime * rate);
        }
        else
        {
            detection = Mathf.Max(0f, detection - Time.deltaTime * 0.5f);
        }

        if (detection >= 1f && !alarmSounded)
        {
            alarmSounded = true;
            SoundManager.PlayAt("camera_alarm", head.position, 1f, 40f, 0f);
        }
        else if (detection <= 0f)
        {
            alarmSounded = false;
        }

        if (seen && detection >= 1f && Time.time >= nextReportTime)
        {
            nextReportTime = Time.time + reportInterval;

            Vector3 velocity = Time.deltaTime > 0f
                ? (player.transform.position - previousPlayerPosition) / Time.deltaTime
                : Vector3.zero;

            AlertManager.Instance.ReportSighting(head.position, player.transform.position, velocity);
            Debug.Log(name + " melihat Player!");
        }

        if (player != null)
            previousPlayerPosition = player.transform.position;

        if (spotLight != null)
        {
            spotLight.color =
                detection >= 1f ? alertColor :
                detection > 0f ? Color.Lerp(idleColor, suspiciousColor, detection) : idleColor;
        }
    }

    private bool CanSeePlayer()
    {
        if (player == null || EnemyDebugMenu.PlayerHidden)
            return false;

        Vector3 eye = head.position;
        Vector3 target = player.VisibilityPoint;
        Vector3 toPlayer = target - eye;
        float distance = toPlayer.magnitude;

        if (distance > viewRange * LightZone.GetVisibility(target))
            return false;

        if (Vector3.Angle(head.forward, toPlayer) > viewAngle * 0.5f)
            return false;

        return !Physics.Raycast(eye, toPlayer.normalized, distance, obstacleMask);
    }

    private void OnDrawGizmosSelected()
    {
        if (head == null)
            return;

        Gizmos.color = Color.green;
        Vector3 left = Quaternion.AngleAxis(-viewAngle * 0.5f, head.up) * head.forward;
        Vector3 right = Quaternion.AngleAxis(viewAngle * 0.5f, head.up) * head.forward;
        Gizmos.DrawRay(head.position, left * viewRange);
        Gizmos.DrawRay(head.position, right * viewRange);
        Gizmos.DrawRay(head.position, head.forward * viewRange);
    }
}
