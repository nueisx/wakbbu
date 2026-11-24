namespace Interaction
{
    /// <summary>
    /// "누르고 있기" 진행 계산(순수 클래스). 누르는 동안 시간이 쌓여 Progress가 0에서 1로 차고,
    /// 다 차면 한 번만 Triggered를 알린다. 손을 떼기 전에는 다시 발생하지 않고, 떼면 처음부터 다시 시작한다.
    /// </summary>
    public sealed class PinchHold
    {
        private float held;
        private bool latched;

        /// <summary>0~1. 누르고 있는 비율(이미 발동한 뒤에는 손을 뗄 때까지 0).</summary>
        public float Progress { get; private set; }

        /// <summary>이번 프레임에 다 차서 발동했는가.</summary>
        public bool Triggered { get; private set; }

        public void Update(bool pressed, float deltaTime, float holdSeconds)
        {
            Triggered = false;

            if (!pressed)
            {
                held = 0f;
                latched = false;
                Progress = 0f;
                return;
            }

            if (latched)
            {
                Progress = 0f;
                return;
            }

            held += deltaTime;
            Progress = holdSeconds > 0f ? held / holdSeconds : 1f;
            if (Progress >= 1f)
            {
                Progress = 0f;
                Triggered = true;
                latched = true;
            }
        }
    }
}
