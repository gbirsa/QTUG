using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ContactDamage2D : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Health component for the attacker. Used to prevent same-team damage.")]
    [SerializeField] private Health2D ownerHealth;

    [Tooltip("Collider used to detect touching targets. Defaults to the first Collider2D on this object.")]
    [SerializeField] private Collider2D ownerCollider;

    [Header("Damage")]
    [Tooltip("Only this team can be damaged by contact.")]
    [SerializeField] private CombatTeam targetTeam = CombatTeam.Player;

    [Tooltip("Damage dealt when the target touches this object.")]
    [SerializeField] private int damage = 1;

    [Tooltip("Minimum time between repeated contact hits on the same target.")]
    [SerializeField] private float hitCooldown = 0.6f;

    private readonly Dictionary<int, float> nextHitTimeByTarget = new Dictionary<int, float>();
    private readonly Collider2D[] contactResults = new Collider2D[16];
    private ContactFilter2D contactFilter;

    private void Reset()
    {
        ownerHealth = GetComponent<Health2D>();
        ownerCollider = GetComponent<Collider2D>();
    }

    private void Awake()
    {
        if (ownerHealth == null)
        {
            ownerHealth = GetComponent<Health2D>();
        }

        if (ownerCollider == null)
        {
            ownerCollider = GetComponent<Collider2D>();
        }

        contactFilter = new ContactFilter2D
        {
            useLayerMask = false,
            useTriggers = true
        };
    }

    private void FixedUpdate()
    {
        if (ownerCollider == null)
        {
            return;
        }

        int hitCount = ownerCollider.GetContacts(contactFilter, contactResults);
        for (int i = 0; i < hitCount; i++)
        {
            TryDamage(contactResults[i]);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryDamage(collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        TryDamage(collision.collider);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDamage(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryDamage(other);
    }

    private void TryDamage(Collider2D other)
    {
        if (ownerHealth != null && ownerHealth.IsDead)
        {
            return;
        }

        Health2D targetHealth = other.GetComponentInParent<Health2D>();
        if (targetHealth == null || targetHealth == ownerHealth)
        {
            return;
        }

        if (targetHealth.Team != targetTeam)
        {
            return;
        }

        int targetId = targetHealth.GetInstanceID();
        if (nextHitTimeByTarget.TryGetValue(targetId, out float nextHitTime) && Time.time < nextHitTime)
        {
            return;
        }

        Vector2 hitDirection = ((Vector2)targetHealth.transform.position - (Vector2)transform.position).normalized;
        if (hitDirection.sqrMagnitude < 0.0001f)
        {
            hitDirection = Vector2.right;
        }

        if (targetHealth.TryApplyDamage(
            damage,
            other.ClosestPoint(transform.position),
            hitDirection,
            gameObject,
            ownerHealth != null ? ownerHealth.Team : CombatTeam.Neutral))
        {
            nextHitTimeByTarget[targetId] = Time.time + Mathf.Max(0f, hitCooldown);
        }
    }
}
