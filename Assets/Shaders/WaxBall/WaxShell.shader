// 왁뿌볼 껍질용 URP 셰이더. 구조는 고무막 > 왁스 껍질 > 속 점토이고, 이 셰이더는 고무막 아래 왁스 껍질의 모습을 그린다.
// _CrackAmount(0~1): Voronoi 균열 선이 하나씩 늘어난다.
// _BrokenAmount(0~1): 균열 칸(판)마다 높이와 기울기가 어긋나 판이 갈라진 것처럼 울퉁불퉁해지고, 실루엣도 불규칙해진다.
// _MixAmount(0~1): 균열 칸이 잘게 쪼개져(Grain Scale Multiplier) 오돌토돌한 알갱이 질감이 된다.
// Quest 성능을 위해 단일 Forward 패스, 텍스처 없이 절차적으로만 계산한다.
Shader "WaxBall/WaxShell"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.95, 0.9, 0.75, 1)
        _CrackColor("Crack Color", Color) = (0.25, 0.15, 0.08, 1)
        _CrackScale("Crack Scale", Range(1, 10)) = 2
        _CrackWidth("Crack Width", Range(0.005, 0.1)) = 0.06
        _CrackAmount("Crack Amount", Range(0, 1)) = 0
        _BrokenAmount("Broken Amount", Range(0, 1)) = 0
        _MixAmount("Mix Amount", Range(0, 1)) = 0
        _HeightAmp("Plate Height", Range(0, 0.1)) = 0.03
        _TiltAmount("Plate Tilt", Range(0, 1)) = 0.35
        _SilhouetteAmp("Silhouette Irregularity", Range(0, 0.05)) = 0.015
        _GrainScaleMul("Grain Scale Multiplier", Range(1, 8)) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _CrackColor;
                float _CrackScale;
                float _CrackWidth;
                float _CrackAmount;
                float _BrokenAmount;
                float _MixAmount;
                float _HeightAmp;
                float _TiltAmount;
                float _SilhouetteAmp;
                float _GrainScaleMul;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 unitPos : TEXCOORD1;
            };

            struct VoronoiResult
            {
                float edge;      // 가장 가까운 두 셀 경계까지의 거리
                float lineHash;  // 경계 선의 무작위 값(같은 선은 항상 같은 값)
                float3 cellId;   // 가장 가까운 셀의 id
            };

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

            float Hash1(float3 p)
            {
                return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
            }

            // 실루엣을 불규칙하게 만드는 저주파 값 노이즈
            float Noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash1(i);
                float n100 = Hash1(i + float3(1, 0, 0));
                float n010 = Hash1(i + float3(0, 1, 0));
                float n110 = Hash1(i + float3(1, 1, 0));
                float n001 = Hash1(i + float3(0, 0, 1));
                float n101 = Hash1(i + float3(1, 0, 1));
                float n011 = Hash1(i + float3(0, 1, 1));
                float n111 = Hash1(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            VoronoiResult Voronoi(float3 p)
            {
                float3 cell = floor(p);
                float3 local = frac(p);

                float d1 = 8.0, d2 = 8.0;
                float3 id1 = 0, id2 = 0;

                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float3 offset = float3(x, y, z);
                    float3 id = cell + offset;
                    float3 diff = offset + Hash3(id) - local;
                    float d = dot(diff, diff);
                    if (d < d1) { d2 = d1; id2 = id1; d1 = d; id1 = id; }
                    else if (d < d2) { d2 = d; id2 = id; }
                }

                VoronoiResult result;
                result.edge = sqrt(d2) - sqrt(d1);
                // 두 셀 id의 합은 순서와 무관하므로 같은 경계 선은 항상 같은 값을 갖는다.
                result.lineHash = Hash1(id1 + id2);
                result.cellId = id1;
                return result;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                // 유니티 기본 구는 반지름 0.5이므로 2를 곱해 단위 구로 맞춘다.
                float3 unitPos = input.positionOS.xyz * 2.0;
                float3 positionOS = input.positionOS.xyz;

                if (_BrokenAmount > 0.001)
                {
                    // 판마다 높이를 어긋나게 하고 저주파 노이즈로 실루엣을 일그러뜨린다. 섞일수록 가라앉는다.
                    VoronoiResult v = Voronoi(unitPos * _CrackScale);
                    float plateHeight = (Hash1(v.cellId + 3.7) - 0.5) * 2.0 * _HeightAmp;
                    float silhouette = (Noise3(unitPos * 2.0 + 11.3) - 0.5) * 2.0 * _SilhouetteAmp;
                    positionOS += input.normalOS * (plateHeight + silhouette) * _BrokenAmount * (1.0 - _MixAmount * 0.7);
                }

                output.positionCS = TransformObjectToHClip(positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.unitPos = unitPos;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);

                // 섞일수록 칸이 잘게 쪼개져 알갱이가 된다.
                float scale = _CrackScale * lerp(1.0, _GrainScaleMul, _MixAmount);

                half crack = 0;
                half grainTone = 1;
                if (_CrackAmount > 0.001 || _BrokenAmount > 0.001)
                {
                    VoronoiResult v = Voronoi(input.unitPos * scale);

                    // 경계 선마다 나타나는 시점이 달라서 amount가 커질수록 선이 하나씩 늘어난다.
                    float visible = saturate((_CrackAmount - v.lineHash) * 8.0);
                    float width = _CrackWidth * lerp(0.4, 1.0, _CrackAmount) * sqrt(scale / _CrackScale);
                    crack = (1.0 - smoothstep(0.0, width, v.edge)) * visible;

                    if (_BrokenAmount > 0.001)
                    {
                        // 칸마다 법선을 다른 방향으로 기울여 판이 어긋난 것처럼 보이게 한다.
                        float3 tilt = Hash3(v.cellId + 7.1) - 0.5;
                        tilt -= dot(tilt, normal) * normal;
                        normal = normalize(normal + tilt * 2.0 * _TiltAmount * _BrokenAmount * (1.0 + _MixAmount));
                        // 알갱이마다 밝기가 조금씩 다르다.
                        grainTone = lerp(1.0, 0.8 + 0.4 * Hash1(v.cellId + 1.3), _MixAmount);
                    }
                }

                Light mainLight = GetMainLight();
                half3 lighting = mainLight.color * saturate(dot(normal, mainLight.direction)) * 0.8
                               + SampleSH(normal);

                half3 albedo = lerp(_BaseColor.rgb, _CrackColor.rgb, crack) * grainTone;
                return half4(albedo * lighting, 1);
            }
            ENDHLSL
        }
    }
}
