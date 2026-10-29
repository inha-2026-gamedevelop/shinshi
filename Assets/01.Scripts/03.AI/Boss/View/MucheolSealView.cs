// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 프리뷰(Dev_MucheolMotionVfxPreview)에서 확정한 무철 방향 봉인 표식을 그린다. 표식은 오른팔, 왼팔, 등에 한 개씩 있고
    // 봉인이 진행되는 순서(오른쪽, 왼쪽, 후방)대로 하나만 나온다. 서버의 봉인 사건을 받아 부르는 일은 네트워크 수신부가 한다.
    // 이 뷰는 서버 어셈블리를 참조하지 않고 클라이언트 안전한 값(방향, 타수)만 받는다. 표식이 스스로 타수를 세거나 방향을 판정하지 않는다.
    //
    // 타수가 서버에서 확정될 때마다 OnSealHitCounted로 진행도가 오르고 표식이 잠깐 번쩍이며 부푼다.
    // 방향이 채워지면 OnSealDirectionCleared로 그 표식이 크게 퍼지며 사라지고 다음 방향의 표식이 나온다. 마지막 방향은 OnSealBroken으로 사라진다.
    // 늦은 접속은 OnSealRestored로 지금 방향과 타수에서 바로 이어 그린다. 표식은 보스 뼈를 따라 움직이되 크기는 뼈 배율을 받지 않는다
    /// <summary> 무철 방향 봉인 표식 표현, 서버가 확정한 타수와 방향 사건을 표식의 진행도와 반응으로 바꾼다 </summary>
    [DisallowMultipleComponent]
    public sealed class MucheolSealView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    SIDE_COUNT      = 3;
        private const string CONTAINER_NAME  = "MucheolSealMarkers";
        private const string MARKER_SUFFIX   = " Seal";
        private const float  NEXT_SOUND_WAIT = 0.6f; // 초, 방향 완료음 뒤 다음 표식 소리를 늦추는 시간

        private static readonly int G_Progress = Shader.PropertyToID("_Progress");
        private static readonly int G_Pulse    = Shader.PropertyToID("_Pulse");
        private static readonly int G_Opacity  = Shader.PropertyToID("_Opacity");
        private static readonly int G_Phase    = Shader.PropertyToID("_Phase");

        /****************************************
        *              Nested Types
        ****************************************/

        private sealed class Marker
        {
            public GameObject Root;
            public Renderer   Renderer;
            public bool       IsVisible;
            public bool       IsDissolving;
            public double     RevealedAt;
            public double     LastHitAt;
            public double     DissolveAt;
            public int        Hits;
            public int        Required;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Transform[] _anchors     = new Transform[SIDE_COUNT]; // 오른팔, 왼팔, 등의 뼈, EMucheolSealSide 순서
        [SerializeField] private Vector3[]   _offsets     = new Vector3[SIDE_COUNT];   // 뼈 기준 위치
        [SerializeField] private Vector3[]   _localAngles = new Vector3[SIDE_COUNT];   // 뼈 기준 회전(도)
        [SerializeField] private Material    _material;                                // 봉인 표식 재질, 표식마다 속성 블록으로 값을 준다
        [SerializeField, Min(0.01f)] private float _size = 1.4f;                       // m, 표식 한 변

        private readonly Marker[] _markers = new Marker[SIDE_COUNT];

        // MonoBehaviour는 필드 초기화에서 속성 블록을 만들 수 없어 시작할 때 만든다
        private MaterialPropertyBlock _block;
        private Transform    _container;
        private Mesh         _quad;
        private Func<double> _serverClock;
        private bool         _isReady;

        [Header("효과음")]
        [SerializeField] private MucheolSoundPlayer _sound; // 비어 있으면 같은 보스 프리팹에서 찾고, 없으면 소리를 생략한다

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if (_material == null)
            {
                Debug.LogError("[Boss] 봉인 표식 재질이 비어 있어 표식을 그리지 않는다", this);
                enabled = false;
                return;
            }

            _block = new MaterialPropertyBlock();

            if (_sound == null)
            {
                _sound = GetComponentInParent<MucheolSoundPlayer>();
            }

            // 표식은 보스 뼈 배율을 받지 않도록 보스 계층 밖에 두고 매 프레임 뼈를 따라가게 한다
            var container = new GameObject(CONTAINER_NAME);
            SceneManager.MoveGameObjectToScene(container, gameObject.scene);
            _container = container.transform;

            _quad = CreateQuad();
            for (int i = 0; i < SIDE_COUNT; ++i)
            {
                _markers[i] = CreateMarker(((EMucheolSealSide)i).ToString() + MARKER_SUFFIX, _quad);
            }

            _isReady = true;
        }

        private void Update()
        {
            Refresh();
        }

        private void OnDisable()
        {
            HideAll();
        }

        private void OnDestroy()
        {
            if (_container != null)
            {
                DestroyTarget(_container.gameObject);
                _container = null;
            }

            if (_quad != null)
            {
                DestroyTarget(_quad);
                _quad = null;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 시각을 읽는 함수, 비우면 로컬 시각이다 </summary>
        public void SetServerClock(Func<double> serverClock)
        {
            _serverClock = serverClock;
        }

        /// <summary> 표식이 지금 보인다 </summary>
        public bool IsMarkerVisible(EMucheolSealSide side)
        {
            Marker marker = GetMarker(side);
            return (marker != null) && (marker.IsVisible);
        }

        /// <summary> 표식이 사라지는 중이다 </summary>
        public bool IsMarkerDissolving(EMucheolSealSide side)
        {
            Marker marker = GetMarker(side);
            return (marker != null) && (marker.IsVisible) && (marker.IsDissolving);
        }

        /// <summary> 표식에 지금 적용된 진행도, 보이지 않으면 0이다 </summary>
        public float GetProgress(EMucheolSealSide side)
        {
            Marker marker = GetMarker(side);
            return ((marker != null) && (marker.IsVisible) && (marker.Required > 0))
                ? Mathf.Clamp01((float)marker.Hits / marker.Required)
                : 0f;
        }

        /// <summary> 봉인이 발동해 첫 방향(오른쪽) 표식이 나온다. 이전 표식은 모두 거둔다 </summary>
        public void OnSealRevealed(int requiredHits)
        {
            HideAll();
            Show(EMucheolSealSide.Right, 0, requiredHits, ReadClock());
            PlaySound(EMucheolSound.SealAppear, EMucheolSealSide.Right, 0f);
        }

        // 서버가 유효타를 센 사건이다. 표식이 아직 없으면(사건이 건너뛰어졌다면) 지금 값으로 나타낸다
        /// <summary> 방향의 유효타 수가 올랐다, 진행도가 오르고 표식이 번쩍인다 </summary>
        public void OnSealHitCounted(EMucheolSealSide side, int hitCount, int requiredHits)
        {
            Marker marker = GetMarker(side);
            if (marker == null)
            {
                return;
            }

            double now = ReadClock();
            if ((!marker.IsVisible) || (marker.IsDissolving))
            {
                Show(side, hitCount, requiredHits, now);
                return;
            }

            bool isIncreased = hitCount > marker.Hits;
            marker.Hits      = Mathf.Clamp(hitCount, 0, Mathf.Max(0, requiredHits));
            marker.Required  = requiredHits;
            marker.LastHitAt = now;

            // 서버가 센 유효타가 늘었을 때만 낸다, 같은 값으로 다시 온 사건은 내지 않는다
            if (isIncreased)
            {
                PlaySound(EMucheolSound.SealTick, side, 0f);
            }
        }

        /// <summary> 방향이 채워졌다. 그 표식은 마지막 번쩍임과 함께 퍼지며 사라지고 다음 방향의 표식이 나온다 </summary>
        public void OnSealDirectionCleared(EMucheolSealSide side, int requiredHits)
        {
            double now = ReadClock();
            Dissolve(side, requiredHits, now);

            int next = (int)side + 1;
            if (next < SIDE_COUNT)
            {
                Show((EMucheolSealSide)next, 0, requiredHits, now);

                // 완료음 바로 뒤에 다음 표식을 겹치지 않게 조금 늦춰 낸다
                PlaySound(EMucheolSound.SealDirectionDone, side, 0f);
                PlaySound(EMucheolSound.SealAppear, (EMucheolSealSide)next, NEXT_SOUND_WAIT);
            }
        }

        /// <summary> 마지막 방향까지 채워 봉인이 깨졌다. 남은 표식이 퍼지며 사라진다 </summary>
        public void OnSealBroken(int requiredHits)
        {
            // 마지막 방향의 완료음과 겹치지 않도록 최종 파괴음 하나만 낸다
            PlaySound(EMucheolSound.SealBreak, EMucheolSealSide.Rear, 0f);

            double now = ReadClock();
            for (int i = 0; i < SIDE_COUNT; ++i)
            {
                Marker marker = _markers[i];
                if ((marker != null) && (marker.IsVisible) && (!marker.IsDissolving))
                {
                    Dissolve((EMucheolSealSide)i, requiredHits, now);
                }
            }
        }

        /// <summary> 사망이나 전투 종료로 봉인이 멈췄다. 표식을 바로 거둔다 </summary>
        public void OnSealStopped()
        {
            HideAll();
        }

        // 늦은 접속과 복원용이다. 나오는 연출은 이미 끝난 것으로 보고 지금 방향과 타수에서 바로 이어 그린다
        /// <summary> 진행 중인 봉인을 지금 방향과 타수로 복원한다 </summary>
        public void OnSealRestored(EMucheolSealSide side, int hitCount, int requiredHits)
        {
            HideAll();
            double now = ReadClock();
            Show(side, hitCount, requiredHits, now - MucheolSealVisual.REVEAL_FADE_SECONDS);
            Marker marker = GetMarker(side);
            if (marker != null)
            {
                marker.LastHitAt = now - MucheolSealVisual.PULSE_SECONDS;
            }
        }

        /// <summary> 표식 값을 지금 시각으로 다시 계산해 그린다. 매 프레임 불리고, 시험은 직접 부른다 </summary>
        public void Refresh()
        {
            if (!_isReady)
            {
                return;
            }

            double now = ReadClock();
            for (int i = 0; i < SIDE_COUNT; ++i)
            {
                Marker marker = _markers[i];
                if ((marker == null) || (!marker.IsVisible))
                {
                    continue;
                }

                float dissolveAge = marker.IsDissolving ? (float)(now - marker.DissolveAt) : MucheolSealVisual.NOT_DISSOLVING;
                if (marker.IsDissolving && (dissolveAge >= MucheolSealVisual.DISSOLVE_SECONDS))
                {
                    Hide(marker);
                    continue;
                }

                float age = (float)(now - marker.RevealedAt);
                MucheolSealLook look = MucheolSealVisual.Evaluate(age, marker.Hits, marker.Required, (float)(now - marker.LastHitAt), dissolveAge);
                Apply(i, marker, look, age);
            }
        }

        // 소리는 연출이라 서버가 관여하지 않는다. 복원(OnSealRestored)은 이 함수를 부르지 않아 지난 소리를 다시 내지 않는다
        private void PlaySound(EMucheolSound id, EMucheolSealSide side, float delaySeconds)
        {
            if ((_sound == null) || (!_isReady))
            {
                return;
            }

            Transform anchor = _anchors[(int)side];
            _sound.Play(id, (anchor != null) ? anchor.position : transform.position, delaySeconds);
        }

        private void Show(EMucheolSealSide side, int hits, int requiredHits, double now)
        {
            Marker marker = GetMarker(side);
            if (marker == null)
            {
                return;
            }

            marker.IsVisible    = true;
            marker.IsDissolving = false;
            marker.RevealedAt   = now;
            marker.LastHitAt    = now;
            marker.Required     = requiredHits;
            marker.Hits         = Mathf.Clamp(hits, 0, Mathf.Max(0, requiredHits));
            marker.Root.SetActive(true);
        }

        private void Dissolve(EMucheolSealSide side, int requiredHits, double now)
        {
            Marker marker = GetMarker(side);
            if ((marker == null) || (!marker.IsVisible) || (marker.IsDissolving))
            {
                return;
            }

            // 마지막 유효타의 번쩍임과 함께 퍼진다
            marker.Required     = requiredHits;
            marker.Hits         = requiredHits;
            marker.LastHitAt    = now;
            marker.IsDissolving = true;
            marker.DissolveAt   = now;
        }

        private void Apply(int index, Marker marker, MucheolSealLook look, float age)
        {
            Transform anchor = _anchors[index];
            if (anchor != null)
            {
                marker.Root.transform.SetPositionAndRotation(anchor.TransformPoint(_offsets[index]),
                    anchor.rotation * Quaternion.Euler(_localAngles[index]));
            }

            marker.Root.transform.localScale = Vector3.one * (_size * look.ScaleFactor);

            marker.Renderer.GetPropertyBlock(_block);
            _block.SetFloat(G_Progress, look.Progress);
            _block.SetFloat(G_Pulse, look.Pulse);
            _block.SetFloat(G_Opacity, look.Opacity);
            _block.SetFloat(G_Phase, age);
            marker.Renderer.SetPropertyBlock(_block);
        }

        private void HideAll()
        {
            for (int i = 0; i < SIDE_COUNT; ++i)
            {
                if (_markers[i] != null)
                {
                    Hide(_markers[i]);
                }
            }
        }

        private static void Hide(Marker marker)
        {
            marker.IsVisible    = false;
            marker.IsDissolving = false;
            marker.Root.SetActive(false);
        }

        private static void DestroyTarget(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private Marker GetMarker(EMucheolSealSide side)
        {
            int index = (int)side;
            return ((index >= 0) && (index < SIDE_COUNT)) ? _markers[index] : null;
        }

        private double ReadClock()
        {
            return (_serverClock != null) ? _serverClock() : Time.timeAsDouble;
        }

        private Marker CreateMarker(string markerName, Mesh quad)
        {
            var root = new GameObject(markerName);
            root.transform.SetParent(_container, false);
            root.AddComponent<MeshFilter>().sharedMesh = quad;

            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial    = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            root.SetActive(false);

            return new Marker { Root = root, Renderer = renderer };
        }

        // 내장 쿼드와 같은 방향과 UV다. 프리뷰에서 맞춘 회전값이 그대로 맞도록 같은 모양으로 만든다
        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "MucheolSealQuad" };
            mesh.vertices  = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            mesh.uv        = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.normals   = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
