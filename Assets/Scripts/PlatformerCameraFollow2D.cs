using UnityEngine;

public class PlatformerCameraFollow2D : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Primary transform the camera follows. Usually your player root.")]
    [SerializeField] private Transform target;
    [Tooltip("Optional alternate follow point used for framing. Leave empty to follow the target transform directly. Use a chest or head child transform if the target pivot is at the feet.")]
    [SerializeField] private Transform framingTarget;
    [Tooltip("Optional rigidbody used for velocity-based look-ahead. Leave empty to auto-read it from the target.")]
    [SerializeField] private Rigidbody2D targetRb;
    [Tooltip("Optional child camera used for idle zoom. Leave empty to auto-find the first child camera.")]
    [SerializeField] private Camera childCamera;

    [Header("Framing")]
    [Tooltip("Base camera offset from the followed point.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 43f, -192.2f);

    [Header("Follow Smoothing")]
    [Tooltip("Horizontal follow smoothing. Lower values feel snappier.")]
    [SerializeField] private float smoothTimeX = 0.12f;
    [Tooltip("Vertical smoothing while the camera is simply settling or holding position.")]
    [SerializeField] private float smoothTimeY = 0.18f;
    [Tooltip("Vertical smoothing used after the target crosses a vertical dead-zone and the camera starts recentering.")]
    [SerializeField] private float verticalFollowSmoothTime = 0.4f;
    [Tooltip("Vertical smoothing used when the camera is following or recentering downward.")]
    [SerializeField] private float verticalDownFollowSmoothTime = 0.22f;
    [Tooltip("Vertical smoothing used to drift back to the resting framing while grounded and inside the dead-zone.")]
    [SerializeField] private float groundedVerticalRecenterSmoothTime = 0.7f;
    [Tooltip("Maximum SmoothDamp speed for both axes.")]
    [SerializeField] private float maxSpeed = Mathf.Infinity;

    [Header("Dead Zones")]
    [Tooltip("How far the target can move upward before the camera starts recentering vertically.")]
    [SerializeField] private float verticalDeadZoneUp = 40f;
    [Tooltip("How far the target can move downward before the camera starts recentering vertically.")]
    [SerializeField] private float verticalDeadZoneDown = 22f;
    [Tooltip("Multiplier applied to the downward dead-zone while the target is moving down. Lower values make the camera react sooner on drops and slopes.")]
    [SerializeField] private float descendingVerticalDeadZoneMultiplier = 0.35f;
    [Tooltip("How close the target must get to the framing point before vertical follow stops again.")]
    [SerializeField] private float verticalStopTolerance = 3f;
    [Tooltip("Optional horizontal dead zone. Set 0 to always follow X.")]
    [SerializeField] private float horizontalDeadZone = 0f;
    [Tooltip("How close the target must get to the framing point before horizontal follow stops again.")]
    [SerializeField] private float horizontalStopTolerance = 1f;

    [Header("Look Ahead")]
    [Tooltip("If enabled, shifts the camera ahead in the direction of horizontal movement.")]
    [SerializeField] private bool useHorizontalLookAhead = false;
    [Tooltip("Maximum horizontal look-ahead distance based on target velocity.")]
    [SerializeField] private float lookAheadDistance = 1.5f;
    [Tooltip("How quickly the horizontal look-ahead catches up to changes in velocity.")]
    [SerializeField] private float lookAheadSmoothTime = 0.15f;
    [Tooltip("Extra downward offset added while falling so more landing space is visible.")]
    [SerializeField] private float fallLookAheadDistance = 18f;
    [Tooltip("Falling speed at which downward look-ahead reaches its full amount.")]
    [SerializeField] private float fallLookAheadMaxSpeed = 80f;

    [Header("Idle Zoom")]
    [Tooltip("If enabled, the camera slowly zooms in after the target has been idle for a short time.")]
    [SerializeField] private bool enableIdleZoom = true;
    [Tooltip("How many seconds the target must stay nearly still before the zoom-in starts.")]
    [SerializeField] private float idleDelay = 5f;
    [Tooltip("Velocity magnitude below this counts as idle.")]
    [SerializeField] private float idleMovementThreshold = 0.5f;
    [Tooltip("For perspective cameras this is FOV degrees. For orthographic cameras this is size units.")]
    [SerializeField] private float idleZoomAmount = 15f;
    [Tooltip("How slowly the idle zoom eases in.")]
    [SerializeField] private float idleZoomInSmoothTime = 4f;
    [Tooltip("How quickly the camera zooms back out when the target moves.")]
    [SerializeField] private float idleZoomOutSmoothTime = 0.4f;
    [Tooltip("Extra camera-root offset applied while zoomed to keep the zoom focused on the character instead of screen center.")]
    [SerializeField] private Vector2 idleZoomFramingOffset = new Vector2(0f, -15.5f);
    [Tooltip("How quickly the zoom framing offset eases in and out.")]
    [SerializeField] private float idleZoomFramingSmoothTime = 0.5f;

    private Vector3 _followVelocity;
    private float _lookAheadVelocity;
    private float _currentLookAhead;
    private bool _isFollowingX;
    private bool _isFollowingY;
    private bool _isRecenteringY;
    private SpineSimpleController2D _targetController;
    private float _idleTimer;
    private float _zoomVelocity;
    private float _baseFieldOfView;
    private float _baseOrthographicSize;
    private Vector3 _idleZoomOffsetCurrent;
    private Vector3 _idleZoomOffsetVelocity;

    private Transform ActiveTarget => framingTarget ? framingTarget : target;

    private void Awake()
    {
        if (!targetRb && target)
            targetRb = target.GetComponent<Rigidbody2D>();
        if (!_targetController && target)
            _targetController = target.GetComponent<SpineSimpleController2D>();
        if (!childCamera)
            childCamera = GetComponentInChildren<Camera>();

        if (childCamera)
        {
            _baseFieldOfView = childCamera.fieldOfView;
            _baseOrthographicSize = childCamera.orthographicSize;
        }
    }

    private void LateUpdate()
    {
        Transform focus = ActiveTarget;
        if (!focus)
            return;

        Vector3 rootNoZoom = transform.position - _idleZoomOffsetCurrent;
        Vector3 desired = GetDesiredPosition(focus.position, rootNoZoom);

        float newX = Mathf.SmoothDamp(rootNoZoom.x, desired.x, ref _followVelocity.x, smoothTimeX, maxSpeed);
        bool cameraMovingDown = desired.y < rootNoZoom.y - 0.01f;
        float verticalSmoothTime = _isFollowingY
            ? (cameraMovingDown ? verticalDownFollowSmoothTime : verticalFollowSmoothTime)
            : (_isRecenteringY
                ? (cameraMovingDown ? Mathf.Min(groundedVerticalRecenterSmoothTime, verticalDownFollowSmoothTime) : groundedVerticalRecenterSmoothTime)
                : smoothTimeY);
        float newY = Mathf.SmoothDamp(rootNoZoom.y, desired.y, ref _followVelocity.y, verticalSmoothTime, maxSpeed);

        Vector3 basePosition = new Vector3(newX, newY, desired.z);
        float zoomAmount01 = HandleIdleZoom();

        Vector3 idleZoomOffsetTarget = new Vector3(idleZoomFramingOffset.x, idleZoomFramingOffset.y, 0f) * zoomAmount01;
        _idleZoomOffsetCurrent = Vector3.SmoothDamp(
            _idleZoomOffsetCurrent,
            idleZoomOffsetTarget,
            ref _idleZoomOffsetVelocity,
            idleZoomFramingSmoothTime
        );

        transform.position = basePosition + _idleZoomOffsetCurrent;
    }

    public void SnapToTarget()
    {
        Transform focus = ActiveTarget;
        if (!focus)
            return;

        _isFollowingX = false;
        _isFollowingY = false;
        _followVelocity = Vector3.zero;
        _lookAheadVelocity = 0f;
        _currentLookAhead = 0f;
        _idleTimer = 0f;
        _zoomVelocity = 0f;
        _idleZoomOffsetCurrent = Vector3.zero;
        _idleZoomOffsetVelocity = Vector3.zero;

        transform.position = focus.position + offset;
        ResetZoom();
    }

    private Vector3 GetDesiredPosition(Vector3 focusPosition, Vector3 currentPosition)
    {
        Vector3 framingPoint = focusPosition + offset;

        float velocityX = targetRb ? targetRb.linearVelocity.x : 0f;
        float velocityY = targetRb ? targetRb.linearVelocity.y : 0f;

        float desiredX = GetDesiredAxisPosition(
            currentPosition.x,
            framingPoint.x + GetHorizontalLookAhead(velocityX),
            horizontalDeadZone,
            horizontalDeadZone,
            horizontalStopTolerance,
            ref _isFollowingX
        );

        float desiredY = GetDesiredVerticalPosition(currentPosition.y, framingPoint.y + GetFallLookAhead(velocityY));

        return new Vector3(desiredX, desiredY, framingPoint.z);
    }

    private float GetDesiredVerticalPosition(float currentPosition, float targetPosition)
    {
        _isRecenteringY = false;

        float upDeadZone = Mathf.Max(0f, verticalDeadZoneUp);
        float velocityY = targetRb ? targetRb.linearVelocity.y : 0f;
        float downDeadZone = Mathf.Max(0f, verticalDeadZoneDown);
        if (velocityY < -0.01f)
            downDeadZone *= Mathf.Clamp(descendingVerticalDeadZoneMultiplier, 0.05f, 1f);
        float tolerance = Mathf.Max(0f, verticalStopTolerance);

        if (upDeadZone <= 0f && downDeadZone <= 0f)
        {
            _isFollowingY = false;
            return targetPosition;
        }

        float delta = targetPosition - currentPosition;
        float activeDeadZone = delta >= 0f ? upDeadZone : downDeadZone;
        bool isGrounded = _targetController && _targetController.IsGrounded;

        if (!_isFollowingY)
        {
            if (Mathf.Abs(delta) > activeDeadZone)
            {
                _isFollowingY = true;
                return targetPosition;
            }

            if (isGrounded && Mathf.Abs(delta) > tolerance)
            {
                _isRecenteringY = true;
                return targetPosition;
            }

            return currentPosition;
        }

        if (Mathf.Abs(delta) <= tolerance)
        {
            _isFollowingY = false;
            return isGrounded ? targetPosition : currentPosition;
        }

        return targetPosition;
    }

    private float GetDesiredAxisPosition(
        float currentPosition,
        float targetPosition,
        float positiveDeadZone,
        float negativeDeadZone,
        float stopTolerance,
        ref bool isFollowing)
    {
        if (positiveDeadZone <= 0f && negativeDeadZone <= 0f)
        {
            isFollowing = false;
            return targetPosition;
        }

        float delta = targetPosition - currentPosition;
        float activeDeadZone = delta >= 0f ? positiveDeadZone : negativeDeadZone;

        if (!isFollowing)
        {
            if (Mathf.Abs(delta) > Mathf.Max(0f, activeDeadZone))
                isFollowing = true;
        }
        else if (Mathf.Abs(delta) <= Mathf.Max(0f, stopTolerance))
        {
            isFollowing = false;
        }

        return isFollowing ? targetPosition : currentPosition;
    }

    private float GetHorizontalLookAhead(float velocityX)
    {
        if (!useHorizontalLookAhead || lookAheadDistance <= 0f)
            return 0f;

        float desiredLookAhead = Mathf.Clamp(velocityX, -1f, 1f) * lookAheadDistance;
        _currentLookAhead = Mathf.SmoothDamp(
            _currentLookAhead,
            desiredLookAhead,
            ref _lookAheadVelocity,
            lookAheadSmoothTime
        );

        return _currentLookAhead;
    }

    private float GetFallLookAhead(float velocityY)
    {
        if (fallLookAheadDistance <= 0f || velocityY >= 0f)
            return 0f;

        float normalizedFallSpeed = Mathf.Clamp01(Mathf.Abs(velocityY) / Mathf.Max(0.01f, fallLookAheadMaxSpeed));
        return -fallLookAheadDistance * normalizedFallSpeed;
    }

    private float HandleIdleZoom()
    {
        if (!enableIdleZoom || !childCamera)
            return 0f;

        float speed = targetRb ? targetRb.linearVelocity.magnitude : 0f;
        bool isIdle = speed <= idleMovementThreshold;
        _idleTimer = isIdle ? _idleTimer + Time.deltaTime : 0f;

        bool shouldZoomIn = _idleTimer >= idleDelay;
        float smoothTime = shouldZoomIn ? idleZoomInSmoothTime : idleZoomOutSmoothTime;

        if (childCamera.orthographic)
        {
            float targetSize = shouldZoomIn
                ? Mathf.Max(0.01f, _baseOrthographicSize - idleZoomAmount)
                : _baseOrthographicSize;

            childCamera.orthographicSize = Mathf.SmoothDamp(
                childCamera.orthographicSize,
                targetSize,
                ref _zoomVelocity,
                smoothTime
            );

            float denom = Mathf.Max(0.0001f, idleZoomAmount);
            return Mathf.Clamp01((_baseOrthographicSize - childCamera.orthographicSize) / denom);
        }

        float targetFov = shouldZoomIn
            ? Mathf.Max(1f, _baseFieldOfView - idleZoomAmount)
            : _baseFieldOfView;

        childCamera.fieldOfView = Mathf.SmoothDamp(
            childCamera.fieldOfView,
            targetFov,
            ref _zoomVelocity,
            smoothTime
        );

        float fovDenom = Mathf.Max(0.0001f, idleZoomAmount);
        return Mathf.Clamp01((_baseFieldOfView - childCamera.fieldOfView) / fovDenom);
    }

    private void ResetZoom()
    {
        if (!childCamera)
            return;

        if (childCamera.orthographic)
            childCamera.orthographicSize = _baseOrthographicSize;
        else
            childCamera.fieldOfView = _baseFieldOfView;
    }

}
