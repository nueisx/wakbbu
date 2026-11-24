using System.Collections;
using UnityEngine;
using WeArt.Components;
using WeArt.Core;
using Texture = WeArt.Core.Texture;

namespace WaxHaptics
{
    public enum HapticFinger { Thumb = 0, Index = 1, Middle = 2 }

    /// <summary>
    /// 왁뿌볼 햅틱 출력 담당. WEART SDK 호출은 전부 이 파일 안에서만 한다.
    /// 상태 머신(WaxBall)은 모르고, "쥐는 힘 / 파삭 / 바삭 / 점토 / 정지"만 제공한다.
    /// </summary>
    public class WaxHapticsPlayer : MonoBehaviour
    {
        [Header("장갑")]
        [SerializeField] private HandSideFlags handSide = HandSideFlags.Right;
        [Tooltip("비워 두면 실행할 때 손가락별 WeArtHapticObject를 자동으로 만든다")]
        [SerializeField] private WeArtHapticObject thumb;
        [SerializeField] private WeArtHapticObject index;
        [SerializeField] private WeArtHapticObject middle;

        [Header("쥐기: 껍질 저항 (Force)")]
        [Range(0f, 1f)] public float shellMaxForce = 0.8f;
        [Range(0f, 1f)] public float pressStartGrip = 0.15f;

        [Header("파삭: 깨지는 순간")]
        public TextureType breakTexture = TextureType.Click;
        [Range(0f, 100f)] public float breakVolume = 100f;
        [Range(0.02f, 0.5f)] public float breakBurstSec = 0.08f;

        [Header("바삭: 깨진 껍질 주무를 때")]
        public TextureType crunchTexture = TextureType.CrushedRock;
        [Range(0f, 100f)] public float crunchMinVolume = 20f;
        [Range(0f, 100f)] public float crunchMaxVolume = 100f;
        [Range(0.02f, 0.5f)] public float crunchBurstSec = 0.06f;

        [Header("다 섞인 뒤: 점토")]
        public TextureType mixTexture = TextureType.ProfiledRubber;
        [Range(0f, 100f)] public float mixVolume = 30f;

        public float CurrentForce { get; private set; }
        public bool MixOn { get; private set; }

        private WeArtHapticObject[] _fingers;
        private readonly Coroutine[] _bursts = new Coroutine[3];

        private void Awake()
        {
            if (!thumb) thumb = CreateFinger("Haptic_Thumb", ActuationPointFlags.Thumb);
            if (!index) index = CreateFinger("Haptic_Index", ActuationPointFlags.Index);
            if (!middle) middle = CreateFinger("Haptic_Middle", ActuationPointFlags.Middle);
            _fingers = new[] { thumb, index, middle };
        }

        private WeArtHapticObject CreateFinger(string objName, ActuationPointFlags point)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(transform, false);
            var h = go.AddComponent<WeArtHapticObject>();
            h.HandSides = handSide;
            h.ActuationPoints = point;
            return h;
        }

        // ---------- 쥐기 ----------

        /// <summary>grip(0~1)에 맞춰 세 손가락 모두 껍질 저항을 준다.</summary>
        public void SetPressByGrip(float grip)
        {
            float t = grip < pressStartGrip ? 0f : Mathf.InverseLerp(pressStartGrip, 1f, grip);
            SetForceAll(t * shellMaxForce);
        }

        public void SetForceAll(float value)
        {
            value = Mathf.Round(Mathf.Clamp01(value) * 50f) / 50f; // 0.02 단위로 끊어서 메시지 폭주 방지
            CurrentForce = value;
            foreach (var h in _fingers)
            {
                h.EnablingActuating(true);
                var f = h.Force;
                f.Value = value;
                f.Active = value > 0f;
                h.Force = f;
            }
        }

        // ---------- 파삭 / 바삭 ----------

        /// <summary>깨지는 순간: 저항을 확 빼고 세 손가락에 짧은 클릭.</summary>
        public void PlayBreak()
        {
            SetForceAll(0f);
            for (int i = 0; i < 3; i++) StartBurst(i, breakTexture, breakVolume, breakBurstSec);
        }

        /// <summary>바삭: 해당 손가락만 짧게. strength(0~1)가 세기.</summary>
        public void PlayCrunch(HapticFinger finger, float strength)
        {
            float vol = Mathf.Lerp(crunchMinVolume, crunchMaxVolume, Mathf.Clamp01(strength));
            StartBurst((int)finger, crunchTexture, vol, crunchBurstSec);
        }

        private void StartBurst(int i, TextureType type, float volume, float sec)
        {
            if (_bursts[i] != null) StopCoroutine(_bursts[i]);
            if (_fingers[i].Texture.Active) SetTexture(_fingers[i], false, type, volume); // 다시 "톡" 시작되게 끊었다 켬
            _bursts[i] = StartCoroutine(Burst(i, type, volume, sec));
        }

        private IEnumerator Burst(int i, TextureType type, float volume, float sec)
        {
            SetTexture(_fingers[i], true, type, volume);
            yield return new WaitForSeconds(sec);
            if (MixOn) SetTexture(_fingers[i], true, mixTexture, mixVolume);
            else SetTexture(_fingers[i], false, type, volume);
            _bursts[i] = null;
        }

        // ---------- 연속 진동 ----------

        /// <summary>다 섞인 뒤 말랑한 점토 느낌을 켜고 끈다.</summary>
        public void SetMix(bool on)
        {
            MixOn = on;
            foreach (var h in _fingers) SetTexture(h, on, mixTexture, mixVolume);
        }

        /// <summary>테스트용: 원하는 진동을 세 손가락에 계속 튼다.</summary>
        public void PlayContinuous(TextureType type, float volume)
        {
            foreach (var h in _fingers) SetTexture(h, true, type, volume);
        }

        public void StopTexture()
        {
            MixOn = false;
            foreach (var h in _fingers) SetTexture(h, false, h.Texture.TextureType, h.Texture.Volume);
        }

        /// <summary>모든 출력 정지 (리셋 때).</summary>
        public void StopAll()
        {
            for (int i = 0; i < 3; i++)
            {
                if (_bursts[i] != null) StopCoroutine(_bursts[i]);
                _bursts[i] = null;
            }
            if (_fingers == null) return;
            SetForceAll(0f);
            StopTexture();
        }

        private static void SetTexture(WeArtHapticObject h, bool on, TextureType type, float volume)
        {
            h.EnablingActuating(true);
            var t = h.Texture;
            t.TextureType = type;
            t.Volume = volume;
            t.ForcedVelocity = true; // 손을 안 움직여도 진동이 나게
            t.Active = on;
            h.Texture = t;
        }

        private void OnDisable()
        {
            if (WeArtController.Instance != null) StopAll();
        }
    }
}
