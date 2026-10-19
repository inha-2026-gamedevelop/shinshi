// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 무철 돌진 구조물 상태를 화면에 맞춘다. 판정은 하지 않는다.
    // 같은 식별자의 씬 표식(BossChargeStructureSet)이 있으면 그 표식의 온전, 부서짐 모습을 바꾸고, 없으면 임시 기둥(원기둥 프리미티브)을
    // 만들어 보여 준다. 맵 배치가 없는 개발 전장에서 서버의 임시 개발 지점을 눈으로 확인하려는 것이고 운영 모습이 아니다.
    //
    // 복원은 연출 없이 상태만 맞추고, 파괴 사건은 구조물마다 한 번만 전환하며 그때만 연출 훅(Destroyed)을 부른다.
    // 호스트에서는 서버가 같은 표식을 먼저 부서짐으로 바꿔 두므로 표식 상태가 아니라 이 표현이 받은 기록으로 한 번을 가린다.
    // 바인더(BossNetworkViewBinder)가 소유하며 프리팹에 붙이지 않는다. 임시 기둥은 연결을 끊을 때 지운다
    /// <summary> 무철 돌진 구조물의 클라이언트 표현, 씬 표식 상태를 맞추거나 임시 기둥을 보여 준다 </summary>
    public sealed class MucheolChargeStructureView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  PILLAR_HEIGHT        = 4f;    // m, 임시 기둥 높이
        private const float  BROKEN_HEIGHT_RATE   = 0.25f; // 부서진 임시 기둥은 밑동만 남긴다
        private const float  HALF                 = 0.5f;
        private const float  IMPACT_HEIGHT        = 1.2f;  // m, 파편이 튀는 기준 높이, 석주 연출의 기본 충격점과 같다
        private const string ROOT_NAME           = "MucheolChargeStructures (임시)";
        private const string PILLAR_NAME_PREFIX   = "ChargeStructure_";

        private readonly Dictionary<int, BossChargeStructureInfo> _structures = new Dictionary<int, BossChargeStructureInfo>();
        private readonly Dictionary<int, GameObject>              _pillars    = new Dictionary<int, GameObject>();
        private readonly HashSet<int>                             _destroyed  = new HashSet<int>();

        private GameObject _root;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 복원으로 받은 구조물 수 </summary>
        public int Count => _structures.Count;

        /// <summary> 씬 표식이 없어 만든 임시 기둥 수 </summary>
        public int TemporaryPillarCount => _pillars.Count;

        // TODO(기웅): 파괴 VFX, 효과음 에셋이 생기면 이 훅에 연결한다. 인자는 구조물 식별자와 바닥 중심이다
        /// <summary> 파괴 사건으로 구조물이 처음 부서질 때 한 번, 복원에서는 부르지 않는다 </summary>
        public event Action<int, Vector3> Destroyed;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 배치와 파괴 여부를 연출 없이 맞춘다, 이전 연결의 임시 기둥은 지우고 새로 만든다 </summary>
        public void Restore(in BossChargeStructureSnapshot snapshot)
        {
            Clear();
            BossChargeStructureSet scene = BossChargeStructureSet.Current;

            for (int i = 0; i < snapshot.Count; ++i)
            {
                BossChargeStructureInfo info = snapshot.GetStructure(i);
                if (_structures.ContainsKey(info.Id))
                {
                    continue;
                }

                bool destroyed = snapshot.IsDestroyed(i);
                _structures.Add(info.Id, info);
                if (destroyed)
                {
                    _destroyed.Add(info.Id);
                }

                if ((scene != null) && (scene.TryGetMarker(info.Id, out BossChargeStructureMarker marker)))
                {
                    marker.RestoreDestroyed(destroyed);
                    continue;
                }

                GameObject pillar = CreatePillar(info);
                _pillars.Add(info.Id, pillar);
                ApplyPillar(pillar, info, destroyed);
            }
        }

        /// <summary> 서버가 확정한 파괴를 한 번 보인다, 이미 부서졌거나 모르는 구조물이면 거짓이다 </summary>
        public bool ApplyDestroyed(int structureId)
        {
            if ((!_structures.TryGetValue(structureId, out BossChargeStructureInfo info)) || (!_destroyed.Add(structureId)))
            {
                return false;
            }

            BossChargeStructureSet scene = BossChargeStructureSet.Current;
            if ((scene != null) && (scene.TryGetMarker(structureId, out BossChargeStructureMarker marker)))
            {
                marker.PlayDestroyed(info.Center + (Vector3.up * IMPACT_HEIGHT));
            }
            else if (_pillars.TryGetValue(structureId, out GameObject pillar))
            {
                ApplyPillar(pillar, info, true);
            }

            Destroyed?.Invoke(structureId, info.Center);
            return true;
        }

        public bool IsDestroyed(int structureId)
        {
            return _destroyed.Contains(structureId);
        }

        // 씬 표식은 씬 소유라 되돌리지 않는다. 다음 복원이 서버 상태로 다시 맞춘다
        /// <summary> 받은 배치를 잊고 임시 기둥을 지운다 </summary>
        public void Clear()
        {
            _structures.Clear();
            _destroyed.Clear();
            _pillars.Clear();

            if (_root != null)
            {
                DestroyObject(_root);
                _root = null;
            }
        }

        // 원기둥 프리미티브의 높이는 축척 y의 두 배이고 지름은 축척 x, z다. 판정과 무관한 표시라 콜라이더를 지운다
        private GameObject CreatePillar(BossChargeStructureInfo info)
        {
            if (_root == null)
            {
                _root = new GameObject(ROOT_NAME);
            }

            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = PILLAR_NAME_PREFIX + info.Id;
            pillar.transform.SetParent(_root.transform, false);
            if (pillar.TryGetComponent(out Collider collider))
            {
                DestroyObject(collider);
            }

            return pillar;
        }

        private static void ApplyPillar(GameObject pillar, BossChargeStructureInfo info, bool destroyed)
        {
            float height   = destroyed ? (PILLAR_HEIGHT * BROKEN_HEIGHT_RATE) : PILLAR_HEIGHT;
            float diameter = info.Radius / HALF;
            pillar.transform.position   = info.Center + (Vector3.up * (height * HALF));
            pillar.transform.localScale = new Vector3(diameter, height * HALF, diameter);
        }

        // 편집 모드 시험에서도 지울 수 있게 한다
        private static void DestroyObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
                return;
            }

            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
