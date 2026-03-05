using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public class AdvancedEnemyTraversal2D : MonoBehaviour
{
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
        Vector2 frontUpperOrigin = frontFootOrigin + Vector2.up * Mathf.Max(0.05f, upperForwardProbeHeight);

        RaycastHit2D wallLowHit = Physics2D.Raycast(frontFootOrigin, forward, forwardCheckDistance, groundLayer);
        RaycastHit2D wallUpperHit = Physics2D.Raycast(frontUpperOrigin, forward, forwardCheckDistance, groundLayer);
        bool wallAhead = IsWallLikeHit(wallLowHit, direction) || IsWallLikeHit(wallUpperHit, direction);

        Vector2 gapOrigin = frontFootOrigin + forward * forwardCheckDistance;
        RaycastHit2D gapHit = Physics2D.Raycast(gapOrigin, Vector2.down, gapCheckDownDistance, groundLayer);
        bool holeAhead = !gapHit.collider;

        bool isPlayerAbove = player.position.y > transform.position.y + 0.1f &&
                             Mathf.Abs(player.position.x - transform.position.x) <= Mathf.Max(0f, playerAboveHorizontalTolerance);
        RaycastHit2D platformAbove = Physics2D.Raycast(transform.position, Vector2.up, aboveCheckDistance, groundLayer);
        bool shouldJumpForUpperLevel = isPlayerAbove && platformAbove.collider;

        return wallAhead || holeAhead || shouldJumpForUpperLevel;
    }

    private static bool IsWallLikeHit(RaycastHit2D hit, int moveDirection)
    {
        if (!hit.collider)
        {
            return false;
        }

        return hit.normal.x * moveDirection < -0.15f;
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
}
