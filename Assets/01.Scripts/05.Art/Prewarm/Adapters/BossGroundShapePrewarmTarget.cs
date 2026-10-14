// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    // 보스 바닥 도형은 프리팹 없이 풀이 런타임에 재질과 메시를 만든다. 운영과 같은 풀 생성 경로를 그대로 타서 데운다
    /// <summary> 보스 바닥 도형(원, 부채꼴, 직사각형, 전장 띠)을 풀에서 빌려 한 번씩 그린다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossGroundShapePrewarmTarget : MonoBehaviour, IPrewarmTarget
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   SHAPE_POOL_SIZE   = 4;
        private const float SHAPE_SPACING     = 2.5f;
        private const float DISC_RADIUS       = 1f;
        private const float SECTOR_RADIUS     = 2f;
        private const float SECTOR_ARC        = 90f;
        private const float RECT_LENGTH       = 3f;
        private const float RECT_WIDTH        = 1f;
        private const float BAND_ARENA_RADIUS = 3f;
        private const float BAND_WIDTH        = 1.2f;
        private const float CHARGE_SECONDS    = 0.3f;

        private static readonly Color BASE_COLOR = new Color(1f, 0.25f, 0.1f, 0.4f);
        private static readonly Color FILL_COLOR = new Color(1f, 0.25f, 0.1f, 0.7f);

        /****************************************
        *                Fields
        ****************************************/

        private BossGroundShapePool               _pool;
        private readonly List<BossGroundShapeVFX> _shapes = new List<BossGroundShapeVFX>();

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDestroy()
        {
            EndPrewarm();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void BeginPrewarm()
        {
            EndPrewarm();

            _pool = new BossGroundShapePool(SHAPE_POOL_SIZE);
            if (!_pool.IsAvailable)
            {
                Debug.LogWarning("[Prewarm] 보스 바닥 도형 재질을 만들지 못했다, Sprites/Default 셰이더가 빌드에 없다", this);
                return;
            }

            Vector3 center  = transform.position;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < Mathf.Epsilon)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Vector3 step = transform.right * SHAPE_SPACING;

            BossGroundShapeVFX disc = _pool.Acquire();
            if (disc != null)
            {
                disc.ShowDisc(center - step, DISC_RADIUS);
                _shapes.Add(disc);
            }

            BossGroundShapeVFX sector = _pool.Acquire();
            if (sector != null)
            {
                sector.ShowSector(center, forward, SECTOR_RADIUS, SECTOR_ARC);
                _shapes.Add(sector);
            }

            BossGroundShapeVFX rect = _pool.Acquire();
            if (rect != null)
            {
                rect.ShowRect(center + step, forward, RECT_LENGTH, RECT_WIDTH);
                _shapes.Add(rect);
            }

            BossGroundShapeVFX band = _pool.Acquire();
            if (band != null)
            {
                band.ShowBand(center, BAND_ARENA_RADIUS, forward, 0f, BAND_WIDTH);
                _shapes.Add(band);
            }
        }

        public void TickPrewarm(float elapsedSeconds)
        {
            float charge = Mathf.Clamp01(elapsedSeconds / CHARGE_SECONDS);
            for (int i = 0; i < _shapes.Count; ++i)
            {
                _shapes[i].Render(charge, BASE_COLOR, FILL_COLOR);
            }
        }

        public void EndPrewarm()
        {
            if (_pool == null)
            {
                return;
            }

            _pool.ReleaseAll();
            _pool.Dispose();
            _pool = null;
            _shapes.Clear();
        }
    }
}
