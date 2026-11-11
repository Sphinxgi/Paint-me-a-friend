using NUnit.Framework;
using System.Collections;
using System;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public static PlayerController Instance { get; private set; }

    [Header("General Raycast")]
    public float generalRaycastDistance = 20f;
    public Ray generalRay { get; private set; }
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float groundDrag = 1f;
    public float maxSpeed = 5f;
    private bool isDecaying = false;
    [Header("Jumping")]
    public float jumpForce = 5f;
    public float jumpCooldown = 1.2f;
    public float airMultiplier = 0.6f;
    public bool isJumpReady = true;
    [Header("Pick Up Object")]
    public bool isHoldingObject { get; private set; }
    public PickUpObject heldObject { get; private set; }
    public Transform holdParent { get; private set; }
    [SerializeField] private LayerMask environmentLayer;
    [SerializeField] private float holdCastRadius = 0.2f;
    [SerializeField] private Vector3 defaultHoldPos;

    [Header("Ground Check")]
    public float playerHeight;
    public LayerMask allGroundLayers;
    [SerializeField] private bool isGrounded;
    public bool IsOnBluePaint { get; private set; }
    public bool IsOnGreenPaint { get; private set; }
    [SerializeField] private float sphereCastRadius = 0.4f;
    public event Action OnGetOnBluePaint;
    public event Action OnGetOnGreenPaint;
    public event Action OnGetOffPaint;

    [Header("Other References")]
    public Transform orientation;
    public PlayerPaintEffects playerPaintEffects { get; private set; }
    [SerializeField] private Camera playerCamera;

    float horizontalInput;
    float verticalInput;

    Vector3 moveDirection;

    public Rigidbody Rb { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        holdParent = GameObject.Find("HoldParent").transform;
        defaultHoldPos = holdParent.localPosition;
    }

    void Start()
    {
        Rb = GetComponent<Rigidbody>();
        Rb.freezeRotation = true;
        playerPaintEffects = GetComponent<PlayerPaintEffects>();
        isJumpReady = true;
        isHoldingObject = false;
        maxSpeed = moveSpeed;
        playerCamera = Camera.main;
    }

    private void Update()
    {
        generalRay = playerCamera.ScreenPointToRay(Input.mousePosition);
        HandleInputs();
        HandlePickUp();

        //Limits Max Speed and such
        AdjustMaxSpeed();
        SpeedControl();

        //Ground Drag
        if (isGrounded)
            Rb.linearDamping = groundDrag;
        else
            Rb.linearDamping = 0f;
    }

    private void FixedUpdate()
    {
        CheckBelow();
        MovePlayer();
    }
    private void LateUpdate()
    {
        UpdateHoldPosition();
    }

    private void HandleInputs()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");

        if (Input.GetKeyDown(KeyCode.Space) && isJumpReady && isGrounded)
        {
            isJumpReady = false;

            Jump();

            Invoke(nameof(RefreshJumpCooldown), jumpCooldown);
        }
    }
    
    private void MovePlayer()
    {
        moveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;
        if (isGrounded)
            Rb.AddForce(moveDirection.normalized * moveSpeed * playerPaintEffects.SpeedMultiplier * 10f, ForceMode.Force);
        else if (!isGrounded)
            Rb.AddForce(moveDirection.normalized * moveSpeed * playerPaintEffects.SpeedMultiplier * 10f * airMultiplier, ForceMode.Force);
    }

    private void AdjustMaxSpeed()
    {
        if (IsOnBluePaint)
        {
            StopCoroutine(DecayMaxSpeed()); // Stop any ongoing decay coroutine if the player is on blue paint
            maxSpeed = moveSpeed * playerPaintEffects.SpeedMultiplier;
            //Debug.Log($"Player is on blue paint, increasing max speed to {maxSpeed}.");
        }
        else
        {
            // Gradually decay the max speed back to the normal moveSpeed when not on blue paint
            if (!isDecaying)
            {
                isDecaying = true;
                StartCoroutine(DecayMaxSpeed());
            }
        }
    }

    private IEnumerator DecayMaxSpeed()
    {
        //Debug.Log($"Player is not on blue paint, decaying max speed from {maxSpeed} to {moveSpeed}.");
        while (maxSpeed > moveSpeed)
        {
            maxSpeed -= 0.4f; // Adjust the decay rate as needed
            if (IsOnBluePaint)
            {
                isDecaying = false; // Stop decaying if the player steps back on blue paint
                //Debug.Log($"Player stepped back on blue paint, stopping decay and setting max speed to {maxSpeed}.");
                yield break;
            }
            yield return new WaitForSeconds(0.1f); // Adjust the wait time as needed
        }
        maxSpeed = moveSpeed; // Ensure it doesn't go below the normal moveSpeed
        isDecaying = false;
        //Debug.Log($"Max speed has decayed to normal move speed: {maxSpeed}.");
    }

    private void SpeedControl()
    {
        Vector3 flatVelocity = new Vector3(Rb.linearVelocity.x, 0f, Rb.linearVelocity.z);

        if (flatVelocity.magnitude > maxSpeed)
        {
            Vector3 limitedVelocity = flatVelocity.normalized * maxSpeed;
            Rb.linearVelocity = new Vector3(limitedVelocity.x, Rb.linearVelocity.y, limitedVelocity.z);
        }
    }

    private void Jump()
    {
        Rb.linearVelocity = new Vector3(Rb.linearVelocity.x, 0f, Rb.linearVelocity.z);
        Rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
    }

    private void RefreshJumpCooldown()
    {
        isJumpReady = true;
    }

    private void CheckBelow()
    {
        isGrounded = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.2f, allGroundLayers);

        if (Physics.SphereCast(transform.position, sphereCastRadius, Vector3.down, out RaycastHit hit, playerHeight * 0.5f + 0.2f))
        {
            PaintPatch paint = hit.collider.GetComponent<PaintPatch>();

            if (paint != null)
            {
                playerPaintEffects.SetPaint(paint.PaintType);
                switch (paint.PaintType)
                {
                    case PaintType.Blue:
                        // Invoke Once
                        if (!IsOnBluePaint)
                        {
                            OnGetOnBluePaint.Invoke();
                            Debug.Log("Player is on blue paint, invoking OnGetOnBluePaint event.");
                        }
                        IsOnBluePaint = true;
                        IsOnGreenPaint = false;
                        break;
                    case PaintType.Green:
                        // Invoke Once
                        if (!IsOnGreenPaint)
                        {
                            OnGetOnGreenPaint.Invoke();
                            Debug.Log("Player is on green paint, invoking OnGetOnGreenPaint event.");
                        }
                        IsOnGreenPaint = true;
                        IsOnBluePaint = false;
                        break;
                    default:
                        IsOnBluePaint = false;
                        IsOnGreenPaint = false;
                        break;
                }
            }
            else
            {
                playerPaintEffects.ClearPaint();
                // Invoke Once
                if (IsOnBluePaint || IsOnGreenPaint)
                {
                    OnGetOffPaint.Invoke();
                    Debug.Log("Player is off paint, invoking OnGetOffPaint event.");
                }
                IsOnBluePaint = false;
                IsOnGreenPaint = false;
            }
        }
    }

    private void HandlePickUp()
    {
        if (isHoldingObject && Input.GetKeyDown(KeyCode.E))
        {
            heldObject.PutDown();
            heldObject = null;
            isHoldingObject = false;
            return;
        }

        if (Physics.Raycast(generalRay, out RaycastHit hit, generalRaycastDistance))
        {
            PickUpObject pickUpObject = hit.collider.GetComponent<PickUpObject>();

            if (pickUpObject != null &&
                Input.GetKeyDown(KeyCode.E) &&
                !pickUpObject.isBeingHeld &&
                !isHoldingObject)
            {
                pickUpObject.PickUp();
                heldObject = pickUpObject;
                isHoldingObject = true;
            }
        }
    }

    private void UpdateHoldPosition()
    {
        // Always reset to the original position first.
        holdParent.localPosition = defaultHoldPos;

        if (!isHoldingObject) return;

        Vector3 origin = transform.position;
        Vector3 target = holdParent.position;

        Vector3 direction = (target - origin).normalized;
        float distance = Vector3.Distance(origin, target);

        if (Physics.SphereCast(
            origin,
            holdCastRadius,
            direction,
            out RaycastHit hit,
            distance,
            environmentLayer,
            QueryTriggerInteraction.Ignore))
        {
            holdParent.position = hit.point - direction * holdCastRadius;
        }
    }
}
