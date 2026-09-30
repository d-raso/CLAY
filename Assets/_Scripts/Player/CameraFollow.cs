using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target; // Drag PlayerCell here
    public float smoothSpeed = 0.125f;
    public Vector3 offset = new Vector3(0, 0, -10); // Keep Z at -10

    [Header("Zoom Settings")]
    public float zoomSpeed = 5f;
    public float minZoom = 3f;   // Closest zoom (smallest orthographic size)
    public float maxZoom = 30f;  // Furthest zoom (largest orthographic size)
    public float defaultZoom = 8f;
    public float zoomSmoothSpeed = 10f;

    [Header("Auto-Zoom (scales with cell size)")]
    [Tooltip("Camera zooms out as the cell grows so the world stays visible (agar.io style).")]
    public bool autoZoom = true;
    public float zoomBase = 5f;        // ortho size when the cell is tiny
    public float zoomPerRadius = 1.5f; // extra ortho size per world unit of cell radius

    private Camera cam;
    private float targetZoom;
    private float manualZoomOffset;    // scroll wheel adds/removes on top of auto-zoom
    private CellBiomass targetBiomass;

    void Start()
    {
        cam = GetComponent<Camera>();

        if (cam == null)
        {
            Debug.LogError("CameraFollow: No Camera component found! This script must be attached to the Camera GameObject.");
            return;
        }

        // Ensure this camera is tagged as MainCamera so Camera.main works
        if (!gameObject.CompareTag("MainCamera"))
        {
            gameObject.tag = "MainCamera";
            Debug.Log("CameraFollow: Set camera tag to MainCamera");
        }

        if (!cam.orthographic)
        {
            Debug.LogWarning("CameraFollow: Camera is not set to Orthographic. Zoom will not work. Change Projection to Orthographic in Camera settings.");
        }

        targetZoom = cam.orthographic ? cam.orthographicSize : defaultZoom;
        if (target != null) targetBiomass = target.GetComponent<CellBiomass>();
    }

    void Update()
    {
        if (cam == null) return;

        // Scroll wheel nudges a manual offset on top of auto-zoom
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (scrollInput != 0f) manualZoomOffset -= scrollInput * zoomSpeed;
        if (Input.GetMouseButtonDown(2)) manualZoomOffset = 0f; // middle-click resets

        if (autoZoom && targetBiomass != null)
        {
            // Bigger cell → larger orthographic size, so the surroundings stay visible.
            float auto = zoomBase + targetBiomass.Radius * zoomPerRadius;
            targetZoom = Mathf.Clamp(auto + manualZoomOffset, minZoom, maxZoom);
        }
        else
        {
            targetZoom = Mathf.Clamp(defaultZoom + manualZoomOffset, minZoom, maxZoom);
        }
    }

    void FixedUpdate()
    {
        if (target == null) return;

        // Smoothly slide the camera to the player's position
        Vector3 desiredPosition = target.position + offset;
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
        transform.position = smoothedPosition;
    }

    void LateUpdate()
    {
        // Smoothly apply zoom
        if (cam != null && cam.orthographic)
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetZoom, Time.deltaTime * zoomSmoothSpeed);
    }

    /// <summary>
    /// Set zoom level directly (useful for events/abilities)
    /// </summary>
    public void SetZoom(float zoom)
    {
        targetZoom = Mathf.Clamp(zoom, minZoom, maxZoom);
    }

    /// <summary>
    /// Reset zoom to default
    /// </summary>
    public void ResetZoom()
    {
        targetZoom = defaultZoom;
    }
}