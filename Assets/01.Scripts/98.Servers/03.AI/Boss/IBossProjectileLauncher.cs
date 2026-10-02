// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 패턴 객체는 예고와 조준과 발사까지만 맡고, 발사된 투사체의 비행과 충돌은 패턴 종료 뒤에도 이어지므로 지속물 관리자가 맡는다
    /// <summary> 투사체 발사 창구, 구현은 BossProjectileManager다 </summary>
    public interface IBossProjectileLauncher
    {
        /// <summary> origin에서 방향으로 갈 수 있는 경로 길이, 벽이 있으면 폭의 절반을 반경으로 둔 벽 앞까지이고 없으면 maxDistance다. 예고 길이와 실제 비행 길이가 같은 값이다 </summary>
        float MeasurePath(Vector3 origin, Vector3 direction, float maxDistance, float width);

        /// <summary> 한 발을 발사한다, 입력이 올바르지 않거나 동시에 날 수 있는 수를 넘으면 거짓이다 </summary>
        bool TryLaunch(in BossProjectileSpec spec);
    }
}
