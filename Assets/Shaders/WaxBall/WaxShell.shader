// 왁뿌볼 껍질용 URP 셰이더. 기준은 docs/references/README.md의 실제 왁뿌볼 사진이다.
// 구조: 투명 고무 케이스 > 색깔 있는 왁스 껍질(납작한 판) > 흰 점토. 이 셰이더는 케이스 아래로 보이는 모습을 그린다.
// - Idle/Pressed: 매끈한 단색 공. 균열 없음.
// - Cracked(_CrackAmount): 판은 그대로이고 삐뚤빼뚤한 균열 두 줄을 따라 흰 점토가 불룩하게 비집고 나온다.
//   균열의 시작점, 방향, 길이, 휘어짐은 C#(WaxBallShellVisual)이 리셋마다 무작위로 넣는다(_Crack1A/B, _Crack2A/B).
// - Broken(_BrokenAmount, _Fragment): 큰 판 몇 개로 갈라지고 틈은 흰 점토 색이다.
//   _Fragment가 커질수록 큰 판 안에 작은 판 경계가 하나씩 생겨 더 잘게 쪼개진다.
// - Mixed(_MixAmount = MixProgress): 작은 판 중 일부(_PieceFraction)만 가장자리가 줄어든 작은 조각으로 남고 나머지는 점토에 묻힌다.
//   흰색이 표면 대부분을 덮고(mixed), 끝에는 표면의 약 20%에 작고 납작한 초록 조각이 군데군데 남는다(mixed-02).
//   점토는 고무에 눌려 매끈하고, 실루엣과 표면에 노이즈 요철은 없다. grip에 따라 손가락 쪽만 둥글게 들어간다(_DentDir).
// - 투명 고무 케이스(docs/references/casing.png)는 가장자리 프레넬과 실루엣 가장자리의 투명한 테두리로만 표현한다.
//   하이라이트(창 모양 반사, 광원 반사)와 표면 물결, 환경 반사는 부자연스럽거나 AR 배경(실제 방)과 맞지 않아 쓰지 않는다.
// 균열과 틈 선은 화면에서 1px 아래로 가늘어지지 않게 보정한다.
// Quest 성능을 위해 단일 Forward 패스, 텍스처 없이 절차적으로만 계산한다.
Shader "WaxBall/WaxShell"
{
    Properties
    {
        _ShellColor("Shell Color (왁스 껍질)", Color) = (0.55, 0.8, 0.15, 1)
        _ClayColor("Clay Color (점토)", Color) = (0.98, 0.98, 0.96, 1)

        [Header(Crack)]
        _CrackAmount("Crack Amount", Range(0, 1)) = 0
        _CrackLength("Crack Length (한쪽 끝까지 호의 비율)", Range(0.1, 0.8)) = 0.35
        _GapWidth("Gap Width (가장 넓을 때 반폭, 구 반지름 기준)", Range(0.01, 0.3)) = 0.1
        _Crack1A("Crack 1 Start(xyz) / Length", Vector) = (-0.25, 0.2, -1, 1)
        _Crack1B("Crack 1 Angle / Warp / Seed / Width", Vector) = (0.4, 0.3, 1.7, 1)
        _Crack2A("Crack 2 Start(xyz) / Length", Vector) = (0.45, -0.35, -0.85, 0.6)
        _Crack2B("Crack 2 Angle / Warp / Seed / Width", Vector) = (2.2, 0.2, 5.3, 0.8)

        [Header(Broken)]
        _BrokenAmount("Broken Amount", Range(0, 1)) = 0
        _Fragment("Fragment (판이 쪼개진 정도)", Range(0, 1)) = 0
        _PlateScale("Plate Scale (큰 판)", Range(1, 6)) = 1.6
        _FineScaleMul("Fine Scale Multiplier (작은 판)", Range(1.5, 4)) = 2.2
        _PlateGapWidth("Plate Gap Width (틈 전체 폭, 구 반지름 기준)", Range(0.02, 0.4)) = 0.2
        _DentDir("Dent Direction (오브젝트 공간)", Vector) = (0, 0, -1, 0)
        _DentAmount("Dent Amount", Range(0, 1)) = 0

        [Header(Mixed)]
        _MixAmount("Mix Amount (MixProgress)", Range(0, 1)) = 0
        _PieceFraction("Piece Fraction (Mixed에서 남는 작은 판의 비율)", Range(0.05, 0.9)) = 0.45
        _PieceInset("Piece Inset (남는 조각이 가장자리에서 줄어든 폭, 클수록 작은 조각)", Range(0.005, 0.08)) = 0.03

        [Header(Edge and Clay)]
        _RimWidth("Rim Width (판 가장자리 밝은 테두리)", Range(0, 0.1)) = 0.03
        _RimBrightness("Rim Brightness", Range(0, 1)) = 0.45
        _BulgeStrength("Clay Bulge (틈 가장자리 음영, 표면 요철처럼 보이므로 작게)", Range(0, 1.5)) = 0.1
        _MinPixelWidth("Min Pixel Width (선 최소 굵기, px)", Range(0, 3)) = 1

        [Header(Casing)]
        _FresnelPower("Fresnel Power", Range(1, 8)) = 3
        _FresnelStrength("Fresnel Strength", Range(0, 0.6)) = 0.25
        _CasingEdge("Casing Edge (실루엣 가장자리 투명 테두리)", Range(0, 1)) = 0.5
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
                half4 _ShellColor;
                half4 _ClayColor;
                float _CrackAmount;
                float _CrackLength;
                float _GapWidth;
                float4 _Crack1A;
                float4 _Crack1B;
                float4 _Crack2A;
                float4 _Crack2B;
                float _BrokenAmount;
                float _Fragment;
                float _PlateScale;
                float _FineScaleMul;
                float _PlateGapWidth;
                float4 _DentDir;
                float _DentAmount;
                float _MixAmount;
                float _PieceFraction;
                float _PieceInset;
                float _RimWidth;
                float _RimBrightness;
                float _BulgeStrength;
                float _MinPixelWidth;
                float _FresnelPower;
                float _FresnelStrength;
                float _CasingEdge;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 unitPos : TEXCOORD1;
            };

            // 틈 한 줄(또는 한 경계)이 만드는 결과
            struct Gap
            {
                float cover;     // 흰 점토가 보이는 정도
                float rim;       // 판 가장자리 밝은 테두리 정도
                float3 shift;    // 점토가 불룩해 보이도록 법선을 기울이는 방향(오브젝트 공간)
            };

            struct VoronoiResult
            {
                float edge;      // 가장 가까운 두 셀 경계까지의 거리(셀 단위)
                float lineHash;  // 경계 선의 무작위 값(같은 선은 항상 같은 값)
                float cellHash;  // 가장 가까운 셀의 무작위 값(Mixed에서 어떤 판이 남는지 정한다)
                float cellHash2; // 두 번째 무작위 값(조각 크기, 사라지는 시점)
                float3 grad;     // 셀1 → 셀2 방향
            };

            float Hash1(float3 p)
            {
                return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
            }

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

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
                float3 v1 = 0, v2 = 0;

                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float3 offset = float3(x, y, z);
                    float3 id = cell + offset;
                    float3 diff = offset + Hash3(id) - local;
                    float d = dot(diff, diff);
                    if (d < d1) { d2 = d1; id2 = id1; v2 = v1; d1 = d; id1 = id; v1 = diff; }
                    else if (d < d2) { d2 = d; id2 = id; v2 = diff; }
                }

                VoronoiResult result;
                result.edge = sqrt(d2) - sqrt(d1);
                // 두 셀 id의 합은 순서와 무관하므로 같은 경계 선은 항상 같은 값을 갖는다.
                result.lineHash = Hash1(id1 + id2);
                result.cellHash = Hash1(id1 * 1.37 + 11.1);
                result.cellHash2 = Hash1(id1 * 2.71 + 5.3);
                result.grad = normalize(v2 - v1 + 1e-5);
                return result;
            }

            // d: 틈 중심선까지의 거리, px: 화면 1px에 해당하는 거리, w: 틈 반폭,
            // dirAway: 구 표면에서 틈 중심으로부터 멀어지는 방향(점토가 불룩하게 보이는 법선 기울기용)
            Gap MakeGap(float d, float px, float w, float inside, float rimW, float3 dirAway)
            {
                w = max(w, px * _MinPixelWidth * 0.5);

                Gap gap;
                gap.cover = (1.0 - smoothstep(w - px * 0.5, w + px * 0.5, d)) * inside;
                gap.rim = (1.0 - smoothstep(w, w + rimW + px, d)) * inside * (1.0 - gap.cover);
                gap.shift = dirAway * saturate(d / max(w, 1e-4)) * gap.cover;
                return gap;
            }

            // 구 위의 균열 한 줄. 시작점(A.xyz)에서 양쪽으로 자라고, 선은 노이즈로 삐뚤빼뚤하며 폭도 들쭉날쭉하다.
            // A.w: 길이 배율, B.x: 방향 각도, B.y: 휘어짐, B.z: 시드, B.w: 폭 배율
            Gap Crack(float3 q, float4 A, float4 B, float amount)
            {
                float3 c = normalize(A.xyz);
                float3 up = abs(c.y) > 0.95 ? float3(1, 0, 0) : float3(0, 1, 0);
                float3 e1 = normalize(cross(c, up));
                float3 e2 = cross(c, e1);
                float3 t = cos(B.x) * e1 + sin(B.x) * e2;
                float3 n = cross(c, t);

                // 시작점에서 균열을 따라 떨어진 정도(0 = 시작점, 1 = 반대편). 양쪽 길이는 서로 다르다.
                float angle = atan2(dot(q, t), dot(q, c));
                float side = angle >= 0.0 ? 1.0 : lerp(0.6, 1.1, frac(B.z * 0.37));
                float along = abs(angle) / 3.14159265;
                float halfLength = max(amount * _CrackLength * A.w * side, 0.001);
                float taper = pow(saturate(1.0 - along / halfLength), 0.6);

                // 큰 휘어짐 + 중간 굴곡. 미분용 값(hBase)에는 잔 떨림을 넣지 않는다.
                float hBase = dot(q, n)
                            + (Noise3(q * 1.8 + B.z) - 0.5) * B.y
                            + (Noise3(q * 4.5 + B.z * 1.3) - 0.5) * B.y * 0.4;
                float px = fwidth(hBase);
                // 가장자리를 삐뚤빼뚤하게 만드는 잔 떨림
                float d = abs(hBase + (Noise3(q * 11.0 + B.z * 2.1) - 0.5) * 0.05);

                float widthVariation = lerp(0.45, 1.7, Noise3(q * 5.0 + B.z * 3.7));
                float w = _GapWidth * lerp(0.35, 1.0, saturate(amount)) * taper * widthVariation * B.w;

                float inside = step(0.001, amount) * step(0.001, taper);
                float3 away = (n - dot(n, q) * q) * (hBase >= 0.0 ? 1.0 : -1.0);
                return MakeGap(d, px, w, inside, _RimWidth * saturate(taper * 3.0), normalize(away + 1e-5));
            }

            // 판 사이 틈(Voronoi 경계). gate: 이 경계가 얼마나 나타났는지(0~1), 클수록 넓다.
            // isFine: 작은 판 층이면 1. Mixed에서 작은 판 중 일부(_PieceFraction)만 작은 조각으로 남고 나머지는 점토에 묻혀 사라진다.
            // 큰 판 층(isFine = 0)의 틈은 섞일수록 가늘어져서 남은 조각을 자르지 않는다.
            Gap PlateGap(float3 q, VoronoiResult v, float scale, float gate, float widthScale, float seed, float isFine)
            {
                // 셀 단위 거리를 구 반지름 단위 거리로 바꾼다(경계까지 거리 ≈ edge의 절반).
                float dBase = 0.5 * v.edge / scale;
                float px = fwidth(dBase);
                float d = dBase + (Noise3(q * 9.0 + seed) - 0.5) * 0.03;

                float widthVariation = lerp(0.5, 1.5, Noise3(q * 4.0 + seed * 1.7));
                float wBase = _PlateGapWidth * 0.5 * widthScale * gate * widthVariation;
                float w = wBase * (1.0 - _MixAmount * lerp(0.85, 1.0, isFine));
                float wMix = 0.0;

                if (isFine > 0.5)
                {
                    // 남는 판은 가장자리에서 조금씩 줄어 작은 조각이 되고, 나머지는 셀 크기를 넘을 때까지 줄어 사라진다.
                    float survive = step(1.0 - _PieceFraction, v.cellHash);
                    float inset = _PieceInset * lerp(0.6, 1.4, v.cellHash2);
                    float vanish = (0.7 / scale) / (0.3 + 0.65 * v.cellHash2);
                    wMix = _MixAmount * lerp(vanish, inset, survive);
                    w = max(w, wMix);
                }

                float inside = step(0.001, gate + wMix) * step(0.001, _BrokenAmount);
                float rimW = _RimWidth * lerp(1.0, lerp(1.0, 0.35, _MixAmount), isFine);
                float3 away = -(v.grad - dot(v.grad, q) * q);
                return MakeGap(d, px, w, inside, rimW, normalize(away + 1e-5));
            }

            Varyings vert(Attributes input)
            {
                float3 positionOS = input.positionOS.xyz;
                float3 normalOS = normalize(input.normalOS);
                // 유니티 기본 구는 반지름 0.5이므로 2를 곱해 단위 구로 맞춘다.
                float3 unitPos = positionOS * 2.0;

                if (_BrokenAmount > 0.001)
                {
                    // 고무 케이스가 감싸고 있어 실루엣은 매끈한 구에 가깝다. grip에 따라 손가락 쪽만 둥글게 들어간다.
                    float dentMask = smoothstep(0.3, 1.0, dot(normalOS, normalize(_DentDir.xyz)));
                    positionOS -= normalOS * dentMask * _DentAmount * 0.17;
                }

                Varyings output;
                output.positionWS = TransformObjectToWorld(positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.unitPos = unitPos;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 q = normalize(input.unitPos);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // 균열 두 줄. 두 번째는 늦게 나타난다.
                float amount2 = saturate((_CrackAmount - 0.3) / 0.7) * 0.75;
                Gap crack1 = Crack(q, _Crack1A, _Crack1B, _CrackAmount);
                Gap crack2 = Crack(q, _Crack2A, _Crack2B, amount2);

                // Broken: 큰 판 사이 틈 + 큰 판 안에 하나씩 생기는 작은 판 경계
                float fineScale = _PlateScale * _FineScaleMul;
                VoronoiResult plateCells = Voronoi(q * _PlateScale);
                VoronoiResult fineCells = Voronoi(q * fineScale);
                float fineGate = saturate((_Fragment - fineCells.lineHash * 0.9) * 5.0) * _BrokenAmount;
                Gap plate0 = PlateGap(q, plateCells, _PlateScale, _BrokenAmount, 1.0, 3.1, 0.0);
                Gap plate1 = PlateGap(q, fineCells, fineScale, fineGate, 0.6, 8.9, 1.0);

                float cover = max(max(crack1.cover, crack2.cover), max(plate0.cover, plate1.cover));
                float rim = max(max(crack1.rim, crack2.rim), max(plate0.rim, plate1.rim));
                float3 shift = crack1.shift + crack2.shift + plate0.shift + plate1.shift;

                // 손가락 쪽 오목한 면은 법선도 중심 쪽으로 살짝 기울인다.
                float3 dentDir = normalize(_DentDir.xyz);
                float dentMask = smoothstep(0.3, 1.0, dot(q, dentDir));
                float3 dentTilt = (dentDir - dot(dentDir, q) * q) * dentMask * (1.0 - dentMask) * 4.0 * _DentAmount * 0.6;

                // 점토가 틈에서 불룩 튀어나온 느낌: 틈 안쪽 법선을 판 쪽으로 기울인다.
                float3 normalOS = normalize(q + shift * _BulgeStrength + dentTilt);
                float3 normal = normalize(TransformObjectToWorldNormal(normalOS));

                half3 rimColor = lerp(_ShellColor.rgb, half3(1, 1, 1), _RimBrightness);
                half3 albedo = lerp(_ShellColor.rgb, rimColor, rim);
                albedo = lerp(albedo, _ClayColor.rgb, cover);

                // 판은 광원 방향에 따라 음영이 지지만, 점토는 광원 색(기본 조명은 따뜻한 크림색)과 무관하게
                // 깨끗한 흰색으로(회색 음영 없이) 비춘다.
                Light mainLight = GetMainLight();
                half3 diffuse = mainLight.color * saturate(dot(normal, mainLight.direction)) * 0.8
                              + SampleSH(normal);
                half wrap = dot(normal, mainLight.direction) * 0.5 + 0.5;
                half3 softDiffuse = half3(1, 1, 1) * lerp(0.9, 1.0, wrap);
                diffuse = lerp(diffuse, softDiffuse, cover);

                // 가장자리: 고무 두께가 비치는 밝고 투명한 테두리와 가는 가장자리 선(하이라이트는 없다)
                half grazing = 1.0 - saturate(dot(normal, viewDir));
                half3 edgeTint = half3(0.9, 0.96, 1.0);
                half3 edge = edgeTint * (pow(grazing, _FresnelPower) * _FresnelStrength
                                         + smoothstep(0.78, 0.97, grazing) * _CasingEdge * 0.4);

                return half4(albedo * diffuse + edge, 1);
            }
            ENDHLSL
        }
    }
}
