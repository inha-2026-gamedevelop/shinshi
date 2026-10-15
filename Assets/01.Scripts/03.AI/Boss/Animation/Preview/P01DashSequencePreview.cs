// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.Serialization;

namespace Shinshi.AI
{
    /// <summary> P01 예고 이펙트를 한 번 터뜨린 뒤 돌진하고 천천히 Idle로 돌아오는 확인용 도구, 돌진 시간은 거리 / 속도이고 재생 속도를 거기에 맞춘다 </summary>
    public sealed class P01DashSequencePreview : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string IDLE_STATE = "Idle";

        private const float PANEL_WIDTH    = 250f;
        private const float BUTTON_WIDTH   = 240f;
        private const float BUTTON_HEIGHT  = 34f;
        private const float START_BUTTON_Y = 100f;
        private const float RESET_BUTTON_Y = 138f;

        private const float DASH_CLIP_SECONDS   = 1.333f;
        private const float DASH_ENTER_SECONDS  = 0.05f;
        private const float RESET_BLEND_SECONDS = 0.1f;
        private const float TELEGRAPH_LIFETIME  = 6f;
        private const float TELEGRAPH_HEIGHT    = 0.02f;
        private const float MIN_DISTANCE        = 0.01f;
        private const float DASH_CLEAR_DISTANCE = 8f;
        private const float DASH_CLEAR_SECONDS  = 0.45f;
        private const float HALF                = 0.5f;

        /****************************************
        *                Fields
        ****************************************/

        [FormerlySerializedAs("animator")]
        [SerializeField] private Animator _animator;

        [FormerlySerializedAs("telegraphPrefab")]
        [SerializeField] private GameObject _telegraphPrefab;

        [FormerlySerializedAs("dashState")]
        [SerializeField] private string _dashState = "move_Flying_front_inplace";

        [FormerlySerializedAs("telegraphScale")]
        [SerializeField] private float _telegraphScale = 0.4f;

        [FormerlySerializedAs("telegraphSeconds")]
        [SerializeField] private float _telegraphSeconds = 0.8f;

        [FormerlySerializedAs("dashDistance")]
        [SerializeField] private float _dashDistance = DASH_CLEAR_DISTANCE;

        [FormerlySerializedAs("dashSpeed")]
        [SerializeField] private float _dashSpeed = DASH_CLEAR_DISTANCE / DASH_CLEAR_SECONDS;

        [FormerlySerializedAs("recoverSeconds")]
        [SerializeField] private float _recoverSeconds = 0.5f;

        [FormerlySerializedAs("maxPlaySpeed")]
        [SerializeField] private float _maxPlaySpeed = 4f;

        private Vector3   _startPosition;
        private Coroutine _running;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _startPosition = transform.position;
        }

        private void OnGUI()
        {
            if (_animator == null)
            {
                return;
            }

            float x = Screen.width - PANEL_WIDTH;

            if (GUI.Button(new Rect(x, START_BUTTON_Y, BUTTON_WIDTH, BUTTON_HEIGHT), "P01 예고 + 돌진"))
            {
                StopRunning();
                _running = StartCoroutine(Play());
            }

            if (GUI.Button(new Rect(x, RESET_BUTTON_Y, BUTTON_WIDTH, BUTTON_HEIGHT), "P01 위치 되돌리기"))
            {
                StopRunning();
                ResetPose();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void StopRunning()
        {
            if (_running != null)
            {
                StopCoroutine(_running);
            }
        }

        private void ResetPose()
        {
            transform.position = _startPosition;
            _animator.speed    = 1f;
            _animator.CrossFade(IDLE_STATE, RESET_BLEND_SECONDS, 0, 0f);
        }

        private IEnumerator Play()
        {
            ResetPose();

            if (_telegraphPrefab != null)
            {
                GameObject fx = Instantiate(_telegraphPrefab, transform.position + (Vector3.up * TELEGRAPH_HEIGHT), Quaternion.identity);
                fx.transform.localScale = Vector3.one * _telegraphScale;
                Destroy(fx, TELEGRAPH_LIFETIME);
            }

            yield return new WaitForSeconds(_telegraphSeconds);

            // 거리가 0이면 돌진 모션 없이 바로 복귀한다
            if (_dashDistance <= MIN_DISTANCE)
            {
                _animator.CrossFadeInFixedTime(IDLE_STATE, _recoverSeconds, 0, 0f);
                yield break;
            }

            float dashSeconds = _dashDistance / _dashSpeed;
            _animator.CrossFade(_dashState, DASH_ENTER_SECONDS, 0, 0f);
            _animator.speed = Mathf.Min(DASH_CLIP_SECONDS / dashSeconds, _maxPlaySpeed);

            yield return Dash(dashSeconds);
            yield return Recover();
        }

        private IEnumerator Dash(float dashSeconds)
        {
            Vector3 direction = transform.forward;
            float   elapsed   = 0f;

            while (elapsed < dashSeconds)
            {
                elapsed += Time.deltaTime;
                transform.position = _startPosition + (direction * Mathf.Min(_dashSpeed * elapsed, _dashDistance));
                yield return null;
            }
        }

        // 돌진이 끝나면 재생 속도를 1배까지 서서히 낮추면서 Idle로 길게 섞는다
        // 재생 속도가 높을 때는 전환이 실제 시간보다 빨리 끝나므로 평균 속도만큼 길이를 늘린다
        private IEnumerator Recover()
        {
            float dashPlaySpeed = _animator.speed;
            float averageSpeed  = (dashPlaySpeed + 1f) * HALF;
            _animator.CrossFadeInFixedTime(IDLE_STATE, _recoverSeconds * averageSpeed, 0, 0f);

            float elapsed = 0f;

            while (elapsed < _recoverSeconds)
            {
                elapsed += Time.deltaTime;
                float t     = Mathf.Clamp01(elapsed / _recoverSeconds);
                float eased = 1f - ((1f - t) * (1f - t));
                _animator.speed = Mathf.Lerp(dashPlaySpeed, 1f, eased);
                yield return null;
            }

            _animator.speed = 1f;
        }
    }
}
