// System
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Shinshi.Launcher.Presentation
{
    /// <summary> 열거값이나 문자열이 매개변수와 같으면 true. 라디오 버튼 선택에 쓰고, 선택될 때만 값을 되돌려 쓴다 </summary>
    public sealed class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if ((value == null) || (parameter == null))
            {
                return false;
            }

            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if ((value is not bool isChecked) || !isChecked || (parameter == null))
            {
                return Binding.DoNothing;
            }

            string text = parameter.ToString() ?? string.Empty;
            if (!targetType.IsEnum)
            {
                return text;
            }

            return Enum.Parse(targetType, text);
        }
    }

    /// <summary> 열거값이 매개변수 이름과 같으면 Visible </summary>
    public sealed class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if ((value == null) || (parameter == null))
            {
                return Visibility.Collapsed;
            }

            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) { return Binding.DoNothing; }
    }

    /// <summary> true면 Visible, 매개변수가 "Invert"면 반대 </summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        private const string INVERT = "Invert";

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isVisible = (value is bool flag) && flag;
            if (string.Equals(parameter as string, INVERT, StringComparison.Ordinal))
            {
                isVisible = !isVisible;
            }

            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) { return Binding.DoNothing; }
    }

    /// <summary> 빈 문자열이나 null이면 Collapsed. 객체에도 쓴다 </summary>
    public sealed class EmptyToCollapsedConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return Visibility.Collapsed;
            }

            if ((value is string text) && (text.Length == 0))
            {
                return Visibility.Collapsed;
            }

            return Visibility.Visible;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) { return Binding.DoNothing; }
    }
}
