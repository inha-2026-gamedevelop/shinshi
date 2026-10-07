// System
using System;
using System.Globalization;
using System.Text;

namespace Shinshi.Network
{
    /// <summary> Prometheus 텍스트 노출 형식(0.0.4) 한 덩어리를 만든다. 한 스레드에서만 쓴다 </summary>
    public sealed class PrometheusTextWriter
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CONTENT_TYPE = "text/plain; version=0.0.4; charset=utf-8";

        private const string TYPE_GAUGE     = "gauge";
        private const string TYPE_COUNTER   = "counter";
        private const string TYPE_HISTOGRAM = "histogram";
        private const string LABEL_LE       = "le";
        private const string BUCKET_SUFFIX  = "_bucket";
        private const string SUM_SUFFIX     = "_sum";
        private const string COUNT_SUFFIX   = "_count";
        private const string POSITIVE_INF   = "+Inf";
        private const string NEGATIVE_INF   = "-Inf";
        private const string NOT_A_NUMBER   = "NaN";

        private readonly StringBuilder _builder;

        /****************************************
        *                Methods
        ****************************************/

        public PrometheusTextWriter(int capacity)
        {
            _builder = new StringBuilder(capacity);
        }

        public void Clear()
        {
            _builder.Clear();
        }

        public override string ToString()
        {
            return _builder.ToString();
        }

        public void WriteGaugeHeader(string name, string help)     { WriteHeader(name, TYPE_GAUGE, help); }
        public void WriteCounterHeader(string name, string help)   { WriteHeader(name, TYPE_COUNTER, help); }
        public void WriteHistogramHeader(string name, string help) { WriteHeader(name, TYPE_HISTOGRAM, help); }

        public void WriteSample(string name, double value)
        {
            _builder.Append(name).Append(' ');
            AppendValue(value);
            _builder.Append('\n');
        }

        public void WriteSample(string name, long value)
        {
            _builder.Append(name).Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        public void WriteSample(string name, string labelName, string labelValue, long value)
        {
            _builder.Append(name).Append('{');
            AppendLabel(labelName, labelValue);
            _builder.Append("} ").Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        /// <summary> 이름과 값 배열의 길이가 같아야 한다 </summary>
        public void WriteSample(string name, string[] labelNames, string[] labelValues, long value)
        {
            if ((labelNames == null) || (labelValues == null) || (labelNames.Length != labelValues.Length))
            {
                throw new ArgumentException("라벨 이름과 값의 개수가 다르다");
            }

            _builder.Append(name).Append('{');
            for (int i = 0; i < labelNames.Length; ++i)
            {
                if (i > 0)
                {
                    _builder.Append(',');
                }

                AppendLabel(labelNames[i], labelValues[i]);
            }

            _builder.Append("} ").Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        /// <summary> 누적 칸 하나, 상한이 양의 무한대면 +Inf 칸이다 </summary>
        public void WriteBucket(string name, double upperBound, long cumulativeCount)
        {
            _builder.Append(name).Append(BUCKET_SUFFIX).Append('{').Append(LABEL_LE).Append("=\"");
            AppendValue(upperBound);
            _builder.Append("\"} ").Append(cumulativeCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        public void WriteHistogramTotals(string name, double sum, long count)
        {
            WriteSample(name + SUM_SUFFIX, sum);
            WriteSample(name + COUNT_SUFFIX, count);
        }

        /// <summary> 라벨 값의 역슬래시, 큰따옴표, 줄바꿈을 형식 규칙대로 바꾼다 </summary>
        public static string EscapeLabelValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }

        private void WriteHeader(string name, string type, string help)
        {
            string escapedHelp = (help ?? string.Empty).Replace("\\", "\\\\").Replace("\n", "\\n");
            _builder.Append("# HELP ").Append(name).Append(' ').Append(escapedHelp).Append('\n');
            _builder.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
        }

        private void AppendLabel(string labelName, string labelValue)
        {
            _builder.Append(labelName).Append("=\"").Append(EscapeLabelValue(labelValue)).Append('"');
        }

        private void AppendValue(double value)
        {
            if (double.IsNaN(value))
            {
                _builder.Append(NOT_A_NUMBER);
                return;
            }

            if (double.IsPositiveInfinity(value))
            {
                _builder.Append(POSITIVE_INF);
                return;
            }

            if (double.IsNegativeInfinity(value))
            {
                _builder.Append(NEGATIVE_INF);
                return;
            }

            _builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
