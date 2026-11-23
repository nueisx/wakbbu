using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using WaxBall;

namespace Interaction
{
    /// <summary>
    /// 손가락 끝 원본 위치와 필터 결과를 CSV로 기록하는 테스트 도구. 떨림 크기와 튀는 빈도를 숫자로 보고 필터 수치를 정하는 용도다.
    /// 오른손 컨트롤러 B 버튼으로 시작하면 준비 시간(컨트롤러를 내려놓는 시간) 뒤에 정해진 동작 순서(PhaseList)가 자동으로 진행되고,
    /// 지금 해야 할 동작과 남은 시간이 디버그 텍스트에 나온다. 끝나면 파일을 닫는다. 다시 시작하려면 B 버튼을 다시 누른다.
    /// 파일은 Application.persistentDataPath에 fingertips_날짜_시간.csv로 저장된다(Quest에서는 /sdcard/Android/data/패키지명/files/).
    /// 화면 UI 문자열(동작 이름)은 영어로 쓴다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FingertipRecorder : MonoBehaviour
    {
        [SerializeField] private FilteredFingertips filtered;
        [SerializeField] private QuestHandFingertips hand;

        [Tooltip("켜면 B 버튼으로 기록을 시작할 수 있다. 끄면 StartRecording()을 직접 불러야 한다.")]
        [SerializeField] private bool startWithButton = true;

        // 동작 순서. 0번은 준비 시간이다(CSV의 phase 열에 이 번호가 들어간다). 이름에 쉼표를 넣지 않는다.
        private static readonly (string name, float seconds)[] PhaseList =
        {
            ("Ready: put controller down, show hand", 8f),
            ("Open hand, hold still", 10f),
            ("Hold ball lightly, still", 10f),
            ("Squeeze firmly, hold still", 10f),
            ("Squeeze and release SLOWLY (2 s each)", 16f),
            ("Squeeze and release FAST (1 s each)", 10f),
            ("Hold ball, move and turn hand", 10f),
            ("Hold ball, turn palm away from headset", 10f),
        };

        private enum RecorderState { Waiting, Running, Done }

        private RecorderState state = RecorderState.Waiting;
        private StreamWriter writer;
        private readonly StringBuilder line = new StringBuilder(512);
        private float startTime;
        private float lastFlushTime;
        private string fileName = "";
        private int phase;
        private float phaseEnd;

        /// <summary>디버그 텍스트에 한 줄로 보여 줄 현재 상태.</summary>
        public string StatusText
        {
            get
            {
                switch (state)
                {
                    case RecorderState.Running:
                        return $"Rec: {phase}/{PhaseList.Length - 1} {PhaseList[phase].name} {Mathf.Max(0f, phaseEnd - Time.time):0}s";
                    case RecorderState.Done:
                        return "Rec: done " + fileName;
                    default:
                        return "Rec: press B to start";
                }
            }
        }

        private void Update()
        {
            if (startWithButton && state != RecorderState.Running && OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
                StartRecording();
        }

        private void LateUpdate()
        {
            if (state != RecorderState.Running) return;

            if (Time.time >= phaseEnd)
            {
                phase++;
                if (phase >= PhaseList.Length)
                {
                    StopRecording();
                    return;
                }
                phaseEnd += PhaseList[phase].seconds;
            }

            WriteRow();
            if (Time.time - lastFlushTime > 1f)
            {
                writer.Flush();
                lastFlushTime = Time.time;
            }
        }

        public void StartRecording()
        {
            if (state == RecorderState.Running || filtered == null) return;

            fileName = "fingertips_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv";
            writer = new StreamWriter(Path.Combine(Application.persistentDataPath, fileName), false, new UTF8Encoding(false));
            writer.WriteLine(Header());

            startTime = Time.time;
            lastFlushTime = Time.time;
            phase = 0;
            phaseEnd = Time.time + PhaseList[0].seconds;
            state = RecorderState.Running;
        }

        private void StopRecording()
        {
            CloseWriter();
            state = RecorderState.Done;
        }

        private void CloseWriter()
        {
            if (writer == null) return;
            writer.Flush();
            writer.Dispose();
            writer = null;
        }

        private void OnDisable()
        {
            if (state == RecorderState.Running) StopRecording();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && writer != null) writer.Flush();
        }

        private static string Header()
        {
            var sb = new StringBuilder("time,frame,phase,phaseName,handTracked,handHigh");
            foreach (WaxFinger finger in Enum.GetValues(typeof(WaxFinger)))
            {
                string f = finger.ToString();
                sb.Append($",{f}_tracked,{f}_high,{f}_rawX,{f}_rawY,{f}_rawZ,{f}_filtX,{f}_filtY,{f}_filtZ,{f}_rejected");
            }
            return sb.ToString();
        }

        private void WriteRow()
        {
            line.Clear();
            line.Append((Time.time - startTime).ToString("F4", CultureInfo.InvariantCulture)).Append(',');
            line.Append(Time.frameCount).Append(',');
            line.Append(phase).Append(',');
            line.Append(PhaseList[phase].name).Append(',');
            line.Append(hand != null && hand.HandTracked ? 1 : 0).Append(',');
            line.Append(hand != null && hand.HandHighConfidence ? 1 : 0);

            foreach (WaxFinger finger in Enum.GetValues(typeof(WaxFinger)))
            {
                FingertipSample raw = filtered.GetRaw(finger);
                filtered.TryGetFingertip(finger, out FingertipSample result);
                line.Append(',').Append(raw.tracked ? 1 : 0);
                line.Append(',').Append(raw.highConfidence ? 1 : 0);
                AppendVector(raw.tracked, raw.worldPosition);
                AppendVector(result.tracked, result.worldPosition);
                line.Append(',').Append(filtered.WasRejected(finger) ? 1 : 0);
            }
            writer.WriteLine(line.ToString());
        }

        // 위치(m)를 소수점 5자리로 쓴다. 추적이 안 될 때는 빈 칸이다.
        private void AppendVector(bool valid, Vector3 v)
        {
            if (!valid)
            {
                line.Append(",,,");
                return;
            }
            line.Append(',').Append(v.x.ToString("F5", CultureInfo.InvariantCulture));
            line.Append(',').Append(v.y.ToString("F5", CultureInfo.InvariantCulture));
            line.Append(',').Append(v.z.ToString("F5", CultureInfo.InvariantCulture));
        }
    }
}
