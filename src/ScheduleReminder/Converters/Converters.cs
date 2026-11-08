using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ScheduleReminder.Domain;
using ScheduleReminder.Services;

namespace ScheduleReminder.Converters
{
    /// <summary>十六进制色串（"#RRGGBB" / "#AARRGGBB"）→ SolidColorBrush；空值返回 null。</summary>
    public class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            try
            {
                Color color = (Color)ColorConverter.ConvertFromString(text);
                return new SolidColorBrush(color);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            SolidColorBrush brush = value as SolidColorBrush;
            if (brush == null)
            {
                return null;
            }
            return brush.Color.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 日程背景色 → 色块填充画笔（所见即所得：选了什么色就显示什么色）。
    /// 空值 / 透明 → 完全透明画笔（即无默认底色）。
    /// </summary>
    public class ScheduleBgBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                return new SolidColorBrush(Colors.Transparent);
            }
            try
            {
                Color color = (Color)ColorConverter.ConvertFromString(text);
                // 日程底色跟随「背景透明度」设置；文字由 ScheduleFontBrushConverter 处理，不受透明度影响。
                // 可选 ConverterParameter：额外 alpha 系数（过期行传 0.5，让底色随之减淡）
                double extraAlpha = 1.0;
                if (parameter != null)
                {
                    double parsed;
                    if (double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    {
                        extraAlpha = parsed;
                    }
                }
                byte alpha = (byte)Math.Round(color.A * ThemeService.BgOpacity * extraAlpha);
                return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            }
            catch (FormatException)
            {
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// 日程字体色 → 文字画笔；空值（透明 = 继承全局）→ 取当前 TextPrimaryBrush。
    /// </summary>
    public class ScheduleFontBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                if (Application.Current != null)
                {
                    object resource = Application.Current.Resources["TextPrimaryBrush"];
                    if (resource is Brush)
                    {
                        return resource;
                    }
                }
                return new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
            }
            try
            {
                Color color = (Color)ColorConverter.ConvertFromString(text);
                return new SolidColorBrush(color);
            }
            catch (FormatException)
            {
                return new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>空字符串 / null → 显示占位「-」。</summary>
    public class EmptyToDashConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                return "-";
            }
            return text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool && (bool)value;
            if (parameter != null && string.Equals(parameter.ToString(), "inverse", StringComparison.OrdinalIgnoreCase))
            {
                flag = !flag;
            }
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>邻月单元格整格显淡（opacity .4）。</summary>
    public class OtherMonthOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isCurrentMonth = value is bool && (bool)value;
            return isCurrentMonth ? 1.0 : 0.4;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>月格边框透明度：当月 0.4，邻月 0.4 × 0.4 = 0.16。</summary>
    public class MonthBorderOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isCurrentMonth = value is bool && (bool)value;
            return isCurrentMonth ? 0.4 : 0.16;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }
    }

    /// <summary>把 0..1 的 double 转成百分数文本（70%）。</summary>
    public class VolumePercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double v = value is double ? (double)value : 0;
            return ((int)Math.Round(v * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>过零长度 → Collapsed（用于「+N」溢出行、空态提示等）。</summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = 0;
            if (value is int)
            {
                count = (int)value;
            }
            else if (value is System.Collections.ICollection)
            {
                count = ((System.Collections.ICollection)value).Count;
            }
            bool visible = count > 0;
            if (parameter != null && string.Equals(parameter.ToString(), "inverse", StringComparison.OrdinalIgnoreCase))
            {
                visible = !visible;
            }
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
