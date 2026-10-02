// System
using System.Globalization;

namespace Shinshi.Launcher.Presentation
{
    /// <summary> 바이트 수와 전송 속도를 "123.4 MB" 형태로 쓴다 </summary>
    public static class ByteFormat
    {
        /****************************************
        *                Fields
        ****************************************/

        private const double UNIT_STEP     = 1024.0;
        private const string NUMBER_FORMAT = "0.0";
        private const string SPEED_SUFFIX  = "/s";

        private static readonly string[] G_Units = { "B", "KB", "MB", "GB", "TB" };

        /****************************************
        *                Methods
        ****************************************/

        public static string Format(long bytes)
        {
            if (bytes < 0)
            {
                bytes = 0;
            }

            double value = bytes;
            int    unit  = 0;
            while ((value >= UNIT_STEP) && (unit < (G_Units.Length - 1)))
            {
                value /= UNIT_STEP;
                ++unit;
            }

            if (unit == 0)
            {
                return bytes.ToString(CultureInfo.InvariantCulture) + " " + G_Units[unit];
            }

            return value.ToString(NUMBER_FORMAT, CultureInfo.InvariantCulture) + " " + G_Units[unit];
        }

        public static string FormatProgress(long completedBytes, long totalBytes) { return Format(completedBytes) + " / " + Format(totalBytes); }

        public static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0.0)
            {
                return string.Empty;
            }

            return Format((long)bytesPerSecond) + SPEED_SUFFIX;
        }
    }
}
