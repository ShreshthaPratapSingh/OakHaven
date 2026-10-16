using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

public class DayNightCycle : NetworkBehaviour
{
    [Header("Time")]
    [SerializeField] private float dayLengthSeconds = 600f;   // real seconds per 24h
    [SerializeField, Range(0, 24)] private float startHour = 8f;

    [Header("References")]
    [SerializeField] private Light sun;
    [SerializeField] private Light moon;
    [SerializeField] private Volume nightVolume;
    [SerializeField] private GameObject[] nightOnlyObjects;  // diner lights etc.

    [Header("Curves (x = time of day 0..1)")]
    [SerializeField] private AnimationCurve nightAmount;     // 0 = day, 1 = night
    [SerializeField] private AnimationCurve sunIntensity;
    [SerializeField] private Gradient sunColor;
    [SerializeField] private float moonMaxIntensity = 0.9f;

    [Header("Day / Night looks")]
    [SerializeField] private Color dayFogColor = new Color(0.79f, 0.85f, 0.89f);
    [SerializeField] private Color nightFogColor = new Color(0.31f, 0.45f, 0.52f);
    [SerializeField] private float dayFogDensity = 0.004f;
    [SerializeField] private float nightFogDensity = 0.025f;
    [SerializeField] private Color dayAmbient = new Color(0.6f, 0.66f, 0.72f);
    [SerializeField] private Color nightAmbient = new Color(0.14f, 0.23f, 0.29f);
    [SerializeField] private Color daySkyTint = new Color(0.5f, 0.5f, 0.5f);
    [SerializeField] private Color nightSkyTint = new Color(0.31f, 0.45f, 0.52f);
    [SerializeField] private float daySkyExposure = 1.1f;
    [SerializeField] private float nightSkyExposure = 0.35f;

    private readonly NetworkVariable<float> _serverHour = new NetworkVariable<float>();
    private float _hour;
    private float _syncTimer;
    private Material _sky;
    private bool _wasNight;

    public float Hour => _hour;
    public bool IsNight { get; private set; }

    public override void OnNetworkSpawn()
    {
        // Clone the skybox so we never edit the shared asset
        _sky = new Material(RenderSettings.skybox);
        RenderSettings.skybox = _sky;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

        if (IsServer) { _hour = startHour; _serverHour.Value = _hour; }
        else { _hour = _serverHour.Value; _serverHour.OnValueChanged += (_, v) => _hour = v; }
    }

    private void Update()
    {
        _hour = (_hour + 24f * Time.deltaTime / dayLengthSeconds) % 24f;

        if (IsServer)
        {
            _syncTimer += Time.deltaTime;
            if (_syncTimer >= 1f) { _syncTimer = 0f; _serverHour.Value = _hour; }
        }
        Apply();
    }

    private void Apply()
    {
        float t = _hour / 24f;
        float night = nightAmount.Evaluate(t);

        sun.transform.rotation = Quaternion.Euler(t * 360f - 90f, 30f, 0f);
        sun.color = sunColor.Evaluate(t);
        sun.intensity = sunIntensity.Evaluate(t);
        moon.intensity = night * moonMaxIntensity;

        nightVolume.weight = night;

        RenderSettings.fogColor = Color.Lerp(dayFogColor, nightFogColor, night);
        RenderSettings.fogDensity = Mathf.Lerp(dayFogDensity, nightFogDensity, night);
        RenderSettings.ambientLight = Color.Lerp(dayAmbient, nightAmbient, night);

        _sky.SetColor("_Tint", Color.Lerp(daySkyTint, nightSkyTint, night));
        _sky.SetFloat("_Exposure", Mathf.Lerp(daySkyExposure, nightSkyExposure, night));

        IsNight = night > 0.5f;
        if (IsNight != _wasNight)
        {
            _wasNight = IsNight;
            RenderSettings.sun = IsNight ? moon : sun;   // the main light casts shadows and drives the volumetric fog
            foreach (var go in nightOnlyObjects) go.SetActive(IsNight);
        }
    }
}