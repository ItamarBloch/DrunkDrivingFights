using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// Attach to any object in the Options panel.
/// Wire up all the UI fields in the Inspector.
///
/// For key bindings: provide a BindingsContainer (VerticalLayoutGroup) and a BindingTagPrefab.
/// The prefab needs a Button component and a TextMeshProUGUI child named "Label".
/// The script generates one row per action inside BindingsContainer automatically.
/// </summary>
public class LobbyOptionsPanel : MonoBehaviour
{
    // ── Player Identity ───────────────────────────────────────
    [Header("Player Identity")]
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private TextMeshProUGUI nameValidationText;

    // ── Camera ────────────────────────────────────────────────
    [Header("Camera")]
    [SerializeField] private Slider          horizSensSlider;
    [SerializeField] private TextMeshProUGUI horizSensValueText;
    [SerializeField] private Slider          vertSensSlider;
    [SerializeField] private TextMeshProUGUI vertSensValueText;
    [SerializeField] private Toggle          invertYToggle;

    // ── Controls ──────────────────────────────────────────────
    [Header("Controls")]
    [Tooltip("A VerticalLayoutGroup parent — one row per action is generated here.")]
    [SerializeField] private Transform  bindingsContainer;

    [Tooltip("Prefab with: Button component + TextMeshProUGUI child named 'Label'.")]
    [SerializeField] private GameObject bindingTagPrefab;

    [SerializeField] private Button     resetBindingsButton;

    // ── Runtime ───────────────────────────────────────────────
    private readonly Dictionary<InputBindingManager.GameAction, Transform> _tagContainers = new();
    private bool   _listeningForKey;
    private InputBindingManager.GameAction _listeningAction;
    private TextMeshProUGUI               _listeningAddBtnLabel;

    // ── Lifecycle ─────────────────────────────────────────────

    private void Start()
    {
        InitPlayerName();
        InitCameraSettings();
        InitControls();
    }

    private void Update()
    {
        if (!_listeningForKey) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) { CancelListen(); return; }

        int code = InputBindingManager.ScanAnyPress();
        if (code == 0) return;

        InputBindingManager.singleton?.AddBinding(_listeningAction, code);
        RefreshTagsForAction(_listeningAction);
        CancelListen();
    }

    // ── Player Name ────────────────────────────────────────────

    private void InitPlayerName()
    {
        if (playerNameInput == null) return;

        playerNameInput.characterLimit = 12;
        playerNameInput.contentType    = TMP_InputField.ContentType.Alphanumeric;
        playerNameInput.text           = PlayerPrefs.GetString("PlayerName", "");

        if (nameValidationText != null) nameValidationText.text = "";

        playerNameInput.onEndEdit.AddListener(name =>
        {
            string trimmed = name.Trim();
            if (trimmed.Length < 3)
            {
                if (nameValidationText != null) nameValidationText.text = "Min 3 characters";
                playerNameInput.text = PlayerPrefs.GetString("PlayerName", "");
                return;
            }
            if (nameValidationText != null) nameValidationText.text = "";
            PlayerPrefs.SetString("PlayerName", trimmed);
            PlayerPrefs.Save();
        });
    }

    // ── Camera ─────────────────────────────────────────────────

    private void InitCameraSettings()
    {
        if (horizSensSlider != null)
        {
            float saved = PlayerPrefs.GetFloat(PrefHorizSens, 0.15f);
            horizSensSlider.minValue = 0.05f;
            horizSensSlider.maxValue = 0.50f;
            horizSensSlider.value    = saved;
            UpdateHorizSensLabel(saved);

            horizSensSlider.onValueChanged.AddListener(v =>
            {
                UpdateHorizSensLabel(v);
                PlayerPrefs.SetFloat(PrefHorizSens, v);
            });
        }

        if (vertSensSlider != null)
        {
            float saved = PlayerPrefs.GetFloat(PrefVertSens, 0.15f);
            vertSensSlider.minValue = 0.05f;
            vertSensSlider.maxValue = 0.50f;
            vertSensSlider.value    = saved;
            UpdateVertSensLabel(saved);

            vertSensSlider.onValueChanged.AddListener(v =>
            {
                UpdateVertSensLabel(v);
                PlayerPrefs.SetFloat(PrefVertSens, v);
            });
        }

        if (invertYToggle != null)
        {
            invertYToggle.isOn = PlayerPrefs.GetInt(PrefInvertY, 0) == 1;
            invertYToggle.onValueChanged.AddListener(v =>
            {
                PlayerPrefs.SetInt(PrefInvertY, v ? 1 : 0);
                PlayerPrefs.Save();
            });
        }
    }

    private void UpdateHorizSensLabel(float v)
    {
        if (horizSensValueText != null) horizSensValueText.text = v.ToString("F2");
    }

    private void UpdateVertSensLabel(float v)
    {
        if (vertSensValueText != null) vertSensValueText.text = v.ToString("F2");
    }

    // ── Controls ───────────────────────────────────────────────

    private void InitControls()
    {
        if (resetBindingsButton != null)
        {
            resetBindingsButton.onClick.AddListener(() =>
            {
                InputBindingManager.singleton?.ResetToDefaults();
                foreach (InputBindingManager.GameAction a in Enum.GetValues(typeof(InputBindingManager.GameAction)))
                    RefreshTagsForAction(a);
            });
        }

        if (bindingsContainer == null) return;

        foreach (InputBindingManager.GameAction action in Enum.GetValues(typeof(InputBindingManager.GameAction)))
            BuildActionRow(action);
    }

    /// <summary>
    /// Creates one row inside bindingsContainer for the given action:
    ///   [Action Label]  [tag] [tag] ... [+ Add]
    /// Layout is handled by the VerticalLayoutGroup on bindingsContainer.
    /// </summary>
    private void BuildActionRow(InputBindingManager.GameAction action)
    {
        int idx = (int)action;
        string label = idx < InputBindingManager.ActionLabels.Length
            ? InputBindingManager.ActionLabels[idx]
            : action.ToString();

        // Row root
        var row = new GameObject($"Row_{action}");
        row.transform.SetParent(bindingsContainer, false);
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 6;
        hl.childForceExpandHeight = false;
        hl.childForceExpandWidth  = false;
        hl.childAlignment         = TextAnchor.MiddleLeft;
        row.AddComponent<LayoutElement>().preferredHeight = 32;

        // Action name label
        var nameGO = new GameObject("ActionName");
        nameGO.transform.SetParent(row.transform, false);
        var nameTmp = nameGO.AddComponent<TextMeshProUGUI>();
        nameTmp.text      = label;
        nameTmp.fontSize  = 13;
        nameTmp.alignment = TextAlignmentOptions.MidlineLeft;
        nameGO.AddComponent<LayoutElement>().preferredWidth = 150;

        // Tags container
        var tagsGO = new GameObject("Tags");
        tagsGO.transform.SetParent(row.transform, false);
        var tagsHL = tagsGO.AddComponent<HorizontalLayoutGroup>();
        tagsHL.spacing              = 4;
        tagsHL.childForceExpandWidth  = false;
        tagsHL.childForceExpandHeight = false;
        tagsHL.childAlignment         = TextAnchor.MiddleLeft;
        var tagsFit = tagsGO.AddComponent<ContentSizeFitter>();
        tagsFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        tagsGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        _tagContainers[action] = tagsGO.transform;

        // "+" Add button
        var addGO = new GameObject("AddBtn");
        addGO.transform.SetParent(row.transform, false);
        var addImg = addGO.AddComponent<Image>();
        addImg.color = Color.clear;
        var addBtn = addGO.AddComponent<Button>();
        addGO.AddComponent<LayoutElement>().preferredWidth = 28;

        var addTxtGO = new GameObject("Label");
        addTxtGO.transform.SetParent(addGO.transform, false);
        var addTmp = addTxtGO.AddComponent<TextMeshProUGUI>();
        addTmp.text      = "+";
        addTmp.fontSize  = 16;
        addTmp.alignment = TextAlignmentOptions.Midline;
        var addRT = addTxtGO.GetComponent<RectTransform>();
        addRT.anchorMin = Vector2.zero; addRT.anchorMax = Vector2.one;
        addRT.offsetMin = addRT.offsetMax = Vector2.zero;

        addBtn.onClick.AddListener(() => StartListening(action, addTmp));

        // Fill initial tags
        RefreshTagsForAction(action);
    }

    private void RefreshTagsForAction(InputBindingManager.GameAction action)
    {
        if (!_tagContainers.TryGetValue(action, out var container)) return;

        // Clear old tags
        for (int i = container.childCount - 1; i >= 0; i--)
            Destroy(container.GetChild(i).gameObject);

        var bindings = InputBindingManager.singleton?.GetBindings(action)
                       ?? new List<int>();

        foreach (int code in bindings)
        {
            int captured = code;

            if (bindingTagPrefab != null)
            {
                var tag = Instantiate(bindingTagPrefab, container);
                var lbl = tag.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
                if (lbl != null) lbl.text = InputBindingManager.CodeDisplayName(code);

                var btn = tag.GetComponent<Button>();
                if (btn != null)
                    btn.onClick.AddListener(() =>
                    {
                        InputBindingManager.singleton?.RemoveBinding(action, captured);
                        RefreshTagsForAction(action);
                    });
            }
            else
            {
                // Fallback plain text tag (no prefab assigned)
                var tagGO = new GameObject($"Tag_{code}");
                tagGO.transform.SetParent(container, false);
                var tmp = tagGO.AddComponent<TextMeshProUGUI>();
                tmp.text     = InputBindingManager.CodeDisplayName(code);
                tmp.fontSize = 12;
                tagGO.AddComponent<LayoutElement>().preferredWidth = 50;
            }
        }
    }

    private void StartListening(InputBindingManager.GameAction action, TextMeshProUGUI addBtnLabel)
    {
        if (_listeningForKey) { CancelListen(); return; }
        _listeningForKey     = true;
        _listeningAction     = action;
        _listeningAddBtnLabel = addBtnLabel;
        if (addBtnLabel != null) addBtnLabel.text = "…";
    }

    private void CancelListen()
    {
        _listeningForKey = false;
        if (_listeningAddBtnLabel != null) _listeningAddBtnLabel.text = "+";
        _listeningAddBtnLabel = null;
    }

    // ── Prefs keys (shared with ThirdPersonCameraController) ──
    public const string PrefHorizSens = "CamHorizSens";
    public const string PrefVertSens  = "CamVertSens";
    public const string PrefInvertY   = "CamInvertY";
}
