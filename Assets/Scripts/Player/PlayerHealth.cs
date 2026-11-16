using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField]
    private int maxHealth = 100;

    [Tooltip("Pulih per detik setelah beberapa saat tidak terkena serangan (0 = tidak pulih).")]
    [SerializeField]
    private float regenPerSecond = 4f;

    [SerializeField]
    private float regenDelay = 6f;

    private int currentHealth;
    private float regenBuffer;
    private float lastHitTime = -Mathf.Infinity;

    // Dipakai DDA (PlayerPerformanceTracker): jumlah damage yang diterima.
    public static event System.Action<int> Damaged;

    public int Current => currentHealth;
    public int Max => maxHealth;
    public float Normalized => maxHealth > 0 ? currentHealth / (float)maxHealth : 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvents()
    {
        Damaged = null;
    }

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    private void Update()
    {
        if (regenPerSecond <= 0f || currentHealth <= 0 || currentHealth >= maxHealth)
            return;

        if (Time.time - lastHitTime < regenDelay)
            return;

        regenBuffer += regenPerSecond * Time.deltaTime;

        if (regenBuffer >= 1f)
        {
            int amount = Mathf.FloorToInt(regenBuffer);
            regenBuffer -= amount;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        }
    }

    public void TakeDamage(int damage)
    {
        if (currentHealth <= 0)
            return;

        int before = currentHealth;

        currentHealth =
            Mathf.Clamp(
                currentHealth - damage,
                0,
                maxHealth
            );

        lastHitTime = Time.time;
        regenBuffer = 0f;
        Damaged?.Invoke(before - currentHealth);

        Debug.Log(
            "Player Health = "
            + currentHealth
        );

        if (currentHealth > 0)
        {
            StealthHUD.Toast("Terkena serangan! HP " + currentHealth + "/" + maxHealth + " - kabur!");
            return;
        }

        Debug.Log("Player Dead");

        if (StealthGameManager.Instance != null)
            StealthGameManager.Instance.Lose("Kamu tertangkap penjaga!");
    }
}
