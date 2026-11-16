using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 2.5f;
    [SerializeField] private float runSpeed = 5f;
    [SerializeField] private float acceleration = 10f;
    [SerializeField] private float turnSpeed = 720f;
    [SerializeField] private float gravity = -20f;

    [Header("Crouch (Ctrl) - mode toggle/tahan diatur di menu Pengaturan")]
    [SerializeField] private float crouchSpeed = 1.4f;
    [SerializeField] private float crouchHeight = 1.1f;
    [SerializeField] private float crouchVisualScale = 0.6f;

    [Header("Dash / Roll (Space)")]
    [SerializeField] private float dashDistance = 4.5f;
    [SerializeField] private float dashDuration = 0.4f;
    [SerializeField] private float dashCooldown = 0.8f;
    [Tooltip("Model yang diputar saat berguling. Kosong = transform milik Animator.")]
    [SerializeField] private Transform rollVisual;

    [Header("Noise (Stealth)")]
    [SerializeField] private float crouchNoiseRadius = 0.6f;
    [SerializeField] private float walkNoiseRadius = 2f;
    [SerializeField] private float runNoiseRadius = 8f;
    [SerializeField] private float dashNoiseRadius = 5f;
    [SerializeField] private float noiseInterval = 0.3f;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    private CharacterController controller;
    private Transform cameraTransform;

    private InputAction moveAction;
    private InputAction sprintAction;
    private InputAction dashAction;
    private InputAction crouchAction;

    private Vector3 moveDirection;
    private float currentSpeed;
    private float verticalVelocity;

    // Crouch
    private float standHeight;
    private Vector3 standCenter;
    private bool wantsCrouch;
    private float crouchBlend;

    // Dash
    private Vector3 dashDirection;
    private float dashTimer;
    private float nextDashTime;

    // Visual
    private Vector3 visualBaseLocalPosition;
    private Quaternion visualBaseLocalRotation;
    private Vector3 visualBaseLocalScale;

    // Noise & footsteps
    private float noiseTimer;
    private float footstepTimer;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    public bool IsRunning { get; private set; }
    public bool IsDashing { get; private set; }
    public bool IsCrouching { get; private set; }
    public float CurrentNoiseRadius { get; private set; }
    public float MaxNoiseRadius => runNoiseRadius;

    // Telapak kaki Player (tidak berubah saat jongkok).
    public Vector3 FeetPosition =>
        transform.position + Vector3.up * (standCenter.y - standHeight * 0.5f);

    // Titik yang dicek penjaga / kamera untuk melihat Player.
    // Saat jongkok titik ini rendah sehingga tertutup dinding rendah.
    public Vector3 VisibilityPoint =>
        FeetPosition + Vector3.up * (IsCrouching ? 0.7f : 1.5f);

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        standHeight = controller.height;
        standCenter = controller.center;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (rollVisual == null && animator != null && animator.transform != transform)
            rollVisual = animator.transform;

        if (rollVisual != null)
        {
            visualBaseLocalPosition = rollVisual.localPosition;
            visualBaseLocalRotation = rollVisual.localRotation;
            visualBaseLocalScale = rollVisual.localScale;
        }

        if (Camera.main != null)
            cameraTransform = Camera.main.transform;

        moveAction = InputSystem.actions.FindAction("Player/Move");
        sprintAction = InputSystem.actions.FindAction("Player/Sprint");
        crouchAction = InputSystem.actions.FindAction("Player/Crouch");

        // Pakai action "Dash" jika ada, jika tidak pakai "Jump" (Space / Gamepad South).
        dashAction = InputSystem.actions.FindAction("Player/Dash")
                     ?? InputSystem.actions.FindAction("Player/Jump");
    }

    private void OnDisable()
    {
        if (IsDashing)
            EndDash();
    }

    private void Update()
    {
        if (!StealthGameManager.IsPlaying)
            return;

        Vector2 moveInput = moveAction.ReadValue<Vector2>();

        Vector3 input = new Vector3(moveInput.x, 0f, moveInput.y);

        if (input.sqrMagnitude > 1f) input.Normalize();

        bool hasInput = input.sqrMagnitude > 0f;

        UpdateCrouchInput(hasInput);

        if (!IsDashing &&
            dashAction != null &&
            dashAction.WasPressedThisFrame() &&
            Time.time >= nextDashTime)
        {
            StartDash(hasInput ? ToCameraRelative(input) : transform.forward);
        }

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 horizontalMove;

        if (IsDashing)
        {
            horizontalMove = UpdateDash();
        }
        else
        {
            IsRunning = hasInput && !IsCrouching && sprintAction.IsPressed();

            float targetSpeed = 0f;

            if (hasInput)
            {
                targetSpeed = IsCrouching ? crouchSpeed : IsRunning ? runSpeed : walkSpeed;
                moveDirection = ToCameraRelative(input);
            }

            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);

            horizontalMove = moveDirection * currentSpeed;

            if (hasInput)
            {
                Quaternion targetRotation =
                    Quaternion.LookRotation(moveDirection);

                transform.rotation =
                    Quaternion.RotateTowards(
                        transform.rotation,
                        targetRotation,
                        turnSpeed * Time.deltaTime
                    );
            }
        }

        Vector3 velocity = horizontalMove + Vector3.up * verticalVelocity;

        controller.Move(velocity * Time.deltaTime);

        Vector3 horizontalVelocity = controller.velocity;
        horizontalVelocity.y = 0f;

        UpdateNoise(horizontalVelocity.magnitude);
        UpdateFootsteps(horizontalVelocity.magnitude);
        UpdateVisual();

        if (animator != null)
        {
            animator.SetFloat(
                SpeedHash,
                IsDashing ? runSpeed : horizontalVelocity.magnitude,
                0.1f,
                Time.deltaTime
            );
        }
    }

    // ==================================================
    // CROUCH
    // ==================================================

    private void UpdateCrouchInput(bool hasInput)
    {
        if (crouchAction != null)
        {
            if (GameSettings.CrouchToggle)
            {
                if (crouchAction.WasPressedThisFrame())
                    wantsCrouch = !wantsCrouch;
            }
            else
            {
                wantsCrouch = crouchAction.IsPressed();
            }
        }

        // Menekan Shift saat jongkok = berdiri lalu lari.
        if (wantsCrouch && hasInput && sprintAction.WasPressedThisFrame())
            wantsCrouch = false;

        if (wantsCrouch && !IsCrouching)
            SetCrouch(true);
        else if (!wantsCrouch && IsCrouching && CanStandUp())
            SetCrouch(false);
    }

    private void SetCrouch(bool crouch)
    {
        IsCrouching = crouch;
        SoundManager.Play("crouch", 0.6f);

        float height = crouch ? crouchHeight : standHeight;

        // Kaki tetap di tempat: geser center ke bawah sebesar selisih tinggi.
        controller.height = height;
        controller.center = standCenter + Vector3.down * ((standHeight - height) * 0.5f);
    }

    // Tidak bisa berdiri jika ada atap rendah (misal di dalam ventilasi).
    private bool CanStandUp()
    {
        float radius = controller.radius * 0.9f;
        Vector3 bottom = FeetPosition + Vector3.up * (crouchHeight + 0.05f);
        Vector3 top = FeetPosition + Vector3.up * (standHeight - controller.radius);

        if (top.y < bottom.y)
            top = bottom;

        int mask = ~(1 << gameObject.layer);

        return !Physics.CheckCapsule(bottom, top, radius, mask, QueryTriggerInteraction.Ignore);
    }

    // ==================================================
    // DASH / ROLL
    // ==================================================

    private void StartDash(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            direction = transform.forward;

        dashDirection = direction.normalized;
        dashTimer = 0f;
        IsDashing = true;
        IsRunning = false;

        transform.rotation = Quaternion.LookRotation(dashDirection);

        // Berguling menimbulkan suara.
        SoundManager.Play("roll", 0.8f, 0.08f);
        NoiseSystem.Emit(transform.position, dashNoiseRadius);
        NoisePulse.Spawn(FeetPosition, dashNoiseRadius, new Color(0.4f, 0.8f, 1f));
        noiseTimer = noiseInterval;
    }

    private Vector3 UpdateDash()
    {
        dashTimer += Time.deltaTime;

        float t = Mathf.Clamp01(dashTimer / dashDuration);

        // Kecepatan melambat di akhir: v(t) = peak * (1 - t^2), rata-rata = 2/3 peak.
        float peakSpeed = dashDistance / dashDuration * 1.5f;
        float speed = peakSpeed * (1f - t * t);

        if (t >= 1f)
            EndDash();

        return dashDirection * speed;
    }

    private void EndDash()
    {
        IsDashing = false;
        nextDashTime = Time.time + dashCooldown;

        moveDirection = dashDirection;
        currentSpeed = IsCrouching ? crouchSpeed : walkSpeed;
    }

    // ==================================================
    // VISUAL (jongkok = model dipendekkan, guling = model diputar)
    // ==================================================

    private void UpdateVisual()
    {
        if (rollVisual == null)
            return;

        crouchBlend = Mathf.MoveTowards(crouchBlend, IsCrouching ? 1f : 0f, 6f * Time.deltaTime);

        Vector3 scale = visualBaseLocalScale;
        scale.y *= Mathf.Lerp(1f, crouchVisualScale, crouchBlend);

        Quaternion roll = Quaternion.identity;

        if (IsDashing)
        {
            float t = Mathf.Clamp01(dashTimer / dashDuration);
            float angle = 360f * (1f - (1f - t) * (1f - t));
            roll = Quaternion.AngleAxis(angle, Vector3.right);
        }

        // Putar model berporos di tengah capsule.
        Vector3 pivot = controller.center;

        rollVisual.localScale = scale;
        rollVisual.localPosition = pivot + roll * (visualBaseLocalPosition - pivot);
        rollVisual.localRotation = roll * visualBaseLocalRotation;
    }

    // ==================================================
    // NOISE
    // ==================================================

    private void UpdateNoise(float horizontalSpeed)
    {
        noiseTimer -= Time.deltaTime;

        if (horizontalSpeed < 0.1f)
        {
            CurrentNoiseRadius = 0f;
            return;
        }

        CurrentNoiseRadius =
            IsDashing ? dashNoiseRadius :
            IsRunning ? runNoiseRadius :
            IsCrouching ? crouchNoiseRadius : walkNoiseRadius;

        if (noiseTimer <= 0f)
        {
            NoiseSystem.Emit(transform.position, CurrentNoiseRadius);
            noiseTimer = noiseInterval;

            if (IsRunning)
                NoisePulse.Spawn(FeetPosition, CurrentNoiseRadius, new Color(1f, 1f, 1f, 0.5f));
        }
    }

    // Bunyi langkah kaki (hanya terdengar oleh pemain, beda dengan NoiseSystem untuk penjaga).
    private void UpdateFootsteps(float horizontalSpeed)
    {
        if (horizontalSpeed < 0.3f || IsDashing)
        {
            footstepTimer = Mathf.Min(footstepTimer, 0.1f);
            return;
        }

        footstepTimer -= Time.deltaTime;

        if (footstepTimer > 0f)
            return;

        float volume = IsRunning ? 0.75f : IsCrouching ? 0.15f : 0.35f;
        SoundManager.Play("footstep", volume, 0.12f);

        footstepTimer = IsRunning ? 0.3f : IsCrouching ? 0.6f : 0.45f;
    }

    private Vector3 ToCameraRelative(Vector3 input)
    {
        if (cameraTransform == null)
            return input;

        Quaternion cameraYaw =
            Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f);

        return cameraYaw * input;
    }

    private void OnDrawGizmosSelected()
    {
        if (CurrentNoiseRadius <= 0f)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, CurrentNoiseRadius);
    }
}
