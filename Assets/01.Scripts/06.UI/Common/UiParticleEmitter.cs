// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.UI
{
    /// <summary>캔버스 안에서 빛 알갱이를 뿌리는 가벼운 파티클이며 한 번에 터뜨리기와 계속 흘리기를 지원한다</summary>
    // ParticleSystem은 Screen Space - Overlay 캔버스에 그려지지 않고 UI 파티클 패키지는 설치돼 있지 않아,
    // 미리 만든 Image를 돌려 쓰며 직접 움직인다. 캔버스 다시 그리기가 창 전체로 번지지 않게 이 오브젝트에 하위 Canvas를 둔다
    [RequireComponent(typeof(RectTransform), typeof(Canvas))]
    public sealed class UiParticleEmitter : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        private struct Particle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float   Age;
            public float   Life;
            public float   Size;
            public float   Spin;
            public float   Angle;
            public bool    Alive;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const float FULL_TURN    = 360f;
        private const float HALF         = 0.5f;
        private const float ONE_PARTICLE = 1f;
        private const float MAX_STEP     = 0.1f;

        [Header("모양")]
        [SerializeField] private Sprite        _sprite       = null;
        [SerializeField] private int           _maxParticles = 48;
        [SerializeField] private RectTransform _area         = null;

        [Header("방출")]
        [SerializeField] private bool  _playOnEnable = false;
        [SerializeField] private int   _burstCount   = 0;
        [SerializeField] private float _rate         = 0f;

        [Header("움직임 (초, 픽셀)")]
        [SerializeField] private Vector2 _lifetime  = new Vector2(1f, 2f);
        [SerializeField] private Vector2 _speed     = new Vector2(40f, 120f);
        [SerializeField] private float   _direction = 90f;
        [SerializeField] private float   _spread    = 360f;
        [SerializeField] private Vector2 _gravity   = Vector2.zero;
        [SerializeField] private float   _drag      = 0f;
        [SerializeField] private Vector2 _spin      = Vector2.zero;

        [Header("모습")]
        [SerializeField] private Vector2        _size          = new Vector2(8f, 16f);
        [SerializeField] private AnimationCurve _sizeOverLife  = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [SerializeField] private Gradient       _colorOverLife = new Gradient();

        private Particle[]      _particles = null;
        private RectTransform[] _rects     = null;
        private Image[]         _images    = null;
        private float           _pending   = 0f;
        private bool            _emitting  = false;
        private Color           _tint      = Color.white;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsEmitting => _emitting;

        /// <summary>색 그라데이션에 곱하는 색이며 결과에 따라 같은 연출을 다른 색으로 쓸 때 바꾼다</summary>
        public Color Tint
        {
            get { return _tint; }
            set { _tint = value; }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake() { BuildPool(); }

        private void OnEnable()
        {
            if (_playOnEnable)
            {
                Play();
            }
        }

        private void OnDisable() { Clear(); }

        private void Update()
        {
            if (_particles == null)
            {
                return;
            }

            // 결과 화면처럼 게임 시간이 멈춘 동안에도 흘러야 하므로 시간 배율을 무시한다.
            // 화면이 잠깐 멈췄다 풀린 첫 프레임에 터뜨린 알갱이가 한꺼번에 수명을 다하지 않도록 한 번에 흐르는 시간을 자른다
            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, MAX_STEP);

            if (_emitting && (_rate > 0f))
            {
                _pending += _rate * deltaTime;

                while (_pending >= ONE_PARTICLE)
                {
                    _pending -= ONE_PARTICLE;
                    Spawn();
                }
            }

            Simulate(deltaTime);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>설정한 개수만큼 터뜨리고 흘리기 속도가 있으면 계속 흘린다</summary>
        public void Play()
        {
            _emitting = true;
            _pending  = 0f;
            Burst(_burstCount);
        }

        /// <summary>새로 만들지만 않고 떠 있는 알갱이는 수명이 끝날 때까지 둔다</summary>
        public void Stop() { _emitting = false; }

        public void Burst(int count)
        {
            for (int i = 0; i < count; ++i)
            {
                Spawn();
            }
        }

        public void Clear()
        {
            _emitting = false;

            if (_particles == null)
            {
                return;
            }

            for (int i = 0; i < _particles.Length; ++i)
            {
                _particles[i].Alive = false;
                _images[i].enabled  = false;
            }
        }

        private void BuildPool()
        {
            if ((_particles != null) || (_maxParticles <= 0))
            {
                return;
            }

            _particles = new Particle[_maxParticles];
            _rects     = new RectTransform[_maxParticles];
            _images    = new Image[_maxParticles];

            for (int i = 0; i < _maxParticles; ++i)
            {
                GameObject go = new GameObject("Particle", typeof(RectTransform), typeof(Image));
                go.layer = gameObject.layer;

                RectTransform rect = (RectTransform)go.transform;
                rect.SetParent(transform, false);

                Image image = go.GetComponent<Image>();
                image.sprite        = _sprite;
                image.raycastTarget = false;
                image.enabled       = false;

                _rects[i]  = rect;
                _images[i] = image;
            }
        }

        private void Spawn()
        {
            int index = FindFreeSlot();

            if (index < 0)
            {
                return;
            }

            Rect    area   = GetSpawnArea();
            float   angle  = (_direction + (Random.Range(-_spread, _spread) * HALF)) * Mathf.Deg2Rad;
            float   speed  = Random.Range(_speed.x, _speed.y);
            Vector2 offset = new Vector2(Random.Range(area.xMin, area.xMax), Random.Range(area.yMin, area.yMax));

            _particles[index] = new Particle
            {
                Position = offset,
                Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed,
                Age      = 0f,
                Life     = Random.Range(_lifetime.x, _lifetime.y),
                Size     = Random.Range(_size.x, _size.y),
                Spin     = Random.Range(_spin.x, _spin.y),
                Angle    = Random.Range(0f, FULL_TURN),
                Alive    = true
            };

            _images[index].enabled = true;
        }

        private void Simulate(float deltaTime)
        {
            float damping = Mathf.Clamp01(1f - (_drag * deltaTime));

            for (int i = 0; i < _particles.Length; ++i)
            {
                if (!_particles[i].Alive)
                {
                    continue;
                }

                Particle p = _particles[i];
                p.Age += deltaTime;

                if (p.Age >= p.Life)
                {
                    p.Alive            = false;
                    _particles[i]      = p;
                    _images[i].enabled = false;
                    continue;
                }

                p.Velocity  = (p.Velocity + (_gravity * deltaTime)) * damping;
                p.Position += p.Velocity * deltaTime;
                p.Angle    += p.Spin * deltaTime;
                _particles[i] = p;

                float t    = p.Age / p.Life;
                float size = p.Size * _sizeOverLife.Evaluate(t);

                RectTransform rect = _rects[i];
                rect.anchoredPosition = p.Position;
                rect.sizeDelta        = new Vector2(size, size);
                rect.localRotation    = Quaternion.Euler(0f, 0f, p.Angle);
                _images[i].color      = _colorOverLife.Evaluate(t) * _tint;
            }
        }

        private int FindFreeSlot()
        {
            if (_particles == null)
            {
                return -1;
            }

            for (int i = 0; i < _particles.Length; ++i)
            {
                if (!_particles[i].Alive)
                {
                    return i;
                }
            }

            return -1;
        }

        // 방출 영역을 이 오브젝트 기준 좌표로 돌려준다. 영역을 지정하지 않으면 자기 크기를 쓴다
        private Rect GetSpawnArea()
        {
            RectTransform self = (RectTransform)transform;

            if ((_area == null) || (_area == self))
            {
                return self.rect;
            }

            Vector3 min = self.InverseTransformPoint(_area.TransformPoint(_area.rect.min));
            Vector3 max = self.InverseTransformPoint(_area.TransformPoint(_area.rect.max));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
