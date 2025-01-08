using UnityEngine;

public class ClickToMove : MonoBehaviour
{
    public float moveSpeed = 5f;       // Movement speed
    public LayerMask terrainLayer;    // Layer for the terrain to interact with
    private Vector3 targetPosition;   // Position to move toward
    private Animator animator;        // Reference to the Animator component
    private SpriteRenderer spriteRenderer; // Reference to the SpriteRenderer for flipping

    void Start()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        //targetPosition = transform.position; // Initialize target to the current position
        float spriteHeight = spriteRenderer.bounds.size.y;
        targetPosition = new Vector3(
                transform.position.x,
                spriteHeight / 2, // Half height ensures the sprite base touches the terrain
                transform.position.z
            );
        if (terrainLayer.value == 0)
        {
            terrainLayer = LayerMask.GetMask("Terrain");
            Debug.Log("LayerMask is empty. Assigned with default value: " + terrainLayer.value);
        }
    }

    void Update()
    {
        HandleInput();
        MoveCharacter();
        transform.forward = Camera.main.transform.forward;
    }

    void HandleInput()
    {
        if (Input.GetMouseButtonDown(0)) // Left mouse button
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, Mathf.Infinity, terrainLayer))
            {
                targetPosition = hit.point; // Set the target position to the hit point
            }
        }
    }

    void MoveCharacter()
    {
        // Calculate the direction to move in
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0; // Ignore vertical movement

        // Check if close to the target
        if (direction.magnitude > 0.1f)
        {
            // Move toward the target
            transform.position += direction.normalized * moveSpeed * Time.deltaTime;

            // Trigger walking animation
            animator.SetBool("isWalking", true);

            // Flip the sprite based on movement direction
            if (direction.x != 0)
            {
                spriteRenderer.flipX = direction.x < 0;
            }
        }
        else
        {
            // Stop walking animation
            animator.SetBool("isWalking", false);
        }
    }
}
