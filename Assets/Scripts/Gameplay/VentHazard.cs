using System.Collections;
using UnityEngine;

// Memerlukan Power Box dinonaktifkan untuk melalui vent
public class VentHazard : MonoBehaviour
{
    [SerializeField] private GameObject hazardObject;
    [SerializeField] private Transform player;
    [SerializeField] private Light ventLight;
    [SerializeField] private int hazardDamage = 100;
    [SerializeField] private float hazardWindup = 2f;

    void Start()
    {
        if (player == null)
        {
            PlayerController found = FindFirstObjectByType<PlayerController>();
            if (found != null)
                player = found.transform;
        }
    }

    public void SetHazard(bool status)
    {
        if (status)
        {
            ventLight.color = new Color(1f, 0.45f, 0.45f);
            StartCoroutine(HazardWindup());
        } else
        {
            hazardObject.SetActive(false);
            ventLight.color = new Color(0.45f, 0.65f, 1f);
        }
    }

    IEnumerator HazardWindup()
    {
        yield return new WaitForSeconds(hazardWindup);

        hazardObject.SetActive(true);
    }

    void OnCollisionEnter(Collision collision)
    {
        if(player != null && collision.transform == player)
        {
            PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();

            if (playerHealth != null)
                playerHealth.TakeDamage(hazardDamage);
        }
    }
}
