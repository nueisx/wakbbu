namespace WaxBall
{
    /// <summary>
    /// 접촉점과 Crunched 이벤트에 쓰는 손가락. 햅틱 장갑(WEART TouchDIVER G1)이 지원하는 세 손가락이다.
    /// 값은 0부터 시작하는 번호로도 쓰인다(Thumb = 0, Index = 1, Middle = 2).
    /// </summary>
    public enum WaxFinger
    {
        Thumb,
        Index,
        Middle
    }
}
