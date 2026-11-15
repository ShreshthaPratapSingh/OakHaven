using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Single authoritative day/night cycle. The server owns the clock (in hours, 0..24) and
/// replicates it roughly once per second; every client derives all purely visual state
/// (sun/moon rotation, light intensity, skybox, fog, ambient, night volume) locally from
/// the synchronized time, so no extra network traffic is needed for visuals.
/// </summary>
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
    [Tooltip("Stylized procedural skybox material driven by this cycle. Falls back to the scene skybox if unassigned.")]
    [SerializeField] private Material skyboxMaterial;

    [Header("Sun movement")]
    [Tooltip("Hour (0..24) at which the sun crosses the horizon in the east.")]
    [SerializeField, Range(0, 24)] private float sunriseHour = 6f;
    [Tooltip("Hour (0..24) at which the sun crosses the horizon in the west.")]
    [SerializeField, Range(0, 24)] private float sunsetHour = 18.5f;
    [Tooltip("Compass heading of the sun at solar noon: 0 = north (+Z), 90 = east (+X), 180 = south.")]
    [SerializeField, Range(0, 360)] private float sunAzimuth = 180f;
    [Tooltip("Sun elevation at solar noon, in degrees.")]
    [SerializeField, Range(15, 89)] private float sunMaxElevation = 62f;
    [Tooltip("Deepest sun elevation below the horizon at midnight, in degrees.")]
    [SerializeField, Range(5, 89)] private float sunNightDip = 35f;

    [Header("Curves (x = time of day 0..1)")]
    [SerializeField] private AnimationCurve nightAmount;     // 0 = day, 1 = night
    [SerializeField] private AnimationCurve sunIntensity;    // direct sunlight intensity (peak = maximum sunlight)
    [Tooltip("Scales the sun intensity curve - quick inspector knob for maximum sunlight strength.")]
    [SerializeField, Range(0, 5)] private float sunIntensityMultiplier = 1f;
    [Tooltip("Sun shadow strength across the day (0 = faint shadows, 1 = solid shadows).")]
    [SerializeField] private AnimationCurve sunShadowStrength = new AnimationCurve(
        new Keyframe(0.22f, 0.30f),
        new Keyframe(0.35f, 0.65f),
        new Keyframe(0.50f, 0.85f),
        new Keyframe(0.65f, 0.65f),
        new Keyframe(0.78f, 0.30f));
    [Tooltip("Sunlight colour across the day: warm at sunrise, white at midday, warm again at sunset.")]
    [SerializeField] private Gradient sunColor;
    [SerializeField] private float moonMaxIntensity = 0.9f;

    [Header("Day / Night looks")]
    [SerializeField] private Color dayFogColor = new Color(0.79f, 0.85f, 0.89f);
    [SerializeField] private Color nightFogColor = new Color(0.31f, 0.45f, 0.52f);
    [SerializeField] private float dayFogDensity = 0.004f;
    [SerializeField] private float nightFogDensity = 0.025f;
    [SerializeField] private Color dayAmbient = new Color(0.6f, 0.66f, 0.72f);
    [SerializeField] private Color nightAmbient = new Color(0.14f, 0.23f, 0.29f);
    [SerializeField] private float daySkyExposure = 1.1f;
    [SerializeField] private float nightSkyExposure = 0.35f;

    [Header("Skybox extras")]
    [Tooltip("Skybox sun disk brightness relative to the current sunlight colour (HDR value, feeds bloom).")]
    [SerializeField, Range(0, 8)] private float sunDiskBrightness = 2.2f;
    [Tooltip("Skybox moon disk brightness, faded in with the night curve.")]
    [SerializeField, Range(0, 2)] private float moonDiskBrightness = 0.9f;
    [SerializeField, Range(0, 1)] private float starIntensity = 1f;
    [SerializeField, Range(0, 1)] private float cloudAmount = 0.35f;

    private readonly NetworkVariable<float> _serverHour = new NetworkVariable<float>();
    private float _hour;
    private float _syncTimer;
    private Material _sky;
    private bool _hasSkyControls;   // cloned material actually exposes our runtime uniforms
    private bool _wasNight;
    private readonly Vector4 _sunDirVec = new Vector4(0f, 1f, 0f, 0f);
    private readonly Vector4 _moonDirVec = new Vector4(0f, 1f, 0f, 0f);

    private static readonly int SunDirectionId = Shader.PropertyToID("_SunDirection");
    private static readonly int SunDiskColorId = Shader.PropertyToID("_SunDiskColor");
    private static readonly int MoonDirectionId = Shader.PropertyToID("_MoonDirection");
    private static readonly int MoonIntensityId = Shader.PropertyToID("_MoonIntensity");
    private static readonly int StarIntensityId = Shader.PropertyToID("_StarIntensity");
    private static readonly int CloudAmountId = Shader.PropertyToID("_CloudAmount");
    private static readonly int ExposureId = Shader.PropertyToID("_Exposure");

    public float Hour => _hour;
    public bool IsNight { get; private set; }

    public void Awake()
    {
        // Clone the skybox so we never edit the shared asset.
        _hour = startHour;
        Material source = skyboxMaterial != null ? skyboxMaterial : RenderSettings.skybox;
        if (source != null)
        {
            _sky = new Material(source);
            RenderSettings.skybox = _sky;
            _hasSkyControls = _sky.HasProperty(SunDirectionId);
            if (_hasSkyControls) _sky.SetFloat(CloudAmountId, cloudAmount);
        }

        // The sun colour gradient is the single source of truth for the light colour;
        // the scene light's colour-temperature filter would otherwise tint it.
        if (sun != null) sun.useColorTemperature = false;

        RenderSettings.ambientMode = AmbientMode.Flat;    
    }

    public override void OnNetworkSpawn()
    {

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

        // --- Sun movement (continuous across the whole 24h, no jump at midnight) ---
        Vector3 sunDir = ComputeSunDirection(_hour);
        sun.transform.rotation = Quaternion.LookRotation(-sunDir);   // light rays travel away from the sun disk

        // --- Direct sunlight ---
        Color lightColor = sunColor.Evaluate(t);
        sun.color = lightColor;
        sun.intensity = sunIntensity.Evaluate(t) * sunIntensityMultiplier;
        sun.shadowStrength = Mathf.Clamp01(sunShadowStrength.Evaluate(t));
        moon.intensity = night * moonMaxIntensity;

        nightVolume.weight = night;

        RenderSettings.fogColor = Color.Lerp(dayFogColor, nightFogColor, night);
        RenderSettings.fogDensity = Mathf.Lerp(dayFogDensity, nightFogDensity, night);
        RenderSettings.ambientLight = Color.Lerp(dayAmbient, nightAmbient, night);

        // --- Procedural skybox ---
        if (_sky != null && _hasSkyControls)
        {
            // The sun disk always follows the actual Sun directional light.
            _sunDirVec.Set(sunDir.x, sunDir.y, sunDir.z, 0f);
            _sky.SetVector(SunDirectionId, _sunDirVec);
            _sky.SetColor(SunDiskColorId, lightColor * sunDiskBrightness);

            // Moon disk follows the existing Moon light (it points at the ground).
            if (moon != null)
            {
                Vector3 moonDir = -moon.transform.forward;
                _moonDirVec.Set(moonDir.x, moonDir.y, moonDir.z, 0f);
                _sky.SetVector(MoonDirectionId, _moonDirVec);
            }
            _sky.SetFloat(MoonIntensityId, night * moonDiskBrightness);
            _sky.SetFloat(StarIntensityId, starIntensity * night);
            _sky.SetFloat(ExposureId, Mathf.Lerp(daySkyExposure, nightSkyExposure, night));
        }

        IsNight = night > 0.5f;
        if (IsNight != _wasNight)
        {
            _wasNight = IsNight;
            RenderSettings.sun = IsNight ? moon : sun;   // the main light casts shadows and drives the volumetric fog
            foreach (var go in nightOnlyObjects) go.SetActive(IsNight);
        }
    }

    /// <summary>
    /// Sun direction (unit vector pointing at the sun in the sky) for the given hour.
    /// The sun rises at sunriseHour on heading (sunAzimuth - 90), peaks at sunMaxElevation
    /// at solar noon, sets at sunsetHour on heading (sunAzimuth + 90) and dips sunNightDip
    /// degrees below the horizon at midnight. All transitions are continuous, including the
    /// 23:59 -> 00:00 wrap.
    /// </summary>
    private Vector3 ComputeSunDirection(float hour)
    {
        float daySpan = Mathf.Repeat(sunsetHour - sunriseHour, 24f);
        if (daySpan < 0.01f) daySpan = 12f;
        float nightSpan = 24f - daySpan;

        // Hours elapsed since the most recent sunrise; wraps continuously at the next sunrise.
        float sinceSunrise = Mathf.Repeat(hour - sunriseHour, 24f);

        float elevation;
        float heading;
        if (sinceSunrise < daySpan)
        {
            // Daytime: sine bump from the horizon up to sunMaxElevation and back down.
            float dayT = sinceSunrise / daySpan;                     // 0 at sunrise, 1 at sunset
            elevation = Mathf.Sin(Mathf.PI * dayT) * sunMaxElevation;
            heading = sunAzimuth - 90f + 180f * dayT;                // east -> west
        }
        else
        {
            // Night: sun travels on below the horizon.
            float nightT = (sinceSunrise - daySpan) / nightSpan;    // 0 at sunset, 1 at next sunrise
            elevation = -Mathf.Sin(Mathf.PI * nightT) * sunNightDip;
            heading = sunAzimuth + 90f + 180f * nightT;             // continues west -> east
        }

        float elevRad = elevation * Mathf.Deg2Rad;
        float headRad = heading * Mathf.Deg2Rad;
        return new Vector3(
            Mathf.Sin(headRad) * Mathf.Cos(elevRad),
            Mathf.Sin(elevRad),
            Mathf.Cos(headRad) * Mathf.Cos(elevRad));
    }
}
