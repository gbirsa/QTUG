using UnityEngine;

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

    private int facingDirection;
    private readonly RaycastHit2D[] wallHits = new RaycastHit2D[8];

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

        if (ShouldTurnAround())
        {
            facingDirection *= -1;
            ApplyFacing();
        }

        rb.linearVelocity = new Vector2(facingDirection * moveSpeed, rb.linearVelocity.y);
    }

    private bool ShouldTurnAround()
    {
        Bounds bounds = bodyCollider.bounds;
        Vector2 edgeOrigin = new Vector2(
            bounds.center.x + facingDirection * (bounds.extents.x + edgeCheckForwardDistance),
            bounds.min.y + 0.1f
        );

        bool groundAhead = Physics2D.Raycast(edgeOrigin, Vector2.down, edgeCheckDownDistance, groundLayer);
        if (!groundAhead)
        {
            return true;
        }

        Vector2 wallOrigin = new Vector2(
            bounds.center.x + facingDirection * bounds.extents.x,
            bounds.center.y + wallCheckHeightOffset
        );

        bool wallAhead = HasSolidWallAhead(wallOrigin);
        return wallAhead;
    }

    private void ApplyFacing()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = facingDirection > 0;
        }
    }

    private bool HasSolidWallAhead(Vector2 wallOrigin)
    {
        Vector2 wallDirection = Vector2.right * facingDirection;
        int hitCount = Physics2D.RaycastNonAlloc(wallOrigin, wallDirection, wallHits, wallCheckDistance, groundLayer);

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
}
