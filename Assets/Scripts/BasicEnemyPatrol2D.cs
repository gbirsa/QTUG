using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class BasicEnemyPatrol2D : MonoBehaviour
{
    private enum MovementState
    {
        Patrol,
        Chase
    }

    [Header("References")]
    [Tooltip("Rigidbody used for movement.")]
    public Rigidbody2D rb;

    [Tooltip("Collider used to estimate bounds for wall and ledge checks.")]
    public Collider2D bodyCollider;

    [Tooltip("Optional renderer flipped when the enemy changes direction.")]
    public SpriteRenderer spriteRenderer;

    [Tooltip("Optional health component. Dead enemies stop moving.")]
    public Health2D health;

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
    public float patrolSpeed = 8f;

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
    [Tooltip("Horizontal speed while chasing.")]
    public float chaseSpeed = 12f;

    [Tooltip("After chase gets blocked (wall, gap, or different floor), the path must stay clear for this long before chase can resume.")]
    [Min(0f)] public float chaseReacquireDelay = 0.35f;

    [Header("Checks")]
    [Tooltip("Layers that count as level geometry for wall and floor checks.")]
    public LayerMask groundLayer;

    [Tooltip("Extra distance past the front edge used for ledge checks.")]
    public float edgeCheckForwardDistance = 0.35f;

    [Tooltip("How far down the ledge check looks for ground.")]
    public float edgeCheckDownDistance = 2.5f;

    [Tooltip("How far ahead the wall check looks.")]
    public float wallCheckDistance = 0.3f;

    [Tooltip("Vertical offset from the enemy center for the wall check.")]
    public float wallCheckHeightOffset = 0f;

    [Tooltip("Maximum Y difference from the enemy while chasing. Higher differences are treated as another floor/platform.")]
    [Min(0f)] public float sameFloorMaxVerticalDelta = 1f;

    private MovementState movementState = MovementState.Patrol;
    private float patrolWaitUntilTime;
    private int patrolWaitPointIndex = -1;
    private bool patrolTowardPointB;
    private bool chaseBlockedLatched;
    private float chaseClearSinceTime = -1f;
    private int lastMoveDirection = 1;
    private readonly RaycastHit2D[] wallHits = new RaycastHit2D[8];
    private bool debugPlayerInAggroRange;
    private bool debugChasePathBlocked;

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

        patrolTowardPointB = startPatrolTowardPointB;
    }

    private void FixedUpdate()
    {
        if (health != null && health.IsDead)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        ResolvePlayerIfNeeded();

        bool playerInAggroRange = player != null && IsTargetWithinAggroRange(player.position);
        debugPlayerInAggroRange = playerInAggroRange;

        int direction;
        float speed;
        bool chasePathBlocked = false;
        bool canAttemptChase = playerInAggroRange && !chaseBlockedLatched;

        if (playerInAggroRange && canAttemptChase && TryGetChaseDirection(out direction, out chasePathBlocked))
        {
            movementState = MovementState.Chase;
            speed = chaseSpeed;
        }
        else
        {
            movementState = MovementState.Patrol;
            direction = ResolvePatrolDirection();
            speed = patrolSpeed;
        }

        if (!playerInAggroRange)
        {
            chaseBlockedLatched = false;
            chaseClearSinceTime = -1f;
        }
        else if (chaseBlockedLatched)
        {
            UpdateChaseLatchReleaseState();
        }
        else if (chasePathBlocked)
        {
            chaseBlockedLatched = true;
            chaseClearSinceTime = -1f;
        }

        debugChasePathBlocked = chasePathBlocked || chaseBlockedLatched;
        ApplyMovement(direction, speed);
    }

    private void ApplyMovement(int direction, float speed)
    {
        float clampedSpeed = Mathf.Max(0f, speed);
        rb.linearVelocity = new Vector2(direction * clampedSpeed, rb.linearVelocity.y);

        if (direction == 0)
        {
            return;
        }

        lastMoveDirection = direction;
        ApplyFacing(direction);
    }

    private bool TryGetChaseDirection(out int direction, out bool pathBlocked)
    {
        direction = 0;
        pathBlocked = false;

        if (player == null)
        {
            return false;
        }

        Vector2 toPlayer = player.position - transform.position;
        if (Mathf.Abs(toPlayer.y) > Mathf.Max(0f, sameFloorMaxVerticalDelta))
        {
            pathBlocked = true;
            return false;
        }

        if (Mathf.Abs(toPlayer.x) <= 0.02f)
        {
            return false;
        }

        direction = toPlayer.x > 0f ? 1 : -1;
        if (IsPathBlocked(direction))
        {
            pathBlocked = true;
            return false;
        }

        return true;
    }

    private void UpdateChaseLatchReleaseState()
    {
        bool hasChaseDirection = TryGetChaseDirection(out _, out _);
        if (!hasChaseDirection)
        {
            chaseClearSinceTime = -1f;
            return;
        }

        if (chaseClearSinceTime < 0f)
        {
            chaseClearSinceTime = Time.time;
        }

        if (Time.time >= chaseClearSinceTime + Mathf.Max(0f, chaseReacquireDelay))
        {
            chaseBlockedLatched = false;
            chaseClearSinceTime = -1f;
        }
    }

    private bool IsPathBlocked(int direction)
    {
        if (!TryGetBounds(out Bounds bounds))
        {
            return false;
        }

        return IsGapAhead(bounds, direction) || IsWallAhead(bounds, direction);
    }

    private int ResolvePatrolDirection()
    {
        if (!TryGetPatrolPoints(out Vector2 pointA, out Vector2 pointB))
        {
            patrolWaitPointIndex = -1;
            return 0;
        }

        float currentX = transform.position.x;
        float reachDistance = Mathf.Max(0f, patrolPointReachDistance);
        Vector2 targetPoint = patrolTowardPointB ? pointB : pointA;
        int targetPointIndex = patrolTowardPointB ? 1 : 0;

        bool targetReached = Mathf.Abs(currentX - targetPoint.x) <= reachDistance;
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

        float deltaX = targetPoint.x - currentX;
        if (Mathf.Abs(deltaX) <= 0.02f)
        {
            return 0;
        }

        int direction = deltaX > 0f ? 1 : -1;
        if (!IsPathBlocked(direction))
        {
            return direction;
        }

        patrolWaitPointIndex = -1;
        patrolTowardPointB = !patrolTowardPointB;
        targetPoint = patrolTowardPointB ? pointB : pointA;
        float alternateDeltaX = targetPoint.x - currentX;
        if (Mathf.Abs(alternateDeltaX) <= 0.02f)
        {
            return 0;
        }

        int alternateDirection = alternateDeltaX > 0f ? 1 : -1;
        return IsPathBlocked(alternateDirection) ? 0 : alternateDirection;
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

    private bool IsWallAhead(Bounds bounds, int direction)
    {
        Vector2 wallOrigin = new Vector2(
            bounds.center.x + direction * bounds.extents.x,
            bounds.center.y + wallCheckHeightOffset
        );
        return HasSolidWallAhead(wallOrigin, direction);
    }

    private bool IsGapAhead(Bounds bounds, int direction)
    {
        Vector2 edgeOrigin = new Vector2(
            bounds.center.x + direction * (bounds.extents.x + edgeCheckForwardDistance),
            bounds.min.y + 0.1f
        );
        return !Physics2D.Raycast(edgeOrigin, Vector2.down, edgeCheckDownDistance, groundLayer);
    }

    private bool HasSolidWallAhead(Vector2 wallOrigin, int direction)
    {
        int hitCount = Physics2D.RaycastNonAlloc(
            wallOrigin,
            Vector2.right * direction,
            wallHits,
            wallCheckDistance,
            groundLayer
        );

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

    private bool TryGetBounds(out Bounds bounds)
    {
        Collider2D colliderToUse = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
        if (colliderToUse == null)
        {
            bounds = new Bounds(transform.position, Vector3.one);
            return false;
        }

        bounds = colliderToUse.bounds;
        return true;
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

    private void ApplyFacing(int direction)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = direction > 0;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!TryGetBounds(out Bounds bounds))
        {
            return;
        }

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

        int gizmoDirection = GetGizmoDirection();
        if (gizmoDirection == 0)
        {
            return;
        }

        Vector2 edgeOrigin = new Vector2(
            bounds.center.x + gizmoDirection * (bounds.extents.x + edgeCheckForwardDistance),
            bounds.min.y + 0.1f
        );
        Vector2 wallOrigin = new Vector2(
            bounds.center.x + gizmoDirection * bounds.extents.x,
            bounds.center.y + wallCheckHeightOffset
        );

        bool gapAhead = IsGapAhead(bounds, gizmoDirection);
        bool wallAhead = IsWallAhead(bounds, gizmoDirection);

        Gizmos.color = gapAhead
            ? new Color(1f, 0.25f, 0.2f, 0.95f)
            : new Color(0.2f, 0.8f, 1f, 0.95f);
        Gizmos.DrawLine(edgeOrigin, edgeOrigin + Vector2.down * edgeCheckDownDistance);
        Gizmos.DrawSphere(edgeOrigin, 0.08f);

        Gizmos.color = wallAhead
            ? new Color(1f, 0.25f, 0.2f, 0.95f)
            : new Color(0.6f, 1f, 0.25f, 0.95f);
        Gizmos.DrawLine(wallOrigin, wallOrigin + Vector2.right * gizmoDirection * wallCheckDistance);
        Gizmos.DrawSphere(wallOrigin, 0.08f);
    }

    private int GetGizmoDirection()
    {
        if (movementState == MovementState.Chase && player != null)
        {
            float towardPlayer = player.position.x - transform.position.x;
            if (Mathf.Abs(towardPlayer) > 0.02f)
            {
                return towardPlayer > 0f ? 1 : -1;
            }
        }

        if (patrolPointA != null && patrolPointB != null)
        {
            float targetX = patrolTowardPointB ? patrolPointB.position.x : patrolPointA.position.x;
            float towardPatrolTarget = targetX - transform.position.x;
            if (Mathf.Abs(towardPatrolTarget) > 0.02f)
            {
                return towardPatrolTarget > 0f ? 1 : -1;
            }
        }

        if (Application.isPlaying && debugChasePathBlocked)
        {
            return lastMoveDirection != 0 ? lastMoveDirection : 1;
        }

        return lastMoveDirection != 0 ? lastMoveDirection : 1;
    }
}
