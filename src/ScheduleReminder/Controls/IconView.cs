using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScheduleReminder.Controls
{
    /// <summary>
    /// 统一矢量图标控件：只暴露「图标 / 尺寸 / 描边」三个属性。
    /// 线宽、端点、连接方式与描边默认值集中在 Themes/Generic.xaml 的默认样式里，
    /// 各窗口不再逐处手写 Path 的样板属性。
    /// </summary>
    public class IconView : Control
    {
        /// <summary>图标几何。声明为 Geometry 基类，GeometryGroup 与 PathGeometry 都能赋值。</summary>
        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(
                "Glyph",
                typeof(Geometry),
                typeof(IconView),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>显示尺寸（正方形边长，设备无关像素）。</summary>
        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register(
                "IconSize",
                typeof(double),
                typeof(IconView),
                new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        /// <summary>描边色。默认取 IconStrokeBrush；标题栏图标可覆盖为按钮的前景色以跟随悬停变色。</summary>
        public static readonly DependencyProperty IconStrokeProperty =
            DependencyProperty.Register(
                "IconStroke",
                typeof(Brush),
                typeof(IconView),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        static IconView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(IconView),
                new FrameworkPropertyMetadata(typeof(IconView)));
        }

        public Geometry Glyph
        {
            get { return (Geometry)GetValue(GlyphProperty); }
            set { SetValue(GlyphProperty, value); }
        }

        public double IconSize
        {
            get { return (double)GetValue(IconSizeProperty); }
            set { SetValue(IconSizeProperty, value); }
        }

        public Brush IconStroke
        {
            get { return (Brush)GetValue(IconStrokeProperty); }
            set { SetValue(IconStrokeProperty, value); }
        }
    }
}
