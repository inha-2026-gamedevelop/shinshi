// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 보스 기준 정면 반각 이내는 정면, 그 초과부터 측면 한계까지는 해당 측면, 나머지는 후방이다. 두 경계 각도는 한 구역에만 속한다
    // (정면 반각은 정면에, 측면 한계는 측면에). 높이는 보지 않고 수평면에서만 잰다
    /// <summary> 보스 정면과 입사 방향으로 방향 구역을 가르는 순수 계산 </summary>
    public static class MucheolSealGeometry
    {
        /****************************************
        *              Constants
        ****************************************/

        // 부동소수 오차로 경계 각도가 바깥 구역으로 넘어가지 않게 하는 허용치(도)다
        private const float ANGLE_EPSILON = 0.001f;
        private const float MIN_SQR_LENGTH = 1e-8f;

        /****************************************
        *                Methods
        ****************************************/

        // incomingDirection은 보스에서 공격이 온 쪽을 가리키는 방향이다. 근접은 공격자 쪽, 투사체는 날아온 쪽(진행 방향의 반대)이다
        /// <summary> 방향 구역을 가른다, 정면이나 입사 방향이 수평면에서 영벡터면 None이다 </summary>
        public static EMucheolSealDirection Classify(Vector3 bossForward, Vector3 incomingDirection, float frontHalfAngle, float sideLimitAngle)
        {
            Vector3 forward  = new Vector3(bossForward.x, 0f, bossForward.z);
            Vector3 incoming = new Vector3(incomingDirection.x, 0f, incomingDirection.z);

            if ((forward.sqrMagnitude < MIN_SQR_LENGTH) || (incoming.sqrMagnitude < MIN_SQR_LENGTH))
            {
                return EMucheolSealDirection.None;
            }

            float signed   = Vector3.SignedAngle(forward, incoming, Vector3.up);
            float absolute = Mathf.Abs(signed);

            if (absolute <= (frontHalfAngle + ANGLE_EPSILON))
            {
                return EMucheolSealDirection.Front;
            }

            if (absolute <= (sideLimitAngle + ANGLE_EPSILON))
            {
                return (signed > 0f) ? EMucheolSealDirection.Right : EMucheolSealDirection.Left;
            }

            return EMucheolSealDirection.Rear;
        }
    }
}
