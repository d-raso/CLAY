using UnityEngine;

namespace CLAY.GalaxyMap
{
    // Free-fly camera. Camera ANGLE is controlled by mouse-drag OR trackpad two-finger scroll (horizontal → yaw,
    // vertical → pitch). WASD/QE fly, +/- dolly. Fly speed scales with distance to the galaxy — slow and precise
    // up close, fast far out. Shift = boost.
    public class GalaxyCameraController : MonoBehaviour
    {
        public float yaw = 0f, pitch = 15f;
        public float lookSensitivity = 4f;
        public float scrollLookSensitivity = 5f;  // trackpad two-finger scroll → camera angle
        public float speed = 25f;                 // base fly speed (scaled by distance to the galaxy)
        public float minSpeed = 0.05f, maxSpeed = 2000f;
        public float boostMultiplier = 5f;

        void Start()
        {
            // Start high above the disk, looking down — a far top-down overview of the whole galaxy.
            transform.position = new Vector3(0f, 230f, -60f);
            yaw = 0f; pitch = 78f;
            Apply();
        }

        void Update()
        {
            // Camera ANGLE: mouse-drag OR trackpad two-finger scroll both rotate the view.
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * lookSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -89f, 89f);
            }
            Vector2 sd = Input.mouseScrollDelta;
            if (sd.sqrMagnitude > 1e-6f)
            {
                yaw += sd.x * scrollLookSensitivity;                                   // horizontal → yaw
                pitch = Mathf.Clamp(pitch - sd.y * scrollLookSensitivity, -89f, 89f);  // vertical → pitch
            }

            var rot = Quaternion.Euler(pitch, yaw, 0f);

            // Fly speed scales with distance to the galaxy centre: slow and precise up close, fast far out.
            float distC = Mathf.Max(transform.position.magnitude, 2f);
            float moveSpeed = Mathf.Clamp(speed * (distC / 100f), minSpeed, maxSpeed);

            // +/- keys ZOOM (dolly along the view), distance-scaled so it's fine near the galaxy, fast far out.
            float dolly = 0f;
            if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus))  dolly += 1f;
            if (Input.GetKey(KeyCode.Minus)  || Input.GetKey(KeyCode.KeypadMinus)) dolly -= 1f;
            if (dolly != 0f)
                transform.position += (rot * Vector3.forward) * dolly * distC * 0.9f * Time.deltaTime;

            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) move -= Vector3.forward;
            if (Input.GetKey(KeyCode.D)) move += Vector3.right;
            if (Input.GetKey(KeyCode.A)) move -= Vector3.right;
            if (Input.GetKey(KeyCode.E)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;

            if (move != Vector3.zero)
            {
                float s = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? boostMultiplier : 1f) * Time.deltaTime;
                transform.position += rot * move.normalized * s;
            }
            transform.rotation = rot;
        }

        void Apply() => transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}
