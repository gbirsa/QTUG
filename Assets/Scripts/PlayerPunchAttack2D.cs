using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerPunchAttack2D : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Character controller used for facing direction.")]
    [SerializeField] private SpineSimpleController2D controller;

    [Tooltip("Optional health component used to identify the attacker's team.")]
    [SerializeField] private Health2D ownerHealth;

    [Header("Attack")]
    [Tooltip("Layers checked by the punch hitbox.")]
    [SerializeField] private LayerMask hitLayers = ~0;

    [Tooltip("Only this team can be hit by the punch.")]
    [SerializeField] private CombatTeam targetTeam = CombatTeam.Enemy;

    [Tooltip("Damage dealt per punch.")]
    [SerializeField] private int damage = 1;

    [Tooltip("How long after the punch input the hitbox becomes active.")]
    [SerializeField] private float hitDelay = 0.08f;

    [Tooltip("How long the hitbox stays active.")]
    [SerializeField] private float activeDuration = 0.12f;

    [Tooltip("Punch hitbox size in world units.")]
    [SerializeField] private Vector2 hitboxSize = new Vector2(12f, 8f);

    [Tooltip("Punch hitbox offset relative to the player.")]
    [SerializeField] private Vector2 hitboxOffset = new Vector2(7f, 9f);

    private readonly Collider2D[] overlapResults = new Collider2D[16];
    private readonly HashSet<int> hitTargetsThisSwing = new HashSet<int>();

    private float attackTimer = -1f;

    private void Reset()
    {
        controller = GetComponent<SpineSimpleController2D>();
        ownerHealth = GetComponent<Health2D>();
    }

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponent<SpineSimpleController2D>();
        }

        if (ownerHealth == null)
        {
            ownerHealth = GetComponent<Health2D>();
        }
    }

    private void Update()
    {
        if (attackTimer < 0f)
        {
            return;
        }

        attackTimer += Time.deltaTime;

        if (attackTimer >= hitDelay && attackTimer <= hitDelay + activeDuration)
        {
            RunHitbox();
        }

        if (attackTimer > hitDelay + activeDuration)
        {
            attackTimer = -1f;
            hitTargetsThisSwing.Clear();
        }
    }

    public void TriggerAttack()
    {
        attackTimer = 0f;
        hitTargetsThisSwing.Clear();
    }

    private void RunHitbox()
    {
        float facingSign = controller != null ? controller.FacingSign : 1f;
        Vector2 center = (Vector2)transform.position + new Vector2(hitboxOffset.x * facingSign, hitboxOffset.y);
        int hitCount = Physics2D.OverlapBoxNonAlloc(center, hitboxSize, 0f, overlapResults, hitLayers);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D other = overlapResults[i];
            if (other == null)
            {
                continue;
            }

            Health2D targetHealth = other.GetComponentInParent<Health2D>();
            if (targetHealth == null || targetHealth == ownerHealth)
            {
                continue;
            }

            if (targetHealth.Team != targetTeam)
            {
                continue;
            }

            int targetId = targetHealth.GetInstanceID();
            if (hitTargetsThisSwing.Contains(targetId))
            {
                continue;
            }

            Vector2 hitDirection = ((Vector2)targetHealth.transform.position - (Vector2)transform.position).normalized;
            if (hitDirection.sqrMagnitude < 0.0001f)
            {
                hitDirection = new Vector2(facingSign, 0f);
            }

            if (targetHealth.TryApplyDamage(
                damage,
                other.ClosestPoint(transform.position),
                hitDirection,
                gameObject,
                ownerHealth != null ? ownerHealth.Team : CombatTeam.Neutral))
            {
                hitTargetsThisSwing.Add(targetId);
            }
        }
    }
}
