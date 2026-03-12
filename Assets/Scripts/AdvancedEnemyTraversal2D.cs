using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public class AdvancedEnemyTraversal2D : MonoBehaviour
{
    private enum MovementState
    {
        Patrol,
        Chase
    }

    private enum VerticalIntent
    {
        FollowUnder,
        TryClimb
    }

    private const int UpperWallProbeSampleCount = 5;
    private const float WallMinSurfaceAngle = 55f;
    private const float WalkableGroundMinNormalY = 0.55f;
    private const float SeamSuppressMaxLowHitDistance = 0.2f;

    [Header("Target")]
    [Tooltip("Optional direct reference to the player target. If empty, the script tries to find a GameObject by Player Tag.")]
    public Transform player;

    [Tooltip("Tag used to auto-find the player when Player is not assigned.")]
    public string playerTag = "Player";

    [Header("Aggro")]
    [Tooltip("Width of the aggro rectangle centered on the enemy.")]
    [Min(0f)] public float aggroWidth = 60f;

    [Tooltip("Height of the aggro rectangle centered on the enemy.")]
    [Min(0f)] public float aggroHeight = 60f;

    [Header("Patrol")]
    [Tooltip("Horizontal speed while patrolling.")]
    public float patrolSpeed = 1f;

    [Tooltip("First patrol point. The enemy moves back and forth between A and B.")]
    public Transform patrolPointA;

    [Tooltip("Second patrol point. The enemy moves back and forth between A and B.")]
    public Transform patrolPointB;

    [Tooltip("How close the enemy must be to a patrol point before switching to the other point.")]
    [Min(0f)] public float patrolPointReachDistance = 0.25f;

    [Tooltip("When patrol begins, move toward point B first. If false, move toward point A first.")]
    public bool startPatrolTowardPointB = true;

    [Tooltip("How long to wait when reaching patrol point A before moving toward point B.")]
    [Min(0f)] public float waitAtPointA = 0f;

    [Tooltip("How long to wait when reaching patrol point B before moving toward point A.")]
    [Min(0f)] public float waitAtPointB = 0f;

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

    [Tooltip("Vertical offset for the upper forward probe used to detect step-like obstacles.")]
    public float upperForwardProbeHeight = 0.35f;

    [Tooltip("Additional vertical range used for a second upper forward probe (base + top) to detect climbable walls at different heights.")]
    [Min(0f)] public float upperForwardProbeVerticalRange = 0.35f;

    [Tooltip("Maximum horizontal distance from the player for the 'player above' jump helper.")]
    public float playerAboveHorizontalTolerance = 0.6f;

    [Tooltip("Minimum absolute ground-height change ahead (up or down) required to trigger jump behavior. Smaller changes are treated as continuous terrain.")]
    [Min(0f)] public float terrainHeightJumpThreshold = 0.12f;

    [Header("Climb Assist")]
    [Tooltip("Horizontal dead zone around the player's X position while attempting upward follow. Inside this zone, direction will not flip each frame.")]
    [Min(0f)] public float abovePlayerDeadZoneX = 0.15f;

    [Tooltip("How long the enemy keeps one horizontal direction while attempting to follow a player above.")]
    [Min(0f)] public float climbDirectionLockTime = 0.4f;

    [Tooltip("Width multiplier (relative to collider width) used by the platform-above overlap check.")]
    [Min(0.1f)] public float abovePlatformProbeWidthMultiplier = 0.9f;

    [Tooltip("Height of the above-platform detection box used for climb intent checks.")]
    [Min(0.1f)] public float abovePlatformProbeHeight = 3f;

    [Tooltip("Player vertical offset required to enter climb intent mode. While climbing intent is active, upper forward wall hits are allowed to trigger jumps.")]
    [Min(0f)] public float climbIntentEnterHeight = 0.45f;

    [Tooltip("Player vertical offset below which climb intent mode is exited. Keep this lower than Enter Height to avoid rapid mode flicker.")]
    [Min(0f)] public float climbIntentExitHeight = 0.2f;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private bool isGrounded;
    private bool shouldJump;
    private bool playerAboveForUpperJump;
    private bool platformAboveForUpperJump;
    private float nextJumpTime;
    private float lastJumpTime = -999f;
    private float climbDirectionLockUntilTime;
    private float patrolWaitUntilTime;
    private int patrolWaitPointIndex = -1;
    private bool patrolTowardPointB;
    private VerticalIntent verticalIntent = VerticalIntent.FollowUnder;
    private MovementState movementState = MovementState.Patrol;
    private bool backoffActive;
    private bool pendingBackoffOnLand;
    private int lockedClimbDirection = 1;
    private int lastMoveDirection = 1;
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
    private Vector3 debugFrontUpperTopOrigin;
    private Vector3 debugGapOrigin;
    private float debugForwardDistance;
    private float debugGapDistance;
    private Vector2 debugAboveProbeCenter;
    private Vector2 debugAboveProbeSize;
    private bool debugAbovePlatformHit;
    private bool debugPlayerAboveForUpperJump;
    private bool debugDirectionLockActive;
    private bool debugTryClimbIntent;
    private bool debugPlayerInAggroRange;

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
        patrolTowardPointB = startPatrolTowardPointB;
        ResolvePlayerIfNeeded();
    }

    private void Update()
    {
        ResolvePlayerIfNeeded();

        isGrounded = CheckGrounded();
        bool playerInAggroRange = player != null && IsTargetWithinAggroRange(player.position);
        debugPlayerInAggroRange = playerInAggroRange;
        movementState = playerInAggroRange ? MovementState.Chase : MovementState.Patrol;

        if (player == null)
        {
            ApplyPatrolMovement();
            return;
        }

        if (!playerInAggroRange)
        {
            ApplyPatrolMovement();
            return;
        }

        float deltaXToPlayer = player.position.x - transform.position.x;
        float deltaYToPlayer = player.position.y - transform.position.y;
        UpdateVerticalIntent(deltaYToPlayer);
        bool playerAboveByIntent = verticalIntent == VerticalIntent.TryClimb;
        playerAboveForUpperJump = playerAboveByIntent &&
                                  Mathf.Abs(deltaXToPlayer) <= Mathf.Max(0f, playerAboveHorizontalTolerance);
        platformAboveForUpperJump = HasPlatformAbove(out Vector2 aboveProbeCenter, out Vector2 aboveProbeSize);
        bool climbIntent = playerAboveByIntent && platformAboveForUpperJump;
        int direction = ResolveHorizontalDirection(deltaXToPlayer, climbIntent);
        debugAboveProbeCenter = aboveProbeCenter;
        debugAboveProbeSize = aboveProbeSize;
        debugAbovePlatformHit = platformAboveForUpperJump;
        debugPlayerAboveForUpperJump = playerAboveForUpperJump;
        debugDirectionLockActive = Time.time < climbDirectionLockUntilTime;
        debugTryClimbIntent = playerAboveByIntent;
        ApplyTraversalMovement(direction, chaseSpeed);
    }

    private void FixedUpdate()
    {
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

    private void ApplyPatrolMovement()
    {
        playerAboveForUpperJump = false;
        platformAboveForUpperJump = false;
        verticalIntent = VerticalIntent.FollowUnder;
        debugAbovePlatformHit = false;
        debugPlayerAboveForUpperJump = false;
        debugDirectionLockActive = false;
        debugTryClimbIntent = false;

        int direction = ResolvePatrolDirection();
        ApplyTraversalMovement(direction, patrolSpeed);
    }

    private void ApplyTraversalMovement(int direction, float moveSpeed)
    {
        if (direction != 0)
        {
            lastMoveDirection = direction;
        }

        float clampedMoveSpeed = Mathf.Max(0f, moveSpeed);

        if (isGrounded)
        {
            int fallbackDirection = direction != 0 ? direction : (lastMoveDirection != 0 ? lastMoveDirection : 1);

            if (pendingBackoffOnLand)
            {
                pendingBackoffOnLand = false;
                backoffActive = true;
                backoffDirection = failedWallDirection != 0 ? -failedWallDirection : -fallbackDirection;
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

            rb.linearVelocity = new Vector2(direction * clampedMoveSpeed, rb.linearVelocity.y);
            shouldJump = ShouldJumpFromGround(direction);
            return;
        }

        int directionSign = direction != 0 ? direction : (lastMoveDirection != 0 ? lastMoveDirection : 1);
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

        float desiredSpeed = direction * clampedMoveSpeed * Mathf.Max(0f, airControl);
        float currentX = rb.linearVelocity.x;
        if (Mathf.Sign(currentX) == Mathf.Sign(desiredSpeed) && Mathf.Abs(currentX) > Mathf.Abs(desiredSpeed))
        {
            desiredSpeed = currentX;
        }

        rb.linearVelocity = new Vector2(desiredSpeed, rb.linearVelocity.y);
    }

    private int ResolvePatrolDirection()
    {
        Vector2 currentPosition = transform.position;
        if (!TryGetPatrolPoints(out Vector2 pointA, out Vector2 pointB))
        {
            patrolWaitPointIndex = -1;
            return 0;
        }

        float reachDistance = Mathf.Max(0f, patrolPointReachDistance);
        Vector2 targetPoint = patrolTowardPointB ? pointB : pointA;
        int targetPointIndex = patrolTowardPointB ? 1 : 0;
        bool targetReached = (currentPosition - targetPoint).sqrMagnitude <= reachDistance * reachDistance;
        if (targetReached)
        {
            if (patrolWaitPointIndex != targetPointIndex)
            {
                patrolWaitPointIndex = targetPointIndex;
                float waitDuration = targetPointIndex == 0
                    ? Mathf.Max(0f, waitAtPointA)
                    : Mathf.Max(0f, waitAtPointB);
                patrolWaitUntilTime = Time.time + waitDuration;
            }

            if (Time.time < patrolWaitUntilTime)
            {
                return 0;
            }

            patrolWaitPointIndex = -1;
            patrolTowardPointB = !patrolTowardPointB;
            targetPoint = patrolTowardPointB ? pointB : pointA;
        }
        else if (patrolWaitPointIndex == targetPointIndex)
        {
            patrolWaitPointIndex = -1;
        }

        float deltaX = targetPoint.x - currentPosition.x;
        if (Mathf.Abs(deltaX) <= 0.02f)
        {
            return 0;
        }

        return deltaX > 0f ? 1 : -1;
    }

    private bool TryGetPatrolPoints(out Vector2 pointA, out Vector2 pointB)
    {
        pointA = patrolPointA != null ? (Vector2)patrolPointA.position : Vector2.zero;
        pointB = patrolPointB != null ? (Vector2)patrolPointB.position : Vector2.zero;

        if (patrolPointA == null || patrolPointB == null)
        {
            return false;
        }

        return (pointA - pointB).sqrMagnitude > 0.0001f;
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
        float upperVerticalRange = Mathf.Max(0f, upperForwardProbeVerticalRange);
        Vector2 frontUpperTopOrigin = frontUpperOrigin + Vector2.up * upperVerticalRange;

        RaycastHit2D wallLowHit = Physics2D.Raycast(wallLowOrigin, forward, forwardCheckDistance, groundLayer);
        bool wallLowBlocks = IsWallLikeHit(wallLowHit, direction);
        bool wallUpperBlocks = false;
        int sampleCount = upperVerticalRange > 0.001f ? UpperWallProbeSampleCount : 1;
        for (int i = 0; i < sampleCount; i++)
        {
            float t = sampleCount == 1 ? 0f : (float)i / (sampleCount - 1);
            Vector2 sampleOrigin = frontUpperOrigin + Vector2.up * (upperVerticalRange * t);
            RaycastHit2D wallUpperSampleHit = Physics2D.Raycast(sampleOrigin, forward, forwardCheckDistance, groundLayer);
            if (IsWallLikeHit(wallUpperSampleHit, direction))
            {
                wallUpperBlocks = true;
                break;
            }
        }
        bool allowUpperWallJump = verticalIntent == VerticalIntent.TryClimb;
        bool wallAheadRaw = wallLowBlocks || wallUpperBlocks;
        bool wallAhead = wallLowBlocks || (allowUpperWallJump && wallUpperBlocks);
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
        bool applyContinuitySuppression = verticalIntent != VerticalIntent.TryClimb;
        if (applyContinuitySuppression && terrainChangeBelowThreshold && wallAhead)
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
        if (applyContinuitySuppression && terrainChangeBelowThreshold && aheadGroundHit.collider)
        {
            holeAhead = false;
        }

        debugWallLowBlocks = wallLowBlocks;
        debugWallUpperBlocks = wallUpperBlocks;
        debugWallAheadRaw = wallAheadRaw;
        debugWallSuppressedByContinuity = wallSuppressedByContinuity;
        debugWallAheadFinal = wallAhead;
        debugHoleAhead = holeAhead;
        debugFrontFootOrigin = new Vector3(wallLowOrigin.x, wallLowOrigin.y, transform.position.z);
        debugFrontUpperOrigin = new Vector3(frontUpperOrigin.x, frontUpperOrigin.y, transform.position.z);
        debugFrontUpperTopOrigin = new Vector3(frontUpperTopOrigin.x, frontUpperTopOrigin.y, transform.position.z);
        debugGapOrigin = new Vector3(gapOrigin.x, gapOrigin.y, transform.position.z);
        debugForwardDistance = forwardCheckDistance;
        debugGapDistance = gapCheckDownDistance;

        return wallAhead || holeAhead;
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

    private int ResolveHorizontalDirection(float deltaXToPlayer, bool climbIntent)
    {
        if (climbIntent && Time.time >= climbDirectionLockUntilTime)
        {
            int chosen = ChooseDirection(deltaXToPlayer, Mathf.Max(0f, abovePlayerDeadZoneX));
            if (chosen == 0)
            {
                chosen = lastMoveDirection != 0 ? lastMoveDirection : 1;
            }

            lockedClimbDirection = chosen;
            climbDirectionLockUntilTime = Time.time + Mathf.Max(0f, climbDirectionLockTime);
        }

        if (Time.time < climbDirectionLockUntilTime)
        {
            lastMoveDirection = lockedClimbDirection != 0 ? lockedClimbDirection : lastMoveDirection;
            return lockedClimbDirection != 0 ? lockedClimbDirection : 1;
        }

        int fallbackDirection = ChooseDirection(deltaXToPlayer, 0.01f);
        if (fallbackDirection != 0)
        {
            lastMoveDirection = fallbackDirection;
            return fallbackDirection;
        }

        return lastMoveDirection != 0 ? lastMoveDirection : 1;
    }

    private static int ChooseDirection(float deltaXToPlayer, float deadZone)
    {
        float clampedDeadZone = Mathf.Max(0f, deadZone);
        if (Mathf.Abs(deltaXToPlayer) <= clampedDeadZone)
        {
            return 0;
        }

        return deltaXToPlayer > 0f ? 1 : -1;
    }

    private void UpdateVerticalIntent(float deltaYToPlayer)
    {
        float enterHeight = Mathf.Max(0f, climbIntentEnterHeight);
        float exitHeight = Mathf.Clamp(climbIntentExitHeight, 0f, enterHeight);

        if (verticalIntent == VerticalIntent.TryClimb)
        {
            if (deltaYToPlayer < exitHeight)
            {
                verticalIntent = VerticalIntent.FollowUnder;
            }
        }
        else
        {
            if (deltaYToPlayer > enterHeight)
            {
                verticalIntent = VerticalIntent.TryClimb;
            }
        }
    }

    private bool HasPlatformAbove(out Vector2 probeCenter, out Vector2 probeSize)
    {
        Bounds bounds = bodyCollider != null
            ? bodyCollider.bounds
            : new Bounds(transform.position, Vector3.one);

        float probeHeight = Mathf.Max(0.1f, abovePlatformProbeHeight);
        float widthMultiplier = Mathf.Max(0.1f, abovePlatformProbeWidthMultiplier);
        float probeWidth = Mathf.Max(0.2f, bounds.size.x * widthMultiplier);
        probeCenter = new Vector2(bounds.center.x, bounds.max.y + probeHeight * 0.5f);
        probeSize = new Vector2(probeWidth, probeHeight);

        return Physics2D.OverlapBox(probeCenter, probeSize, 0f, groundLayer) != null;
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

    private bool IsTargetWithinAggroRange(Vector2 targetPosition)
    {
        float clampedHalfWidth = Mathf.Max(0f, aggroWidth) * 0.5f;
        float clampedHalfHeight = Mathf.Max(0f, aggroHeight) * 0.5f;
        Vector2 aggroCenter = bodyCollider != null
            ? bodyCollider.bounds.center
            : (Vector2)transform.position;
        Vector2 delta = targetPosition - aggroCenter;
        return Mathf.Abs(delta.x) <= clampedHalfWidth && Mathf.Abs(delta.y) <= clampedHalfHeight;
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

        {
            float clampedWidth = Mathf.Max(0f, aggroWidth);
            float clampedHeight = Mathf.Max(0f, aggroHeight);
            float clampedHalfWidth = clampedWidth * 0.5f;
            float clampedHalfHeight = clampedHeight * 0.5f;
            Vector3 aggroCenter = new Vector3(bounds.center.x, bounds.center.y, transform.position.z);
            Vector3 aggroSize = new Vector3(clampedWidth, clampedHeight, 0f);
            bool playerInRange = false;

            if (player != null)
            {
                Vector2 deltaToPlayer = (Vector2)player.position - (Vector2)bounds.center;
                playerInRange = Mathf.Abs(deltaToPlayer.x) <= clampedHalfWidth &&
                                Mathf.Abs(deltaToPlayer.y) <= clampedHalfHeight;
            }

            if (Application.isPlaying)
            {
                playerInRange = debugPlayerInAggroRange;
            }

            Gizmos.color = playerInRange
                ? new Color(0.2f, 1f, 0.35f, 0.95f)
                : new Color(1f, 0.6f, 0.1f, 0.95f);
            Gizmos.DrawWireCube(aggroCenter, aggroSize);
        }

        if (patrolPointA != null && patrolPointB != null)
        {
            Vector3 pointA = patrolPointA.position;
            Vector3 pointB = patrolPointB.position;
            pointA.z = transform.position.z;
            pointB.z = transform.position.z;
            Gizmos.color = new Color(0.2f, 0.95f, 1f, 0.95f);
            Gizmos.DrawLine(pointA, pointB);
            Gizmos.DrawSphere(pointA, 0.12f);
            Gizmos.DrawSphere(pointB, 0.12f);
        }

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
        Vector3 frontUpperTopOrigin = frontUpperOrigin + Vector3.up * Mathf.Max(0f, upperForwardProbeVerticalRange);
        float forwardDistance = forwardCheckDistance;

        if (Application.isPlaying)
        {
            frontFootOrigin = debugFrontFootOrigin;
            frontUpperOrigin = debugFrontUpperOrigin;
            frontUpperTopOrigin = debugFrontUpperTopOrigin;
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
        Gizmos.DrawLine(frontUpperTopOrigin, frontUpperTopOrigin + Vector3.right * direction * forwardDistance);
        Gizmos.DrawSphere(frontFootOrigin, 0.06f);
        Gizmos.DrawSphere(frontUpperOrigin, 0.06f);
        Gizmos.DrawSphere(frontUpperTopOrigin, 0.06f);

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

        float aboveProbeHeight = Mathf.Max(0.1f, abovePlatformProbeHeight);
        float aboveProbeWidth = Mathf.Max(0.2f, bounds.size.x * Mathf.Max(0.1f, abovePlatformProbeWidthMultiplier));
        Vector3 aboveProbeCenter = new Vector3(
            bounds.center.x,
            bounds.max.y + aboveProbeHeight * 0.5f,
            transform.position.z
        );
        Vector3 aboveProbeSize = new Vector3(aboveProbeWidth, aboveProbeHeight, 0f);

        if (Application.isPlaying)
        {
            aboveProbeCenter = new Vector3(debugAboveProbeCenter.x, debugAboveProbeCenter.y, transform.position.z);
            aboveProbeSize = new Vector3(debugAboveProbeSize.x, debugAboveProbeSize.y, 0f);
        }

        Color aboveColor = new Color(0.45f, 0.45f, 1f, 0.9f);
        if (Application.isPlaying)
        {
            if (debugTryClimbIntent && debugPlayerAboveForUpperJump && debugAbovePlatformHit)
            {
                aboveColor = new Color(1f, 0.2f, 0.2f, 0.95f);
            }
            else if (debugTryClimbIntent && debugAbovePlatformHit)
            {
                aboveColor = new Color(1f, 0.5f, 0.1f, 0.95f);
            }
            else if (debugAbovePlatformHit)
            {
                aboveColor = new Color(1f, 0.9f, 0.2f, 0.95f);
            }
            else if (debugDirectionLockActive)
            {
                aboveColor = new Color(0.9f, 0.5f, 0.1f, 0.95f);
            }
        }

        Gizmos.color = aboveColor;
        Gizmos.DrawWireCube(aboveProbeCenter, aboveProbeSize);

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
