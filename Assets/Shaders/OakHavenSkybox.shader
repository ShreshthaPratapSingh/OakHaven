// OakHaven stylized procedural skybox.
// Renders a vertical gradient sky with a glowing sun disk, soft clouds, a moon disk and stars.
// All colours blend smoothly based on the sun elevation so the sky stays in sync with the
// directional Sun light. The following properties are driven at runtime by DayNightCycle:
//   _SunDirection, _SunDiskColor, _MoonDirection, _MoonIntensity, _StarIntensity, _Exposure
Shader "OakHaven/Skybox"
{
    Properties
    {
        // --- Runtime-driven by DayNightCycle ---
        _SunDirection  ("Sun Direction (xyz, normalized)", Vector) = (0, 1, 0, 0)
        _SunDiskColor  ("Sun Disk Color (HDR)", Color) = (1, 0.95, 0.82, 1)
        _MoonDirection ("Moon Direction (xyz, normalized)", Vector) = (0, 1, 0, 0)
        _MoonIntensity ("Moon Disk Intensity", Range(0, 1)) = 0
        _StarIntensity ("Star Intensity", Range(0, 1)) = 1
        _CloudAmount   ("Cloud Amount", Range(0, 1)) = 0.35
        _Exposure      ("Exposure", Range(0, 8)) = 1.1

        // --- Sky palette ---
        _DayZenithColor      ("Day Zenith", Color) = (0.073, 0.343, 0.914, 1)
        _DayHorizonColor     ("Day Horizon", Color) = (0.507, 0.710, 0.912, 1)
        _TwilightZenithColor ("Twilight Zenith", Color) = (0.051, 0.051, 0.264, 1)
        _TwilightHorizonColor("Twilight Horizon", Color) = (0.955, 0.233, 0.084, 1)
        _NightZenithColor    ("Night Zenith", Color) = (0.010, 0.024, 0.087, 1)
        _NightHorizonColor   ("Night Horizon", Color) = (0.022, 0.076, 0.153, 1)
        _GroundColor         ("Ground Haze", Color) = (0.147, 0.119, 0.101, 1)

        // --- Sun disk ---
        _SunDiskSize      ("Sun Disk Size (degrees)", Range(0.5, 10)) = 3.5
        _SunHaloPower     ("Sun Halo Falloff", Range(1, 32)) = 8
        _SunHaloIntensity ("Sun Halo Intensity", Range(0, 1)) = 0.5

        // --- Moon disk ---
        _MoonDiskSize ("Moon Disk Size (degrees)", Range(0.5, 10)) = 2.5

        // --- Stars ---
        _StarScale ("Star Density Scale", Range(20, 200)) = 80

        // --- Clouds ---
        _CloudScale      ("Cloud Scale", Range(0.05, 1)) = 0.28
        _CloudDrift      ("Cloud Drift Speed", Range(0, 0.05)) = 0.008
        _CloudDayColor   ("Cloud Day Color", Color) = (1, 1, 1, 1)
        _CloudNightColor ("Cloud Night Color", Color) = (0.102, 0.171, 0.297, 1)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "IgnoreProjector"="True" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            // Runtime-driven uniforms
            float4 _SunDirection;
            float4 _SunDiskColor;
            float4 _MoonDirection;
            float  _MoonIntensity;
            float  _StarIntensity;
            float  _CloudAmount;
            float  _Exposure;

            // Palette
            float4 _DayZenithColor;
            float4 _DayHorizonColor;
            float4 _TwilightZenithColor;
            float4 _TwilightHorizonColor;
            float4 _NightZenithColor;
            float4 _NightHorizonColor;
            float4 _GroundColor;

            // Sun / moon disks
            float _SunDiskSize;
            float _SunHaloPower;
            float _SunHaloIntensity;
            float _MoonDiskSize;

            // Stars
            float _StarScale;

            // Clouds
            float  _CloudScale;
            float  _CloudDrift;
            float4 _CloudDayColor;
            float4 _CloudNightColor;

            struct appdata_t
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 ray : TEXCOORD0;   // world-space view direction (outward from camera)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.ray = normalize(mul((float3x3)unity_ObjectToWorld, v.vertex.xyz));
                return o;
            }

            float hash12 (float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float hash13 (float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float vnoise (float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash12(i);
                float b = hash12(i + float2(1.0, 0.0));
                float c = hash12(i + float2(0.0, 1.0));
                float d = hash12(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm (float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    v += a * vnoise(p);
                    p = p * 2.03 + 11.7;
                    a *= 0.5;
                }
                return v;
            }

            half4 frag (v2f i) : SV_Target
            {
                float3 ray = normalize(i.ray);
                float3 sunDir = normalize(_SunDirection.xyz);
                float se = sunDir.y; // sin of sun elevation: >0 above horizon, <0 below

                // Time-of-day blend weights (always sum to 1: day and night cannot overlap).
                float dayF   = smoothstep(0.03, 0.38, se);
                float nightF = smoothstep(0.04, 0.30, -se);
                float twF    = (1.0 - dayF) * (1.0 - nightF); // bell around the horizon crossings

                float3 zenith  = _DayZenithColor.rgb      * dayF
                               + _TwilightZenithColor.rgb  * twF
                               + _NightZenithColor.rgb     * nightF;
                float3 horizon = _DayHorizonColor.rgb      * dayF
                               + _TwilightHorizonColor.rgb * twF
                               + _NightHorizonColor.rgb    * nightF;

                // Vertical gradient above the horizon, warm ground haze below it.
                float3 above = lerp(horizon, zenith, pow(saturate(ray.y), 0.65));
                float3 below = lerp(horizon * 0.8, _GroundColor.rgb, pow(saturate(-ray.y), 0.55));
                float3 col = lerp(below, above, smoothstep(-0.015, 0.015, ray.y));

                float sunAmount = saturate(dot(ray, sunDir));

                // Warm glow on the horizon side the sun is rising/setting on.
                float horizMask = 1.0 - saturate(ray.y * 1.5);
                col += _TwilightHorizonColor.rgb * (pow(sunAmount, 8.0) * horizMask * twF * 0.55);

                // --- Stars (drawn under the clouds, fading in at night) ---
                float starVis = _StarIntensity * smoothstep(0.0, 0.2, ray.y);
                if (starVis > 0.001)
                {
                    float3 sp = ray * _StarScale;
                    float3 cell = floor(sp);
                    float h1 = hash13(cell);
                    float h2 = hash13(cell + 17.0);
                    float h3 = hash13(cell + 43.0);
                    float exists = step(h1, 0.28); // ~28% of cells hold a star
                    float3 starPos = cell + 0.5 + (float3(h1, h2, h3) - 0.5) * 0.7;
                    float d = length(sp - starPos);
                    float star = exp(-d * d * 14.0);
                    float twinkle = 0.72 + 0.28 * sin(_Time.y * (1.5 + h2 * 3.0) + h3 * 6.2832);
                    float3 starCol = lerp(float3(0.75, 0.85, 1.0), float3(1.0, 0.9, 0.75), step(0.5, h2));
                    col += starCol * (star * exists * twinkle * starVis * (0.4 + h3));
                }

                // --- Moon disk ---
                float3 moonDir = normalize(_MoonDirection.xyz);
                float moonUp = smoothstep(0.0, 0.05, moonDir.y);
                if (_MoonIntensity > 0.001 && moonUp > 0.0)
                {
                    float moonAmount = saturate(dot(ray, moonDir));
                    float moonAngle = degrees(acos(clamp(moonAmount, -1.0, 1.0)));
                    float moonDisk = 1.0 - smoothstep(_MoonDiskSize * 0.4, _MoonDiskSize, moonAngle);
                    float moonHalo = pow(moonAmount, 24.0) * 0.12;
                    float3 moonCol = float3(0.53, 0.64, 0.89) * _MoonIntensity;
                    col += moonCol * ((moonDisk * 1.4 + moonHalo) * moonUp);
                }

                // --- Soft clouds (perspective layer, drifting slowly) ---
                float cloudVis = _CloudAmount * smoothstep(0.01, 0.18, ray.y);
                float cloudMask = 0.0;
                if (cloudVis > 0.001)
                {
                    float2 cuv = (ray.xz / max(ray.y, 0.05)) * _CloudScale
                               + float2(_Time.y * _CloudDrift, _Time.y * _CloudDrift * 0.35);
                    float n = fbm(cuv * 3.0);
                    cloudMask = smoothstep(0.48, 0.82, n) * cloudVis;
                    float3 cloudCol = lerp(_CloudNightColor.rgb, _CloudDayColor.rgb, dayF);
                    cloudCol = lerp(cloudCol, _TwilightHorizonColor.rgb * 0.85, twF * 0.45); // pink dusk clouds
                    cloudCol += _SunDiskColor.rgb * (pow(sunAmount, 4.0) * 0.22 * (dayF + twF)); // sun-lit edges
                    col = lerp(col, cloudCol, cloudMask);
                }

                // --- Sun disk with halo (drawn last, dimmed where clouds cover it) ---
                float sunVis = smoothstep(-0.05, 0.02, se); // hide once the sun is well below the horizon
                if (sunVis > 0.001)
                {
                    float occlusion = 1.0 - cloudMask * 0.75;
                    float sunAngle = degrees(acos(clamp(sunAmount, -1.0, 1.0)));
                    float disk = 1.0 - smoothstep(_SunDiskSize * 0.35, _SunDiskSize, sunAngle);
                    float halo = pow(sunAmount, _SunHaloPower) * _SunHaloIntensity;
                    col += _SunDiskColor.rgb * ((disk + halo * (0.4 + 0.6 * disk)) * sunVis * occlusion);
                }

                col *= _Exposure;
                return half4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
