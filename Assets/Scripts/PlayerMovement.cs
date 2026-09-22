using System.Collections;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float defaultMoveSpeed = 5f;
    public float groundDrag = 1f;
    [SerializeField] private float currentMoveSpeed;

    [Header("Jumping")]
    public float defaultJumpForce = 5f;
    public float jumpCooldown = 1.2f;
    public float airMultiplier = 0.6f;
    public bool isJumpReady = true;
    [SerializeField] private float currentJumpForce;

    [Header("Ground Check")]
    public float playerHeight;
    public LayerMask whatIsGround;
    [SerializeField] bool isGrounded;

    [Header("Reference")]
    public Transform orientation;

    [Header("Paint Mechanic")]
    [SerializeField] bool isOnBlue;
    [SerializeField] bool isOnGreen;
    public LayerMask blueLayer;
    public LayerMask greenLayer;
    public float blueVelBoost = 1f;
    public float greenJumpBoost = 1f;

    float horizontalInput;
    float verticalInput;

    Vector3 moveDirection;

    Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        isJumpReady = true;
    }

    private void Update()
    {
        // For Ground and Colors
        CheckBelow();
        HandleColorBuffs();
        HandleInputs();

        //Limits Max Speed and such
        SpeedControl();

        //Ground Drag
        if (isGrounded)
            rb.linearDamping = groundDrag;
        else
            rb.linearDamping = 0f;
    }

    private void FixedUpdate()
    {
        MovePlayer();
    }

    private void HandleInputs()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");

        if (Input.GetKeyDown(KeyCode.Space) && isJumpReady && isGrounded)
        {
            Jump();
        }
    }
    
    private void MovePlayer()
    {
        moveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;
        if (isGrounded)
            rb.AddForce(moveDirection.normalized * currentMoveSpeed * 10f, ForceMode.Force);
        else
            rb.AddForce(moveDirection.normalized * currentMoveSpeed * 10f * airMultiplier, ForceMode.Force);
    }

    private void SpeedControl()
    {
        Vector3 flatVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        if (flatVelocity.magnitude > currentMoveSpeed)
        {
            Vector3 limitedVel = flatVelocity.normalized * currentMoveSpeed;
            rb.linearVelocity = new Vector3(limitedVel.x, rb.linearVelocity.y, limitedVel.z);
        }
    }

    private void Jump()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * currentJumpForce * 10f * Time.deltaTime, ForceMode.Impulse);
        isJumpReady = false;
        StartCoroutine(RefreshJumpCooldown());
    }

    IEnumerator RefreshJumpCooldown()
    {
        yield return new WaitForSeconds(jumpCooldown);
        isJumpReady = true;
    }

    private void CheckBelow()
    {
        //Ground Check
        isGrounded = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.2f, whatIsGround);
        //Check Blue
        isOnBlue = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.2f, blueLayer);
        isOnGreen = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.2f, greenLayer);
    }

    private void HandleColorBuffs()
    {
        if (!isOnBlue && !isOnGreen)
        {
            currentMoveSpeed = defaultMoveSpeed;
            currentJumpForce = defaultJumpForce;
        }
        if (isOnBlue)
        {
            currentMoveSpeed = defaultMoveSpeed * blueVelBoost;
        }
        if (isOnGreen)
        {
            currentJumpForce = defaultJumpForce * greenJumpBoost;
        }    
    }
}
