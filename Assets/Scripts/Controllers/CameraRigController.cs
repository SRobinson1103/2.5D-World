using UnityEngine;

public class CameraRigController : MonoBehaviour
{
    public Camera RigCamera;

    

    [Header("Movement Settings")]
    public float moveSpeed = 10f;         // Base movement speed
    public float shiftMultiplier = 2f;    // Speed multiplier when Shift is held
    public float movementSmoothTime = 0.2f; // Smoothing for position
    public float offset = 10;             // horizontal offset from the rig

    [Header("Rotation Settings")]
    public float rotationSpeed = 60f;     // Degrees per second around Y-axis
    public float rotationSmoothTime = 0.2f; // Smoothing for rotation

    [Header("Zoom Settings")]
    public float zoomSpeed = 5f;      // How quickly we zoom in/out
    public float minDistance = 5f;    // Minimum distance from rig
    public float maxDistance = 20f;   // Maximum distance from rig
    public float zoomSmoothTime = 0.2f; // Smoothing for zoom

    // Internal states for smoothing
    private Vector3 movementVelocity;   // Used by Vector3.SmoothDamp
    private float rotationVelocity;     // Used by Mathf.SmoothDampAngle
    private float zoomVelocity;         // Used by Mathf.SmoothDamp

    // We'll store these so we know what final values we want to move/rotate/zoom to:
    private Vector3 targetPosition;
    private float targetYaw;          // We'll only rotate around Y in this example
    private float targetDistance;     // How far camera is from rig

    private void Start()
    {
        RigCamera = GetComponentInChildren<Camera>();
        if(RigCamera == null)
        {
            Debug.Log("CameraRigController : There is no camera attached to the camera rig.");
            return;
        }

        // Initialize our smoothing targets to current values
        targetPosition = transform.position;

        // Extract the current yaw from transform.eulerAngles
        targetYaw = transform.eulerAngles.y;

        // Save the current zoom distance from the rig
        targetDistance = RigCamera.transform.localPosition.magnitude;
    }

    void Update()
    {
        if (RigCamera == null)
            return;

        HandleMovement();
        HandleRotation();
        HandleZoom();
    }

    private void LateUpdate()
    {
        // 1. Smoothly move the rig to targetPosition
        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref movementVelocity,
            movementSmoothTime
        );

        // 2. Smoothly rotate the rig on Y
        float currentYaw = transform.eulerAngles.y;
        float newYaw = Mathf.SmoothDampAngle(
            currentYaw,
            targetYaw,
            ref rotationVelocity,
            rotationSmoothTime
        );
        transform.rotation = Quaternion.Euler(0f, newYaw, 0f);

        // 3. Smoothly zoom camera
        Vector3 localPos = RigCamera.transform.localPosition;
        float currentDist = localPos.magnitude;

        float newDist = Mathf.SmoothDamp(
            currentDist,
            targetDistance,
            ref zoomVelocity,
            zoomSmoothTime
        );

        // Keep direction the same but clamp distance
        newDist = Mathf.Clamp(newDist, minDistance, maxDistance);
        RigCamera.transform.localPosition = localPos.normalized * newDist;

        // Optionally always look at the rig
        RigCamera.transform.LookAt(transform.position);        
    }


    private void HandleMovement()
    {
        // 1. Increase move speed if shift is held
        float actualSpeed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            actualSpeed *= shiftMultiplier;
        }

        // 2. Get input: W/S => Vertical, A/D => Horizontal
        float vertical = Input.GetAxis("Vertical");   // W/S
        float horizontal = Input.GetAxis("Horizontal"); // A/D

        // 3. Get camera's forward and right directions
        //    but zero out the Y so we move only on the XZ plane
        Vector3 camForward = RigCamera.transform.forward;
        Vector3 camRight = RigCamera.transform.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        // 4. Compute desired direction based on camera
        //    e.g., pressing W => move in camForward
        Vector3 moveDir = camForward * vertical + camRight * horizontal;

        // 5. Move the rig
        if (moveDir.sqrMagnitude > 0.001f)
        {
            targetPosition += moveDir * actualSpeed * Time.deltaTime;
        }
    }

    private void HandleRotation()
    {
        // Rotate around Y-axis (Space.World so it’s truly global Y)
        if (Input.GetKey(KeyCode.Q))
        {
            // Rotate left
            targetYaw += rotationSpeed * Time.deltaTime;
        }
        else if (Input.GetKey(KeyCode.E))
        {
            // Rotate right
            targetYaw -= rotationSpeed * Time.deltaTime;
        }
    }

    private void HandleZoom()
    {
        // Check scroll wheel
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            // Decrease targetDistance to zoom in, increase to zoom out
            targetDistance -= scroll * zoomSpeed;
            targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
        }
    }
}
