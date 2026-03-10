using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public class AdvancedEnemyTraversal2D : MonoBehaviour
{
    private const float WallMinSurfaceAngle = 55f;
    private const float WalkableGroundMinNormalY = 0.55f;
    private const float SeamSuppressMaxLowHitDistance = 0.2f;

    [Header("Target")]
    [Tooltip("Optional direct reference to the player target. If empty, the script tries to find a GameObject by Player Tag.")]
    public Transform player;

    [Tooltip("Tag used to auto-find the player when Player is not assigned.")]
    public string playerTag = "Player";

    [Header("Movement")]
    [Tooltip("Horizontal chase speed while the enemy is grounded.")]
    public float chaseSpeed = 2f;

    [Tooltip("Upward impulse applied when the enemy jumps.")]
    public float jumpForce = 2f;

    [Tooltip("Horizontal control multiplier while airborne. 0 means none, 1 means same as ground chase speed.")]
    [Range(0f, 1.5f)] public float airControl = 0.8f;

    [Tooltip("How far ahead we probe while airborne to detect a wall/overhang that would cause sticking.")]
    public float airborneWallProbeDistance = 0.12f;

    [Tooltip("Small horizontal push away from a wall while airborne to help detach from corners/overhangs.")]
    public float wallDetachPush = 0.8f;

    [Tooltip("Minimum downward speed enforced when airborne and pressed into a wall.")]
    public float stuckWallDropSpeed = 1.5f;

    [Tooltip("Time after a jump starts during which anti-stick wall detach is ignored while moving upward.")]
    public float wallDetachJumpGraceTime = 0.2f;

    [Header("Backoff")]
    [Tooltip("How long the enemy moves away from a wall after a failed jump attempt.")]
    public float failedJumpBackoffDuration = 0.25f;

    [Tooltip("Ground speed used while backing away from a wall after a failed jump.")]
    public float failedJumpBackoffSpeed = 2.5f;

    [Tooltip("Minimum delay between jump attempts.")]
    public float jumpCooldown = 0.2f;

    [Tooltip("Layers that count as level geometry for ground, wall, and gap checks.")]
    public LayerMask groundLayer;

    [Header("Checks")]
    [Tooltip("How far below the feet we check for grounded state.")]
    public float groundCheckDistance = 0.2f;

    [Tooltip("How far ahead we check for walls/steps that require a jump.")]
    public float forwardCheckDistance = 2f;

    [Tooltip("How far down from the forward probe we check for a hole.")]
    public float gapCheckDownDistance = 2f;

    [Tooltip("How far above the enemy we check for geometry when evaluating upward jumps.")]
    public float aboveCheckDistance = 3f;

    [Tooltip("Vertical offset for the upper forward probe used to detect step-like obstacles.")]
    public float upperForwardProbeHeight = 0.35f;

    [Tooltip("Maximum horizontal distance from the player for the 'player above' jump helper.")]
    public float playerAboveHorizontalTolerance = 0.6f;

    [Tooltip("Minimum absolute ground-height change ahead (up or down) required to trigger jump behavior. Smaller changes are treated as continuous terrain.")]
    [Min(0f)] public float terrainHeightJumpThreshold = 0.12f;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private bool isGrounded;
    private bool shouldJump;
    private float nextJumpTime;
    private float lastJumpTime = -999f;
    private bool backoffActive;
    private bool pendingBackoffOnLand;
    private int failedWallDirection;
    private int backoffDirection;
    private float backoffEndTime;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
    private ContactFilter2D castFilter;
    private bool debugWallLowBlocks;
    private bool debugWallUpperBlocks;
    private bool debugWallAheadRaw;
    private bool debugWallSuppressedByContinuity;
    private bool debugWallAheadFinal;
    private bool debugHoleAhead;
    private Vector3 debugFrontFootOrigin;
    private Vector3 debugFrontUpperOrigin;
    private Vector3 debugGapOrigin;
    private float debugForwardDistance;
    private float debugGapDistance;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        castFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = groundLayer,
            useTriggers = false
        };
        ResolvePlayerIfNeeded();
    }

    private void Update()
    {
        ResolvePlayerIfNeeded();

        if (player == null)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            shouldJump = false;
            return;
        }

        isGrounded = CheckGrounded();
        float direction = Mathf.Sign(player.position.x - transform.position.x);

        if (isGrounded)
        {
            if (pendingBackoffOnLand)
            {
                pendingBackoffOnLand = false;
                backoffActive = true;
                backoffDirection = failedWallDirection != 0 ? -failedWallDirection : -(direction > 0f ? 1 : (direction < 0f ? -1 : 0));
                backoffEndTime = Time.time + Mathf.Max(0f, failedJumpBackoffDuration);
            }

            if (backoffActive)
            {
                if (Time.time < backoffEndTime && backoffDirection != 0)
                {
                    rb.linearVelocity = new Vector2(backoffDirection * Mathf.Max(0f, failedJumpBackoffSpeed), rb.linearVelocity.y);
                    shouldJump = false;
                    return;
                }

                backoffActive = false;
            }

            rb.linearVelocity = new Vector2(direction * chaseSpeed, rb.linearVelocity.y);
            shouldJump = ShouldJumpFromGround((int)direction);
        }
        else
        {
            int directionSign = direction > 0f ? 1 : (direction < 0f ? -1 : 0);
            bool inJumpGrace = Time.time < lastJumpTime + Mathf.Max(0f, wallDetachJumpGraceTime) && rb.linearVelocity.y > 0f;
            bool pressedIntoWall = !inJumpGrace && directionSign != 0 && IsAirbornePressedIntoWall(directionSign);

            if (pressedIntoWall)
            {
                failedWallDirection = directionSign;
                pendingBackoffOnLand = true;
                float detachX = -directionSign * Mathf.Max(0f, wallDetachPush);
                float dropY = Mathf.Min(rb.linearVelocity.y, -Mathf.Max(0f, stuckWallDropSpeed));
                rb.linearVelocity = new Vector2(detachX, dropY);
                return;
            }

            float airSpeed = direction * chaseSpeed * airControl;
            float currentX = rb.linearVelocity.x;

            if (Mathf.Sign(currentX) == Mathf.Sign(airSpeed) && Mathf.Abs(currentX) > Mathf.Abs(airSpeed))
            {
                airSpeed = currentX;
            }

            rb.linearVelocity = new Vector2(airSpeed, rb.linearVelocity.y);
        }
    }

    private void FixedUpdate()
    {
        if (player == null)
        {
            return;
        }

        if (isGrounded && shouldJump)
        {
            if (Time.time < nextJumpTime)
            {
                return;
            }

            shouldJump = false;
            nextJumpTime = Time.time + Mathf.Max(0f, jumpCooldown);
            lastJumpTime = Time.time;
            rb.AddForce(new Vector2(0f, jumpForce), ForceMode2D.Impulse);
        }
    }

    private bool ShouldJumpFromGround(int direction)
    {
        if (direction == 0)
        {
            debugWallAheadRaw = false;
            debugWallSuppressedByContinuity = false;
            debugWallAheadFinal = false;
            debugHoleAhead = false;
            return false;
        }

        Bounds bounds = bodyCollider != null
            ? bodyCollider.bounds
            : new Bounds(transform.position, Vector3.one);

        Vector2 forward = new Vector2(direction, 0f);
        Vector2 frontFootOrigin = new Vector2(
            bounds.center.x + direction * (bounds.extents.x + 0.05f),
            bounds.min.y + 0.05f
        );
        Vector2 wallLowOrigin = frontFootOrigin;
        Vector2 frontUpperOrigin = frontFootOrigin + Vector2.up * Mathf.Max(0.05f, upperForwardProbeHeight);

        RaycastHit2D wallLowHit = Physics2D.Raycast(wallLowOrigin, forward, forwardCheckDistance, groundLayer);
        RaycastHit2D wallUpperHit = Physics2D.Raycast(frontUpperOrigin, forward, forwardCheckDistance, groundLayer);
        bool wallLowBlocks = IsWallLikeHit(wallLowHit, direction);
        bool wallUpperBlocks = IsWallLikeHit(wallUpperHit, direction);
        bool wallAhead = wallLowBlocks || wallUpperBlocks;
        bool wallAheadRaw = wallAhead;
        bool wallSuppressedByContinuity = false;

        Vector2 nearAheadGroundOrigin = new Vector2(
            frontFootOrigin.x + forward.x * Mathf.Max(0.08f, bounds.extents.x * 0.35f),
            bounds.min.y + Mathf.Max(0.2f, upperForwardProbeHeight)
        );
        RaycastHit2D nearAheadGroundHit = Physics2D.Raycast(
            nearAheadGroundOrigin,
            Vector2.down,
            Mathf.Max(groundCheckDistance + 0.25f, 0.6f + upperForwardProbeHeight),
            groundLayer
        );
        bool hasWalkableGroundNearAhead =
            nearAheadGroundHit.collider &&
            nearAheadGroundHit.normal.y >= WalkableGroundMinNormalY;

        bool seamLikeLowOnlyHit =
            wallLowBlocks &&
            !wallUpperBlocks &&
            wallLowHit.collider &&
            wallLowHit.distance <= SeamSuppressMaxLowHitDistance;

        if (wallAheadRaw && seamLikeLowOnlyHit && hasWalkableGroundNearAhead)
        {
            wallAhead = false;
            wallSuppressedByContinuity = true;
        }

        float continuityThreshold = Mathf.Max(0f, terrainHeightJumpThreshold);
        float continuityProbeDistance = gapCheckDownDistance + Mathf.Max(0.6f, upperForwardProbeHeight);
        Vector2 currentGroundProbeOrigin = new Vector2(
            bounds.center.x,
            bounds.min.y + Mathf.Max(0.2f, upperForwardProbeHeight)
        );
        Vector2 aheadGroundProbeOrigin = new Vector2(
            frontFootOrigin.x + forward.x * forwardCheckDistance,
            bounds.min.y + Mathf.Max(0.2f, upperForwardProbeHeight)
        );
        RaycastHit2D currentGroundHit = Physics2D.Raycast(
            currentGroundProbeOrigin,
            Vector2.down,
            continuityProbeDistance,
            groundLayer
        );
        RaycastHit2D aheadGroundHit = Physics2D.Raycast(
            aheadGroundProbeOrigin,
            Vector2.down,
            continuityProbeDistance,
            groundLayer
        );
        bool hasGroundPairForContinuity =
            currentGroundHit.collider &&
            aheadGroundHit.collider &&
            currentGroundHit.normal.y >= WalkableGroundMinNormalY &&
            aheadGroundHit.normal.y >= WalkableGroundMinNormalY;
        bool terrainChangeBelowThreshold = hasGroundPairForContinuity &&
                                           Mathf.Abs(aheadGroundHit.point.y - currentGroundHit.point.y) <= continuityThreshold;
        if (terrainChangeBelowThreshold && wallAhead)
        {
            wallAhead = false;
            wallSuppressedByContinuity = true;
        }

        Vector2 gapOrigin = new Vector2(
            frontFootOrigin.x + forward.x * forwardCheckDistance,
            frontFootOrigin.y
        );
        RaycastHit2D gapHit = Physics2D.Raycast(gapOrigin, Vector2.down, gapCheckDownDistance, groundLayer);
        bool holeAhead = !gapHit.collider;
        if (terrainChangeBelowThreshold && aheadGroundHit.collider)
        {
            holeAhead = false;
        }

        bool isPlayerAbove = player.position.y > transform.position.y + 0.1f &&
                             Mathf.Abs(player.position.x - transform.position.x) <= Mathf.Max(0f, playerAboveHorizontalTolerance);
        RaycastHit2D platformAbove = Physics2D.Raycast(transform.position, Vector2.up, aboveCheckDistance, groundLayer);
        bool shouldJumpForUpperLevel = isPlayerAbove && platformAbove.collider;

        debugWallLowBlocks = wallLowBlocks;
        debugWallUpperBlocks = wallUpperBlocks;
        debugWallAheadRaw = wallAheadRaw;
        debugWallSuppressedByContinuity = wallSuppressedByContinuity;
        debugWallAheadFinal = wallAhead;
        debugHoleAhead = holeAhead;
        debugFrontFootOrigin = new Vector3(wallLowOrigin.x, wallLowOrigin.y, transform.position.z);
        debugFrontUpperOrigin = new Vector3(frontUpperOrigin.x, frontUpperOrigin.y, transform.position.z);
        debugGapOrigin = new Vector3(gapOrigin.x, gapOrigin.y, transform.position.z);
        debugForwardDistance = forwardCheckDistance;
        debugGapDistance = gapCheckDownDistance;

        return wallAhead || holeAhead || shouldJumpForUpperLevel;
    }

    private static bool IsWallLikeHit(RaycastHit2D hit, int moveDirection)
    {
        if (!hit.collider)
        {
            return false;
        }

        bool opposesMovement = hit.normal.x * moveDirection < -0.05f;
        float surfaceAngleFromUp = Vector2.Angle(hit.normal, Vector2.up);
        bool isSteepSurface = surfaceAngleFromUp >= WallMinSurfaceAngle;
        return opposesMovement && isSteepSurface;
    }

    private bool CheckGrounded()
    {
        if (bodyCollider == null)
        {
            return Physics2D.Raycast(transform.position, Vector2.down, groundCheckDistance, groundLayer);
        }

        Bounds bounds = bodyCollider.bounds;
        float inset = Mathf.Min(0.2f, bounds.extents.x * 0.5f);
        Vector2 leftOrigin = new Vector2(bounds.min.x + inset, bounds.min.y + 0.05f);
        Vector2 rightOrigin = new Vector2(bounds.max.x - inset, bounds.min.y + 0.05f);

        return Physics2D.Raycast(leftOrigin, Vector2.down, groundCheckDistance, groundLayer) ||
               Physics2D.Raycast(rightOrigin, Vector2.down, groundCheckDistance, groundLayer);
    }

    private void ResolvePlayerIfNeeded()
    {
        if (player != null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(playerTag))
        {
            return;
        }

        GameObject taggedPlayer = GameObject.FindGameObjectWithTag(playerTag);
        if (taggedPlayer != null)
        {
            player = taggedPlayer.transform;
        }
    }

    private bool IsAirbornePressedIntoWall(int moveDirection)
    {
        if (bodyCollider == null)
        {
            return false;
        }

        int hitCount = bodyCollider.Cast(
            Vector2.right * moveDirection,
            castFilter,
            castHits,
            Mathf.Max(0.01f, airborneWallProbeDistance)
        );

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = castHits[i];
            if (hit.collider == null || hit.collider == bodyCollider)
            {
                continue;
            }

            if (hit.normal.x * moveDirection < -0.05f)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Collider2D colliderForGizmos = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
        if (colliderForGizmos == null)
        {
            return;
        }

        Bounds bounds = colliderForGizmos.bounds;
        int direction = GetGizmoDirection();

        float inset = Mathf.Min(0.2f, bounds.extents.x * 0.5f);
        Vector3 leftGroundOrigin = new Vector3(bounds.min.x + inset, bounds.min.y + 0.05f, transform.position.z);
        Vector3 rightGroundOrigin = new Vector3(bounds.max.x - inset, bounds.min.y + 0.05f, transform.position.z);

        Gizmos.color = new Color(1f, 0.3f, 0.9f, 0.9f);
        Gizmos.DrawLine(leftGroundOrigin, leftGroundOrigin + Vector3.down * groundCheckDistance);
        Gizmos.DrawLine(rightGroundOrigin, rightGroundOrigin + Vector3.down * groundCheckDistance);
        Gizmos.DrawSphere(leftGroundOrigin, 0.06f);
        Gizmos.DrawSphere(rightGroundOrigin, 0.06f);

        Vector3 frontFootOrigin = new Vector3(
            bounds.center.x + direction * (bounds.extents.x + 0.05f),
            bounds.min.y + 0.05f,
            transform.position.z
        );
        Vector3 frontUpperOrigin = frontFootOrigin + Vector3.up * Mathf.Max(0.05f, upperForwardProbeHeight);
        float forwardDistance = forwardCheckDistance;

        if (Application.isPlaying)
        {
            frontFootOrigin = debugFrontFootOrigin;
            frontUpperOrigin = debugFrontUpperOrigin;
            forwardDistance = debugForwardDistance;
        }

        Color wallColor = new Color(0.1f, 1f, 0.35f, 0.9f);
        if (Application.isPlaying)
        {
            if (debugWallAheadFinal)
            {
                wallColor = new Color(1f, 0.2f, 0.2f, 0.95f);
            }
            else if (debugWallSuppressedByContinuity)
            {
                wallColor = new Color(1f, 0.9f, 0.2f, 0.95f);
            }
            else if (debugWallAheadRaw)
            {
                wallColor = new Color(1f, 0.5f, 0.1f, 0.95f);
            }
        }

        Gizmos.color = wallColor;
        Gizmos.DrawLine(frontFootOrigin, frontFootOrigin + Vector3.right * direction * forwardDistance);
        Gizmos.DrawLine(frontUpperOrigin, frontUpperOrigin + Vector3.right * direction * forwardDistance);
        Gizmos.DrawSphere(frontFootOrigin, 0.06f);
        Gizmos.DrawSphere(frontUpperOrigin, 0.06f);

        Vector3 gapOrigin = frontFootOrigin + Vector3.right * direction * forwardDistance;
        float gapDistance = gapCheckDownDistance;
        if (Application.isPlaying)
        {
            gapOrigin = debugGapOrigin;
            gapDistance = debugGapDistance;
        }

        Gizmos.color = Application.isPlaying && debugHoleAhead
            ? new Color(1f, 0.2f, 0.2f, 0.95f)
            : new Color(0.2f, 0.8f, 1f, 0.95f);
        Gizmos.DrawLine(gapOrigin, gapOrigin + Vector3.down * gapDistance);
        Gizmos.DrawSphere(gapOrigin, 0.06f);

        Vector3 airborneWallProbeOrigin = new Vector3(
            bounds.center.x + direction * bounds.extents.x,
            bounds.center.y,
            transform.position.z
        );
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.95f);
        Gizmos.DrawLine(
            airborneWallProbeOrigin,
            airborneWallProbeOrigin + Vector3.right * direction * airborneWallProbeDistance
        );
        Gizmos.DrawSphere(airborneWallProbeOrigin, 0.06f);
    }

    private int GetGizmoDirection()
    {
        if (player != null)
        {
            float towardPlayer = player.position.x - transform.position.x;
            if (Mathf.Abs(towardPlayer) > 0.01f)
            {
                return towardPlayer > 0f ? 1 : -1;
            }
        }

        if (rb != null && Mathf.Abs(rb.linearVelocity.x) > 0.01f)
        {
            return rb.linearVelocity.x > 0f ? 1 : -1;
        }

        return 1;
    }
}
