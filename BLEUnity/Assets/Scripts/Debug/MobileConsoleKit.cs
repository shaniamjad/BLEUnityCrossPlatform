using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MobileConsole
{
    /// <summary>
    /// Lightweight in-game console that collects Application logs and renders them on-screen.
    /// Automatically instantiates itself on the first scene load and persists across scenes.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class MobileConsoleKit : MonoBehaviour
    {
        private const string ConsoleRootName = "[MobileConsoleKit]";
        private const int DefaultCapacity = 200;
        private const int MaxTotalCharacters = 9000; // keep Text vertex count under Unity's 65k limit
        private static readonly int NewLineLength = System.Environment.NewLine.Length;

        private static MobileConsoleKit _instance;

        private readonly List<LogEntry> _entries = new List<LogEntry>(DefaultCapacity);
        private readonly StringBuilder _builder = new StringBuilder(MaxTotalCharacters + 512);
        private int _currentCharacterCount;

        private RectTransform _consolePanel;
        private Button _clearButton;
        private Text _logText;
        private ScrollRect _scrollRect;
        private bool _dirty;
        private bool _isVisible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var root = new GameObject(ConsoleRootName, typeof(MobileConsoleKit));
            DontDestroyOnLoad(root);
            _instance = root.GetComponent<MobileConsoleKit>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            SetupEventSystem();
            SetupUI();
            Application.logMessageReceived += HandleLog;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                Application.logMessageReceived -= HandleLog;
            }
        }

        private void LateUpdate()
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;
            RefreshLogView();
        }

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            var color = type switch
            {
                LogType.Warning => "#FFCC00",
                LogType.Error => "#FF3300",
                LogType.Assert => "#FF3300",
                LogType.Exception => "#FF3300",
                _ => "#FFFFFF"
            };

            var entry = new LogEntry
            {
                Message = $"<color={color}>[{type}] {condition}</color>",
                StackTrace = stackTrace
            };

            entry.CharacterCount = entry.Message.Length + NewLineLength;
            if (!string.IsNullOrEmpty(entry.StackTrace))
            {
                entry.CharacterCount += entry.StackTrace.Length + NewLineLength;
            }

            _entries.Add(entry);
            _currentCharacterCount += entry.CharacterCount;

            TrimEntriesToCharacterBudget();

            _dirty = true;
        }

        private void TrimEntriesToCharacterBudget()
        {
            while ((_entries.Count > DefaultCapacity || _currentCharacterCount > MaxTotalCharacters) && _entries.Count > 0)
            {
                var removed = _entries[0];
                _currentCharacterCount -= removed.CharacterCount;
                _entries.RemoveAt(0);
            }

            if (_currentCharacterCount < 0)
            {
                _currentCharacterCount = 0;
            }
        }

        private void RefreshLogView()
        {
            _builder.Clear();

            for (var i = 0; i < _entries.Count; i++)
            {
                _builder.AppendLine(_entries[i].Message);
                if (!string.IsNullOrEmpty(_entries[i].StackTrace))
                {
                    _builder.AppendLine(_entries[i].StackTrace);
                }
            }

            _logText.text = _builder.ToString();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_logText.rectTransform);

            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = 0f;
        }

        private void SetupEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var eventSystemRoot = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystemRoot);
        }

        private void SetupUI()
        {
            var canvas = CreateCanvas();
            _consolePanel = CreateConsolePanel(canvas.transform);
            _logText = CreateLogView(_consolePanel);
            _scrollRect = _consolePanel.GetComponentInChildren<ScrollRect>(true);
            CreateToggleButton(canvas.transform);
            _clearButton = CreateClearButton(_consolePanel);

            SetConsoleVisibility(false);
        }

        private Canvas CreateCanvas()
        {
            var canvasRoot = new GameObject("MobileConsoleCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasRoot);

            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        private RectTransform CreateConsolePanel(Transform parent)
        {
            var panelRoot = new GameObject("ConsolePanel", typeof(RectTransform), typeof(Image));
            panelRoot.transform.SetParent(parent, false);

            var rect = panelRoot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.05f, 0.05f);
            rect.anchorMax = new Vector2(0.95f, 0.6f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = panelRoot.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.85f);

            var scrollView = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            scrollView.transform.SetParent(panelRoot.transform, false);

            var scrollRectTransform = scrollView.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0.15f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(10f, 10f);
            scrollRectTransform.offsetMax = new Vector2(-10f, -10f);

            var maskImage = scrollView.GetComponent<Image>();
            maskImage.color = new Color(0f, 0f, 0f, 0f);

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollView.transform, false);

            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);

            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 0f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.offsetMin = new Vector2(0f, 0f);
            contentRect.offsetMax = new Vector2(0f, 0f);
            contentRect.pivot = new Vector2(0.5f, 1f);

            var scrollRect = scrollView.GetComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            return rect;
        }

        private Text CreateLogView(RectTransform panel)
        {
            var scrollRect = panel.GetComponentInChildren<ScrollRect>();
            var content = scrollRect.content;

            var textObj = new GameObject("LogText", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(content, false);

            var rect = textObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(10f, 0f);
            rect.offsetMax = new Vector2(-10f, 0f);

            var text = textObj.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 28;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.text = string.Empty;

            return text;
        }

        private Button CreateToggleButton(Transform parent)
        {
            var buttonObj = new GameObject("ToggleConsoleButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObj.transform.SetParent(parent, false);

            var rect = buttonObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.02f, 0.92f);
            rect.anchorMax = new Vector2(0.28f, 0.98f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = buttonObj.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.6f);

            var button = buttonObj.GetComponent<Button>();
            button.onClick.AddListener(ToggleConsoleVisibility);

            var labelObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObj.transform.SetParent(buttonObj.transform, false);

            var labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelObj.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 28;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = "Console";

            return button;
        }

        private Button CreateClearButton(RectTransform panel)
        {
            var buttonObj = new GameObject("ClearButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObj.transform.SetParent(panel, false);

            var rect = buttonObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.72f, 0.02f);
            rect.anchorMax = new Vector2(0.98f, 0.12f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = buttonObj.GetComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            var button = buttonObj.GetComponent<Button>();
            button.onClick.AddListener(ClearLogs);

            var labelObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObj.transform.SetParent(buttonObj.transform, false);

            var labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelObj.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 28;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = "Clear";

            return button;
        }

        private void ToggleConsoleVisibility()
        {
            SetConsoleVisibility(!_isVisible);
        }

        private void SetConsoleVisibility(bool visible)
        {
            _isVisible = visible;
            _consolePanel.gameObject.SetActive(visible);
            _clearButton.gameObject.SetActive(visible);
        }

        private void ClearLogs()
        {
            _entries.Clear();
            _builder.Clear();
            _currentCharacterCount = 0;
            _logText.text = string.Empty;
            _scrollRect.verticalNormalizedPosition = 1f;
        }

        private struct LogEntry
        {
            public string Message;
            public string StackTrace;
            public int CharacterCount;
        }
    }
}
