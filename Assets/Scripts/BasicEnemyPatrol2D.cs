using UnityEngine;

public enum EnemyMovementMode
{
    Simple = 0,
    Advanced = 1
}

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class BasicEnemyPatrol2D : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Rigidbody used for horizontal patrol movement.")]
    [SerializeField] private Rigidbody2D rb;

    [Tooltip("Collider used to estimate the enemy bounds for wall and ledge checks.")]
    [SerializeField] private Collider2D bodyCollider;

    [Tooltip("Optional renderer flipped when the enemy changes direction.")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Tooltip("Optional health component. Dead enemies stop moving.")]
    [SerializeField] private Health2D health;

    [Header("Movement Mode")]
    [Tooltip("Simple uses turn-around movement. Advanced can jump while chasing to cross gaps and obstacles.")]
    [SerializeField] private EnemyMovementMode movementMode = EnemyMovementMode.Simple;

    [Header("Patrol")]
    [Tooltip("Which layers count as level geometry for walls and floor checks.")]
    [SerializeField] private LayerMask groundLayer;

    [Tooltip("Movement speed in world units per second.")]
    [SerializeField] private float moveSpeed = 12f;

    [Tooltip("Start moving right instead of left.")]
    [SerializeField] private bool startMovingRight = false;

    [Tooltip("Extra distance past the front edge used for the ledge check.")]
    [SerializeField] private float edgeCheckForwardDistance = 0.35f;

    [Tooltip("How far down the ledge check looks for ground.")]
    [SerializeField] private float edgeCheckDownDistance = 2.5f;

    [Tooltip("How far ahead the wall check looks.")]
    [SerializeField] private float wallCheckDistance = 0.3f;

    [Tooltip("Vertical offset from the enemy center for the wall check.")]
    [SerializeField] private float wallCheckHeightOffset = 0f;

    [Tooltip("Height above the feet used to detect short obstacles that should trigger an earlier jump.")]
    [SerializeField] private float lowObstacleCheckHeight = 1.2f;

    [Header("Aggro")]
    [Tooltip("If enabled, this enemy can detect and chase the player.")]
    [SerializeField] private bool useAggroChase = false;

    [Tooltip("Target to chase. If empty, the first player Health2D in the scene is used.")]
    [SerializeField] private Transform chaseTarget;

    [Tooltip("How close the player must be before this enemy starts chasing.")]
    [SerializeField] private float aggroRange = 40f;

    [Tooltip("How far above or below this enemy the player can be and still trigger chase.")]
    [SerializeField] private float aggroHeightTolerance = 20f;

    [Tooltip("How close the enemy gets before it stops pushing forward into the target.")]
    [SerializeField] private float chaseStopDistance = 2f;

    [Tooltip("Horizontal speed used while chasing.")]
    [SerializeField] private float chaseSpeed = 16f;

    [Tooltip("If true, the enemy stands still until the player enters aggro range. If false, it patrols until aggro.")]
    [SerializeField] private bool idleWhenNoTarget = true;

    [Header("Advanced Traversal")]
    [Tooltip("How far below the collider we check for ground contact.")]
    [SerializeField] private float groundCheckDistance = 0.2f;

    [Tooltip("Horizontal speed multiplier applied while airborne.")]
    [Range(0f, 1.5f)]
    [SerializeField] private float airControl = 0.9f;

    [Tooltip("Vertical impulse used when the enemy jumps during advanced chase.")]
    [SerializeField] private float jumpForce = 42f;

    [Tooltip("Minimum time between advanced jumps.")]
    [SerializeField] private float jumpCooldown = 0.45f;

    [Tooltip("Extra gravity applied while the enemy is still rising after a jump. Higher values make the jump less floaty.")]
    [SerializeField] private float jumpRiseGravityMultiplier = 6f;

    [Tooltip("Extra gravity applied while the enemy is falling. Higher values make the enemy drop faster after the jump apex.")]
    [SerializeField] private float jumpFallGravityMultiplier = 10f;

    [Tooltip("Extra upward velocity applied once if a traversal jump catches the front lip of an obstacle while still rising.")]
    [SerializeField] private float ledgeClearUpwardBoost = 12f;

    [Tooltip("Minimum forward speed maintained when the ledge-clear assist triggers.")]
    [SerializeField] private float ledgeClearForwardSpeed = 18f;

    [Tooltip("If the enemy catches a wall or ledge in the air, force at least this much downward speed so it can slide off instead of hanging.")]
    [SerializeField] private float stuckWallDropSpeed = 8f;

    [Tooltip("Once vertical speed is below this, the enemy is allowed to repath toward the target while airborne.")]
    [SerializeField] private float fallingRepathVelocityThreshold = -0.5f;

    [Tooltip("If true, the enemy jumps when a wall blocks a chase path.")]
    [SerializeField] private bool jumpOverWalls = true;

    [Tooltip("If true, the enemy jumps when it reaches a gap while chasing.")]
    [SerializeField] private bool jumpAcrossGaps = true;

    [Tooltip("If true, the enemy can jump toward a target that is standing on a higher platform.")]
    [SerializeField] private bool jumpTowardHigherTarget = true;

    [Tooltip("How much higher than the enemy the player must be before a direct upward chase jump is attempted.")]
    [SerializeField] private float minTargetHeightForJump = 4f;

    [Tooltip("Maximum target height difference considered reachable for a chase jump.")]
    [SerializeField] private float maxTargetHeightForJump = 28f;

    [Tooltip("Minimum horizontal distance from the target before a higher-target jump is allowed.")]
    [SerializeField] private float minHorizontalDistanceForJump = 6f;

    [Tooltip("Maximum horizontal distance at which the enemy will proactively jump toward a higher target, even before standing directly underneath.")]
    [SerializeField] private float higherTargetJumpApproachDistance = 18f;

    [Tooltip("If the target is at least this far below the enemy, the enemy will prefer dropping into the gap instead of auto-jumping across it.")]
    [SerializeField] private float lowerTargetDropHeight = 4f;

    [Tooltip("How far ahead to predict traversal based on current chase speed so jumps start before reaching the wall.")]
    [SerializeField] private float traversalAnticipationTime = 0.18f;

    private int facingDirection;
    private int committedAirDirection;
    private bool wasGroundedLastStep;
    private bool canUseLedgeClearAssist;
    private readonly ContactPoint2D[] groundContacts = new ContactPoint2D[8];
    private readonly RaycastHit2D[] wallHits = new RaycastHit2D[8];
    private float nextJumpTime;

    private void Reset()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        health = GetComponent<Health2D>();
    }

    private void Awake()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }

        if (bodyCollider == null)
        {
            bodyCollider = GetComponent<Collider2D>();
        }

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (health == null)
        {
            health = GetComponent<Health2D>();
        }

        ResolveChaseTarget();
        facingDirection = startMovingRight ? 1 : -1;
        ApplyFacing();
    }

    private void FixedUpdate()
    {
        if (health != null && health.IsDead)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        bool isGrounded = IsGrounded();
        UpdateAirborneState(isGrounded);

        if (TryGetChaseMovement(out int chaseDirection, out float desiredSpeed))
        {
            if (movementMode == EnemyMovementMode.Advanced)
            {
                ApplyAdvancedMovement(chaseDirection, desiredSpeed, isGrounded);
                ApplyAdvancedJumpGravity(isGrounded);
                return;
            }

            if (chaseDirection == 0 || IsBlockedInDirection(chaseDirection))
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                return;
            }

            facingDirection = chaseDirection;
            ApplyFacing();
            rb.linearVelocity = new Vector2(chaseDirection * desiredSpeed, rb.linearVelocity.y);
            return;
        }

        if (useAggroChase && idleWhenNoTarget)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        if (movementMode == EnemyMovementMode.Advanced)
        {
            ApplyAdvancedPatrol(isGrounded);
            ApplyAdvancedJumpGravity(isGrounded);
            return;
        }

        if (IsBlockedInDirection(facingDirection))
        {
            facingDirection *= -1;
            ApplyFacing();
        }

        rb.linearVelocity = new Vector2(facingDirection * moveSpeed, rb.linearVelocity.y);
    }

    private void UpdateAirborneState(bool isGrounded)
    {
        if (isGrounded)
        {
            committedAirDirection = 0;
            canUseLedgeClearAssist = false;
        }
        else if (wasGroundedLastStep)
        {
            committedAirDirection = facingDirection != 0 ? facingDirection : (rb.linearVelocity.x >= 0f ? 1 : -1);
        }

        wasGroundedLastStep = isGrounded;
    }

    private bool TryGetChaseMovement(out int chaseDirection, out float desiredSpeed)
    {
        chaseDirection = 0;
        desiredSpeed = 0f;

        if (!useAggroChase)
        {
            return false;
        }

        ResolveChaseTarget();
        if (chaseTarget == null)
        {
            return false;
        }

        Vector2 toTarget = chaseTarget.position - transform.position;
        float horizontalDistance = Mathf.Abs(toTarget.x);
        float verticalDistance = Mathf.Abs(toTarget.y);

        if (horizontalDistance > aggroRange || verticalDistance > aggroHeightTolerance)
        {
            return false;
        }

        desiredSpeed = chaseSpeed;
        if (horizontalDistance <= chaseStopDistance)
        {
            if (movementMode == EnemyMovementMode.Advanced &&
                jumpTowardHigherTarget &&
                toTarget.y >= minTargetHeightForJump &&
                toTarget.y <= maxTargetHeightForJump)
            {
                chaseDirection = Mathf.Abs(toTarget.x) > 0.25f
                    ? (toTarget.x > 0f ? 1 : -1)
                    : (facingDirection != 0 ? facingDirection : 1);
                return true;
            }

            chaseDirection = 0;
            return true;
        }

        chaseDirection = toTarget.x > 0f ? 1 : -1;
        return true;
    }

    private void ApplyAdvancedMovement(int desiredDirection, float desiredSpeed, bool isGrounded)
    {
        if (desiredDirection == 0)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        if (isGrounded && ShouldJumpForTraversal(desiredDirection, desiredSpeed))
        {
            PerformJump(desiredDirection, desiredSpeed);
            isGrounded = false;
        }

        if (isGrounded)
        {
            facingDirection = desiredDirection;
            ApplyFacing();
            rb.linearVelocity = new Vector2(desiredDirection * desiredSpeed, rb.linearVelocity.y);
            return;
        }

        int airDirection = committedAirDirection != 0 ? committedAirDirection : desiredDirection;
        float appliedSpeed = desiredSpeed * airControl;
        bool pressedIntoWall = IsAirborneAndPressedIntoWall(airDirection);
        bool canRepathInAir = rb.linearVelocity.y <= fallingRepathVelocityThreshold;

        if (pressedIntoWall && canUseLedgeClearAssist && rb.linearVelocity.y > 0f)
        {
            canUseLedgeClearAssist = false;
            airDirection = desiredDirection != 0 ? desiredDirection : airDirection;
            committedAirDirection = airDirection;
            float assistedForwardSpeed = Mathf.Max(Mathf.Abs(rb.linearVelocity.x), ledgeClearForwardSpeed);
            float assistedVerticalSpeed = Mathf.Max(rb.linearVelocity.y, ledgeClearUpwardBoost);
            rb.linearVelocity = new Vector2(airDirection * assistedForwardSpeed, assistedVerticalSpeed);
            pressedIntoWall = false;
        }

        if (pressedIntoWall)
        {
            committedAirDirection = 0;
            airDirection = 0;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Min(rb.linearVelocity.y, -stuckWallDropSpeed));
        }

        if (desiredDirection != 0 && (committedAirDirection == 0 || canRepathInAir) && !IsAirborneAndPressedIntoWall(desiredDirection))
        {
            airDirection = desiredDirection;
            committedAirDirection = desiredDirection;
        }

        if (airDirection != 0)
        {
            facingDirection = airDirection;
            ApplyFacing();
        }

        float currentHorizontalSpeed = rb.linearVelocity.x;
        float targetHorizontalSpeed = airDirection * appliedSpeed;

        if (Mathf.Sign(currentHorizontalSpeed) == airDirection && Mathf.Abs(currentHorizontalSpeed) > Mathf.Abs(targetHorizontalSpeed))
        {
            targetHorizontalSpeed = currentHorizontalSpeed;
        }

        rb.linearVelocity = new Vector2(targetHorizontalSpeed, rb.linearVelocity.y);
    }

    private void ApplyAdvancedPatrol(bool isGrounded)
    {
        if (isGrounded && IsBlockedInDirection(facingDirection))
        {
            facingDirection *= -1;
            ApplyFacing();
        }

        if (isGrounded)
        {
            rb.linearVelocity = new Vector2(facingDirection * moveSpeed, rb.linearVelocity.y);
            return;
        }

        int airDirection = committedAirDirection != 0 ? committedAirDirection : facingDirection;
        float appliedSpeed = moveSpeed * airControl;

        if (IsAirborneAndPressedIntoWall(airDirection))
        {
            committedAirDirection = 0;
            airDirection = 0;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Min(rb.linearVelocity.y, -stuckWallDropSpeed));
        }

        if (airDirection != 0)
        {
            facingDirection = airDirection;
            ApplyFacing();
        }

        float currentHorizontalSpeed = rb.linearVelocity.x;
        float targetHorizontalSpeed = airDirection * appliedSpeed;

        if (Mathf.Sign(currentHorizontalSpeed) == airDirection && Mathf.Abs(currentHorizontalSpeed) > Mathf.Abs(targetHorizontalSpeed))
        {
            targetHorizontalSpeed = currentHorizontalSpeed;
        }

        rb.linearVelocity = new Vector2(targetHorizontalSpeed, rb.linearVelocity.y);
    }

    private bool IsBlockedInDirection(int direction)
    {
        if (!HasGroundAhead(direction))
        {
            return true;
        }

        Bounds bounds = bodyCollider.bounds;
        Vector2 wallOrigin = new Vector2(
            bounds.center.x + direction * bounds.extents.x,
            bounds.center.y + wallCheckHeightOffset
        );

        bool wallAhead = HasSolidWallAhead(wallOrigin, direction);
        return wallAhead;
    }

    private bool IsGrounded()
    {
        if (bodyCollider == null)
        {
            return false;
        }

        int contactCount = rb != null ? rb.GetContacts(groundContacts) : 0;
        for (int i = 0; i < contactCount; i++)
        {
            ContactPoint2D contact = groundContacts[i];
            if (contact.collider == null)
            {
                continue;
            }

            if (((1 << contact.collider.gameObject.layer) & groundLayer.value) == 0)
            {
                continue;
            }

            if (contact.normal.y >= 0.2f)
            {
                return true;
            }
        }

        Bounds bounds = bodyCollider.bounds;
        float inset = Mathf.Min(0.25f, bounds.extents.x * 0.5f);
        Vector2 leftOrigin = new Vector2(bounds.min.x + inset, bounds.min.y + 0.05f);
        Vector2 rightOrigin = new Vector2(bounds.max.x - inset, bounds.min.y + 0.05f);

        return Physics2D.Raycast(leftOrigin, Vector2.down, groundCheckDistance, groundLayer) ||
               Physics2D.Raycast(rightOrigin, Vector2.down, groundCheckDistance, groundLayer);
    }

    private bool HasGroundAhead(int direction)
    {
        Bounds bounds = bodyCollider.bounds;
        Vector2 edgeOrigin = new Vector2(
            bounds.center.x + direction * (bounds.extents.x + edgeCheckForwardDistance),
            bounds.min.y + 0.1f
        );

        return Physics2D.Raycast(edgeOrigin, Vector2.down, edgeCheckDownDistance, groundLayer);
    }

    private void ApplyFacing()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = facingDirection > 0;
        }
    }

    private bool HasSolidWallAhead(Vector2 wallOrigin, int direction)
    {
        return HasSolidWallAhead(wallOrigin, direction, wallCheckDistance);
    }

    private bool HasSolidWallAhead(Vector2 wallOrigin, int direction, float distance)
    {
        Vector2 wallDirection = Vector2.right * direction;
        int hitCount = Physics2D.RaycastNonAlloc(wallOrigin, wallDirection, wallHits, distance, groundLayer);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hitCollider = wallHits[i].collider;
            if (hitCollider == null || hitCollider == bodyCollider)
            {
                continue;
            }

            if (hitCollider.GetComponentInParent<Health2D>() != null)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool ShouldJumpForTraversal(int direction, float desiredSpeed)
    {
        if (Time.time < nextJumpTime || chaseTarget == null)
        {
            return false;
        }

        Vector2 toTarget = chaseTarget.position - transform.position;
        float horizontalDistance = Mathf.Abs(toTarget.x);
        float targetHeight = toTarget.y;

        Bounds bounds = bodyCollider.bounds;
        bool wallAhead = HasTraversalObstacleAhead(bounds, direction, desiredSpeed);
        bool gapAhead = !HasGroundAhead(direction);
        bool targetAbove = targetHeight >= minTargetHeightForJump && targetHeight <= maxTargetHeightForJump;
        bool targetBelow = targetHeight <= -lowerTargetDropHeight;
        bool targetCloseEnoughToMatter = horizontalDistance <= aggroRange;
        bool targetFarEnoughToJump = horizontalDistance >= minHorizontalDistanceForJump;
        bool targetDirectlyAbove = horizontalDistance <= Mathf.Max(chaseStopDistance, bounds.extents.x * 0.75f);
        bool targetOnHigherApproach = horizontalDistance <= higherTargetJumpApproachDistance &&
                                      horizontalDistance >= Mathf.Max(chaseStopDistance, bounds.extents.x * 0.5f);

        if (jumpOverWalls && wallAhead)
        {
            return true;
        }

        if (jumpAcrossGaps && gapAhead && targetCloseEnoughToMatter && !targetBelow)
        {
            return true;
        }

        if (jumpTowardHigherTarget &&
            targetAbove &&
            targetCloseEnoughToMatter &&
            ((targetFarEnoughToJump && (wallAhead || gapAhead)) || targetDirectlyAbove || targetOnHigherApproach))
        {
            return true;
        }

        return false;
    }

    private bool IsAirborneAndPressedIntoWall(int direction)
    {
        if (bodyCollider == null || direction == 0)
        {
            return false;
        }

        Bounds bounds = bodyCollider.bounds;
        float probeDistance = Mathf.Max(0.08f, wallCheckDistance * 0.6f);
        Vector2 lowerWallOrigin = new Vector2(
            bounds.center.x + direction * bounds.extents.x,
            bounds.min.y + Mathf.Max(0.2f, bounds.extents.y * 0.45f)
        );
        Vector2 upperWallOrigin = new Vector2(
            bounds.center.x + direction * bounds.extents.x,
            bounds.center.y + wallCheckHeightOffset
        );

        return HasSolidWallAhead(lowerWallOrigin, direction, probeDistance) ||
               HasSolidWallAhead(upperWallOrigin, direction, probeDistance);
    }

    private bool HasTraversalObstacleAhead(Bounds bounds, int direction, float desiredSpeed)
    {
        float anticipatedWallDistance = GetTraversalWallProbeDistance(bounds, desiredSpeed);
        Vector2 lowWallOrigin = new Vector2(
            bounds.center.x + direction * bounds.extents.x,
            bounds.min.y + Mathf.Min(bounds.extents.y * 0.35f, lowObstacleCheckHeight)
        );
        Vector2 lowerWallOrigin = new Vector2(
            bounds.center.x + direction * bounds.extents.x,
            bounds.min.y + Mathf.Max(0.2f, bounds.extents.y * 0.45f)
        );
        Vector2 upperWallOrigin = new Vector2(
            bounds.center.x + direction * bounds.extents.x,
            bounds.center.y + wallCheckHeightOffset
        );

        return HasSolidWallAhead(lowWallOrigin, direction, anticipatedWallDistance) ||
               HasSolidWallAhead(lowerWallOrigin, direction, anticipatedWallDistance) ||
               HasSolidWallAhead(upperWallOrigin, direction, anticipatedWallDistance);
    }

    private float GetTraversalWallProbeDistance(Bounds bounds, float desiredSpeed)
    {
        float sizeBasedPadding = Mathf.Max(0.75f, bounds.extents.x * 0.9f);
        float speedBasedPadding = Mathf.Abs(desiredSpeed) * traversalAnticipationTime;
        return Mathf.Max(wallCheckDistance, wallCheckDistance + sizeBasedPadding + speedBasedPadding);
    }

    private void PerformJump(int direction, float desiredSpeed)
    {
        nextJumpTime = Time.time + jumpCooldown;
        committedAirDirection = direction;
        canUseLedgeClearAssist = true;
        facingDirection = direction;
        ApplyFacing();
        rb.linearVelocity = new Vector2(direction * desiredSpeed, 0f);
        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
    }

    private void ApplyAdvancedJumpGravity(bool isGrounded)
    {
        if (isGrounded)
        {
            return;
        }

        if (rb.linearVelocity.y > 0f && jumpRiseGravityMultiplier > 1f)
        {
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (jumpRiseGravityMultiplier - 1f) * Time.fixedDeltaTime;
            return;
        }

        if (rb.linearVelocity.y < 0f && jumpFallGravityMultiplier > 1f)
        {
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (jumpFallGravityMultiplier - 1f) * Time.fixedDeltaTime;
        }
    }

    private void ResolveChaseTarget()
    {
        if (chaseTarget != null)
        {
            return;
        }

        Health2D[] healths = FindObjectsByType<Health2D>(FindObjectsSortMode.None);
        for (int i = 0; i < healths.Length; i++)
        {
            if (healths[i] != null && healths[i].Team == CombatTeam.Player)
            {
                chaseTarget = healths[i].transform;
                return;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Collider2D colliderToUse = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
        if (colliderToUse == null)
        {
            return;
        }

        Bounds bounds = colliderToUse.bounds;
        int gizmoDirection = facingDirection != 0 ? facingDirection : (startMovingRight ? 1 : -1);

        if (useAggroChase)
        {
            Gizmos.color = new Color(1f, 0.35f, 0.15f, 0.8f);
            Vector3 aggroSize = new Vector3(aggroRange * 2f, aggroHeightTolerance * 2f, 0f);
            Gizmos.DrawWireCube(bounds.center, aggroSize);

            Gizmos.color = new Color(1f, 0.9f, 0.15f, 0.8f);
            Vector3 stopLeft = bounds.center + Vector3.left * chaseStopDistance;
            Vector3 stopRight = bounds.center + Vector3.right * chaseStopDistance;
            Gizmos.DrawLine(stopLeft + Vector3.down * bounds.extents.y, stopLeft + Vector3.up * bounds.extents.y);
            Gizmos.DrawLine(stopRight + Vector3.down * bounds.extents.y, stopRight + Vector3.up * bounds.extents.y);
        }

        if (movementMode == EnemyMovementMode.Advanced)
        {
            Gizmos.color = new Color(1f, 0.4f, 1f, 0.85f);
            Vector3 leftGroundCheck = new Vector3(bounds.min.x + Mathf.Min(0.25f, bounds.extents.x * 0.5f), bounds.min.y + 0.05f, transform.position.z);
            Vector3 rightGroundCheck = new Vector3(bounds.max.x - Mathf.Min(0.25f, bounds.extents.x * 0.5f), bounds.min.y + 0.05f, transform.position.z);
            Gizmos.DrawLine(leftGroundCheck, leftGroundCheck + Vector3.down * groundCheckDistance);
            Gizmos.DrawLine(rightGroundCheck, rightGroundCheck + Vector3.down * groundCheckDistance);
            Gizmos.DrawSphere(leftGroundCheck, 0.12f);
            Gizmos.DrawSphere(rightGroundCheck, 0.12f);
        }

        Vector3 edgeOrigin = new Vector3(
            bounds.center.x + gizmoDirection * (bounds.extents.x + edgeCheckForwardDistance),
            bounds.min.y + 0.1f,
            transform.position.z
        );

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Gizmos.DrawLine(edgeOrigin, edgeOrigin + Vector3.down * edgeCheckDownDistance);
        Gizmos.DrawSphere(edgeOrigin, 0.15f);

        Vector3 wallOrigin = new Vector3(
            bounds.center.x + gizmoDirection * bounds.extents.x,
            bounds.center.y + wallCheckHeightOffset,
            transform.position.z
        );

        Gizmos.color = new Color(0.6f, 1f, 0.25f, 0.9f);
        Gizmos.DrawLine(wallOrigin, wallOrigin + Vector3.right * gizmoDirection * wallCheckDistance);
        Gizmos.DrawSphere(wallOrigin, 0.15f);
    }
}
