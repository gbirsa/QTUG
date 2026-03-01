using UnityEngine;

[DisallowMultipleComponent]
public class GroundProjectedShadow2D : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Shadow transform that gets projected onto the ground. Defaults to the child named Shadow.")]
    [SerializeField] private Transform shadowTransform;

    [Tooltip("Renderer toggled on and off when ground is found or missing. Defaults to the shadow object's renderer.")]
    [SerializeField] private Renderer shadowRenderer;

    [Tooltip("Optional controller used to copy the same ground layer mask as the character.")]
    [SerializeField] private SpineSimpleController2D controller;

    [Header("Ground Detection")]
    [Tooltip("Layers treated as valid ground. If left empty, the controller's ground layer is used.")]
    [SerializeField] private LayerMask groundLayer;

    [Tooltip("How far above the character the downward ground check starts.")]
    [SerializeField] private float rayStartHeight = 2f;

    [Tooltip("Maximum distance below the character where the shadow is still allowed to appear.")]
    [SerializeField] private float maxShadowDistance = 80f;

    [Tooltip("Small lift above the hit surface so the shadow does not clip into the ground.")]
    [SerializeField] private float surfaceOffset = 0.05f;

    [Header("Shadow Scaling")]
    [Tooltip("Shadow scale used when the character is at or near the ground.")]
    [SerializeField] private Vector3 baseScale = new Vector3(12.720456f, 0.9955139f, 13.357f);

    [Tooltip("Smallest scale multiplier used when the character is high above the ground.")]
    [Range(0f, 1f)]
    [SerializeField] private float minScaleMultiplier = 0.35f;

    [Tooltip("Height at which the shadow reaches its minimum size.")]
    [SerializeField] private float shrinkDistance = 45f;

    [Tooltip("How quickly the shadow moves to its new ground position.")]
    [SerializeField] private float positionSmoothTime = 0.06f;

    [Tooltip("How quickly the shadow scales as the character rises or falls.")]
    [SerializeField] private float scaleSmoothTime = 0.12f;

    private Vector3 shadowWorldPositionVelocity;
    private Vector3 shadowScaleVelocity;
    private float shadowWorldZOffset;

    private void Reset()
    {
        AssignReferences();
        CacheDefaults();
    }

    private void OnValidate()
    {
        AssignReferences();
        CacheDefaults();
    }

    private void Awake()
    {
        AssignReferences();
        CacheDefaults();
        shadowWorldZOffset = shadowTransform != null ? shadowTransform.position.z - transform.position.z : 0f;
    }

    private void LateUpdate()
    {
        if (shadowTransform == null)
        {
            return;
        }

        Vector2 rayOrigin = new Vector2(transform.position.x, transform.position.y + rayStartHeight);
        float rayDistance = rayStartHeight + maxShadowDistance;
        RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, rayDistance, ResolveGroundLayer());

        if (hit.collider == null)
        {
            SetShadowVisible(false);
            return;
        }

        Vector3 desiredWorldPosition = new Vector3(
            transform.position.x,
            hit.point.y + surfaceOffset,
            transform.position.z + shadowWorldZOffset
        );

        if (shadowRenderer != null && !shadowRenderer.enabled)
        {
            shadowTransform.position = desiredWorldPosition;
        }
        else
        {
            shadowTransform.position = Vector3.SmoothDamp(
                shadowTransform.position,
                desiredWorldPosition,
                ref shadowWorldPositionVelocity,
                positionSmoothTime
            );
        }

        float heightAboveGround = Mathf.Max(0f, transform.position.y - hit.point.y);
        float shrinkT = shrinkDistance <= 0f ? 1f : Mathf.Clamp01(heightAboveGround / shrinkDistance);
        float scaleMultiplier = Mathf.Lerp(1f, minScaleMultiplier, shrinkT);
        Vector3 targetScale = baseScale * scaleMultiplier;

        shadowTransform.localScale = Vector3.SmoothDamp(
            shadowTransform.localScale,
            targetScale,
            ref shadowScaleVelocity,
            scaleSmoothTime
        );

        SetShadowVisible(true);
    }

    private void AssignReferences()
    {
        if (controller == null)
        {
            controller = GetComponent<SpineSimpleController2D>();
        }

        if (shadowTransform == null)
        {
            Transform candidate = transform.Find("Shadow");
            if (candidate != null)
            {
                shadowTransform = candidate;
            }
        }

        if (shadowRenderer == null && shadowTransform != null)
        {
            shadowRenderer = shadowTransform.GetComponent<Renderer>();
        }
    }

    private void CacheDefaults()
    {
        if (shadowTransform != null && baseScale == Vector3.zero)
        {
            baseScale = shadowTransform.localScale;
        }
    }

    private int ResolveGroundLayer()
    {
        if (groundLayer.value != 0)
        {
            return groundLayer.value;
        }

        if (controller != null)
        {
            return controller.GroundLayer.value;
        }

        return Physics2D.DefaultRaycastLayers;
    }

    private void SetShadowVisible(bool visible)
    {
        if (shadowRenderer == null)
        {
            return;
        }

        if (!visible)
        {
            shadowWorldPositionVelocity = Vector3.zero;
            shadowScaleVelocity = Vector3.zero;
        }

        shadowRenderer.enabled = visible;
    }
}
