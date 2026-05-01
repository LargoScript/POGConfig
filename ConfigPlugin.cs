using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppGame.Menu;
using Il2CppInterop.Runtime.Injection;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(POGMods.Config.PogConfigMelon), "POG Config", "1.0.0", "largo", "")]

namespace POGMods.Config
{
    internal static class Clicks
    {
        internal static readonly Dictionary<RectTransform, Action> Map = new();
        internal static void Register(RectTransform rt, Action cb) => Map[rt] = cb;
        internal static void Clear() => Map.Clear();
    }

    internal static class UI
    {
        private static Sprite _white;
        internal static Sprite White
        {
            get
            {
                if (_white != null) return _white;
                var t = new Texture2D(1, 1);
                t.SetPixel(0, 0, Color.white);
                t.Apply();
                _white = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
                return _white;
            }
        }

        internal static GameObject Make(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            return go;
        }

        internal static RectTransform Rt(GameObject go) => go.GetComponent<RectTransform>();

        internal static void Stretch(RectTransform rt, float l = 0, float r = 0, float b = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        internal static Image AddImage(GameObject go, Color color, bool raycast = true)
        {
            var img = go.AddComponent<Image>();
            img.sprite = White; img.color = color; img.raycastTarget = raycast;
            return img;
        }

        internal static TextMeshProUGUI AddText(GameObject go, string text, TMP_FontAsset font,
            int size, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text; tmp.fontSize = size; tmp.color = color;
            tmp.alignment = align; tmp.raycastTarget = false;
            return tmp;
        }

        // Anchors rt to top of parent. yTop = distance from parent top. height = item height.
        internal static void PlaceFromTop(RectTransform rt, float yTop, float height,
            float padL = 0, float padR = 0)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(padL,  -(yTop + height));
            rt.offsetMax = new Vector2(-padR, -yTop);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public abstract class ConfigEntry
    {
        public string Label { get; protected set; }
        protected ConfigEntry(string label) { Label = label; }

        public virtual void Draw(float x, float y, float width, GUIStyle labelStyle) { }
        public virtual float Height => 36f;

        internal abstract void BuildRowInto(GameObject row, TMP_FontAsset font);
        internal virtual void BuildRow(Transform parent, TMP_FontAsset font) { }
        internal virtual void OnUpdate() { }
        internal virtual void BindPrefs(MelonPreferences_Category cat) { }
    }

    public class ToggleEntry : ConfigEntry
    {
        private readonly Func<bool> _get;
        private Action<bool> _set;
        private readonly string _prefKey;
        private Toggle _toggle;
        private bool _lastValue;
        private bool _suppressToggleCallback;

        public ToggleEntry(string label, Func<bool> get, Action<bool> set)
            : this(label, get, set, null) { }
        public ToggleEntry(string label, Func<bool> get, Action<bool> set, string prefKey) : base(label)
        { _get = get; _set = set; _prefKey = prefKey; }

        internal override void BindPrefs(MelonPreferences_Category cat)
        {
            if (_prefKey == null) return;
            var pref = cat.CreateEntry(_prefKey, _get());
            _set(pref.Value);
            var orig = _set;
            _set = v => { orig(v); pref.Value = v; MelonPreferences.Save(); };
        }

        internal override void BuildRowInto(GameObject row, TMP_FontAsset font)
        {
            var lbl = UI.Make("Label", row.transform);
            UI.Rt(lbl).anchorMin = Vector2.zero;
            UI.Rt(lbl).anchorMax = new Vector2(0.78f, 1f);
            UI.Rt(lbl).offsetMin = UI.Rt(lbl).offsetMax = Vector2.zero;
            UI.AddText(lbl, Label, font, 14, new Color(0.88f, 0.88f, 0.88f));

            var tGo = UI.Make("Toggle", row.transform);
            var tRt = UI.Rt(tGo);
            tRt.anchorMin = tRt.anchorMax = new Vector2(1f, 0.5f);
            tRt.pivot = new Vector2(1f, 0.5f);
            tRt.sizeDelta = new Vector2(40, 24);
            tRt.anchoredPosition = Vector2.zero;

            _toggle = tGo.AddComponent<Toggle>();
            var bg = UI.Make("BG", tGo.transform);
            UI.Stretch(UI.Rt(bg));
            var bgImg = UI.AddImage(bg, new Color(0.18f, 0.18f, 0.2f));
            var ck = UI.Make("Check", bg.transform);
            UI.Stretch(UI.Rt(ck), 3, 3, 3, 3);
            var ckImg = UI.AddImage(ck, new Color(1f, 0.82f, 0.3f));

            _toggle.targetGraphic = bgImg;
            _toggle.graphic = ckImg;
            _lastValue = _get();
            _toggle.SetIsOnWithoutNotify(_lastValue);
            _toggle.onValueChanged.AddListener((Action<bool>)OnToggleChanged);
        }

        private void OnToggleChanged(bool value)
        {
            if (_suppressToggleCallback) return;
            if (value == _lastValue) return;
            _lastValue = value;
            _set(value);
        }

        internal override void OnUpdate()
        {
            if (_toggle == null) return;
            bool ext = _get();
            if (_toggle.isOn == ext) return;
            _suppressToggleCallback = true;
            _toggle.SetIsOnWithoutNotify(ext);
            _suppressToggleCallback = false;
            _lastValue = ext;
        }
    }

    public class SliderEntry : ConfigEntry
    {
        private readonly Func<float> _get;
        private Action<float> _set;
        private readonly float _min, _max;
        private readonly Func<float, string> _fmt;
        private readonly string _prefKey;
        private readonly float _originValue;
        private readonly bool _showFill;
        private readonly bool _wholeNumbers;
        private readonly int _stepPointsCount;
        private Slider _slider;
        private TextMeshProUGUI _inputText;
        private TMP_InputField _inputField;
        private RectTransform _fillRt;
        private bool _editingInput;
        private RectTransform _labelMaskRt;
        private RectTransform _valueMaskRt;
        private RectTransform _labelTextRt;
        private RectTransform _valueTextRt;
        private TextMeshProUGUI _labelTmp;
        private float _lastValue;
        private float _marqueeTimer;
        private const float MarqueeSpeed = 52f;

        public SliderEntry(string label, Func<float> get, Action<float> set, float min, float max,
                           Func<float, string> fmt = null) : this(label, get, set, min, max, fmt, null) { }
        public SliderEntry(string label, Func<float> get, Action<float> set, float min, float max,
                           Func<float, string> fmt, string prefKey)
            : this(label, get, set, min, max, fmt, prefKey, 0f, true, false, 0) { }
        public SliderEntry(string label, Func<float> get, Action<float> set, float min, float max,
                           Func<float, string> fmt, string prefKey, float originValue, bool showFill, bool wholeNumbers)
            : this(label, get, set, min, max, fmt, prefKey, originValue, showFill, wholeNumbers, 0) { }
        public SliderEntry(string label, Func<float> get, Action<float> set, float min, float max,
                           Func<float, string> fmt, string prefKey, float originValue, bool showFill, bool wholeNumbers, int stepPointsCount) : base(label)
        {
            _get = get;
            _set = set;
            _min = min;
            _max = max;
            _fmt = fmt ?? (v => v.ToString("F1"));
            _prefKey = prefKey;
            _originValue = Mathf.Clamp(originValue, min, max);
            _showFill = showFill;
            _wholeNumbers = wholeNumbers;
            _stepPointsCount = Math.Max(0, stepPointsCount);
        }

        internal override void BindPrefs(MelonPreferences_Category cat)
        {
            if (_prefKey == null) return;
            var pref = cat.CreateEntry(_prefKey, _get());
            _set(pref.Value);
            var orig = _set;
            _set = v => { orig(v); pref.Value = v; MelonPreferences.Save(); };
        }

        internal override void BuildRowInto(GameObject row, TMP_FontAsset font)
        {
            var lblMask = UI.Make("LabelMask", row.transform);
            _labelMaskRt = UI.Rt(lblMask);
            _labelMaskRt.anchorMin = Vector2.zero;
            _labelMaskRt.anchorMax = new Vector2(0.26f, 1f);
            _labelMaskRt.offsetMin = _labelMaskRt.offsetMax = Vector2.zero;
            lblMask.AddComponent<RectMask2D>();

            var lbl = UI.Make("Label", lblMask.transform);
            _labelTextRt = UI.Rt(lbl);
            _labelTextRt.anchorMin = Vector2.zero;
            _labelTextRt.anchorMax = Vector2.one;
            _labelTextRt.offsetMin = _labelTextRt.offsetMax = Vector2.zero;
            _labelTmp = UI.AddText(lbl, Label, font, 14, new Color(0.88f, 0.88f, 0.88f));
            _labelTmp.enableWordWrapping = false;
            _labelTmp.overflowMode = TextOverflowModes.Ellipsis;

            var valMask = UI.Make("ValueMask", row.transform);
            _valueMaskRt = UI.Rt(valMask);
            _valueMaskRt.anchorMin = new Vector2(0.26f, 0f);
            _valueMaskRt.anchorMax = new Vector2(0.52f, 1f);
            _valueMaskRt.offsetMin = _valueMaskRt.offsetMax = Vector2.zero;
            valMask.AddComponent<RectMask2D>();

            var valBg = UI.Make("ValueBG", valMask.transform);
            var valBgRt = UI.Rt(valBg);
            valBgRt.anchorMin = new Vector2(0f, 0.18f);
            valBgRt.anchorMax = new Vector2(1f, 0.82f);
            valBgRt.offsetMin = new Vector2(2f, 0f);
            valBgRt.offsetMax = new Vector2(-2f, 0f);
            var valBgImg = UI.AddImage(valBg, new Color(0.16f, 0.16f, 0.18f, 0.95f));

            var inputArea = UI.Make("ValueInput", valBg.transform);
            _valueTextRt = UI.Rt(inputArea);
            _valueTextRt.anchorMin = Vector2.zero;
            _valueTextRt.anchorMax = Vector2.one;
            _valueTextRt.offsetMin = new Vector2(6f, 0f);
            _valueTextRt.offsetMax = new Vector2(-6f, 0f);

            _inputText = UI.AddText(inputArea, _fmt(_get()), font, 13, new Color(1f, 0.82f, 0.3f),
                                    TextAlignmentOptions.MidlineRight);
            _inputText.enableWordWrapping = false;
            _inputText.overflowMode = TextOverflowModes.Ellipsis;
            _inputField = inputArea.AddComponent<TMP_InputField>();
            _inputField.textComponent = _inputText;
            _inputField.targetGraphic = valBgImg;
            _inputField.pointSize = 13;
            _inputField.contentType = TMP_InputField.ContentType.Custom;
            _inputField.text = _fmt(_get());
            _inputField.onSelect.AddListener((Action<string>)OnInputSelect);
            _inputField.onDeselect.AddListener((Action<string>)OnInputDeselect);
            _inputField.onSubmit.AddListener((Action<string>)OnInputCommit);
            _inputField.onEndEdit.AddListener((Action<string>)OnInputCommit);

            var sGo = UI.Make("Slider", row.transform);
            UI.Rt(sGo).anchorMin = new Vector2(0.54f, 0.25f);
            UI.Rt(sGo).anchorMax = new Vector2(1f, 0.75f);
            UI.Rt(sGo).offsetMin = UI.Rt(sGo).offsetMax = Vector2.zero;

            _slider = sGo.AddComponent<Slider>();
            _slider.minValue = _min; _slider.maxValue = _max;
            _slider.wholeNumbers = _wholeNumbers;
            _slider.direction = Slider.Direction.LeftToRight;

            var track = UI.Make("Track", sGo.transform);
            UI.Stretch(UI.Rt(track));
            UI.AddImage(track, new Color(0.2f, 0.2f, 0.22f));
            BuildStepPoints(sGo.transform);

            var fillArea = UI.Make("FillArea", sGo.transform);
            UI.Rt(fillArea).anchorMin = new Vector2(0f, 0.25f);
            UI.Rt(fillArea).anchorMax = new Vector2(1f, 0.75f);
            UI.Rt(fillArea).offsetMin = UI.Rt(fillArea).offsetMax = Vector2.zero;
            var fill = UI.Make("Fill", fillArea.transform);
            UI.Rt(fill).anchorMin = Vector2.zero;
            UI.Rt(fill).anchorMax = new Vector2(0f, 1f);
            UI.Rt(fill).offsetMin = UI.Rt(fill).offsetMax = Vector2.zero;
            UI.AddImage(fill, new Color(1f, 0.72f, 0.1f));
            _fillRt = UI.Rt(fill);
            fillArea.SetActive(_showFill);

            var handleArea = UI.Make("HandleArea", sGo.transform);
            UI.Stretch(UI.Rt(handleArea));
            var handle = UI.Make("Handle", handleArea.transform);
            UI.Rt(handle).anchorMin = UI.Rt(handle).anchorMax = new Vector2(0f, 0.5f);
            UI.Rt(handle).sizeDelta = new Vector2(14, 14);
            var hImg = UI.AddImage(handle, new Color(1f, 0.82f, 0.3f));

            _slider.fillRect = null;
            _slider.handleRect = UI.Rt(handle);
            _slider.targetGraphic = hImg;
            _lastValue = _get();
            _slider.SetValueWithoutNotify(_lastValue);
            _slider.onValueChanged.AddListener((Action<float>)OnSliderChanged);
            UpdateFillFromOrigin(_lastValue);
        }

        private void BuildStepPoints(Transform sliderTransform)
        {
            if (_stepPointsCount < 2) return;
            var pointsGo = UI.Make("StepPoints", sliderTransform);
            var pointsRt = UI.Rt(pointsGo);
            pointsRt.anchorMin = new Vector2(0f, 0.5f);
            pointsRt.anchorMax = new Vector2(1f, 0.5f);
            pointsRt.offsetMin = new Vector2(0f, -1f);
            pointsRt.offsetMax = new Vector2(0f, 1f);

            for (int i = 0; i < _stepPointsCount; i++)
            {
                float t = _stepPointsCount <= 1 ? 0f : i / (float)(_stepPointsCount - 1);
                var dot = UI.Make("P" + i, pointsGo.transform);
                var dotRt = UI.Rt(dot);
                dotRt.anchorMin = dotRt.anchorMax = new Vector2(t, 0.5f);
                dotRt.pivot = new Vector2(0.5f, 0.5f);
                dotRt.sizeDelta = new Vector2(4f, 4f);
                dotRt.anchoredPosition = Vector2.zero;
                UI.AddImage(dot, new Color(0.48f, 0.48f, 0.52f, 0.75f), false);
            }
        }

        private void OnInputSelect(string _)
        {
            _editingInput = true;
            _marqueeTimer = 0f;
            if (_inputField != null)
            {
                _inputField.SetTextWithoutNotify(ToEditableNumericString(_lastValue));
                _inputField.MoveTextEnd(false);
            }
        }

        private void OnInputDeselect(string value)
        {
            _editingInput = false;
            OnInputCommit(value);
        }

        private void OnInputCommit(string value)
        {
            if (TryParseValue(value, out var parsed))
            {
                ApplyValue(parsed, true);
            }
            else if (_inputField != null)
            {
                _inputField.SetTextWithoutNotify(_fmt(_lastValue));
            }
        }

        private bool TryParseValue(string text, out float parsed)
        {
            text = (text ?? string.Empty).Trim();
            parsed = 0f;
            if (text.Length == 0) return false;
            var m = Regex.Match(text, @"[-+]?\d+(?:[.,]\d+)?");
            if (m.Success) text = m.Value;
            string normalized = text.Replace(',', '.');
            bool ok = float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
            if (!ok) ok = float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed);
            if (!ok) return false;
            parsed = Mathf.Clamp(parsed, _min, _max);
            if (_wholeNumbers) parsed = Mathf.Round(parsed);
            return true;
        }

        private string ToEditableNumericString(float value)
        {
            if (_wholeNumbers) return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void OnSliderChanged(float value) => ApplyValue(value, true);

        private void ApplyValue(float value, bool writeBack)
        {
            value = Mathf.Clamp(value, _min, _max);
            if (_wholeNumbers) value = Mathf.Round(value);
            _lastValue = value;
            if (_slider != null && Math.Abs(_slider.value - value) > 0.0001f) _slider.SetValueWithoutNotify(value);
            if (_inputField != null && !_editingInput) _inputField.SetTextWithoutNotify(_fmt(value));
            UpdateFillFromOrigin(value);
            if (writeBack) _set(value);
        }

        private void UpdateFillFromOrigin(float current)
        {
            if (_fillRt == null || !_showFill) return;
            float originNorm = Mathf.InverseLerp(_min, _max, _originValue);
            float currentNorm = Mathf.InverseLerp(_min, _max, current);
            _fillRt.anchorMin = new Vector2(Mathf.Min(originNorm, currentNorm), 0f);
            _fillRt.anchorMax = new Vector2(Mathf.Max(originNorm, currentNorm), 1f);
            _fillRt.offsetMin = Vector2.zero;
            _fillRt.offsetMax = Vector2.zero;
        }

        private void UpdateMarquee(RectTransform maskRt, RectTransform textRt, TextMeshProUGUI tmp, bool hovered)
        {
            if (maskRt == null || textRt == null || tmp == null) return;
            bool overflow = tmp.preferredWidth > maskRt.rect.width + 1f;
            if (!overflow)
            {
                tmp.overflowMode = TextOverflowModes.Ellipsis;
                textRt.anchoredPosition = Vector2.zero;
                return;
            }

            if (!hovered)
            {
                tmp.overflowMode = TextOverflowModes.Ellipsis;
                textRt.anchoredPosition = Vector2.zero;
                _marqueeTimer = 0f;
                return;
            }

            tmp.overflowMode = TextOverflowModes.Overflow;
            _marqueeTimer += Time.unscaledDeltaTime * MarqueeSpeed;
            float maxShift = Mathf.Max(0f, tmp.preferredWidth - maskRt.rect.width + 12f);
            float shift = Mathf.PingPong(_marqueeTimer, maxShift);
            textRt.anchoredPosition = new Vector2(-shift, 0f);
        }

        internal override void OnUpdate()
        {
            if (_slider == null) return;
            float ext = _get();
            if (Math.Abs(ext - _lastValue) > 0.0001f && !_editingInput) ApplyValue(ext, false);

            Vector2 mouse = Input.mousePosition;
            UpdateMarquee(_labelMaskRt, _labelTextRt, _labelTmp, ConfigBehaviour.HitTest(_labelMaskRt, mouse));
            UpdateMarquee(_valueMaskRt, _valueTextRt, _inputText, ConfigBehaviour.HitTest(_valueMaskRt, mouse));
        }
    }

    public class OptionsSliderEntry : ConfigEntry
    {
        private readonly Func<int> _get;
        private Action<int> _set;
        private readonly string[] _options;
        private readonly string _prefKey;
        private Slider _slider;
        private TextMeshProUGUI _displayText;
        private int _lastIndex;

        public OptionsSliderEntry(string label, Func<int> get, Action<int> set, string[] options, string prefKey = null)
            : base(label)
        {
            _get = get;
            _set = set;
            _options = options ?? Array.Empty<string>();
            _prefKey = prefKey;
        }

        internal override void BindPrefs(MelonPreferences_Category cat)
        {
            if (_prefKey == null || _options.Length == 0) return;
            var pref = cat.CreateEntry(_prefKey, Mathf.Clamp(_get(), 0, _options.Length - 1));
            _set(Mathf.Clamp(pref.Value, 0, _options.Length - 1));
            var orig = _set;
            _set = i => { orig(i); pref.Value = i; MelonPreferences.Save(); };
        }

        internal override void BuildRowInto(GameObject row, TMP_FontAsset font)
        {
            var lbl = UI.Make("Label", row.transform);
            UI.Rt(lbl).anchorMin = Vector2.zero;
            UI.Rt(lbl).anchorMax = new Vector2(0.26f, 1f);
            UI.Rt(lbl).offsetMin = UI.Rt(lbl).offsetMax = Vector2.zero;
            var lblTmp = UI.AddText(lbl, Label, font, 14, new Color(0.88f, 0.88f, 0.88f));
            lblTmp.enableWordWrapping = false;
            lblTmp.overflowMode = TextOverflowModes.Ellipsis;

            var valueTextGo = UI.Make("ValueText", row.transform);
            var valueTextRt = UI.Rt(valueTextGo);
            valueTextRt.anchorMin = new Vector2(0.26f, 0f);
            valueTextRt.anchorMax = new Vector2(0.52f, 1f);
            valueTextRt.offsetMin = valueTextRt.offsetMax = Vector2.zero;
            _displayText = UI.AddText(valueTextGo, "", font, 13, new Color(1f, 0.82f, 0.3f), TextAlignmentOptions.MidlineRight);
            _displayText.enableWordWrapping = false;
            _displayText.overflowMode = TextOverflowModes.Ellipsis;

            var sGo = UI.Make("Slider", row.transform);
            UI.Rt(sGo).anchorMin = new Vector2(0.54f, 0.25f);
            UI.Rt(sGo).anchorMax = new Vector2(1f, 0.75f);
            UI.Rt(sGo).offsetMin = UI.Rt(sGo).offsetMax = Vector2.zero;

            _slider = sGo.AddComponent<Slider>();
            _slider.minValue = 0;
            _slider.maxValue = Mathf.Max(0, _options.Length - 1);
            _slider.wholeNumbers = true;
            _slider.direction = Slider.Direction.LeftToRight;

            var track = UI.Make("Track", sGo.transform);
            UI.Stretch(UI.Rt(track));
            UI.AddImage(track, new Color(0.2f, 0.2f, 0.22f));

            var handleArea = UI.Make("HandleArea", sGo.transform);
            UI.Stretch(UI.Rt(handleArea));
            var handle = UI.Make("Handle", handleArea.transform);
            UI.Rt(handle).anchorMin = UI.Rt(handle).anchorMax = new Vector2(0f, 0.5f);
            UI.Rt(handle).sizeDelta = new Vector2(14, 14);
            var hImg = UI.AddImage(handle, new Color(1f, 0.82f, 0.3f));
            _slider.fillRect = null;
            _slider.handleRect = UI.Rt(handle);
            _slider.targetGraphic = hImg;
            _slider.onValueChanged.AddListener((Action<float>)OnSliderChanged);

            _lastIndex = Mathf.Clamp(_get(), 0, _options.Length - 1);
            _slider.SetValueWithoutNotify(_lastIndex);
            SyncText(_lastIndex);
        }

        private void OnSliderChanged(float value)
        {
            int idx = Mathf.Clamp(Mathf.RoundToInt(value), 0, _options.Length - 1);
            if (idx == _lastIndex) return;
            _lastIndex = idx;
            SyncText(idx);
            _set(idx);
        }

        private void SyncText(int idx)
        {
            string txt = (_options.Length == 0 || idx < 0 || idx >= _options.Length) ? "-" : _options[idx];
            if (_displayText != null) _displayText.text = txt;
        }

        internal override void OnUpdate()
        {
            if (_slider == null || _options.Length == 0) return;
            int ext = Mathf.Clamp(_get(), 0, _options.Length - 1);
            if (ext != _lastIndex)
            {
                _lastIndex = ext;
                _slider.SetValueWithoutNotify(ext);
                SyncText(ext);
            }
        }
    }

    public class KeyEntry : ConfigEntry
    {
        private readonly Func<KeyCode> _get;
        private Action<KeyCode> _set;
        private readonly string _prefKey;

        // Set to true to allow binding mouse buttons (Mouse0–Mouse6).
        public bool AllowMouseButtons { get; set; } = false;

        internal static bool AnyWaiting;
        private bool _listening;
        private TextMeshProUGUI _keyText;
        private TextMeshProUGUI _btnText;

        public KeyEntry(string label, Func<KeyCode> get, Action<KeyCode> set)
            : this(label, get, set, null) { }
        public KeyEntry(string label, Func<KeyCode> get, Action<KeyCode> set, string prefKey) : base(label)
        { _get = get; _set = set; _prefKey = prefKey; }

        internal override void BindPrefs(MelonPreferences_Category cat)
        {
            if (_prefKey == null) return;
            var pref = cat.CreateEntry(_prefKey, _get().ToString());
            if (Enum.TryParse<KeyCode>(pref.Value, out var loaded)) _set(loaded);
            var orig = _set;
            _set = v => { orig(v); pref.Value = v.ToString(); MelonPreferences.Save(); };
        }

        internal override void BuildRowInto(GameObject row, TMP_FontAsset font)
        {
            var lbl = UI.Make("Label", row.transform);
            UI.Rt(lbl).anchorMin = Vector2.zero;
            UI.Rt(lbl).anchorMax = new Vector2(0.42f, 1f);
            UI.Rt(lbl).offsetMin = UI.Rt(lbl).offsetMax = Vector2.zero;
            UI.AddText(lbl, Label, font, 14, new Color(0.88f, 0.88f, 0.88f));

            var keyGo = UI.Make("KeyName", row.transform);
            UI.Rt(keyGo).anchorMin = new Vector2(0.42f, 0f);
            UI.Rt(keyGo).anchorMax = new Vector2(0.70f, 1f);
            UI.Rt(keyGo).offsetMin = UI.Rt(keyGo).offsetMax = Vector2.zero;
            _keyText = UI.AddText(keyGo, _get().ToString(), font, 13, new Color(1f, 0.82f, 0.3f),
                                  TextAlignmentOptions.MidlineRight);

            var btnGo = UI.Make("ChangeBtn", row.transform);
            var btnRt = UI.Rt(btnGo);
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.sizeDelta = new Vector2(90, 28);
            btnRt.anchoredPosition = Vector2.zero;
            UI.AddImage(btnGo, new Color(0.25f, 0.25f, 0.3f));

            var btnTxtGo = UI.Make("Text", btnGo.transform);
            UI.Stretch(UI.Rt(btnTxtGo));
            _btnText = UI.AddText(btnTxtGo, "Change", font, 12,
                                  new Color(0.85f, 0.85f, 0.85f), TextAlignmentOptions.Center);
            Clicks.Register(btnRt, ToggleListen);
        }

        private void ToggleListen()
        {
            if (_listening)
            {
                _listening = false; AnyWaiting = false;
                if (_keyText != null) _keyText.text = _get().ToString();
                if (_btnText != null) _btnText.text = "Change";
            }
            else if (!AnyWaiting)
            {
                _listening = true; AnyWaiting = true;
                if (_keyText != null) _keyText.text = "[ press key ]";
                if (_btnText != null) _btnText.text = "Cancel";
            }
        }

        internal override void OnUpdate()
        {
            if (!_listening) return;
            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.None || kc == KeyCode.Escape) continue;
                if (!AllowMouseButtons && kc >= KeyCode.Mouse0 && kc <= KeyCode.Mouse6) continue;
                if (!Input.GetKeyDown(kc)) continue;
                _set(kc);
                _listening = false; AnyWaiting = false;
                if (_keyText != null) _keyText.text = kc.ToString();
                if (_btnText != null) _btnText.text = "Change";
                break;
            }
        }
    }

    // ── Registry ──────────────────────────────────────────────────────────────

    public static class POGConfig
    {
        private static readonly List<(string Name, List<ConfigEntry> Entries)> _mods = new();
        private static int _registryVersion;

        // True while MOD SETTINGS panel is open. Check this before processing hotkeys.
        public static bool PanelOpen { get; internal set; }
        internal static int RegistryVersion => _registryVersion;

        public static void Register(string modName, List<ConfigEntry> entries)
        {
            try
            {
                var cat = MelonPreferences.CreateCategory(modName.Replace(" ", "_"));
                foreach (var e in entries)
                {
                    try { e.BindPrefs(cat); }
                    catch (Exception ex)
                    { MelonLogger.Warning($"[POGConfig] BindPrefs '{modName}': {ex.Message}"); }
                }
            }
            catch (Exception ex)
            { MelonLogger.Warning($"[POGConfig] Prefs skipped for '{modName}': {ex.Message}"); }

            _mods.Add((modName, entries));
            _registryVersion++;
            MelonLogger.Msg($"[POGConfig] Registered: {modName} ({entries.Count} entries)");
        }

        internal static IReadOnlyList<(string Name, List<ConfigEntry> Entries)> GetAll() => _mods;
    }

    // ── Harmony patch ─────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(NetworkMenu), "TogglePauseMenu")]
    class PatchTogglePauseMenu
    {
        static bool Prefix()
        {
            if (!ConfigBehaviour.PanelOpen) return true;
            ConfigBehaviour.RequestClose();
            return false;
        }
    }

    // ── Mod entry ─────────────────────────────────────────────────────────────

    public class PogConfigMelon : MelonMod
    {
        public override void OnInitializeMelon()
        {
            try { HarmonyInstance.PatchAll(); }
            catch (Exception ex) { MelonLogger.Warning($"[POGConfig] Harmony patch failed: {ex.Message}"); }

            ClassInjector.RegisterTypeInIl2Cpp<ConfigBehaviour>();
            var runner = new GameObject("POGConfigRunner");
            UnityEngine.Object.DontDestroyOnLoad(runner);
            runner.hideFlags = HideFlags.HideAndDontSave;
            runner.AddComponent<ConfigBehaviour>();
            MelonLogger.Msg("POG Config 1.0.0 loaded.");
        }
    }

    // ── ConfigBehaviour ───────────────────────────────────────────────────────

    public class ConfigBehaviour : MonoBehaviour
    {
        public ConfigBehaviour(IntPtr ptr) : base(ptr) { }

        internal static bool PanelOpen;
        internal static void RequestClose() => _instance?.DoClose();
        private static ConfigBehaviour _instance;

        private const float PANEL_H    = 560f;
        private const float TOP_H      = 68f;
        private const float BOT_H      = 60f;
        private const float SB_W       = 20f;
        private const float VIEWPORT_H = PANEL_H - TOP_H - BOT_H; // 432
        private const float SCROLL_SPD = 40f;

        private bool _isPause, _isMainMenu, _panelOpen;
        private bool _updateErrLogged;

        private RectTransform _modsBtnRt;
        private RectTransform _doneBtnRt;
        private GameObject    _overlay;
        private Image         _modsBtnImg;

        private Transform     _scrollContent;
        private RectTransform _scrollContentRt;
        private RectTransform _scrollTrackRt;
        private RectTransform _scrollThumbRt;
        private GameObject    _scrollbarGo;

        private float _totalContentH;
        private float _scrollOffset;
        private bool  _uiReady, _contentBuilt;
        private int _builtRegistryVersion = -1;
        private TMP_FontAsset _font;

        void Awake() { _instance = this; }
        void Start()  { BuildStaticUI(); }

        // ── HitTest ───────────────────────────────────────────────────────────
        // RectangleContainsScreenPoint handles ScreenSpaceOverlay coord space.
        // GetWorldCorners returns canvas-centered coords (0,0=screen center),
        // while Input.mousePosition is screen-pixel (0,0=bottom-left) — they
        // differ by (Screen.width/2, Screen.height/2), so raw corners don't work.

        internal static bool HitTest(RectTransform rt, Vector2 screenPoint)
        {
            if (rt == null) return false;
            try
            {
                return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null);
            }
            catch
            {
                // Fallback: GetWorldCorners + screen-center offset
                try
                {
                    var c = new Vector3[4];
                    rt.GetWorldCorners(c);
                    float cx = screenPoint.x - Screen.width  * 0.5f;
                    float cy = screenPoint.y - Screen.height * 0.5f;
                    return cx >= c[0].x && cx <= c[2].x && cy >= c[0].y && cy <= c[2].y;
                }
                catch { return false; }
            }
        }

        // ── Update ────────────────────────────────────────────────────────────

        void Update()
        {
            if (!_uiReady) BuildStaticUI();
            try
            {
                var nm = NetworkMenu.Instance;
                string state = (nm != null && nm.CurrentState != null)
                    ? nm.CurrentState.GetIl2CppType().Name : "";
                _isPause    = state == "NetworkMenuPauseState";
                _isMainMenu = state == "NetworkMenuMainState";

                bool show = _isPause || _isMainMenu;
                if (_modsBtnRt != null)
                {
                    _modsBtnRt.gameObject.SetActive(show);
                    if (show) { PositionModsButton(); UpdateModsButtonHover(); }
                    else if (_modsBtnImg != null) _modsBtnImg.color = new Color(0, 0, 0, 0);
                }

                if (!show && _panelOpen) DoClose();

                HandleClicks();

                if (_panelOpen)
                {
                    HandleScroll();
                    foreach (var (_, entries) in POGConfig.GetAll())
                        foreach (var e in entries)
                            e.OnUpdate();
                }
            }
            catch (Exception ex)
            {
                if (!_updateErrLogged)
                {
                    _updateErrLogged = true;
                    MelonLogger.Warning($"[POGConfig] Update error: {ex}");
                }
            }
        }

        // ── Scroll ────────────────────────────────────────────────────────────

        private void HandleScroll()
        {
            float maxScroll = Mathf.Max(0, _totalContentH - VIEWPORT_H);
            if (maxScroll <= 0) return;

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.001f)
            {
                _scrollOffset = Mathf.Clamp(_scrollOffset - wheel * SCROLL_SPD, 0, maxScroll);
                ApplyScroll(maxScroll);
            }
            else
            {
                UpdateScrollThumb(maxScroll);
            }
        }

        private void ApplyScroll(float maxScroll)
        {
            _scrollOffset = Mathf.Clamp(_scrollOffset, 0, maxScroll);
            if (_scrollContentRt != null)
                _scrollContentRt.anchoredPosition = new Vector2(0, _scrollOffset);
            UpdateScrollThumb(maxScroll);
        }

        private void UpdateScrollThumb(float maxScroll)
        {
            if (_scrollThumbRt == null || _totalContentH <= 0) return;
            float ratio   = maxScroll > 0 ? _scrollOffset / maxScroll : 0f;
            float thumbH  = Mathf.Max(30f, VIEWPORT_H * VIEWPORT_H / _totalContentH);
            float thumbY  = -ratio * (VIEWPORT_H - thumbH);
            _scrollThumbRt.sizeDelta        = new Vector2(0, thumbH);
            _scrollThumbRt.anchoredPosition = new Vector2(0, thumbY);
        }

        // ── Clicks ────────────────────────────────────────────────────────────

        private void HandleClicks()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            Vector2 mp = Input.mousePosition;

            // MODS button
            if (_modsBtnRt != null && _modsBtnRt.gameObject.activeSelf && HitTest(_modsBtnRt, mp))
            {
                if (_panelOpen) DoClose(); else OpenPanel();
                return;
            }

            if (!_panelOpen) return;

            // Scrollbar track click → jump to position
            if (_scrollTrackRt != null && HitTest(_scrollTrackRt, mp))
            {
                var c = new Vector3[4];
                _scrollTrackRt.GetWorldCorners(c);
                float trackH = c[1].y - c[0].y;
                float clickY = mp.y - c[0].y;
                float ratio  = 1f - Mathf.Clamp01(clickY / trackH);
                float maxScroll = Mathf.Max(0, _totalContentH - VIEWPORT_H);
                _scrollOffset = ratio * maxScroll;
                ApplyScroll(maxScroll);
                return;
            }

            // Registered entry buttons (KeyEntry "Change", etc.)
            foreach (var (rt, cb) in Clicks.Map)
            {
                if (rt == null) continue;
                if (HitTest(rt, mp)) { cb?.Invoke(); return; }
            }
        }

        // ── Hover ─────────────────────────────────────────────────────────────

        private void UpdateModsButtonHover()
        {
            if (_modsBtnImg == null || _modsBtnRt == null) return;
            bool over = HitTest(_modsBtnRt, Input.mousePosition);
            _modsBtnImg.color = over ? new Color(1f, 1f, 1f, 0.14f) : new Color(0f, 0f, 0f, 0f);
        }

        // ── Position ──────────────────────────────────────────────────────────

        private void PositionModsButton()
        {
            if (_isPause)
            {
                _modsBtnRt.anchorMin = _modsBtnRt.anchorMax = new Vector2(0.5f, 0.685f);
                _modsBtnRt.sizeDelta = new Vector2(260, 34);
            }
            else
            {
                _modsBtnRt.anchorMin = _modsBtnRt.anchorMax = new Vector2(0.70f, 0.25f);
                _modsBtnRt.sizeDelta = new Vector2(200, 34);
            }
            _modsBtnRt.anchoredPosition = Vector2.zero;
        }

        // ── Open / Close ──────────────────────────────────────────────────────

        private void OpenPanel()
        {
            if (_builtRegistryVersion != POGConfig.RegistryVersion) _contentBuilt = false;
            EnsureContent();
            _overlay.SetActive(true);
            _panelOpen = true;
            PanelOpen  = true;
            POGConfig.PanelOpen = true;
        }

        internal void DoClose()
        {
            if (_overlay != null) _overlay.SetActive(false);
            _panelOpen = false;
            PanelOpen  = false;
            POGConfig.PanelOpen = false;
            KeyEntry.AnyWaiting = false;
        }

        // ── Build static UI ───────────────────────────────────────────────────

        private void BuildStaticUI()
        {
            if (_uiReady) return;
            _uiReady = true;
            try
            {
                try
                {
                    var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                    if (fonts != null && fonts.Count > 0) _font = fonts[0];
                }
                catch { }
                try { if (_font == null) _font = TMP_Settings.defaultFontAsset; }
                catch { }

                // Standalone canvas root (NOT under HideAndDontSave runner)
                var canvasGo = new GameObject("POGCanvas");
                UnityEngine.Object.DontDestroyOnLoad(canvasGo);
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 9999;
                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight  = 0.5f;
                canvasGo.AddComponent<GraphicRaycaster>();

                if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
                {
                    var es = new GameObject("POG_EventSystem");
                    UnityEngine.Object.DontDestroyOnLoad(es);
                    es.AddComponent<EventSystem>();
                    es.AddComponent<StandaloneInputModule>();
                }

                var modsBtn = BuildModsButton(canvasGo.transform);
                _modsBtnRt = UI.Rt(modsBtn);
                modsBtn.SetActive(false);

                _overlay = BuildOverlay(canvasGo.transform);
                _overlay.SetActive(false);

                MelonLogger.Msg("[POGConfig] Canvas created.");
            }
            catch (Exception ex)
            {
                _uiReady = false; // allow retry next frame
                MelonLogger.Warning($"[POGConfig] BuildStaticUI failed: {ex.Message}");
            }
        }

        private GameObject BuildModsButton(Transform parent)
        {
            var go = UI.Make("ModsButton", parent);
            _modsBtnImg = UI.AddImage(go, new Color(0, 0, 0, 0));
            var textGo = UI.Make("Text", go.transform);
            UI.Stretch(UI.Rt(textGo));
            var txt = UI.AddText(textGo, "MODS", _font, 20, Color.white, TextAlignmentOptions.Center);
            txt.fontStyle = FontStyles.Bold;
            return go;
        }

        private GameObject BuildOverlay(Transform parent)
        {
            var overlayGo = UI.Make("Overlay", parent);
            UI.Stretch(UI.Rt(overlayGo));
            UI.AddImage(overlayGo, new Color(0, 0, 0, 0.82f));

            var panelGo = UI.Make("Panel", overlayGo.transform);
            var pRt = UI.Rt(panelGo);
            pRt.anchorMin = pRt.anchorMax = new Vector2(0.5f, 0.5f);
            pRt.sizeDelta = new Vector2(740, PANEL_H);
            var panelColor = new Color(0.055f, 0.055f, 0.07f, 0.99f);
            UI.AddImage(panelGo, panelColor);

            // ── 1. Scrollable viewport (lowest z-order among children) ──
            BuildViewport(panelGo.transform);

            // ── 2. Scrollbar (right strip) ──
            BuildScrollbar(panelGo.transform);

            // ── 3. Covers — same panel color, mask overflow above/below viewport ──
            var topCover = UI.Make("TopCover", panelGo.transform);
            UI.PlaceFromTop(UI.Rt(topCover), 0f, TOP_H);
            UI.AddImage(topCover, panelColor, false);

            var botCover = UI.Make("BotCover", panelGo.transform);
            var bcRt = UI.Rt(botCover);
            bcRt.anchorMin = Vector2.zero; bcRt.anchorMax = new Vector2(1, 0);
            bcRt.pivot = new Vector2(0.5f, 0f);
            bcRt.sizeDelta = new Vector2(0, BOT_H);
            bcRt.anchoredPosition = Vector2.zero;
            UI.AddImage(botCover, panelColor, false);

            // ── 4. Title & separator (on top of covers) ──
            var titleGo = UI.Make("Title", panelGo.transform);
            UI.PlaceFromTop(UI.Rt(titleGo), 14f, 40f, 24f, 24f);
            var titleTxt = UI.AddText(titleGo, "MOD SETTINGS", _font, 22, Color.white,
                                      TextAlignmentOptions.MidlineLeft);
            titleTxt.fontStyle = FontStyles.Bold;

            var sepGo = UI.Make("Sep", panelGo.transform);
            UI.PlaceFromTop(UI.Rt(sepGo), 62f, 1f, 24f, 24f);
            UI.AddImage(sepGo, new Color(1, 1, 1, 0.12f), false);

            // ── 5. DONE button (on top) ──
            var doneGo = UI.Make("DoneBtn", panelGo.transform);
            _doneBtnRt = UI.Rt(doneGo);
            _doneBtnRt.anchorMin = new Vector2(1, 0); _doneBtnRt.anchorMax = new Vector2(1, 0);
            _doneBtnRt.pivot     = new Vector2(1, 0);
            _doneBtnRt.offsetMin = new Vector2(-170, 14); _doneBtnRt.offsetMax = new Vector2(-20, 52);
            UI.AddImage(doneGo, new Color(0.85f, 0.65f, 0.1f));
            var doneTxtGo = UI.Make("Text", doneGo.transform);
            UI.Stretch(UI.Rt(doneTxtGo));
            var doneTxt = UI.AddText(doneTxtGo, "DONE", _font, 15,
                                     new Color(0.08f, 0.04f, 0f), TextAlignmentOptions.Center);
            doneTxt.fontStyle = FontStyles.Bold;

            return overlayGo;
        }

        private void BuildViewport(Transform panelTr)
        {
            var vpGo = UI.Make("Viewport", panelTr);
            var vpRt = UI.Rt(vpGo);
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(16, BOT_H);
            vpRt.offsetMax = new Vector2(-(16 + SB_W + 6), -TOP_H);
            vpGo.AddComponent<RectMask2D>();

            var contentGo = UI.Make("ScrollContent", vpGo.transform);
            _scrollContentRt = UI.Rt(contentGo);
            _scrollContentRt.anchorMin = new Vector2(0, 1);
            _scrollContentRt.anchorMax = new Vector2(1, 1);
            _scrollContentRt.pivot = new Vector2(0.5f, 1f);
            _scrollContentRt.sizeDelta = new Vector2(0, 0);
            _scrollContentRt.anchoredPosition = Vector2.zero;
            _scrollContent = contentGo.transform;
        }

        private void BuildScrollbar(Transform panelTr)
        {
            // Track
            _scrollbarGo = UI.Make("ScrollTrack", panelTr);
            _scrollTrackRt = UI.Rt(_scrollbarGo);
            _scrollTrackRt.anchorMin = new Vector2(1, 0);
            _scrollTrackRt.anchorMax = new Vector2(1, 1);
            _scrollTrackRt.pivot     = new Vector2(1, 0.5f);
            _scrollTrackRt.offsetMin = new Vector2(-(SB_W + 8), BOT_H);
            _scrollTrackRt.offsetMax = new Vector2(-8, -TOP_H);
            UI.AddImage(_scrollbarGo, new Color(0.12f, 0.12f, 0.14f));

            // Thumb
            var thumbGo = UI.Make("Thumb", _scrollbarGo.transform);
            _scrollThumbRt = UI.Rt(thumbGo);
            _scrollThumbRt.anchorMin = new Vector2(0, 1);
            _scrollThumbRt.anchorMax = new Vector2(1, 1);
            _scrollThumbRt.pivot     = new Vector2(0.5f, 1f);
            _scrollThumbRt.offsetMin = new Vector2(2, 0);
            _scrollThumbRt.offsetMax = new Vector2(-2, 0);
            _scrollThumbRt.sizeDelta = new Vector2(-4, 40);
            _scrollThumbRt.anchoredPosition = Vector2.zero;
            UI.AddImage(thumbGo, new Color(0.45f, 0.45f, 0.5f));

            _scrollbarGo.SetActive(false);
        }

        // ── EnsureContent ─────────────────────────────────────────────────────

        private void EnsureContent()
        {
            if (_contentBuilt || _scrollContent == null) return;
            _contentBuilt = true;
            Clicks.Clear();
            _builtRegistryVersion = POGConfig.RegistryVersion;

            for (int i = _scrollContent.childCount - 1; i >= 0; i--)
            {
                var child = _scrollContent.GetChild(i);
                if (child != null) UnityEngine.Object.Destroy(child.gameObject);
            }

            // DONE button goes into Clicks.Map so HitTest path handles it too
            if (_doneBtnRt != null) Clicks.Register(_doneBtnRt, DoClose);

            try
            {
                var mods = POGConfig.GetAll();
                MelonLogger.Msg($"[POGConfig] Building content: {mods.Count} mod(s).");

                const float HDR_H = 32f;
                const float ROW_H = 40f;
                const float SEP_H = 6f;
                float y = 4f;

                if (mods.Count == 0)
                {
                    var empty = UI.Make("Empty", _scrollContent);
                    UI.PlaceFromTop(UI.Rt(empty), y, 36f);
                    UI.AddText(empty, "No mods registered yet.", _font, 14, new Color(0.6f, 0.6f, 0.6f));
                    y += 40f;
                }
                else
                {
                    for (int i = 0; i < mods.Count; i++)
                    {
                        var (name, entries) = mods[i];

                        var header = UI.Make("H_" + name, _scrollContent);
                        UI.PlaceFromTop(UI.Rt(header), y, HDR_H);
                        var hTxt = UI.AddText(header, name, _font, 15, new Color(1f, 0.82f, 0.3f));
                        hTxt.fontStyle = FontStyles.Bold;
                        y += HDR_H + 2f;

                        for (int j = 0; j < entries.Count; j++)
                        {
                            var entry = entries[j];
                            try
                            {
                                var row = UI.Make("R_" + entry.Label, _scrollContent);
                                UI.PlaceFromTop(UI.Rt(row), y, ROW_H);
                                entry.BuildRowInto(row, _font);
                            }
                            catch (Exception ex)
                            { MelonLogger.Warning($"[POGConfig] Row '{entry.Label}': {ex.Message}"); }
                            y += ROW_H + 2f;
                        }

                        if (i < mods.Count - 1)
                        {
                            var sep = UI.Make("Sep_" + i, _scrollContent);
                            UI.PlaceFromTop(UI.Rt(sep), y, SEP_H);
                            UI.AddImage(sep, new Color(1, 1, 1, 0.08f), false);
                            y += SEP_H + 4f;
                        }
                    }
                }

                _totalContentH = y + 4f;
                _scrollOffset  = 0f;

                // Set content container height
                if (_scrollContentRt != null)
                    _scrollContentRt.sizeDelta = new Vector2(0, _totalContentH);

                // Show scrollbar only if content overflows
                bool needsScroll = _totalContentH > VIEWPORT_H;
                if (_scrollbarGo != null) _scrollbarGo.SetActive(needsScroll);

                MelonLogger.Msg($"[POGConfig] Done. ContentH={_totalContentH:F0} ViewportH={VIEWPORT_H} Scroll={needsScroll}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[POGConfig] EnsureContent failed: {ex}");
            }
        }
    }
}
