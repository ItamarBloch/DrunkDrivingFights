using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to any object in the Create Match panel.
/// Wire up the toggle, the slider, and an optional value label in the Inspector.
///
/// Slider setup in Unity:
///   Min Value: 0  |  Max Value: 4  |  Whole Numbers: true
/// The 5 steps map to: 0=30, 1=60, 2=90, 3=120, 4=150
/// </summary>
public class MatchSettingsPanel : MonoBehaviour
{
    [Header("Self Damage")]
    [SerializeField] private Toggle selfDamageToggle;

    [Header("Rocket Damage")]
    [SerializeField] private Slider rocketDamageSlider;
    [SerializeField] private TextMeshProUGUI rocketDamageValueText;

    private void Start()
    {
        LoadSettings();

        selfDamageToggle.onValueChanged.AddListener(v =>
        {
            MatchCreationData.SelfDamageEnabled = v;
            PlayerPrefs.SetInt("MatchSelfDamage", v ? 1 : 0);
        });

        rocketDamageSlider.minValue     = 0;
        rocketDamageSlider.maxValue     = MatchCreationData.RocketDamageOptions.Length - 1;
        rocketDamageSlider.wholeNumbers = true;
        rocketDamageSlider.onValueChanged.AddListener(v => SelectDamage(Mathf.RoundToInt(v)));
    }

    private void SelectDamage(int index)
    {
        MatchCreationData.RocketDamage = MatchCreationData.RocketDamageOptions[index];
        PlayerPrefs.SetInt("MatchRocketDamage", MatchCreationData.RocketDamage);
        if (rocketDamageValueText != null)
            rocketDamageValueText.text = MatchCreationData.RocketDamage.ToString();
    }

    private void LoadSettings()
    {
        selfDamageToggle.isOn = PlayerPrefs.GetInt("MatchSelfDamage", 1) == 1;
        MatchCreationData.SelfDamageEnabled = selfDamageToggle.isOn;

        int saved = PlayerPrefs.GetInt("MatchRocketDamage", MatchCreationData.BaselineDamage);
        int idx = 1;
        for (int i = 0; i < MatchCreationData.RocketDamageOptions.Length; i++)
            if (MatchCreationData.RocketDamageOptions[i] == saved) { idx = i; break; }

        rocketDamageSlider.value = idx;
        SelectDamage(idx);
    }
}
