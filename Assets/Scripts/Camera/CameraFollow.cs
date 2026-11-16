using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("View")]
    [Range(30f, 89f)]
    [SerializeField] private float pitch = 75f;
    [SerializeField] private float yaw = 0f;   
    [SerializeField] private float distance = 16f;

    [Header("Smoothing")]
    [SerializeField] private float smoothTime = 0.15f;

    private Vector3 velocity;

    private void Start()
    {
        if (target == null)
            return;
        
        transform.position = GetDesiredPosition();
        transform.rotation = GetRotation();

    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        transform.rotation = GetRotation();

        transform.position = 
            Vector3.SmoothDamp(
                transform.position,
                GetDesiredPosition(),
                ref velocity,
                smoothTime
            );

    }

    private Quaternion GetRotation()
    {
        return Quaternion.Euler(pitch, yaw, 0f);
    }

    private Vector3 GetDesiredPosition()
    {
        Vector3 lookDirection = GetRotation() * Vector3.forward;

        return target.position - lookDirection * distance;
    }

}