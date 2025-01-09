using UnityEngine;

public class CameraController : MonoBehaviour
{
    public float moveSpeed = 10f; // Movement speed
    public float zoomSpeed = 5f; // Speed for zooming in and out
    public float minHeight = 10f; // Minimum camera height
    public float maxHeight = 50f; // Maximum camera height
    public Vector3 rotation = Vector3.zero;

    private void Start()
    {
        // Ensure the camera starts facing directly downward
        transform.rotation = Quaternion.Euler(rotation);
    }

    void Update()
    {
        HandleMovement();
        HandleZoom();
    }

    void HandleMovement()
    {
        // Get input for movement
        float horizontal = Input.GetAxis("Horizontal"); // A/D or Left/Right
        float vertical = Input.GetAxis("Vertical");     // W/S or Up/Down

        // Movement relative to the camera's rotation
        Vector3 forward = transform.forward; // Camera's forward direction
        Vector3 right = transform.right;     // Camera's right direction

        // Remove vertical component (keep movement on the horizontal plane)
        forward.y = 0;
        right.y = 0;
        forward.Normalize();
        right.Normalize();

        // Calculate movement direction
        Vector3 movement = (forward * vertical + right * horizontal) * moveSpeed * Time.deltaTime;

        // Apply movement to the camera's position
        transform.position += movement;
    }

    void HandleZoom()
    {
        // Get input from the mouse scroll wheel
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (Mathf.Abs(scroll) > 0.01f)
        {
            // Adjust the camera's height based on scroll input
            float newHeight = Mathf.Clamp(transform.position.y - scroll * zoomSpeed, minHeight, maxHeight);
            transform.position = new Vector3(transform.position.x, newHeight, transform.position.z);
        }
    }
}
