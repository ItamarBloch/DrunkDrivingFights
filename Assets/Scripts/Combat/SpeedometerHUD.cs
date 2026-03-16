using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

/// <summary>
/// Builds the speedometer widget at runtime inside the existing CombatHUDCanvas.
/// Add this component to the Car prefab and assign the two sprites in the Inspector.
/// </summary>
public class SpeedometerHUD : NetworkBehaviour
{
    [Header("Sprites — assign from Assets/SpeedoMeter/")]
    [SerializeField] private Sprite dialSprite;    // speedo.png
    [SerializeField] private Sprite needleSprite;  // needle.png

    [Header("Layout")]
    [Tooltip("Size of the speedometer widget in pixels.")]
    [SerializeField] private Vector2 size = new Vector2(220f, 220f);
    [Tooltip("Position from the bottom-right corner. Negative X = leftward, positive Y = upward.")]
    [SerializeField] private Vector2 anchoredPosition = new Vector2(-120f, 120f);

    [Header("Needle Calibration")]
    [Tooltip("Needle Z-rotation (degrees) at 0 km/h. Default −135 = 7:30 o'clock position.")]
    [SerializeField] private float minAngle = -135f;
    [Tooltip("Needle Z-rotation (degrees) at max dial speed. Default −405 = 4:30 o'clock (270° CW sweep through top).")]
    [SerializeField] private float maxAngle = -405f;
    [Tooltip("The km/h value that corresponds to full-scale on the dial (dial reads up to 180).")]
    [SerializeField] private float maxDisplaySpeed = 180f;
    [Tooltip("Pivot of the needle image — where its rotation center is. (0.5, 0.5) = center of image.")]
    [SerializeField] private Vector2 needlePivot = new Vector2(0.5f, 0.5f);
    [Tooltip("How fast the needle smoothly follows the actual speed.")]
    [SerializeField] private float smoothSpeed = 10f;

    [Header("Speed Readout")]
    [SerializeField] private bool showReadout = true;
    [Tooltip("Position of the readout text relative to the panel center.")]
    [SerializeField] private Vector2 readoutOffset = new Vector2(0f, 20f);
    [SerializeField] private float readoutFontSize = 20f;

    private CarController _car;
    private RectTransform _needleRect;
    private TMP_Text _readout;
    private float _smoothedSpeed;

    private void Awake()
    {
        _car = GetComponent<CarController>();
    }

    public override void OnStartLocalPlayer()
    {
        var canvas = GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            Debug.LogWarning("[SpeedometerHUD] No Canvas found in children — speedometer will not appear.");
            return;
        }
        if (dialSprite == null || needleSprite == null)
        {
            Debug.LogWarning("[SpeedometerHUD] Dial or needle sprite not assigned in Inspector — speedometer will not appear.");
            return;
        }
        BuildUI(canvas.transform);
    }

    private void BuildUI(Transform canvasRoot)
    {
        // Panel anchored to the bottom-right corner
        var panel = new GameObject("SpeedometerPanel").AddComponent<RectTransform>();
        panel.SetParent(canvasRoot, false);
        panel.anchorMin        = new Vector2(1f, 0f);
        panel.anchorMax        = new Vector2(1f, 0f);
        panel.pivot            = new Vector2(0.5f, 0.5f);
        panel.sizeDelta        = size;
        panel.anchoredPosition = anchoredPosition;

        // Dial background (non-rotating)
        MakeImage(panel, "Dial", dialSprite, new Vector2(0.5f, 0.5f));

        // Needle (rotates around its pivot)
        _needleRect = MakeImage(panel, "Needle", needleSprite, needlePivot).GetComponent<RectTransform>();

        // Optional km/h readout text
        if (showReadout)
        {
            var go = new GameObject("SpeedReadout");
            go.transform.SetParent(panel, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(130f, 40f);
            rt.anchoredPosition = readoutOffset;

            _readout = go.AddComponent<TextMeshProUGUI>();
            _readout.text      = "0";
            _readout.fontSize  = readoutFontSize;
            _readout.fontStyle = FontStyles.Bold;
            _readout.color     = Color.white;
            _readout.alignment = TextAlignmentOptions.Center;
        }

        SetNeedle(0f);
    }

    private static GameObject MakeImage(RectTransform parent, string name, Sprite sprite, Vector2 pivot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.pivot     = pivot;           // Set pivot BEFORE anchor/offset so layout stays correct
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.sprite         = sprite;
        img.preserveAspect = true;
        img.raycastTarget  = false;
        return go;
    }

    private void Update()
    {
        if (!isLocalPlayer || _needleRect == null) return;

        float target = _car != null ? Mathf.Abs(_car.SyncedSpeedKmh) : 0f;
        _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, target, Time.deltaTime * smoothSpeed);

        SetNeedle(_smoothedSpeed);

        if (_readout != null)
            _readout.text = Mathf.RoundToInt(_smoothedSpeed).ToString();
    }

    private void SetNeedle(float kmh)
    {
        float t = Mathf.Clamp01(kmh / maxDisplaySpeed);
        _needleRect.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(minAngle, maxAngle, t));
    }
}
