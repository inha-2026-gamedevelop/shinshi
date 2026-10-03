// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 임시 표현이다. 장판 셰이더(Shinshi/VFX/AreaGround) 머티리얼을 그대로 쓰고 도넛 모양 메시로 안쪽 안전 구역만 비운다
    // 서버가 알린 시작 값과 서버 시각으로만 단계를 정하고 피해와 대상은 계산하지 않는다. 늦게 받은 시작 값도 현재 단계부터 이어서 그린다
    // 주기 맥동은 서버 활성 시각과 간격으로 계산한 표시일 뿐이고 실제 피해 시점의 근거가 아니다
    /// <summary> 결계 위험 구역을 그리는 표현, 예고와 지속 활성 단계를 구분해 보여 주고 정리 알림이 올 때까지 유지한다 </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class BossBarrierView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    SEGMENTS          = 64;
        private const float  HALF              = 0.5f;
        private const float  DIAMETER_SCALE    = 2f;
        private const float  GROUND_OFFSET     = 0.03f;
        private const float  TELEGRAPH_LIFE    = 0.3f;
        private const float  TELEGRAPH_FADE_MIN = 0.35f;
        private const float  TELEGRAPH_FADE_AMP = 0.3f;
        private const float  TELEGRAPH_BLINK_HZ = 3f;
        private const float  NO_PULSE          = 99f;
        private const string MESH_NAME         = "BossBarrierView Runtime Mesh";

        private static readonly int _stateId = Shader.PropertyToID("_State");
        private static readonly int _sizeId  = Shader.PropertyToID("_Size");

        /****************************************
        *                Fields
        ****************************************/

        private MeshRenderer          _surface;
        private MaterialPropertyBlock _properties;
        private Mesh                  _mesh;
        private Func<double>          _serverClock;
        private BossBarrierTrack      _track;
        private float                 _builtInnerRatio = -1f;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 표시 중인 위험 구역이 있다 </summary>
        public bool IsShown => (_track != null) && (!_track.IsCleared);

        /// <summary> 지금 서버 시각 기준 단계, 표시 중이 아니면 None이다 </summary>
        public EBossBarrierPhase Phase => (_track == null) ? EBossBarrierPhase.None : _track.GetPhase(ReadServerTime());

        public int BarrierId => (_track == null) ? 0 : _track.Start.BarrierId;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _surface    = GetComponent<MeshRenderer>();
            _properties = new MaterialPropertyBlock();
            _surface.enabled = false;
            _surface.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _surface.receiveShadows    = false;
        }

        private void Update()
        {
            if (_track == null)
            {
                return;
            }

            Render(ReadServerTime());
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetServerClock(Func<double> serverClock)
        {
            _serverClock = serverClock;
        }

        /// <summary> 서버가 알린 시작 값으로 표시를 시작한다, 이미 같은 번호를 표시 중이거나 지난 번호면 거절한다 </summary>
        public bool Begin(in BossBarrierStart start)
        {
            if ((_surface == null) || ((_track != null) && (_track.Start.BarrierId == start.BarrierId)))
            {
                return false;
            }

            _track = new BossBarrierTrack(in start);
            BuildMesh(start.InnerRadius / start.OuterRadius);

            float diameter = start.OuterRadius * DIAMETER_SCALE;
            transform.SetPositionAndRotation(start.Center, Quaternion.identity);
            transform.localScale = new Vector3(diameter, 1f, diameter);

            _surface.enabled = true;
            Render(ReadServerTime());
            return true;
        }

        /// <summary> 서버가 알린 정리, 전투 종료와 사망과 리셋에서 부른다 </summary>
        public void Clear()
        {
            if (_track != null)
            {
                _track.TryClear();
            }

            _track = null;

            if (_surface != null)
            {
                _surface.enabled = false;
            }
        }

        private void Render(double now)
        {
            EBossBarrierPhase phase = _track.GetPhase(now);
            if (phase == EBossBarrierPhase.None)
            {
                _surface.enabled = false;
                return;
            }

            _surface.enabled = true;

            float life;
            float pulseAge;
            float fade;
            if (phase == EBossBarrierPhase.Telegraph)
            {
                life     = TELEGRAPH_LIFE;
                pulseAge = NO_PULSE;
                fade     = TELEGRAPH_FADE_MIN + (TELEGRAPH_FADE_AMP * (HALF + (HALF * Mathf.Sin((float)now * TELEGRAPH_BLINK_HZ * Mathf.PI * DIAMETER_SCALE))));
            }
            else
            {
                float interval = Mathf.Max(0.01f, _track.Start.TickInterval);
                life     = 1f;
                pulseAge = _track.GetActiveAge(now) % interval;
                fade     = 1f;
            }

            float outer = _track.Start.OuterRadius;
            _surface.GetPropertyBlock(_properties);
            _properties.SetVector(_stateId, new Vector4(life, pulseAge, fade, outer));
            _properties.SetVector(_sizeId, new Vector4(outer, GROUND_OFFSET, 0f, 0f));
            _surface.SetPropertyBlock(_properties);
        }

        // 바깥 반지름이 0.5인 단위 도넛, UV는 사각형 전체 기준이라 셰이더가 원을 오리는 방식과 맞는다
        private void BuildMesh(float innerRatio)
        {
            if (Mathf.Approximately(_builtInnerRatio, innerRatio))
            {
                return;
            }

            _builtInnerRatio = innerRatio;

            int      vertexCount = SEGMENTS * 2;
            Vector3[] vertices   = new Vector3[vertexCount];
            Vector2[] uvs        = new Vector2[vertexCount];
            Vector3[] normals    = new Vector3[vertexCount];
            int[]     triangles  = new int[SEGMENTS * 6];

            for (int i = 0; i < SEGMENTS; ++i)
            {
                float angle = (Mathf.PI * DIAMETER_SCALE * i) / SEGMENTS;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 outerPoint = direction * HALF;
                Vector3 innerPoint = direction * (HALF * innerRatio);

                vertices[i]            = outerPoint;
                vertices[SEGMENTS + i] = innerPoint;
                uvs[i]                 = new Vector2(outerPoint.x + HALF, outerPoint.z + HALF);
                uvs[SEGMENTS + i]      = new Vector2(innerPoint.x + HALF, innerPoint.z + HALF);
                normals[i]             = Vector3.up;
                normals[SEGMENTS + i]  = Vector3.up;

                int next = (i + 1) % SEGMENTS;
                int t    = i * 6;
                triangles[t]     = i;
                triangles[t + 1] = SEGMENTS + i;
                triangles[t + 2] = next;
                triangles[t + 3] = next;
                triangles[t + 4] = SEGMENTS + i;
                triangles[t + 5] = SEGMENTS + next;
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = MESH_NAME };
                GetComponent<MeshFilter>().sharedMesh = _mesh;
            }

            _mesh.Clear();
            _mesh.vertices  = vertices;
            _mesh.uv        = uvs;
            _mesh.normals   = normals;
            _mesh.triangles = triangles;
            _mesh.RecalculateBounds();
        }

        private double ReadServerTime()
        {
            return (_serverClock != null) ? _serverClock() : Time.timeAsDouble;
        }
    }
}
