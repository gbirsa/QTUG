using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class DamageBlinkFeedback2D : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Health component that drives the feedback.")]
    [SerializeField] private Health2D health;

    [Tooltip("Renderers blinked when damage is taken. If empty, child renderers are auto-filled.")]
    [SerializeField] private Renderer[] renderersToBlink;

    [Header("Blink")]
    [Tooltip("Total white flash time after taking damage.")]
    [SerializeField] private float blinkDuration = 0.14f;

    [Tooltip("How many on/off pulses happen during the flash.")]
    [SerializeField] private int blinkCount = 2;

    [Tooltip("Color used for the hit flash.")]
    [SerializeField] private Color flashColor = Color.white;

    [Tooltip("Shader property used by Spine fill shaders to control flash amount.")]
    [SerializeField] private string fillPhaseProperty = "_FillPhase";

    [Tooltip("Shader property used by Spine fill shaders to control flash color.")]
    [SerializeField] private string fillColorProperty = "_FillColor";

    [Tooltip("Shader property used by Spine shaders to enable the fill effect.")]
    [SerializeField] private string fillToggleProperty = "_Fill";

    [Header("Death")]
    [Tooltip("Hide the character renderers when health reaches zero.")]
    [SerializeField] private bool hideOnDeath = true;

    private Coroutine blinkRoutine;
    private Color[] originalSpriteColors;
    private MaterialPropertyBlock materialPropertyBlock;
    private int fillTogglePropertyId;
    private int fillPhasePropertyId;
    private int fillColorPropertyId;
    private bool flashActive;

    private void Reset()
    {
        health = GetComponent<Health2D>();
        if (renderersToBlink == null || renderersToBlink.Length == 0)
        {
            renderersToBlink = GetComponentsInChildren<Renderer>(true);
        }
    }

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponent<Health2D>();
        }

        if (renderersToBlink == null || renderersToBlink.Length == 0)
        {
            renderersToBlink = GetComponentsInChildren<Renderer>(true);
        }

        CacheRendererState();
    }

    private void OnEnable()
    {
        if (health == null)
        {
            return;
        }

        health.Damaged += HandleDamaged;
        health.Died += HandleDied;
    }

    private void LateUpdate()
    {
        SetFlashState(flashActive);
    }

    private void OnDisable()
    {
        if (health == null)
        {
            return;
        }

        health.Damaged -= HandleDamaged;
        health.Died -= HandleDied;
    }

    private void HandleDamaged(Health2D damagedHealth)
    {
        if (damagedHealth.IsDead)
        {
            return;
        }

        if (blinkRoutine != null)
        {
            StopCoroutine(blinkRoutine);
        }

        blinkRoutine = StartCoroutine(BlinkRoutine());
    }

    private void HandleDied(Health2D deadHealth)
    {
        if (!hideOnDeath)
        {
            return;
        }

        SetRenderersEnabled(false);
    }

    private IEnumerator BlinkRoutine()
    {
        float halfBlink = Mathf.Max(0.01f, blinkDuration / Mathf.Max(1, blinkCount * 2));

        for (int i = 0; i < blinkCount; i++)
        {
            flashActive = true;
            yield return new WaitForSeconds(halfBlink);
            flashActive = false;
            yield return new WaitForSeconds(halfBlink);
        }

        flashActive = false;
        SetFlashState(false);
        blinkRoutine = null;
    }

    private void CacheRendererState()
    {
        if (renderersToBlink == null)
        {
            return;
        }

        originalSpriteColors = new Color[renderersToBlink.Length];
        for (int i = 0; i < renderersToBlink.Length; i++)
        {
            if (renderersToBlink[i] is SpriteRenderer spriteRenderer)
            {
                originalSpriteColors[i] = spriteRenderer.color;
            }
            else
            {
                originalSpriteColors[i] = Color.white;
            }
        }

        materialPropertyBlock = new MaterialPropertyBlock();
        fillTogglePropertyId = Shader.PropertyToID(fillToggleProperty);
        fillPhasePropertyId = Shader.PropertyToID(fillPhaseProperty);
        fillColorPropertyId = Shader.PropertyToID(fillColorProperty);
    }

    private void SetFlashState(bool active)
    {
        if (renderersToBlink == null)
        {
            return;
        }

        for (int i = 0; i < renderersToBlink.Length; i++)
        {
            Renderer renderer = renderersToBlink[i];
            if (renderer == null)
            {
                continue;
            }

            if (renderer is SpriteRenderer spriteRenderer)
            {
                spriteRenderer.color = active ? flashColor : originalSpriteColors[i];
                continue;
            }

            renderer.GetPropertyBlock(materialPropertyBlock);
            materialPropertyBlock.SetFloat(fillTogglePropertyId, active ? 1f : 0f);
            materialPropertyBlock.SetColor(fillColorPropertyId, flashColor);
            materialPropertyBlock.SetFloat(fillPhasePropertyId, active ? 1f : 0f);
            renderer.SetPropertyBlock(materialPropertyBlock);
        }
    }

    private void SetRenderersEnabled(bool enabled)
    {
        if (renderersToBlink == null)
        {
            return;
        }

        for (int i = 0; i < renderersToBlink.Length; i++)
        {
            if (renderersToBlink[i] != null)
            {
                renderersToBlink[i].enabled = enabled;
            }
        }
    }
}
