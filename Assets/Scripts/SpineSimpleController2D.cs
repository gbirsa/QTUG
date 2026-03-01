using UnityEngine;
using Spine;
using Spine.Unity;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SkeletonAnimation))]
public class SpineSimpleController2D : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private SkeletonAnimation skeletonAnimation;

    [Header("Environment Layers")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private LayerMask wallLayer;

    [Header("Ground Detection (stable on slopes)")]
    [Tooltip("How far to cast the collider down to detect ground. Start 0.10–0.18.")]
    [SerializeField] private float groundCastDistance = 0.12f;

    [Tooltip("How 'floor-like' a surface must be. 0.6 ≈ up to ~53° slopes count as ground.")]
    [Range(0f, 1f)]
    [SerializeField] private float groundNormalYThreshold = 0.6f;

    [Tooltip("Prevents 1-frame ungrounding on slopes/edges (seconds). 0.05–0.12 is common.")]
    [SerializeField] private float groundedGraceTime = 0.08f;

    [Header("Jump Ground Lock (IMPORTANT)")]
    [Tooltip("After jump starts, force airborne for this long so slope-ground logic can't kill the jump.")]
    [SerializeField] private float jumpGroundLockTime = 0.10f;

    [Header("Slope Movement")]
    [Tooltip("When grounded, we move along the slope tangent to avoid launching at crests.")]
    [SerializeField] private float groundSnapDownSpeed = 2.0f;

    [Tooltip("If upward velocity is below this, clamp it to 0 while grounded (prevents micro-launches).")]
    [SerializeField] private float clampUpwardVelWhileGrounded = 2.0f;

    [Tooltip("If true, compensate speed on slopes so horizontal left/right feels similar to flat ground.")]
    [SerializeField] private bool compensateSlopeSpeed = true;

    [Tooltip("Minimum tangent.x used for compensation (prevents crazy speed on near-vertical).")]
    [SerializeField] private float minTangentXForCompensation = 0.35f;

    [Header("Wall Stop (air)")]
    [Tooltip("How far to cast the collider sideways to detect walls. Start 0.06–0.12.")]
    [SerializeField] private float wallCastDistance = 0.08f;

    [Tooltip("How 'vertical' a surface must be to count as a wall. 0.7 means ~45° or steeper.")]
    [Range(0f, 1f)]
    [SerializeField] private float wallNormalXThreshold = 0.7f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float jumpForce = 10f;

    [Header("Air control")]
    [Range(0f, 1f)]
    [SerializeField] private float airControl = 1f;

    [Header("Better Jump (reduces floatiness)")]
    [SerializeField] private float fallMultiplier = 3f;
    [SerializeField] private float lowJumpMultiplier = 2f;

    [Header("Held Jump Height Control")]
    [SerializeField] private float maxHoldRiseTime = 0.12f;
    [SerializeField] private float heldRiseGravityMultiplierAfterMax = 3f;

    [Header("Jump Feel Helpers")]
    [SerializeField] private float jumpBufferTime = 0.12f;
    [SerializeField] private float coyoteTime = 0.10f;

    [Header("Animation / Facing")]
    [SerializeField] private float inputDeadzone = 0.01f;

    [Tooltip("If no input, you can still flip based on velocity when airborne. Helps if you're knocked back.")]
    [SerializeField] private float airFlipVelocityThreshold = 0.12f;

    [Tooltip("Grounded run animation threshold if you *want* run on external pushes. Keep small or set 0 to disable.")]
    [SerializeField] private float groundedRunVelocityThreshold = 0.10f;

    [Header("Spine Animation Names (must match exactly)")]
    [SerializeField] private string idleAnim = "idle";
    [SerializeField] private string idleBreakAnim = "idle break";
    [SerializeField] private string runAnim = "run";

    [Header("Upper-body punch animations (Track 1)")]
    [SerializeField] private string punchUpperGroundAnim = "Punch_Upper";
    [SerializeField] private string punchUpperAirAnim = "Punch_Upper_Air";
    [SerializeField] private float punchMixIn = 0.05f;
    [SerializeField] private float punchMixOut = 0.08f;

    [Header("Jump Animations (Uppercase)")]
    [SerializeField] private string jumpStartAnim = "Jump_Start";
    [SerializeField] private string jumpAirAnim = "Jump_Air";
    [SerializeField] private string jumpLandAnim = "Jump_Land";

    [Header("Spine Mix (Crossfade) Times")]
    [SerializeField] private float mixStartToAir = 0.05f;
    [SerializeField] private float mixAirToLand = 0.0f;
    [SerializeField] private float mixLandToLocomotion = 0.05f;

    [Header("Landing Behavior")]
    [SerializeField] private float minLandShowTime = 0.10f;

    [Tooltip("Must be airborne at least this long to play landing (prevents slope flicker landing).")]
    [SerializeField] private float minAirTimeToLand = 0.06f;

    [Header("Idle Break Settings")]
    [SerializeField] private float idleBreakMinTime = 3f;
    [SerializeField] private float idleBreakMaxTime = 8f;
    [Range(0f, 1f)]
    [SerializeField] private float idleBreakChance = 0.6f;

    // ===== State =====
    private bool isPunching;
    private bool isJumping;
    private bool isLanding;

    // Input
    private float inputX;
    private bool jumpHeldThisFrame;

    // Timers
    private float jumpBufferCounter;
    private float coyoteCounter;
    private float jumpHeldRiseTimer;
    private float landingStartedAt = -999f;
    private float nextIdleBreakTime;

    // Grounding
    private bool isGrounded;
    private bool wasGrounded;
    private float groundedGraceCounter;
    private float airTime;

    // Jump lock
    private float jumpGroundLockTimer;

    // Slope info
    private Vector2 groundNormal = Vector2.up;
    private Vector2 groundTangent = Vector2.right;

    private string currentBaseAnim;

    // Cast filters & buffers
    private ContactFilter2D groundFilter;
    private ContactFilter2D wallFilter;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[12];

    private void Reset()
    {
        rb = GetComponent<Rigidbody2D>();
        skeletonAnimation = GetComponent<SkeletonAnimation>();
    }

    private void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!skeletonAnimation) skeletonAnimation = GetComponent<SkeletonAnimation>();

        if (wallLayer == 0) wallLayer = groundLayer;

        groundFilter = new ContactFilter2D { useLayerMask = true, layerMask = groundLayer, useTriggers = false };
        wallFilter   = new ContactFilter2D { useLayerMask = true, layerMask = wallLayer,   useTriggers = false };

        var data = skeletonAnimation.AnimationState.Data;
        if (data != null)
        {
            data.SetMix(jumpStartAnim, jumpAirAnim, mixStartToAir);
            data.SetMix(jumpAirAnim, jumpLandAnim, mixAirToLand);
            data.SetMix(jumpLandAnim, idleAnim, mixLandToLocomotion);
            data.SetMix(jumpLandAnim, runAnim, mixLandToLocomotion);
        }

        SetBaseAnimation(idleAnim, loop: true);
        ScheduleNextIdleBreak();
    }

    private void Update()
    {
        // INPUT
        float x = 0f;
        bool jumpPressedThisFrame = false;
        bool punchPressed = false;
        jumpHeldThisFrame = false;

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;

            jumpPressedThisFrame = kb.spaceKey.wasPressedThisFrame;
            jumpHeldThisFrame = kb.spaceKey.isPressed;

            punchPressed = kb.eKey.wasPressedThisFrame;
        }
#endif

        inputX = x;

        // Facing:
        // - On ground: ONLY flip from input (prevents turning downhill when stopped on a slope)
        // - In air: can flip from velocity if no input (nice for knockback)
        if (Mathf.Abs(inputX) > inputDeadzone)
        {
            float abs = Mathf.Abs(skeletonAnimation.Skeleton.ScaleX);
            skeletonAnimation.Skeleton.ScaleX = Mathf.Sign(inputX) * abs;
        }
        else if (!isGrounded && Mathf.Abs(rb.linearVelocity.x) > airFlipVelocityThreshold)
        {
            float abs = Mathf.Abs(skeletonAnimation.Skeleton.ScaleX);
            skeletonAnimation.Skeleton.ScaleX = Mathf.Sign(rb.linearVelocity.x) * abs;
        }

        // Punch
        if (punchPressed && !isPunching)
        {
            string punchAnim = isGrounded ? punchUpperGroundAnim : punchUpperAirAnim;
            PlayUpperPunch(punchAnim);
        }

        // Jump buffer
        if (jumpPressedThisFrame) jumpBufferCounter = jumpBufferTime;
        else jumpBufferCounter -= Time.deltaTime;

        // Coyote timer
        if (isGrounded) coyoteCounter = coyoteTime;
        else coyoteCounter -= Time.deltaTime;

        // Jump (snappy)
        bool canJumpNow = jumpBufferCounter > 0f && coyoteCounter > 0f && !isLanding;
        if (canJumpNow)
        {
            jumpBufferCounter = 0f;
            coyoteCounter = 0f;
            StartJump();
        }

        // Landing cancel into run after minimum time
        if (isLanding)
        {
            bool minShown = (Time.time - landingStartedAt) >= minLandShowTime;
            bool wantsMove = Mathf.Abs(inputX) > inputDeadzone;
            if (minShown && wantsMove)
                CancelLandingToRun();
        }

        // Locomotion animations (don’t override Track 0 while jumping/landing)
        if (!isJumping && !isLanding)
        {
            bool inputWantsRun = Mathf.Abs(inputX) > inputDeadzone;

            // Optional: allow "run" when being pushed while grounded.
            // If you don't want that at all, set groundedRunVelocityThreshold = 999.
            bool pushedRun = isGrounded && groundedRunVelocityThreshold > 0f &&
                             Mathf.Abs(rb.linearVelocity.x) > groundedRunVelocityThreshold;

            bool wantsRun = inputWantsRun || pushedRun;

            if (wantsRun)
            {
                SetBaseAnimation(runAnim, loop: true);
                ScheduleNextIdleBreak();
            }
            else
            {
                SetBaseAnimation(idleAnim, loop: true);

                if (Time.time >= nextIdleBreakTime)
                {
                    if (Random.value <= idleBreakChance)
                        PlayIdleBreak();

                    ScheduleNextIdleBreak();
                }
            }
        }
    }

    private void FixedUpdate()
    {
        // Tick jump lock
        if (jumpGroundLockTimer > 0f)
            jumpGroundLockTimer -= Time.fixedDeltaTime;

        // Ground detection (cast + grace) and slope info
        bool castGrounded = CheckGroundedByCast(out Vector2 newGroundNormal);

        if (castGrounded)
        {
            groundedGraceCounter = groundedGraceTime;
            groundNormal = newGroundNormal;

            groundTangent = new Vector2(groundNormal.y, -groundNormal.x).normalized;
            if (groundTangent.x < 0f) groundTangent = -groundTangent;
        }
        else
        {
            groundedGraceCounter -= Time.fixedDeltaTime;
        }

        wasGrounded = isGrounded;

        bool forcedAirborne = jumpGroundLockTimer > 0f;
        isGrounded = !forcedAirborne && (groundedGraceCounter > 0f);

        // Air time for landing gating
        if (!isGrounded) airTime += Time.fixedDeltaTime;
        else airTime = 0f;

        bool justLanded = (!wasGrounded && isGrounded);

        if (justLanded && isJumping && !isLanding && airTime >= minAirTimeToLand)
        {
            StartLanding();
        }
        else if (justLanded && isJumping && !isLanding && airTime < minAirTimeToLand)
        {
            isJumping = false;
        }

        // ===== Movement =====
        bool hasInput = Mathf.Abs(inputX) > inputDeadzone;

        if (isGrounded)
        {
            if (!hasInput)
            {
                // KEY FIX:
                // When stopped on a slope, force velocity to 0 so you DON'T slide down and don't "run" downhill.
                rb.linearVelocity = Vector2.zero;
            }
            else
            {
                // Slope-projected movement along tangent
                float desiredSpeed = inputX * moveSpeed;

                if (compensateSlopeSpeed)
                {
                    // Remove the "slow uphill" feel by compensating for the horizontal component of the tangent.
                    float tx = Mathf.Abs(groundTangent.x);
                    tx = Mathf.Max(tx, minTangentXForCompensation);
                    desiredSpeed /= tx;
                }

                Vector2 desiredVel = groundTangent * desiredSpeed;

                // Prevent micro upward launch while grounded
                if (rb.linearVelocity.y > 0f && rb.linearVelocity.y < clampUpwardVelWhileGrounded)
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);

                rb.linearVelocity = new Vector2(desiredVel.x, desiredVel.y);

                // Small snap-down keeps contact at crests/reversals while moving
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Min(rb.linearVelocity.y, -groundSnapDownSpeed));
            }
        }
        else
        {
            float desiredX = inputX * moveSpeed * airControl;

            if (Mathf.Abs(desiredX) > 0.0001f)
            {
                float dir = Mathf.Sign(desiredX);
                if (IsWallInDirection(dir))
                    desiredX = 0f;
            }

            rb.linearVelocity = new Vector2(desiredX, rb.linearVelocity.y);
        }

        ApplyBetterJumpPhysics();
    }

    private bool CheckGroundedByCast(out Vector2 bestNormal)
    {
        bestNormal = Vector2.up;

        int hitCount = rb.Cast(Vector2.down, groundFilter, castHits, groundCastDistance);
        if (hitCount <= 0) return false;

        bool found = false;
        float bestY = -1f;

        for (int i = 0; i < hitCount; i++)
        {
            Vector2 n = castHits[i].normal;

            if (n.y >= groundNormalYThreshold)
            {
                if (n.y > bestY)
                {
                    bestY = n.y;
                    bestNormal = n;
                    found = true;
                }
            }
        }

        return found;
    }

    private bool IsWallInDirection(float dirSign)
    {
        Vector2 dir = dirSign < 0f ? Vector2.left : Vector2.right;

        int hitCount = rb.Cast(dir, wallFilter, castHits, wallCastDistance);
        if (hitCount <= 0) return false;

        for (int i = 0; i < hitCount; i++)
        {
            if (Mathf.Abs(castHits[i].normal.x) >= wallNormalXThreshold)
                return true;
        }

        return false;
    }

    private void ApplyBetterJumpPhysics()
    {
        if (isGrounded)
        {
            jumpHeldRiseTimer = 0f;
            return;
        }

        if (rb.linearVelocity.y > 0f)
            jumpHeldRiseTimer += Time.fixedDeltaTime;

        if (rb.linearVelocity.y < 0f)
        {
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
            return;
        }

        if (rb.linearVelocity.y > 0f)
        {
            if (!jumpHeldThisFrame)
            {
                rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (lowJumpMultiplier - 1f) * Time.fixedDeltaTime;
                return;
            }

            if (jumpHeldRiseTimer > maxHoldRiseTime)
            {
                rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (heldRiseGravityMultiplierAfterMax - 1f) * Time.fixedDeltaTime;
            }
        }
    }

    private void StartJump()
    {
        jumpGroundLockTimer = jumpGroundLockTime;
        groundedGraceCounter = 0f;
        isGrounded = false;
        airTime = 0f;

        isJumping = true;
        isLanding = false;
        landingStartedAt = -999f;
        jumpHeldRiseTimer = 0f;

        HardClearUpperTrack();

        var state = skeletonAnimation.AnimationState;
        state.ClearTrack(0);

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

        state.SetAnimation(0, jumpStartAnim, false);
        state.AddAnimation(0, jumpAirAnim, false, 0f);

        currentBaseAnim = jumpAirAnim;
        ScheduleNextIdleBreak();
    }

    private void StartLanding()
    {
        isLanding = true;
        isJumping = false;
        landingStartedAt = Time.time;

        HardClearUpperTrack();

        var state = skeletonAnimation.AnimationState;
        state.ClearTrack(0);

        TrackEntry landEntry = state.SetAnimation(0, jumpLandAnim, false);

        landEntry.Complete += _ =>
        {
            isLanding = false;

            bool wantsRun = Mathf.Abs(inputX) > inputDeadzone ||
                            (isGrounded && groundedRunVelocityThreshold > 0f &&
                             Mathf.Abs(rb.linearVelocity.x) > groundedRunVelocityThreshold);

            string after = wantsRun ? runAnim : idleAnim;
            state.SetAnimation(0, after, true);
            currentBaseAnim = after;

            ForcePoseRefresh();
        };

        ScheduleNextIdleBreak();
    }

    private void CancelLandingToRun()
    {
        isLanding = false;
        isJumping = false;

        HardClearUpperTrack();

        var state = skeletonAnimation.AnimationState;
        state.ClearTrack(0);

        SetBaseAnimation(runAnim, loop: true);
        ScheduleNextIdleBreak();
        ForcePoseRefresh();
    }

    private void HardClearUpperTrack()
    {
        var state = skeletonAnimation.AnimationState;
        state.ClearTrack(1);
        state.SetEmptyAnimation(1, 0f);
        isPunching = false;
        ForcePoseRefresh();
    }

    private void ForcePoseRefresh()
    {
        var skel = skeletonAnimation.Skeleton;
        skel.SetToSetupPose();
        skeletonAnimation.AnimationState.Apply(skel);
        skeletonAnimation.Update(0f);
    }

    private void SetBaseAnimation(string animName, bool loop)
    {
        if (string.IsNullOrEmpty(animName)) return;
        if (currentBaseAnim == animName) return;

        skeletonAnimation.AnimationState.SetAnimation(0, animName, loop);
        currentBaseAnim = animName;
    }

    private void PlayIdleBreak()
    {
        var state = skeletonAnimation.AnimationState;
        state.SetAnimation(0, idleBreakAnim, false);
        state.AddAnimation(0, idleAnim, true, 0f);
        currentBaseAnim = idleAnim;
    }

    private void PlayUpperPunch(string animName)
    {
        isPunching = true;

        var state = skeletonAnimation.AnimationState;
        state.ClearTrack(1);

        TrackEntry entry = state.SetAnimation(1, animName, false);
        entry.MixDuration = punchMixIn;
        entry.MixBlend = MixBlend.Replace;
        entry.Alpha = 1f;

        entry.Complete += _ =>
        {
            state.SetEmptyAnimation(1, punchMixOut);
            isPunching = false;
        };
    }

    private void ScheduleNextIdleBreak()
    {
        nextIdleBreakTime = Time.time + Random.Range(idleBreakMinTime, idleBreakMaxTime);
    }
}
