namespace Shinshi.AI
{
    /// <summary> 보스 예고가 보여 주는 범위 형태, 돌진은 같은 직사각형이어도 발동 중 이동을 표시해서 따로 둔다 </summary>
    public enum EBossTelegraphShape : byte
    {
        /// <summary> 부채꼴, 월백 P02 </summary>
        Fan = 0,

        /// <summary> 원형, 월백 P03 </summary>
        Circle = 1,

        /// <summary> 직사각형 한 번 타격, 월백 P06 </summary>
        Line = 2,

        /// <summary> 직사각형 위를 이동하며 타격, 월백 P01 </summary>
        Dash = 3,

        /// <summary> 투사체 경로 직사각형, 월백 P04. 발사하면 예고는 사라지고 투사체는 따로 날아간다 </summary>
        Projectile = 4
    }
}
