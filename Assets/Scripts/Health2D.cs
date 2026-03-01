using System;
using UnityEngine;

public enum CombatTeam
{
    Neutral = 0,
    Player = 1,
    Enemy = 2
}

[DisallowMultipleComponent]
public class Health2D : MonoBehaviour
{
    [Header("Health")]
    [Tooltip("Which side this character belongs to. Matching teams do not damage each other.")]
    [SerializeField] private CombatTeam team = CombatTeam.Neutral;

    [Tooltip("Maximum health this character can have.")]
    [SerializeField] private int maxHealth = 3;

    [Tooltip("Short invulnerability after taking damage so one touch does not delete all health instantly.")]
    [SerializeField] private float invulnerabilityDuration = 0.25f;

    [Header("Death")]
    [Tooltip("Destroy this GameObject when health reaches zero.")]
    [SerializeField] private bool destroyOnDeath = false;

    [Tooltip("Optional delay before this GameObject is destroyed, so hit feedback can finish.")]
    [SerializeField] private float destroyDelay = 0f;

    [Tooltip("Optional behaviours to disable when this character dies.")]
    [SerializeField] private MonoBehaviour[] behavioursToDisableOnDeath;

    [Tooltip("Optional colliders to disable when this character dies.")]
    [SerializeField] private Collider2D[] collidersToDisableOnDeath;

    [Tooltip("Optional rigidbody to stop on death.")]
    [SerializeField] private Rigidbody2D rigidbodyToStop;

    public CombatTeam Team => team;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => isDead;

    public event Action<Health2D> Damaged;
    public event Action<Health2D> Died;

    private int currentHealth;
    private bool isDead;
    private float nextDamageTime;

    private void Reset()
    {
        rigidbodyToStop = GetComponent<Rigidbody2D>();
    }

    private void Awake()
    {
        if (rigidbodyToStop == null)
        {
            rigidbodyToStop = GetComponent<Rigidbody2D>();
        }

        currentHealth = Mathf.Max(1, maxHealth);
        nextDamageTime = 0f;
        isDead = false;
    }

    public bool TryApplyDamage(int amount, Vector2 hitPoint, Vector2 hitDirection, GameObject source, CombatTeam sourceTeam)
    {
        if (isDead || amount <= 0)
        {
            return false;
        }

        if (source != null && source.transform.root == transform.root)
        {
            return false;
        }

        if (team != CombatTeam.Neutral && sourceTeam == team)
        {
            return false;
        }

        if (Time.time < nextDamageTime)
        {
            return false;
        }

        currentHealth = Mathf.Max(0, currentHealth - amount);
        nextDamageTime = Time.time + Mathf.Max(0f, invulnerabilityDuration);
        Damaged?.Invoke(this);

        if (currentHealth <= 0)
        {
            Die();
        }

        return true;
    }

    public void ResetHealth()
    {
        currentHealth = Mathf.Max(1, maxHealth);
        nextDamageTime = 0f;
        isDead = false;
    }

    private void Die()
    {
        if (isDead)
        {
            return;
        }

        isDead = true;
        Died?.Invoke(this);

        if (rigidbodyToStop != null)
        {
            rigidbodyToStop.linearVelocity = Vector2.zero;
        }

        if (behavioursToDisableOnDeath != null)
        {
            for (int i = 0; i < behavioursToDisableOnDeath.Length; i++)
            {
                if (behavioursToDisableOnDeath[i] != null)
                {
                    behavioursToDisableOnDeath[i].enabled = false;
                }
            }
        }

        if (collidersToDisableOnDeath != null)
        {
            for (int i = 0; i < collidersToDisableOnDeath.Length; i++)
            {
                if (collidersToDisableOnDeath[i] != null)
                {
                    collidersToDisableOnDeath[i].enabled = false;
                }
            }
        }

        if (destroyOnDeath)
        {
            Destroy(gameObject, Mathf.Max(0f, destroyDelay));
        }
    }
}
