using UnityEngine;

/// <summary>
/// Attach to any GameObject to make it move with parallax relative to the camera.
/// Objects with parallaxMultiplier less than 1 appear further away (background).
/// Objects with parallaxMultiplier greater than 1 appear closer (foreground).
/// </summary>
public class ParallaxLayer : MonoBehaviour
{
    [Header("Parallax Settings")]
    [Tooltip("0 = static (infinitely far), 1 = moves with camera (play layer), >1 = foreground")]
    public float parallaxMultiplier = 1f;

    [Header("Infinite Scrolling")]
    [Tooltip("Enable to tile horizontally as camera moves")]
    public bool infiniteHorizontal = false;
    [Tooltip("Enable to tile vertically as camera moves")]
    public bool infiniteVertical = false;
    [Tooltip("Size of the repeating tile (set to sprite width/height)")]
    public Vector2 tileSize = new Vector2(20f, 20f);

    [Header("Visual Settings")]
    [Tooltip("Sorting order for this layer")]
    public int sortingOrder = 0;
    [Tooltip("Sorting layer name")]
    public string sortingLayerName = "Default";

    // Runtime
    private Transform cameraTransform;
    private Vector3 previousCameraPosition;
    private SpriteRenderer spriteRenderer;

    void Start()
    {
        cameraTransform = Camera.main.transform;
        previousCameraPosition = cameraTransform.position;

        // Apply sorting settings
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.sortingOrder = sortingOrder;
            spriteRenderer.sortingLayerName = sortingLayerName;
        }

        // Auto-detect tile size from sprite if not set
        if (spriteRenderer != null && tileSize == Vector2.zero)
        {
            tileSize = spriteRenderer.bounds.size;
        }
    }

    void LateUpdate()
    {
        if (cameraTransform == null) return;

        // Calculate how much the camera moved this frame
        Vector3 cameraDelta = cameraTransform.position - previousCameraPosition;

        // Move this layer by a fraction of the camera movement
        // parallaxMultiplier = 0 means layer doesn't move (stays fixed in world)
        // parallaxMultiplier = 1 means layer moves exactly with camera (normal gameplay layer)
        // parallaxMultiplier = 0.5 means layer moves half as fast (appears further away)
        // The formula: layer moves by (1 - parallaxMultiplier) opposite to camera
        // This creates the illusion that lower multiplier = further away
        float parallaxX = cameraDelta.x * (1f - parallaxMultiplier);
        float parallaxY = cameraDelta.y * (1f - parallaxMultiplier);

        transform.position += new Vector3(parallaxX, parallaxY, 0f);

        // Handle infinite scrolling
        if (infiniteHorizontal || infiniteVertical)
        {
            HandleInfiniteScroll();
        }

        previousCameraPosition = cameraTransform.position;
    }

    void HandleInfiniteScroll()
    {
        Vector3 cameraPos = cameraTransform.position;
        Vector3 layerPos = transform.position;

        if (infiniteHorizontal && tileSize.x > 0)
        {
            // Calculate offset from camera in tile units
            float offsetX = cameraPos.x - layerPos.x;

            // If we've scrolled more than half a tile, wrap around
            if (Mathf.Abs(offsetX) >= tileSize.x)
            {
                float sign = Mathf.Sign(offsetX);
                layerPos.x += sign * tileSize.x;
            }
        }

        if (infiniteVertical && tileSize.y > 0)
        {
            float offsetY = cameraPos.y - layerPos.y;

            if (Mathf.Abs(offsetY) >= tileSize.y)
            {
                float sign = Mathf.Sign(offsetY);
                layerPos.y += sign * tileSize.y;
            }
        }

        transform.position = layerPos;
    }

    /// <summary>
    /// Set the parallax multiplier at runtime
    /// </summary>
    public void SetParallaxMultiplier(float multiplier)
    {
        parallaxMultiplier = multiplier;
    }

    /// <summary>
    /// Snap the layer position to align with camera (useful after teleporting)
    /// </summary>
    public void SnapToCamera()
    {
        if (cameraTransform != null)
        {
            previousCameraPosition = cameraTransform.position;
        }
    }
}
