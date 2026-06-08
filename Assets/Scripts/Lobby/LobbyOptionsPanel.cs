using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// Attach to the Options panel object. Wire all fields in the Inspector.
///
/// CONTROLS SECTION — two prefabs required:
///
///   BindingRowPrefab — one row per action. Must have two named children:
///     • "ActionNameText"  — TextMeshProUGUI  — shows the action label (e.g. "Drive Forward")
///     • "KeyBindingsArea" — any Transform     — key buttons are spawned here (give it a HorizontalLayoutGroup)
///
///   KeyButtonPrefab — one button per bound key. Must have:
///     • Button component on the root
///     • Any TextMeshProUGUI child — shows the key name (e.g. "W", "Space")
///
///   The script spawns rows + key buttons automatically at Start.
///   Click a key button → press a new key → that binding is replaced.
///   Click "+" at the end of a row → press a key → new binding added.
/// </summary>
public class LobbyOptionsPanel : MonoBehaviour
{
    // ── Player Identity ───────────────────────────────────────
    [Header("Player Identity")]
    [SerializeField] private TMP_InputField      playerNameInput;
    [SerializeField] private TextMeshProUGUI     nameValidationText;

    // ── Camera ────────────────────────────────────────────────
    [Header("Camera")]
    [SerializeField] private Slider              horizSensSlider;
    [SerializeField] private TextMeshProUGUI     horizSensValueText;
    [SerializeField] private Slider              vertSensSlider;
    [SerializeField] private TextMeshProUGUI     vertSensValueText;
    [SerializeField] private Toggle              invertYToggle;

    // ── Controls ──────────────────────────────────────────────
    [Header("Controls")]
    [SerializeField] private Transform           bindingsColumnLeft;
    [SerializeField] private Transform           bindingsColumnRight;

    [Tooltip("Row prefab. Needs children named 'ActionNameText' (TMP) and 'KeyBindingsArea' (Transform).")]
    [SerializeField] private GameObject          bindingRowPrefab;

    [Tooltip("Key button prefab. Needs a Button component and any TMP child for the key label.")]
    [SerializeField] private GameObject          keyButtonPrefab;

    [SerializeField] private Button              resetBindingsButton;

    // ── Runtime state ─────────────────────────────────────────
    private bool                               _listening;
    private InputBindingManager.GameAction     _listenAction;
    private Button                             _listenButton;

    // ── Prefs keys ────────────────────────────────────────────
    public const string PrefHorizSens = "CamHorizSens";
    public const string PrefVertSens  = "CamVertSens";
    public const string PrefInvertY   = "CamInvertY";

    // ── Lifecycle ─────────────────────────────────────────────

    private void Start()
    {
        InitPlayerName();
        InitCameraSettings();
        InitControls();
    }

    private void Update()
    {
        if (!_listening) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) { CancelListen(); return; }

        int code = InputBindingManager.ScanAnyPress();
        if (code == 0) return;

        // Replace the single binding for this action
        InputBindingManager.singleton?.SetBindings(_listenAction, new List<int> { code });
        RefreshRow(_listenAction);
        CancelListen();
    }

    // ── Player Name ───────────────────────────────────────────

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

    // ── Camera ────────────────────────────────────────────────

    private void InitCameraSettings()
    {
        if (horizSensSlider != null)
        {
            float saved = PlayerPrefs.GetFloat(PrefHorizSens, 0.15f);
            horizSensSlider.minValue = 0.05f;
            horizSensSlider.maxValue = 1f;
            horizSensSlider.value    = saved;
            if (horizSensValueText != null) horizSensValueText.text = saved.ToString("F2");
            horizSensSlider.onValueChanged.AddListener(v =>
            {
                if (horizSensValueText != null) horizSensValueText.text = v.ToString("F2");
                PlayerPrefs.SetFloat(PrefHorizSens, v);
            });
        }

        if (vertSensSlider != null)
        {
            float saved = PlayerPrefs.GetFloat(PrefVertSens, 0.15f);
            vertSensSlider.minValue = 0.05f;
            vertSensSlider.maxValue = 1f;
            vertSensSlider.value    = saved;
            if (vertSensValueText != null) vertSensValueText.text = saved.ToString("F2");
            vertSensSlider.onValueChanged.AddListener(v =>
            {
                if (vertSensValueText != null) vertSensValueText.text = v.ToString("F2");
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

    // ── Controls ──────────────────────────────────────────────

    private void InitControls()
    {
        if (resetBindingsButton != null)
        {
            resetBindingsButton.onClick.AddListener(() =>
            {
                InputBindingManager.singleton?.ResetToDefaults();
                foreach (InputBindingManager.GameAction a in Enum.GetValues(typeof(InputBindingManager.GameAction)))
                    RefreshRow(a);
            });
        }

        if (bindingRowPrefab == null) return;

        int idx = 0;
        foreach (InputBindingManager.GameAction action in Enum.GetValues(typeof(InputBindingManager.GameAction)))
            BuildRow(action, idx++);
    }

    // Stores the key button per action so RefreshRow can update its text
    private readonly Dictionary<InputBindingManager.GameAction, Button> _keyButtons = new();

    private void BuildRow(InputBindingManager.GameAction action, int idx)
    {
        int    actionIdx = (int)action;
        string label     = actionIdx < InputBindingManager.ActionLabels.Length
            ? InputBindingManager.ActionLabels[actionIdx] : action.ToString();

        int       total  = Enum.GetValues(typeof(InputBindingManager.GameAction)).Length;
        int       half   = (total + 1) / 2;
        Transform column = idx < half ? bindingsColumnLeft : bindingsColumnRight;
        if (column == null) return;

        var row = Instantiate(bindingRowPrefab, column);

        var nameTmp = row.transform.Find("ActionNameText")?.GetComponent<TextMeshProUGUI>();
        if (nameTmp != null) nameTmp.text = label;

        var keyArea = row.transform.Find("KeyBindingsArea");
        if (keyArea == null) return;

        var btn = Instantiate(keyButtonPrefab, keyArea);
        var btnComponent = btn.GetComponent<Button>();
        _keyButtons[action] = btnComponent;

        UpdateKeyButtonText(action);
        btnComponent.onClick.AddListener(() => StartListen(action, btnComponent));
    }

    private void RefreshRow(InputBindingManager.GameAction action)
    {
        UpdateKeyButtonText(action);
    }

    private void UpdateKeyButtonText(InputBindingManager.GameAction action)
    {
        if (!_keyButtons.TryGetValue(action, out var btn)) return;
        var bindings = InputBindingManager.singleton?.GetBindings(action);
        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
            tmp.text = bindings != null && ((IReadOnlyList<int>)bindings).Count > 0
                ? InputBindingManager.CodeDisplayName(((IReadOnlyList<int>)bindings)[0])
                : "---";
    }

    // ── Listen mode ───────────────────────────────────────────

    private void StartListen(InputBindingManager.GameAction action, Button btn)
    {
        if (_listening) CancelListen();
        _listening    = true;
        _listenAction = action;
        _listenButton = btn;
        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null) tmp.text = "...";
    }

    private void CancelListen()
    {
        _listening    = false;
        if (_listenButton != null) UpdateKeyButtonText(_listenAction);
        _listenButton = null;
    }
}
