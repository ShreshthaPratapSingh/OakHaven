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
    [SerializeField] private GameObject[] nightOnlyObjects;  // diner signs etc. (toggled mid-transition)
    [SerializeField] private Light[] nightLights;            // diner lights etc. (faded with the night amount)

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

    [SerializeField] private Color moonColor = new Color(0.61f, 0.77f, 0.86f);

    // RenderSettings.sun is swapped only while both lights sit at ~zero
    // intensity (very start/end of the dusk & dawn ramps) so the swap is
    // invisible instead of a one-frame pop.
    private const float MainLightSwapThreshold = 0.05f;
    // Server clock corrections smaller than this (hours) are absorbed
    // smoothly; bigger ones (fresh join / big desync) snap once.
    private const float HardSyncThreshold = 1f;
    // Fraction of a pending clock correction absorbed per second.
    private const float CorrectionRate = 3f;

    private readonly NetworkVariable<float> _serverHour = new NetworkVariable<float>();
    private float _hour;
    private float _hourCorrection;
    private float _syncTimer;
    private Material _sky;
    private float[] _nightLightBase;
    private bool _wasNight;
    private bool _moonPhase;

    public float Hour => _hour;
    public bool IsNight { get; private set; }

    private void Awake()
    {
        // Clone the skybox so we never edit the shared asset. Done in Awake so
        // lighting keeps working even if networking never spawns us (e.g. a
        // relay sign-in failure) - Update can then never null-ref on _sky.
        if (RenderSettings.skybox != null)
        {
            _sky = new Material(RenderSettings.skybox);
            RenderSettings.skybox = _sky;
        }
        RenderSettings.ambientMode = AmbientMode.Flat;

        int lightCount = nightLights != null ? nightLights.Length : 0;
        _nightLightBase = new float[lightCount];
        for (int i = 0; i < lightCount; i++)
            _nightLightBase[i] = nightLights[i] != null ? nightLights[i].intensity : 0f;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _hour = startHour;
            _serverHour.Value = _hour;
        }
        else
        {
            // 0 = the variable has not synced yet; better to sit on the start
            // hour than flash midnight for the first second.
            if (_serverHour.Value > 0f) _hour = _serverHour.Value;
            _serverHour.OnValueChanged += OnServerHourChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        _serverHour.OnValueChanged -= OnServerHourChanged;
    }

    private void OnServerHourChanged(float _, float serverHour)
    {
        if (IsServer) return;

        // Shortest path around the 24h wrap.
        float diff = serverHour - _hour;
        if (diff > 12f) diff -= 24f;
        else if (diff < -12f) diff += 24f;

        if (Mathf.Abs(diff) > HardSyncThreshold)
        {
            // Fresh join or real desync: snap once, it is invisible at spawn.
            _hour = serverHour;
            _hourCorrection = 0f;
        }
        else
        {
            // Normal drift: absorb the correction smoothly over the next
            // ~third of a second instead of yanking the lighting back.
            _hourCorrection = diff;
        }
    }

    private void Update()
    {
        float advance = 24f * Time.deltaTime / dayLengthSeconds;
        float absorb = _hourCorrection * Mathf.Clamp01(Time.deltaTime * CorrectionRate);
        _hourCorrection -= absorb;
        _hour = Mathf.Repeat(_hour + advance + absorb, 24f);

        if (IsServer)
        {
            _syncTimer += Time.deltaTime;
            if (_syncTimer >= 1f)
            {
                _syncTimer = 0f;
                _serverHour.Value = _hour;
            }
        }
        Apply();
    }

    private void Apply()
    {
        if (_sky == null) return;

        float t = _hour / 24f;
        // The curve tangents are authored data; clamp so a rogue key can never
        // push fog/ambient/sky Lerps into extrapolation territory again.
        float night = Mathf.Clamp01(nightAmount.Evaluate(t));

        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(t * 360f - 90f, 30f, 0f);
            sun.color = sunColor.Evaluate(t);
            sun.intensity = sunIntensity.Evaluate(t);
        }

        if (moon != null)
        {
            moon.color = moonColor;
            moon.intensity = night * moonMaxIntensity;
        }

        if (nightVolume != null) nightVolume.weight = night;

        RenderSettings.fogColor = Color.Lerp(dayFogColor, nightFogColor, night);
        RenderSettings.fogDensity = Mathf.Lerp(dayFogDensity, nightFogDensity, night);
        RenderSettings.ambientLight = Color.Lerp(dayAmbient, nightAmbient, night);

        _sky.SetColor("_Tint", Color.Lerp(daySkyTint, nightSkyTint, night));
        _sky.SetFloat("_Exposure", Mathf.Lerp(daySkyExposure, nightSkyExposure, night));

        // Diner lights fade in/out with the night amount instead of popping on.
        if (nightLights != null)
        {
            for (int i = 0; i < nightLights.Length; i++)
            {
                if (nightLights[i] != null)
                    nightLights[i].intensity = _nightLightBase[i] * night;
            }
        }

        IsNight = night > 0.5f;

        // Swap the shadow-driving light only while both lights are at ~zero
        // intensity (start/end of the dusk/dawn ramps) so it never pops.
        bool moonPhase = night > MainLightSwapThreshold;
        if (moonPhase != _moonPhase)
        {
            _moonPhase = moonPhase;
            if (sun != null && moon != null)
                RenderSettings.sun = moonPhase ? moon : sun;
        }

        if (IsNight != _wasNight)
        {
            _wasNight = IsNight;
            if (nightOnlyObjects != null)
            {
                for (int i = 0; i < nightOnlyObjects.Length; i++)
                {
                    if (nightOnlyObjects[i] != null)
                        nightOnlyObjects[i].SetActive(IsNight);
                }
            }
        }
    }
}
