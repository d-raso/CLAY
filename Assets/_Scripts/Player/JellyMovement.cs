using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class JellyMovement : MonoBehaviour
{
    public enum ControlMode { WASD, FollowMouse }

    [Header("Control Settings")]
    public ControlMode controlMode = ControlMode.WASD;
    [Tooltip("Key to toggle between control modes")]
    public KeyCode toggleKey = KeyCode.Tab;

    [Header("Movement")]
    public float acceleration = 30f;
    public float maxSpeed = 10f;
    [Tooltip("Water resistance — higher = stops faster")]
    public float drag = 2f;

    [Header("Mouse Follow")]
    [Tooltip("Dead zone radius — won't move if mouse is this close")]
    public float mouseDeadZone = 0.5f;
    [Tooltip("Distance at which full speed is reached")]
    public float mouseMaxDistance = 5f;

    [Tooltip("Extra speed multiplier from developed parts (e.g. a flagellum). 1 = none.")]
    public float externalSpeedMultiplier = 1f;

    [Header("Flow Field")]
    [Range(0f, 2f)]
    public float flowFieldInfluence = 1f;
    [Tooltip("Separate scale for flow force — increase to 20-40 for noticeable current push")]
    public float flowForceScale = 20f;

    private Rigidbody2D rb;
    private Vector2 moveInput;
    private Camera mainCamera;
    private CellBiomass biomass;   // optional — scales speed with size

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.linearDamping = 0f;
        mainCamera = Camera.main;
        biomass = GetComponent<CellBiomass>();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            controlMode = controlMode == ControlMode.WASD ? ControlMode.FollowMouse : ControlMode.WASD;
            Debug.Log($"[JellyMovement] Control mode → {controlMode}");
        }

        switch (controlMode)
        {
            case ControlMode.WASD:       GetWASDInput();  break;
            case ControlMode.FollowMouse: GetMouseInput(); break;
        }
    }

    void GetWASDInput()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float y = Input.GetAxisRaw("Vertical");
        // W (+Vertical) moves up in the Y-up 2D world — no flip needed.
        moveInput = new Vector2(x, y).normalized;
    }

    void GetMouseInput()
    {
        if (mainCamera == null) return;

        Vector3 mouseWorld = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = 0f;

        Vector2 toMouse = (Vector2)mouseWorld - (Vector2)transform.position;
        float distance = toMouse.magnitude;

        if (distance < mouseDeadZone)
        {
            moveInput = Vector2.zero;
            return;
        }

        float strength = Mathf.Clamp01(Mathf.InverseLerp(mouseDeadZone, mouseMaxDistance, distance));
        // Move toward the cursor's world position directly (no flip).
        moveInput = new Vector2(toMouse.x, toMouse.y).normalized * strength;
    }

    void FixedUpdate()
    {
        // Biomass slows you as you grow (speed ∝ mass^-exp) — the agar.io tradeoff.
        // Developed parts (flagellum) multiply it back up.
        float speedMul = (biomass != null ? biomass.SpeedMultiplier : 1f) * externalSpeedMultiplier;

        if (moveInput.sqrMagnitude > 0f)
            rb.AddForce(moveInput * acceleration * speedMul);

        // Flow field is now applied by the FlowFieldPush component (pushes the whole soft body,
        // mass-scaled) — shared with AI cells so everything drifts consistently. See FlowFieldPush.cs.

        float effectiveMaxSpeed = maxSpeed * speedMul;
        if (rb.linearVelocity.magnitude > effectiveMaxSpeed)
            rb.linearVelocity = rb.linearVelocity.normalized * effectiveMaxSpeed;

        rb.AddForce(-rb.linearVelocity * drag);
    }

    public void SetControlMode(ControlMode mode) => controlMode = mode;
}
