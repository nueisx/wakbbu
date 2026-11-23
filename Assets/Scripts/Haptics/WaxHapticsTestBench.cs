using System;
using UnityEngine;
using WeArt.Components;
using WeArt.Core;

namespace WaxHaptics
{
    /// <summary>
    /// 장갑 테스트용 키보드 조작판 (장갑 튜닝용, 빈 오브젝트에 붙여서 Play).
    /// G 누르고 있기 = 쥐기 → 0.85 넘으면 자동 파삭. 화면 왼쪽 위에 키 설명이 뜬다.
    /// </summary>
    [RequireComponent(typeof(WaxHapticsPlayer))]
    public class WaxHapticsTestBench : MonoBehaviour
    {
        public float gripSpeed = 0.6f;      // 초당 grip 증가량
        public float breakAtGrip = 0.85f;   // 팀 상태머신의 Broken 기준과 같게
        public float kneadInterval = 0.12f; // K 누르고 있을 때 바삭 간격

        private WaxHapticsPlayer _p;
        private float _grip;
        private bool _broken;
        private float _strength = 0.6f;
        private bool _audition;
        private int _auditionIdx;
        private float _nextKnead;
        private string _last = "-";
        private TextureType[] _all;

        private void Awake()
        {
            _p = GetComponent<WaxHapticsPlayer>();
            _all = (TextureType[])Enum.GetValues(typeof(TextureType));
        }

        private void Update()
        {
            // 쥐기 시뮬레이션
            if (!_broken)
            {
                _grip += (Input.GetKey(KeyCode.G) ? gripSpeed : -gripSpeed * 1.5f) * Time.deltaTime;
                _grip = Mathf.Clamp01(_grip);
                _p.SetPressByGrip(_grip);
                if (_grip >= breakAtGrip) { _broken = true; _p.PlayBreak(); _last = "BREAK (auto)"; }
            }

            if (Input.GetKeyDown(KeyCode.B)) { _p.PlayBreak(); _last = "BREAK"; }
            if (Input.GetKeyDown(KeyCode.Alpha1)) Crunch(HapticFinger.Thumb);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Crunch(HapticFinger.Index);
            if (Input.GetKeyDown(KeyCode.Alpha3)) Crunch(HapticFinger.Middle);

            if (Input.GetKey(KeyCode.K) && Time.time >= _nextKnead)
            {
                _nextKnead = Time.time + kneadInterval * UnityEngine.Random.Range(0.6f, 1.4f);
                Crunch((HapticFinger)UnityEngine.Random.Range(0, 3), true);
            }

            if (Input.GetKeyDown(KeyCode.Q)) _strength = Mathf.Clamp01(_strength - 0.1f);
            if (Input.GetKeyDown(KeyCode.E)) _strength = Mathf.Clamp01(_strength + 0.1f);

            // 진동 종류 둘러보기
            if (Input.GetKeyDown(KeyCode.RightArrow)) Step(1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) Step(-1);
            if (Input.GetKeyDown(KeyCode.T))
            {
                _audition = !_audition;
                if (_audition) _p.PlayContinuous(_all[_auditionIdx], _strength * 100f);
                else _p.StopTexture();
            }
            if (Input.GetKeyDown(KeyCode.C)) { _p.crunchTexture = _all[_auditionIdx]; _last = "crunch <- " + _all[_auditionIdx]; }
            if (Input.GetKeyDown(KeyCode.V)) { _p.breakTexture = _all[_auditionIdx]; _last = "break <- " + _all[_auditionIdx]; }

            if (Input.GetKeyDown(KeyCode.M)) { _p.SetMix(!_p.MixOn); _last = "mix " + (_p.MixOn ? "ON" : "OFF"); }
            if (Input.GetKeyDown(KeyCode.R)) { _grip = 0; _broken = false; _audition = false; _p.StopAll(); _last = "RESET"; }
            if (Input.GetKeyDown(KeyCode.X)) { _audition = false; _p.StopAll(); _last = "STOP"; }
        }

        private void Crunch(HapticFinger f, bool randomize = false)
        {
            float s = randomize ? Mathf.Clamp01(_strength * UnityEngine.Random.Range(0.6f, 1.2f)) : _strength;
            _p.PlayCrunch(f, s);
            _last = $"crunch {f} {s:0.00}";
        }

        private void Step(int dir)
        {
            _auditionIdx = (_auditionIdx + dir + _all.Length) % _all.Length;
            if (_audition) _p.PlayContinuous(_all[_auditionIdx], _strength * 100f);
        }

        private void OnGUI()
        {
            var c = WeArtController.Instance;
            string conn = c == null ? "no WeArtController" :
                $"connected={c.Client != null && c.Client.IsConnected}  calibrated={c.IsCalibrated}";
            GUI.Box(new Rect(10, 10, 430, 300), "");
            GUILayout.BeginArea(new Rect(20, 15, 410, 290));
            GUILayout.Label(conn);
            GUILayout.Label($"grip {_grip:0.00}  force {_p.CurrentForce:0.00}  broken={_broken}  strength {_strength:0.0}");
            GUILayout.Label($"audition [{_auditionIdx}] {_all[_auditionIdx]}  playing={_audition}");
            GUILayout.Label($"break={_p.breakTexture}  crunch={_p.crunchTexture}  mix={_p.MixOn}");
            GUILayout.Label("last: " + _last);
            GUILayout.Space(6);
            GUILayout.Label("G hold: squeeze (auto BREAK at 0.85)   B: break");
            GUILayout.Label("1/2/3: crunch thumb/index/middle   K hold: knead");
            GUILayout.Label("Q/E: strength -/+   <-/->: texture   T: play/stop it");
            GUILayout.Label("C: use as crunch   V: use as break   M: mix (clay)");
            GUILayout.Label("R: reset   X: stop all");
            GUILayout.EndArea();
        }
    }
}
