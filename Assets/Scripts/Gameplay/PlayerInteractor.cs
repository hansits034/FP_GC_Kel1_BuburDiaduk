using UnityEngine;
using UnityEngine.InputSystem;

// Interaksi Player: E untuk memakai objek terdekat (tekan / tahan),
// Klik kiri / Enter untuk melempar kaleng sebagai pengalih perhatian.
[RequireComponent(typeof(PlayerInventory))]
public class PlayerInteractor : MonoBehaviour
{
    // Dipakai DDA: jumlah pengalih yang dipakai pemain.
    public static event System.Action CanThrown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCanThrown() { CanThrown = null; }

    [Header("Interact (E)")]
    [SerializeField] private float interactRange = 1.8f;

    [Header("Throw (Klik Kiri)")]
    [SerializeField] private string throwableItemId = "can";
    [SerializeField] private float maxThrowDistance = 12f;
    [SerializeField] private float throwNoiseRadius = 10f;
    [SerializeField] private float throwNoiseStrength = 2f;
    [SerializeField] private Material defaultMaterial;

    private PlayerInventory inventory;
    private PlayerController playerController;
    private Collider[] ownColliders;

    private InputAction interactAction;
    private InputAction throwAction;

    private Interactable holdTarget;
    private float holdTimer;
    private float holdTickTimer;

    public PlayerInventory Inventory => inventory;
    public Interactable Current { get; private set; }
    public float HoldProgress { get; private set; }
    public string ThrowableItemId => throwableItemId;

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        playerController = GetComponent<PlayerController>();
        ownColliders = GetComponentsInChildren<Collider>();

        interactAction = InputSystem.actions.FindAction("Player/Interact");
        throwAction = InputSystem.actions.FindAction("Player/Attack");
    }

    private void Update()
    {
        if (!StealthGameManager.IsPlaying)
        {
            ResetHold();
            return;
        }

        Current = FindBestInteractable();

        if (holdTarget != null && holdTarget != Current)
            ResetHold();

        if (Current != null && interactAction != null && interactAction.WasPressedThisFrame())
            BeginInteract(Current);

        if (holdTarget != null)
            UpdateHold();

        if (throwAction != null && throwAction.WasPressedThisFrame() && !EnemyDebugMenu.IsPointerOverPanel)
            TryThrow();
    }

    // ==================================================
    // INTERACT
    // ==================================================

    private Interactable FindBestInteractable()
    {
        Interactable best = null;
        float bestScore = float.MaxValue;

        foreach (Interactable interactable in Interactable.All)
        {
            if (!interactable.IsAvailable)
                continue;

            Vector3 offset = interactable.InteractionPoint - transform.position;

            if (Mathf.Abs(offset.y) > 2.5f)
                continue;

            offset.y = 0f;
            float distance = offset.magnitude;

            if (distance > interactRange)
                continue;

            // Prioritaskan yang dekat dan ada di depan Player.
            float facing = distance > 0.01f ? Vector3.Dot(transform.forward, offset / distance) : 1f;
            float score = distance - facing * 0.5f;

            if (score < bestScore)
            {
                bestScore = score;
                best = interactable;
            }
        }

        return best;
    }

    private void BeginInteract(Interactable target)
    {
        if (!target.CanInteract(inventory, out string reason))
        {
            StealthHUD.Toast(reason);
            SoundManager.Play("denied", 0.7f, 0f);
            return;
        }

        if (target.HoldDuration <= 0f)
        {
            target.Interact(this);
            return;
        }

        holdTarget = target;
        holdTimer = 0f;
    }

    private void UpdateHold()
    {
        if (!interactAction.IsPressed())
        {
            ResetHold();
            return;
        }

        holdTimer += Time.deltaTime;
        HoldProgress = Mathf.Clamp01(holdTimer / holdTarget.HoldDuration);

        holdTickTimer -= Time.deltaTime;
        if (holdTickTimer <= 0f)
        {
            SoundManager.Play("hold_tick", 0.5f, 0.1f);
            holdTickTimer = 0.2f;
        }

        if (HoldProgress >= 1f)
        {
            Interactable target = holdTarget;
            ResetHold();
            target.Interact(this);
        }
    }

    private void ResetHold()
    {
        holdTarget = null;
        holdTimer = 0f;
        HoldProgress = 0f;
    }

    // ==================================================
    // THROW
    // ==================================================

    private void TryThrow()
    {
        if (!inventory.Has(throwableItemId))
        {
            StealthHUD.Toast("Tidak punya kaleng untuk dilempar");
            SoundManager.Play("denied", 0.5f, 0f);
            return;
        }

        Vector3 origin = transform.position + transform.forward * 0.5f + Vector3.up * 0.6f;
        Vector3 target = GetAimPoint();

        inventory.Remove(throwableItemId);

        GameObject can = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        can.name = "ThrownCan";
        can.transform.position = origin;
        can.transform.localScale = new Vector3(0.18f, 0.12f, 0.18f);
        // defaultMaterial boleh kosong (sebelumnya memicu exception -> game ter-pause "freeze").
        Renderer canRenderer = can.GetComponent<Renderer>();

        if (defaultMaterial != null)
            canRenderer.material = new Material(defaultMaterial);

        canRenderer.material.color = new Color(0.75f, 0.78f, 0.8f);

        Collider canCollider = can.GetComponent<Collider>();
        foreach (Collider own in ownColliders)
        {
            if (own != null)
                Physics.IgnoreCollision(canCollider, own);
        }

        Rigidbody body = can.AddComponent<Rigidbody>();
        body.mass = 0.3f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = ComputeBallisticVelocity(origin, target);
        body.angularVelocity = Random.insideUnitSphere * 10f;

        ThrowableNoise noise = can.AddComponent<ThrowableNoise>();
        noise.Configure(throwNoiseRadius, throwNoiseStrength);

        SoundManager.Play("throw", 0.7f, 0.1f);
        CanThrown?.Invoke();

        // Melempar sendiri juga sedikit bersuara.
        NoiseSystem.Emit(transform.position, 1.5f);
    }

    private Vector3 GetAimPoint()
    {
        Vector3 feet = playerController != null ? playerController.FeetPosition : transform.position;
        Vector3 fallback = feet + transform.forward * (maxThrowDistance * 0.65f);

        Camera cam = Camera.main;

        if (cam == null || Mouse.current == null)
            return fallback;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane ground = new Plane(Vector3.up, feet);

        if (!ground.Raycast(ray, out float enter))
            return fallback;

        Vector3 point = ray.GetPoint(enter);
        Vector3 offset = point - feet;
        offset.y = 0f;

        if (offset.magnitude > maxThrowDistance)
            point = feet + offset.normalized * maxThrowDistance;

        // Hadapkan Player ke arah lemparan.
        if (offset.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(offset.normalized);

        return point;
    }

    private static Vector3 ComputeBallisticVelocity(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
        float time = Mathf.Clamp(flat.magnitude / 10f, 0.35f, 1.1f);

        Vector3 velocity = delta / time - 0.5f * Physics.gravity * time;
        return velocity;
    }
}
