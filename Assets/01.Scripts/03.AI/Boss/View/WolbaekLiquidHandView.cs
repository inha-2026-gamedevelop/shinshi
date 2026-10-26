// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 월백 1P P06 대형 방출에 쓰는 액체 손 표현. 손이 바닥에서 솟아 앞으로 밀고 나가며 웅덩이를 남기고 무너진다
    // 길이와 폭은 서버 사건 값에서 받아 루트 스케일로 맞추고, 웅덩이 드러남 값은 원본 연출의 키 값을 같은 시각에 넣는다
    /// <summary> 액체 손 한 번의 재생과 웅덩이 경계 구동을 맡는 표현 </summary>
    public sealed class WolbaekLiquidHandView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 원본 연출은 30프레임 기준이고 손이 솟기 직전(90프레임)부터 재생해 예고 구간의 빈 시간을 건너뛴다
        private const float SOURCE_FPS         = 30f;
        private const float START_FRAME        = 90f;
        private const float FIRST_FRAME        = 1f;   // 원본 클립의 첫 프레임 번호, 정규화 시간의 기준점
        private const float CLIP_END_FRAME     = 160f;
        private const float REFERENCE_DISTANCE = 14.5f;
        private const float REFERENCE_WIDTH    = 4.4f;
        private const float HIDDEN_Y           = -2f;
        private const float GROUND_LIFT        = 0.02f; // m, 바닥 메시와 겹쳐 깜박이지 않게 살짝 띄운다

        // 원본 웅덩이 앞 경계(손을 따라간다), 프레임과 로컬 Y 쌍
        private static readonly float[] FRONT_FRAMES = { 1f, 94f, 101f, 106f, 114f, 121f, 160f };
        private static readonly float[] FRONT_VALUES = { -2f, -1f, 1f, 3.3f, 9.3f, 14.5f, 16f };

        // 원본 웅덩이 뒤 경계(뒤에서부터 마른다)
        private static readonly float[] REAR_FRAMES = { 1f, 141f, 159f };
        private static readonly float[] REAR_VALUES = { -2f, -2f, 16f };

        private static readonly int FRONT_ID = Shader.PropertyToID("_FrontY");
        private static readonly int REAR_ID  = Shader.PropertyToID("_RearY");

        [Header("연결")]
        [SerializeField] private Animator _handAnimator;
        [SerializeField] private Renderer _puddleRenderer;
        [SerializeField] private string _handStateName = "Liquid";

        [Header("시험 재생")]
        [SerializeField] private bool _loopPreview;
        [SerializeField] private float _previewLength = 14f;
        [SerializeField] private float _previewWidth = 4f;
        [SerializeField] private float _previewInterval = 6f;

        private MaterialPropertyBlock _block;
        private bool  _playing;
        private float _elapsed;
        private float _nextPreviewTime;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _block = new MaterialPropertyBlock();

            // 뼈 없이 블렌드셰이프만 쓰는 메시는 이 값을 켜야 GPU 스키닝이 블렌드셰이프를 반영한다. 꺼 두면 첫 모양에서 움직이지 않는다
            if (_handAnimator != null)
            {
                SkinnedMeshRenderer[] hands = _handAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true);

                for (int i = 0; i < hands.Length; ++i)
                {
                    hands[i].rootBone = hands[i].transform;
                    hands[i].skinnedMotionVectors = false;
                    hands[i].forceMatrixRecalculationPerRender = true;
                    hands[i].updateWhenOffscreen = true;
                }
            }

            ApplyBounds(HIDDEN_Y, HIDDEN_Y);
            SetVisible(false);
        }

        private void Start()
        {
            if (_loopPreview)
            {
                Play(_previewLength, _previewWidth);
            }
        }

        private void Update()
        {
            if (_playing)
            {
                _elapsed += Time.deltaTime;
                float frame = START_FRAME + (_elapsed * SOURCE_FPS);

                if (frame >= CLIP_END_FRAME)
                {
                    Stop();
                    _nextPreviewTime = Time.time + _previewInterval;
                    return;
                }

                ApplyBounds(Evaluate(FRONT_FRAMES, FRONT_VALUES, frame), Evaluate(REAR_FRAMES, REAR_VALUES, frame));
                return;
            }

            if (_loopPreview && (Time.time >= _nextPreviewTime))
            {
                Play(_previewLength, _previewWidth);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버가 알린 방출 시작 위치와 방향으로 옮겨 놓고 길이와 폭에 맞춰 손을 한 번 재생한다 </summary>
        public void Play(Vector3 origin, Vector3 direction, float length, float width)
        {
            direction.y = 0f;

            if (direction.sqrMagnitude < Mathf.Epsilon)
            {
                return;
            }

            transform.SetPositionAndRotation(origin + (Vector3.up * GROUND_LIFT), Quaternion.LookRotation(direction.normalized, Vector3.up));
            Play(length, width);
        }

        /// <summary> 서버가 알린 방출 길이와 폭에 맞춰 손을 한 번 재생한다 </summary>
        public void Play(float length, float width)
        {
            if (_handAnimator == null)
            {
                return;
            }

            transform.localScale = new Vector3(width / REFERENCE_WIDTH, 1f, length / REFERENCE_DISTANCE);

            _elapsed = 0f;
            _playing = true;
            SetVisible(true);

            _handAnimator.speed = 1f;
            _handAnimator.Play(_handStateName, 0, NormalizedTime(START_FRAME));
            _handAnimator.Update(0f);
        }

        /// <summary> 재생 없이 시작 뒤 경과 시간의 장면 한 컷을 보여 준다, 시험과 화면 확인용 </summary>
        public void ShowPose(float elapsed, float length, float width)
        {
            if (_handAnimator == null)
            {
                return;
            }

            _playing = false;
            transform.localScale = new Vector3(width / REFERENCE_WIDTH, 1f, length / REFERENCE_DISTANCE);
            SetVisible(true);

            float frame = START_FRAME + (elapsed * SOURCE_FPS);
            _handAnimator.speed = 0f;
            _handAnimator.Play(_handStateName, 0, NormalizedTime(frame));
            _handAnimator.Update(0f);
            ApplyBounds(Evaluate(FRONT_FRAMES, FRONT_VALUES, frame), Evaluate(REAR_FRAMES, REAR_VALUES, frame));
        }

        /// <summary> 재생을 멈추고 모두 숨긴다 </summary>
        public void Stop()
        {
            _playing = false;
            ApplyBounds(HIDDEN_Y, HIDDEN_Y);
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (_handAnimator != null)
            {
                _handAnimator.gameObject.SetActive(visible);
            }

            if (_puddleRenderer != null)
            {
                _puddleRenderer.enabled = visible;
            }
        }

        private void ApplyBounds(float frontY, float rearY)
        {
            if (_puddleRenderer == null)
            {
                return;
            }

            _puddleRenderer.GetPropertyBlock(_block);
            _block.SetFloat(FRONT_ID, frontY);
            _block.SetFloat(REAR_ID, rearY);
            _puddleRenderer.SetPropertyBlock(_block);
        }

        private static float NormalizedTime(float frame)
        {
            return (frame - FIRST_FRAME) / (CLIP_END_FRAME - FIRST_FRAME);
        }

        private static float Evaluate(float[] frames, float[] values, float frame)
        {
            if (frame <= frames[0])
            {
                return values[0];
            }

            for (int i = 1; i < frames.Length; ++i)
            {
                if (frame <= frames[i])
                {
                    float t = (frame - frames[i - 1]) / (frames[i] - frames[i - 1]);
                    return Mathf.Lerp(values[i - 1], values[i], t);
                }
            }

            return values[values.Length - 1];
        }
    }
}
