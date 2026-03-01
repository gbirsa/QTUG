using UnityEngine;

public class CameraFollow2D : MonoBehaviour
{
    [Header("Assign these")]
    [Tooltip("Your player/character transform.")]
    [SerializeField] private Transform target;

    [Tooltip("Put this script on the CAMERA ROOT. Drag the child Camera here.")]
    [SerializeField] private Camera childCamera;

    [Tooltip("Optional. Used for look-ahead + idle detection. Unity 6: linearVelocity.")]
    [SerializeField] private Rigidbody2D targetRb;

    [Header("Framing")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    [Header("Rubber Band (Smooth Follow)")]
    [Tooltip("Lower = snappier, higher = more lag.")]
    [SerializeField] private float smoothTimeX = 0.12f;
    [Tooltip("Lower = snappier, higher = more lag.")]
    [SerializeField] private float smoothTimeY = 0.18f;
    [SerializeField] private float maxSpeed = Mathf.Infinity;

    [Header("Vertical Dead Zone (no bob on small jumps)")]
    [Tooltip("How far above screen center the player can move before the camera starts following vertically.")]
    [SerializeField] private float verticalScreenFollowThresholdUp = 0.18f;
    [Tooltip("How far below screen center the player can move before the camera starts following vertically.")]
    [SerializeField] private float verticalScreenFollowThresholdDown = 0.16f;
    [Tooltip("How close to screen center the player must be before vertical follow stops again.")]
    [SerializeField] private float verticalScreenStopFollowTolerance = 0.08f;
    [Tooltip("Extra downward framing while falling so you can see where you're landing.")]
    [SerializeField] private float fallLookAheadDistance = 1.25f;
    [Tooltip("Falling speed where downward look-ahead reaches its maximum.")]
    [SerializeField] private float fallLookAheadMaxSpeed = 12f;
    [Header("Horizontal Look-Ahead (optional)")]
    [SerializeField] private bool useLookAhead = true;
    [SerializeField] private float lookAheadDistance = 1.5f;
    [SerializeField] private float lookAheadSmoothTime = 0.15f;

    [Header("Idle Zoom (Perspective: FOV)")]
    [SerializeField] private bool enableIdleZoom = true;

    [Tooltip("Seconds of (almost) no movement before zoom starts.")]
    [SerializeField] private float idleDelay = 2.0f;

    [Tooltip("How many FOV degrees to zoom in by when idle.")]
    [SerializeField] private float idleZoomInDegrees = 8.0f;

    [Header("Zoom Speeds")]
    [Tooltip("How slow/gradual the idle zoom-in is.")]
    [SerializeField] private float zoomInSmoothTime = 1.2f;

    [Tooltip("How fast it snaps back out when the player moves.")]
    [SerializeField] private float zoomOutSmoothTime = 0.25f;

    [Header("Zoom Framing Offset (applied while zoomed)")]
    [Tooltip("World-units offset applied to the CAMERA ROOT while zoomed (X,Y). Use negative Y to 'center' the character more.")]
    [SerializeField] private Vector2 zoomFramingOffset = new Vector2(0f, -1.0f);

    [Tooltip("How fast the zoom framing offset eases in/out.")]
    [SerializeField] private float zoomFramingSmoothTime = 0.35f;

    // Internal state
    private Vector3 _followVel;
    private float _lookAheadVel;
    private float _currentLookAhead;

    private float _idleTimer;
    private bool _verticalFollowActive;
    private float _verticalViewportAnchor = 0.5f;

    private float _baseFov;
    private float _fovVel;

    private Vector3 _zoomOffsetCurrent;
    private Vector3 _zoomOffsetVel;

    private void Reset()
    {
        childCamera = GetComponentInChildren<Camera>();
    }

    private void Awake()
    {
        if (!childCamera) childCamera = GetComponentInChildren<Camera>();
        if (!targetRb && target) targetRb = target.GetComponent<Rigidbody2D>();

        if (childCamera)
            _baseFov = childCamera.fieldOfView;

        CacheVerticalViewportAnchor();
    }

    private void LateUpdate()
    {
        if (!target || !childCamera) return;

        // IMPORTANT: follow smoothing must use the root position WITHOUT zoom offset
        Vector3 rootNoZoom = transform.position - _zoomOffsetCurrent;

        // 1) Desired X (with optional look-ahead)
        float targetX = target.position.x;

        if (useLookAhead)
        {
            float vx = targetRb ? targetRb.linearVelocity.x : 0f; // Unity 6
            float desiredLookAhead = Mathf.Clamp(vx, -1f, 1f) * lookAheadDistance;

            _currentLookAhead = Mathf.SmoothDamp(
                _currentLookAhead,
                desiredLookAhead,
                ref _lookAheadVel,
                lookAheadSmoothTime
            );

            targetX += _currentLookAhead;
        }

        // 2) Vertical dead-zone anchor
        float targetY = target.position.y;
        float cameraCenterY = rootNoZoom.y - offset.y;
        float targetViewportY = childCamera.WorldToViewportPoint(target.position).y;
        float desiredYCenter = GetDesiredCameraCenterY(cameraCenterY, targetY, targetViewportY);

        // 3) Base follow target (no zoom offset yet)
        Vector3 desired = new Vector3(targetX, desiredYCenter, target.position.z) + offset;

        float newX = Mathf.SmoothDamp(rootNoZoom.x, desired.x, ref _followVel.x, smoothTimeX, maxSpeed);
        float newY = Mathf.SmoothDamp(rootNoZoom.y, desired.y, ref _followVel.y, smoothTimeY, maxSpeed);

        Vector3 basePos = new Vector3(newX, newY, desired.z);

        // 4) Idle zoom (FOV) and get zoom amount 0..1
        float zoom01 = HandleIdleZoomAndGetAmount01(out bool isMoving);

        // 5) Apply zoom framing offset (predictable composition)
        Vector3 zoomOffsetTarget = new Vector3(zoomFramingOffset.x, zoomFramingOffset.y, 0f) * zoom01;

        _zoomOffsetCurrent = Vector3.SmoothDamp(
            _zoomOffsetCurrent,
            zoomOffsetTarget,
            ref _zoomOffsetVel,
            zoomFramingSmoothTime
        );

        // 6) Final root position
        transform.position = basePos + _zoomOffsetCurrent;
    }

    private float HandleIdleZoomAndGetAmount01(out bool isMoving)
    {
        isMoving = false;

        if (!enableIdleZoom || !childCamera)
            return 0f;

        if (targetRb)
        {
            Vector2 v = targetRb.linearVelocity;
            isMoving = v.sqrMagnitude > 0.02f;
        }
        else
        {
            isMoving = Mathf.Abs(_currentLookAhead) > 0.05f;
        }

        _idleTimer = isMoving ? 0f : _idleTimer + Time.deltaTime;

        float targetFov = _baseFov;
        bool shouldZoomIn = (_idleTimer >= idleDelay);

        if (shouldZoomIn)
            targetFov = Mathf.Max(1f, _baseFov - idleZoomInDegrees);

        // Use different smooth times depending on direction
        float currentFov = childCamera.fieldOfView;
        bool zoomingInNow = targetFov < currentFov;     // decreasing FOV = zoom in
        float smooth = zoomingInNow ? zoomInSmoothTime : zoomOutSmoothTime;

        childCamera.fieldOfView = Mathf.SmoothDamp(
            currentFov,
            targetFov,
            ref _fovVel,
            smooth
        );

        float denom = Mathf.Max(0.0001f, idleZoomInDegrees);
        return Mathf.Clamp01((_baseFov - childCamera.fieldOfView) / denom);
    }

    public void SnapToTarget()
    {
        if (!target) return;

        _verticalFollowActive = false;

        _zoomOffsetCurrent = Vector3.zero;
        _zoomOffsetVel = Vector3.zero;

        Vector3 desired = target.position + offset;
        transform.position = desired;
        CacheVerticalViewportAnchor();

        _followVel = Vector3.zero;
        _lookAheadVel = 0f;
        _currentLookAhead = 0f;

        _idleTimer = 0f;

        if (childCamera)
        {
            childCamera.fieldOfView = _baseFov;
            _fovVel = 0f;
        }
    }

    private float GetDesiredCameraCenterY(float cameraCenterY, float targetY, float targetViewportY)
    {
        float upThreshold = Mathf.Clamp(verticalScreenFollowThresholdUp, 0f, 0.49f);
        float downThreshold = Mathf.Clamp(verticalScreenFollowThresholdDown, 0f, 0.49f);

        if (upThreshold <= 0f && downThreshold <= 0f)
        {
            _verticalFollowActive = false;
            return targetY;
        }

        float vy = targetRb ? targetRb.linearVelocity.y : 0f;
        float anchor = Mathf.Clamp01(_verticalViewportAnchor);
        float upperTrigger = anchor + upThreshold;
        float lowerTrigger = anchor - downThreshold;

        if (!_verticalFollowActive)
        {
            bool leftUpperDeadZone = targetViewportY > upperTrigger;
            bool leftLowerDeadZone = targetViewportY < lowerTrigger;

            if (leftUpperDeadZone || leftLowerDeadZone)
                _verticalFollowActive = true;
        }
        else
        {
            float distanceFromCenter = Mathf.Abs(targetViewportY - anchor);
            if (distanceFromCenter <= Mathf.Clamp(verticalScreenStopFollowTolerance, 0f, 0.25f))
                _verticalFollowActive = false;
        }

        if (!_verticalFollowActive)
            return cameraCenterY;

        float fallLookAhead = 0f;
        if (vy < 0f && fallLookAheadDistance > 0f)
        {
            float normalizedFallSpeed = Mathf.Clamp01(Mathf.Abs(vy) / Mathf.Max(0.01f, fallLookAheadMaxSpeed));
            fallLookAhead = -fallLookAheadDistance * normalizedFallSpeed;
        }

        return targetY + fallLookAhead;
    }

    private void CacheVerticalViewportAnchor()
    {
        if (!childCamera || !target)
            return;

        Vector3 viewportPoint = childCamera.WorldToViewportPoint(target.position);
        if (viewportPoint.z > 0f)
            _verticalViewportAnchor = viewportPoint.y;
    }
}
