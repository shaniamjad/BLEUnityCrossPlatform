using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Lightweight toast/bubble notification for BLE workflow feedback.
/// </summary>
public class UI_BLEToast : MonoBehaviour
{
    public static UI_BLEToast Instance { get; private set; }

    [SerializeField] private TMP_Text messageLabel;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField, Min(0.1f)] private float displaySeconds = 3f;
    [SerializeField, Min(0.05f)] private float fadeDuration = 0.2f;

    private Coroutine routine;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Displays a toast message for the configured duration.
    /// </summary>
    public void Show(string message, float? durationOverride = null)
    {
        if (string.IsNullOrEmpty(message))
            return;

        if (routine != null)
        {
            StopCoroutine(routine);
        }

        float duration = durationOverride.HasValue && durationOverride.Value > 0f
            ? durationOverride.Value
            : displaySeconds;

        routine = StartCoroutine(ShowRoutine(message, duration));
    }

    private IEnumerator ShowRoutine(string message, float duration)
    {
        if (messageLabel != null)
        {
            messageLabel.text = message;
        }

        yield return FadeTo(1f);
        yield return new WaitForSeconds(duration);
        yield return FadeTo(0f);

        routine = null;
    }

    private IEnumerator FadeTo(float targetAlpha)
    {
        if (canvasGroup == null)
        {
            yield break;
        }

        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = fadeDuration > 0f ? Mathf.Clamp01(elapsed / fadeDuration) : 1f;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
    }
}
