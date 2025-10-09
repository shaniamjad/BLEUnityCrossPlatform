using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime on-screen console that mirrors Unity's debug output.
/// Provides a toggleable overlay so logs can be inspected on devices
/// where the standard console is unavailable (e.g. mobile builds).
/// </summary>
public class OnScreenConsole : MonoBehaviour
{
    private const string ToggleButtonLabelShow = "Show Logs";
    private const string ToggleButtonLabelHide = "Hide Logs";

    [SerializeField] private KeyCode toggleKey = KeyCode.BackQuote;
    [SerializeField] private int maxEntries = 200;

    private readonly Queue<string> logEntries = new Queue<string>();
    private readonly StringBuilder logBuilder = new StringBuilder();
    private readonly object logLock = new object();

    private bool pendingRefresh;
    private bool isVisible;

    private RectTransform panelTransform;
    private ScrollRect scrollRect;
    private Text logText;
    private Text toggleButtonLabel;

    private static OnScreenConsole instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateInstance()
    {
        if (instance != null)
            return;

        var go = new GameObject("OnScreenConsole");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<OnScreenConsole>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        BuildUI();
        SetVisibility(false);

        Application.logMessageReceived += HandleLogMessage;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            Application.logMessageReceived -= HandleLogMessage;
            instance = null;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleVisibility();
        }

        if (!pendingRefresh)
            return;

        RefreshLogText();
    }

    private void HandleLogMessage(string condition, string stackTrace, LogType type)
    {
        lock (logLock)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var prefix = type switch
            {
                LogType.Warning => "[WRN]",
                LogType.Error => "[ERR]",
                LogType.Assert => "[AST]",
                LogType.Exception => "[EXC]",
                _ => "[LOG]"
            };

            var formatted = new StringBuilder()
                .Append('[')
                .Append(timestamp)
                .Append("] ")
                .Append(prefix)
                .Append(' ')
                .Append(condition)
                .ToString();

            logEntries.Enqueue(formatted);

            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
            {
                foreach (var line in stackTrace.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    logEntries.Enqueue($"    {line}");
            }

            while (logEntries.Count > maxEntries)
            {
                logEntries.Dequeue();
            }

            pendingRefresh = true;
        }
    }

    private void RefreshLogText()
    {
        lock (logLock)
        {
            logBuilder.Clear();

            foreach (var entry in logEntries)
            {
                logBuilder.AppendLine(entry);
            }

            if (logText != null)
            {
                logText.text = logBuilder.ToString();
                LayoutRebuilder.ForceRebuildLayoutImmediate(logText.rectTransform);
                Canvas.ForceUpdateCanvases();
                if (scrollRect != null)
                {
                    scrollRect.verticalNormalizedPosition = 0f;
                }
            }

            pendingRefresh = false;
        }
    }

    private void ToggleVisibility()
    {
        SetVisibility(!isVisible);
    }

    private void SetVisibility(bool visible)
    {
        isVisible = visible;

        if (panelTransform != null)
            panelTransform.gameObject.SetActive(isVisible);

        if (toggleButtonLabel != null)
            toggleButtonLabel.text = isVisible ? ToggleButtonLabelHide : ToggleButtonLabelShow;
    }

    private void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        panelTransform = CreatePanel(transform);
        (scrollRect, logText) = CreateScrollView(panelTransform);
        var toggleButton = CreateToggleButton(transform);
        toggleButton.onClick.AddListener(ToggleVisibility);
    }

    private static RectTransform CreatePanel(Transform parent)
    {
        var panelObject = new GameObject("LogPanel", typeof(RectTransform), typeof(Image));
        var rectTransform = panelObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = new Vector2(0f, 0f);
        rectTransform.anchorMax = new Vector2(1f, 0.4f);
        rectTransform.offsetMin = new Vector2(20f, 20f);
        rectTransform.offsetMax = new Vector2(-20f, -20f);

        var background = panelObject.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.7f);

        return rectTransform;
    }

    private static (ScrollRect scrollRect, Text logText) CreateScrollView(RectTransform parent)
    {
        var scrollObject = new GameObject("ScrollView", typeof(RectTransform), typeof(ScrollRect));
        var scrollTransform = scrollObject.GetComponent<RectTransform>();
        scrollTransform.SetParent(parent, false);
        scrollTransform.anchorMin = new Vector2(0f, 0f);
        scrollTransform.anchorMax = new Vector2(1f, 1f);
        scrollTransform.offsetMin = new Vector2(10f, 10f);
        scrollTransform.offsetMax = new Vector2(-10f, -10f);

        var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        var viewportTransform = viewportObject.GetComponent<RectTransform>();
        viewportTransform.SetParent(scrollTransform, false);
        viewportTransform.anchorMin = Vector2.zero;
        viewportTransform.anchorMax = Vector2.one;
        viewportTransform.offsetMin = Vector2.zero;
        viewportTransform.offsetMax = Vector2.zero;

        var viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0f);

        var mask = viewportObject.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        var contentObject = new GameObject("Content", typeof(RectTransform));
        var contentTransform = contentObject.GetComponent<RectTransform>();
        contentTransform.SetParent(viewportTransform, false);
        contentTransform.anchorMin = new Vector2(0f, 1f);
        contentTransform.anchorMax = new Vector2(1f, 1f);
        contentTransform.pivot = new Vector2(0.5f, 1f);
        contentTransform.offsetMin = Vector2.zero;
        contentTransform.offsetMax = Vector2.zero;

        var textObject = new GameObject("LogText", typeof(RectTransform), typeof(Text), typeof(ContentSizeFitter));
        var textTransform = textObject.GetComponent<RectTransform>();
        textTransform.SetParent(contentTransform, false);
        textTransform.anchorMin = new Vector2(0f, 1f);
        textTransform.anchorMax = new Vector2(1f, 1f);
        textTransform.pivot = new Vector2(0.5f, 1f);
        textTransform.offsetMin = Vector2.zero;
        textTransform.offsetMax = Vector2.zero;

        var text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 20;
        text.color = Color.white;
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = "";

        var fitter = textObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scrollRect = scrollObject.GetComponent<ScrollRect>();
        scrollRect.viewport = viewportTransform;
        scrollRect.content = contentTransform;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 20f;

        return (scrollRect, text);
    }

    private Button CreateToggleButton(Transform parent)
    {
        var buttonObject = new GameObject("ToggleButton", typeof(RectTransform), typeof(Image), typeof(Button));
        var rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = new Vector2(1f, 1f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.pivot = new Vector2(1f, 1f);
        rectTransform.anchoredPosition = new Vector2(-20f, -20f);
        rectTransform.sizeDelta = new Vector2(160f, 50f);

        var image = buttonObject.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.7f);

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        var labelTransform = labelObject.GetComponent<RectTransform>();
        labelTransform.SetParent(rectTransform, false);
        labelTransform.anchorMin = Vector2.zero;
        labelTransform.anchorMax = Vector2.one;
        labelTransform.offsetMin = Vector2.zero;
        labelTransform.offsetMax = Vector2.zero;

        var label = labelObject.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = ToggleButtonLabelShow;

        toggleButtonLabel = label;

        return buttonObject.GetComponent<Button>();
    }
}
