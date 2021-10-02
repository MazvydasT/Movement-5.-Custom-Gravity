using UnityEngine;

public class MovingSphere : MonoBehaviour
{
    [SerializeField]
    Transform playerInputSpace = default;

    [SerializeField, Range(0f, 100f)]
    float maxSpeed = 10f;

    [SerializeField, Range(0f, 100f)]
    float maxSnapSpeed = 100f;

    [SerializeField, Min(0f)]
    float probeDistance = 1f;

    [SerializeField, Range(0f, 100f)]
    float maxAcceleration = 10f, maxAirAcceleration = 1f;

    [SerializeField, Range(0f, 10f)]
    float jumpHeight = 2f;

    [SerializeField, Range(0, 5)]
    int maxAirJumps = 0;

    [SerializeField, Range(0f, 90f)]
    float maxGroundAngle = 25f, maxStairsAngle = 50f;

    [SerializeField]
    LayerMask probeMask = -1, stairsMask = -1;

    Rigidbody rigidBody;

    Vector3 velocity, desiredVelocity, contactNormal, steepNormal;

    bool desiredJump;

    int groundContactCount, steepContactCount;

    int jumpPhase = 0;

    float minGroundDotProduct, minStairsDotProduct;

    bool OnGround => groundContactCount > 0;
    bool OnSteep => steepContactCount > 0;

    int stepsSinceLastGrounded, stepsSinceLastJump;

    void OnValidate()
    {
        minGroundDotProduct = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
        minStairsDotProduct = Mathf.Cos(maxStairsAngle * Mathf.Deg2Rad);
    }

    void Awake()
    {
        rigidBody = GetComponent<Rigidbody>();

        OnValidate();
    }

    void Update()
    {
        var playerInput = Vector2.ClampMagnitude(
            new Vector2(
                Input.GetAxis("Horizontal"),
                Input.GetAxis("Vertical")
            ),
            1f
        );

        if (playerInputSpace)
        {
            var forward = playerInputSpace.forward;
            forward.y = 0f;
            forward.Normalize();

            var right = playerInputSpace.right;
            right.y = 0f;
            right.Normalize();

            desiredVelocity = (forward * playerInput.y + right * playerInput.x) * maxSpeed;
        }

        else
            desiredVelocity = new Vector3(playerInput.x, 0f, playerInput.y) * maxSpeed;

        desiredJump |= Input.GetButtonDown("Jump");

        //GetComponent<Renderer>().material.SetColor("_Color", OnGround ? Color.black : Color.white);
    }

    void FixedUpdate()
    {
        UpdateState();
        AdjustVelocity();

        if (desiredJump)
        {
            desiredJump = false;
            Jump();
        }

        rigidBody.velocity = velocity;

        ClearState();
    }

    float GetMinDot(int layer)
    {
        return (stairsMask & (1 << layer)) == 0 ? minGroundDotProduct : minStairsDotProduct;
    }

    void ClearState()
    {
        groundContactCount = steepContactCount = 0;
        contactNormal = steepNormal = Vector3.zero;
    }

    void Jump()
    {
        Vector3 jumpDirection;

        if (OnGround) jumpDirection = contactNormal;

        else if (OnSteep)
        {
            jumpDirection = steepNormal;
            jumpPhase = 0;
        }

        else if (maxAirJumps > 0 && jumpPhase <= maxAirJumps)
        {
            if (jumpPhase == 0) jumpPhase = 1;

            jumpDirection = contactNormal;
        }

        else return;

        stepsSinceLastJump = 0;
        ++jumpPhase;

        var jumpSpeed = Mathf.Sqrt(-2f * Physics.gravity.y * jumpHeight);

        jumpDirection = (jumpDirection + Vector3.up).normalized;

        var alignedSpeed = Vector3.Dot(velocity, jumpDirection);

        if (alignedSpeed > 0)
        {
            jumpSpeed = Mathf.Max(jumpSpeed - alignedSpeed, 0f);
        }

        velocity += jumpDirection * jumpSpeed;
    }

    bool SnapToGround()
    {
        if (stepsSinceLastGrounded > 1 || stepsSinceLastJump <= 2) return false;

        var speed = velocity.magnitude;
        if (speed > maxSnapSpeed) return false;

        if (!Physics.Raycast(rigidBody.position, Vector3.down, out var hit, probeDistance, probeMask)) return false;

        if (hit.normal.y < GetMinDot(hit.collider.gameObject.layer)) return false;

        groundContactCount = 1;
        contactNormal = hit.normal;
        var dot = Vector3.Dot(velocity, hit.normal);

        if (dot > 0) velocity = (velocity - hit.normal * dot).normalized * speed;

        return true;
    }

    bool CheckSteepContacts()
    {
        if (steepContactCount > 1)
        {
            steepNormal.Normalize();

            if (steepNormal.y >= minGroundDotProduct)
            {
                groundContactCount = 1;
                contactNormal = steepNormal;
                return true;
            }
        }

        return false;
    }

    void UpdateState()
    {
        ++stepsSinceLastGrounded;
        ++stepsSinceLastJump;

        velocity = rigidBody.velocity;

        if (OnGround || SnapToGround() || CheckSteepContacts())
        {
            stepsSinceLastGrounded = 0;

            if (stepsSinceLastJump > 1) jumpPhase = 0;

            if (groundContactCount > 1)
            {
                contactNormal.Normalize();
            }
        }

        else
        {
            contactNormal = Vector3.up;
        }
    }

    void OnCollisionStay(Collision collision)
    {
        EvaluateColision(collision);
    }

    void OnCollisionEnter(Collision collision)
    {
        EvaluateColision(collision);
    }

    void EvaluateColision(Collision collision)
    {
        var minDot = GetMinDot(collision.gameObject.layer);

        for (var i = 0; i < collision.contactCount; ++i)
        {
            var normal = collision.GetContact(i).normal;

            if (normal.y >= minDot)
            {
                ++groundContactCount;

                contactNormal += normal;
            }

            else if (normal.y > -0.01f)
            {
                ++steepContactCount;
                steepNormal += normal;
            }
        }
    }

    Vector3 ProjectOnContactPlane(Vector3 vector)
    {
        return vector - contactNormal * Vector3.Dot(vector, contactNormal);
    }

    void AdjustVelocity()
    {
        var xAxis = ProjectOnContactPlane(Vector3.right).normalized;
        var zAxis = ProjectOnContactPlane(Vector3.forward).normalized;

        var currentX = Vector3.Dot(velocity, xAxis);
        var currentZ = Vector3.Dot(velocity, zAxis);

        var acceleration = OnGround ? maxAcceleration : maxAirAcceleration;
        var maxSpeedChange = acceleration * Time.deltaTime;

        var newX = Mathf.MoveTowards(currentX, desiredVelocity.x, maxSpeedChange);
        var newZ = Mathf.MoveTowards(currentZ, desiredVelocity.z, maxSpeedChange);

        velocity += xAxis * (newX - currentX) + zAxis * (newZ - currentZ);
    }
}
