using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScheduleReminder.Models;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 外观主题：把 settings.json 里的颜色 / 背景图片应用到全局资源字典与各窗口。
    /// 颜色默认值统一定义在 AppSettings（唯一来源）。
    /// </summary>
    public static class ThemeService
    {
        // 颜色默认值统一定义在 AppSettings（唯一来源）。

        // 今日高光渐隐终点：月历与周历分开配置，但共用 CreateTodayGlowBrush。
        private const double MonthTodayGlowFade = 0.10;
        private const double WeekTodayGlowFade = 0.05;

        // ================= 界面整体缩放 =================

        /// <summary>当前缩放系数（1.0 = 100%，原始设计尺寸）。</summary>
        public static double UiScale
        {
            get
            {
                AppSettings settings = AppServices.Settings;
                if (settings == null)
                {
                    return 1.0;
                }
                return settings.UiScalePercent / 100.0;
            }
        }

        /// <summary>
        /// 背景透明度系数（0.0–1.0）。
        /// 与「颜色」走**完全同一条路径**：只写进画刷的 alpha（WindowBgBrush / ControlFillBrush / 日程色块），
        /// 不使用 window.Opacity——因此改设置后，所有通过 DynamicResource 引用这些资源的窗口**即时生效**，
        /// 不需要遍历窗口、也不会在任意操作过程中重复渲染。文字画笔与边框画笔不走这里，始终不透明。
        /// </summary>
        public static double BgOpacity
        {
            get
            {
                AppSettings settings = AppServices.Settings;
                if (settings == null)
                {
                    return 1.0;
                }
                double value = settings.WindowBgOpacity / 100.0;
                if (value < 0)
                {
                    value = 0;
                }
                if (value > 1)
                {
                    value = 1;
                }
                return value;
            }
        }

        /// <summary>
        /// 各窗口的「基准尺寸」登记表（宽、高、最小宽、最小高），用于缩放设置改变后统一重算。
        /// 注意：存的是**100% 时的原始值**，避免反复缩放导致尺寸累积放大。
        /// </summary>
        private static readonly Dictionary<Window, double[]> ScaleRegistry = new Dictionary<Window, double[]>();

        /// <summary>
        /// 对某个窗口应用整体缩放：内容用 ScaleTransform 等比缩放（控件 / 字号 / 间距 / 圆角全跟着变），
        /// 窗口自身宽高按同一系数放大，避免内容被裁切。
        /// </summary>
        public static void ApplyScale(Window window, double baseWidth, double baseHeight,
                                      double baseMinWidth = 0, double baseMinHeight = 0)
        {
            if (window == null)
            {
                return;
            }
            ScaleRegistry[window] = new[] { baseWidth, baseHeight, baseMinWidth, baseMinHeight };
            // 关闭即注销：登记过的窗口关闭后不被静态表持有
            window.Closed -= OnRegisteredWindowClosed;
            window.Closed += OnRegisteredWindowClosed;
            ApplyScaleCore(window, baseWidth, baseHeight, baseMinWidth, baseMinHeight);
        }

        private static void OnRegisteredWindowClosed(object sender, EventArgs e)
        {
            UnregisterScale(sender as Window);
        }

        /// <summary>仅更新某窗口的「基准尺寸」登记（供窗口被手动缩放后同步，不改变当前视觉）。</summary>
        public static void RegisterScaleBase(Window window, double baseWidth, double baseHeight,
                                            double baseMinWidth = 0, double baseMinHeight = 0)
        {
            if (window == null)
            {
                return;
            }
            ScaleRegistry[window] = new[] { baseWidth, baseHeight, baseMinWidth, baseMinHeight };
        }

        /// <summary>窗口关闭时移除缩放登记：新建 / 编辑窗、提醒浮层等会被反复创建，不移除会让旧实例一直被引用而无法回收。</summary>
        public static void UnregisterScale(Window window)
        {
            if (window == null)
            {
                return;
            }
            ScaleRegistry.Remove(window);
        }

        /// <summary>缩放设置改变后，对所有已登记窗口重新应用（无需重开程序）。</summary>
        public static void ReapplyScaleToAllWindows()
        {
            List<Window> windows = new List<Window>(ScaleRegistry.Keys);
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                double[] size;
                if (window == null || !ScaleRegistry.TryGetValue(window, out size))
                {
                    continue;
                }
                if (!window.IsLoaded)
                {
                    continue;
                }
                ApplyScaleCore(window, size[0], size[1], size[2], size[3]);
            }
        }

        private static void ApplyScaleCore(Window window, double baseWidth, double baseHeight,
                                           double baseMinWidth, double baseMinHeight)
        {
            double scale = UiScale;
            FrameworkElement content = window.Content as FrameworkElement;
            if (content != null)
            {
                content.LayoutTransform = new ScaleTransform(scale, scale);
            }

            // 三段式设置（OPT-010，修「缩放往返后窗口尺寸卡大」）：
            // ① 先把 MinWidth/MinHeight 归 0（解除旧档位留下的最小尺寸约束）；
            // ② 再写目标 Width/Height（此时不会被旧 Min 钳制成"上一档的尺寸"）；
            // ③ 最后恢复正确的 MinWidth/MinHeight。
            // 依据 .NET Framework 4.8 参考源码：Window 的最小尺寸在 WM_GETMINMAXINFO 按"当下"值钳制；
            // 而 OnMinWidthChanged 是单向的（调大立即撑大窗口、调小绝不缩回）。
            // 若"先 Min 后尺寸"，缩小方向会被旧 Min 钳住，停在上一档的尺寸，
            // 若"先 Min(新) 后尺寸"，放大方向会先跳到中间尺寸再到位（可见抖动）；三段式两方向均无中间帧。
            double newMinWidth = baseMinWidth > 0 ? baseMinWidth * scale : 0;
            double newMinHeight = baseMinHeight > 0 ? baseMinHeight * scale : 0;
            window.MinWidth = 0;
            window.MinHeight = 0;
            window.Width = baseWidth * scale;
            window.Height = baseHeight * scale;
            window.MinWidth = newMinWidth;
            window.MinHeight = newMinHeight;
        }

        public static void ApplySettings()
        {
            if (Application.Current == null)
            {
                return;
            }
            ResourceDictionary resources = Application.Current.Resources;
            AppSettings settings = AppServices.Settings;
            if (settings == null)
            {
                return;
            }

            // 主文字色：直接作用于 TextPrimaryBrush（就地修改，保证日程项里缓存的旧画笔引用也同步更新）。
            // 其余文字画笔由它派生（不同透明度），这样「改一处系统文字颜色 → 所有系统文字一起变」。
            SetBrush(resources, "TextPrimaryBrush", settings.GlobalTextColor);
            SetTextShade(resources, "TextSecondaryBrush", settings.GlobalTextColor, 0.78);
            SetTextShade(resources, "TextNoteBrush", settings.GlobalTextColor, 0.70);
            SetTextShade(resources, "TextPastBrush", settings.GlobalTextColor, 0.50);

            // 控件填充色（按钮 / 输入框 / 下拉框底色）：alpha 跟随「背景透明度」设置
            SetBrushWithAlpha(resources, "ControlFillBrush", settings.ControlFillColor, BgOpacity);

            // 边框 / 强调色：不透明，不受背景透明度影响
            SetBrush(resources, "BorderWeakBrush", settings.SystemBorderColor);
            SetBrush(resources, "BorderStrongBrush", settings.SystemBorderColor);
            // 图标线条色跟随「边框色」：不单独提供图标色设置，改边框色即改全部图标（ADR-009）
            SetBrush(resources, "IconStrokeBrush", settings.SystemBorderColor);
            SetBrush(resources, "TodayBorderBrush", settings.TodayBorderColor);

            // 今日高亮「线性渐变」高亮：从顶部边缘最浓（峰值 35%）、向下渐隐至透明。
            // 月历与周历使用同一个工厂，但渐隐终点分开配置：
            // 月历 0.10，周历 0.05；以后调高度只改这两个常量，不再各写一套 stops。
            // 仅设置变更时重建一次，不随渲染 / 滚动重复分配。
            Color todayGlow = ParseColor(settings.TodayBorderColor);
            resources["TodayGlowBrush"] = CreateTodayGlowBrush(todayGlow, MonthTodayGlowFade);
            resources["WeekTodayGlowBrush"] = CreateTodayGlowBrush(todayGlow, WeekTodayGlowFade);

            SetBrush(resources, "FocusBorderBrush", settings.FocusBorderColor);

            // 窗口背景（纯色 或 背景图）：只更新这一个资源，所有 DynamicResource 引用的窗口即时生效
            Brush windowBg = CreateWindowBgBrush(settings);
            if (windowBg != null)
            {
                resources["WindowBgBrush"] = windowBg;
            }

            // 需要排除背景图的窗口使用：始终纯色，alpha 仍跟随背景透明度
            SetBrushWithAlpha(resources, "WindowBgSolidBrush", settings.WindowBgColor, BgOpacity);

            // 提醒浮层专用底色：永远不透明，不受背景透明度影响
            SetBrush(resources, "PopupBgBrush", settings.WindowBgColor);

            // 悬停色块（含与之共用同一资源的选中 / 打钩 / 滚动条 / 徽章）：
            // 按「窗口背景色」的深浅自动在「黑 14% / 白 14%」之间反色 —— 浅底用黑、深底用白。
            // 只依据背景纯色判定，不考虑背景透明度、也不读背景图。
            SetBrushColor(resources, "BgHoverBrush", HoverBlockColor(settings.WindowBgColor));

            // 今日圆点上的日期数字：按「今日色」亮度自动黑 / 白（选与圆底对比度更高者）。
            SetBrushColor(resources, "TodayOnColorBrush", TodayTextColor(ParseColor(settings.TodayBorderColor)));
        }

        /// <summary>
        /// 生成窗口最终背景画笔。只写进资源 WindowBgBrush 这一处 → 所有 DynamicResource 引用它的窗口即时生效。
        /// - 无背景图：纯色，alpha = 设置透明度
        /// - 有背景图：背景颜色层 100% 透明（不画颜色），图片层 alpha = 设置透明度（透明度同样作用于图片）
        /// </summary>
        private static Brush CreateWindowBgBrush(AppSettings settings)
        {
            // 设置里只存文件名，这里统一解析成当前完整路径再判断 / 读取
            string bgPath = BackgroundImageStore.ResolveFullPath(settings.WindowBgImage);
            bool hasImage = !string.IsNullOrWhiteSpace(bgPath) && File.Exists(bgPath);
            if (hasImage)
            {
                Brush image = CreateImageBrush(settings, bgPath);
                if (image != null)
                {
                    return image;
                }
                LogWriter.Write("背景图片读取失败，回退纯色背景：" + bgPath);
            }

            Color color = ParseColor(settings.WindowBgColor);
            return new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(color.A * BgOpacity), color.R, color.G, color.B));
        }

        /// <summary>
        /// 背景图画笔：颜色层 100% 透明（不画颜色，直接透出桌面），图片本身按设置透明度半透明。
        /// </summary>
        private static Brush CreateImageBrush(AppSettings settings, string bgPath)
        {
            try
            {
                BitmapImage bitmap = GetBackgroundBitmap(bgPath);

                Stretch stretch = Stretch.Fill;
                // 「填充裁剪」：等比放大到刚好铺满整个窗口，不变形、不留边，超出部分裁掉。
                if (string.Equals(settings.WindowBgStretch, "UniformToFill", StringComparison.OrdinalIgnoreCase))
                {
                    stretch = Stretch.UniformToFill;
                }

                ImageBrush brush = new ImageBrush(bitmap);
                brush.Stretch = stretch;
                brush.AlignmentX = AlignmentX.Center;
                brush.AlignmentY = AlignmentY.Center;
                brush.Opacity = BgOpacity; // 背景透明度同样作用于背景图片
                brush.Freeze();
                return brush;
            }
            catch (Exception ex)
            {
                LogWriter.Write("创建背景画笔失败", ex);
                return null;
            }
        }

        // 背景图缓存：路径未变则复用，避免每次刷新资源（改颜色 / 改透明度）都重新解码同一张图
        private static BitmapImage _cachedBgBitmap;
        private static string _cachedBgPath;

        private static BitmapImage GetBackgroundBitmap(string path)
        {
            if (_cachedBgBitmap != null && string.Equals(_cachedBgPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedBgBitmap;
            }
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            _cachedBgBitmap = bitmap;
            _cachedBgPath = path;
            return bitmap;
        }

        /// <summary>
        /// 内存方案 B：隐藏到托盘时释放背景图位图。
        /// 位图被 ImageBrush 引用着就不会释放，所以这里把资源里的背景画笔换回纯色，
        /// 位图才能真正回收；重新显示时用 ApplySettings() 按需重建即可。
        /// </summary>
        public static void ReleaseBackgroundCache()
        {
            _cachedBgBitmap = null;
            _cachedBgPath = null;
            if (Application.Current == null || AppServices.Settings == null)
            {
                return;
            }
            Color color = ParseColor(AppServices.Settings.WindowBgColor);
            Application.Current.Resources["WindowBgBrush"] = new SolidColorBrush(
                Color.FromArgb((byte)Math.Round(color.A * BgOpacity), color.R, color.G, color.B));
        }

        /// <summary>
        /// **安全版**释放：只有在「除了 exclude 之外没有任何可见窗口」时才真的释放背景图位图。
        /// 返回 true = 已释放；false = 还有别的窗口开着（此时绝不能释放）。
        ///
        /// 为什么必须先判断：释放动作会把全局的 WindowBgBrush 换成纯色画笔，
        /// 而所有窗口的背景都引用这个资源——若此时还有别的窗口可见，它们的背景会**瞬间变纯色**。
        /// </summary>
        public static bool ReleaseBackgroundCacheIfIdle(Window exclude)
        {
            if (HasOtherWindowInUse(exclude))
            {
                return false;
            }
            ReleaseBackgroundCache();
            return true;
        }

        /// <summary>
        /// 该窗口是否「正在使用中」：只要它**还没关闭、且不是隐藏状态**就算在用。
        /// 口径：可见 = 在用；**最小化也算在用**（用户随时会还原，还原时必须能看到原来的背景图）；
        /// 只有真正被 Hide 的窗口（如悬停预览）或已关闭的窗口才算不占用背景。
        /// </summary>
        public static bool IsWindowInUse(Window window)
        {
            if (window == null)
            {
                return false;
            }
            return window.IsVisible || window.WindowState == WindowState.Minimized;
        }

        /// <summary>
        /// 是否存在「其它正在使用背景的窗口」（排除 exclude 自身）。
        /// 只有全部其它窗口都关闭（或处于隐藏状态）时才返回 false —— 即"只剩主窗"才允许释放。
        /// </summary>
        public static bool HasOtherWindowInUse(Window exclude)
        {
            if (Application.Current == null)
            {
                return false;
            }
            foreach (Window window in Application.Current.Windows)
            {
                if (window == null || window == exclude)
                {
                    continue;
                }
                if (IsWindowInUse(window))
                {
                    return true;
                }
            }
            return false;
        }



        /// <summary>写入画笔并把 alpha 按「背景透明度」系数缩放（文字 / 边框画笔不调用它，始终不透明）。</summary>
        private static void SetBrushWithAlpha(ResourceDictionary resources, string key, string colorText, double factor)
        {
            Color color = ParseColor(colorText);
            byte alpha = (byte)Math.Round(color.A * factor);
            SetBrushColor(resources, key, Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        private static void SetBrush(ResourceDictionary resources, string key, string colorText)
        {
            SetBrushColor(resources, key, ParseColor(colorText));
        }

        /// <summary>
        /// 由基础色派生一个「半透明同色」画笔（用于次级 / 注释 / 过往文字），
        /// 让它们跟随系统文字颜色但更淡。
        /// </summary>
        private static void SetTextShade(ResourceDictionary resources, string key, string colorText, double alpha)
        {
            Color baseColor = ParseColor(colorText);
            Color shaded = Color.FromArgb((byte)Math.Round(alpha * 255), baseColor.R, baseColor.G, baseColor.B);
            SetBrushColor(resources, key, shaded);
        }

        /// <summary>
        /// 写入颜色画笔：若资源里已有同键的 SolidColorBrush 且未冻结，则**就地修改其 Color**（画笔实例不变），
        /// 这样所有通过该实例引用它的地方（含日程项转换器抓到的旧引用）都会同步刷新；
        /// 否则替换为新画笔（供 DynamicResource 重新解析）。
        /// </summary>
        private static void SetBrushColor(ResourceDictionary resources, string key, Color color)
        {
            SolidColorBrush brush = resources[key] as SolidColorBrush;
            if (brush != null && !brush.IsFrozen)
            {
                brush.Color = color;
                return;
            }
            resources[key] = new SolidColorBrush(color);
        }

        /// <summary>把形如 #RRGGBB / #AARRGGBB 的串解析为 Color。</summary>
        public static Color ParseColor(string text)
        {
            return (Color)ColorConverter.ConvertFromString(text);
        }

        /// <summary>
        /// 悬停色块颜色：按「窗口背景色」的明暗在「黑 14% / 白 14%」之间自动反色。
        /// 明暗由 IsLightBackground 判定；只依据背景纯色，不考虑透明度与背景图。
        /// </summary>
        private static Color HoverBlockColor(string windowBgColor)
        {
            return InverseBlockColor(windowBgColor, 0x24);
        }

        /// <summary>日程反色块（标签背景 / 行内悬浮按钮共用）：优先日程背景色，空则用程序背景色。</summary>
        public static Brush ScheduleHoverBrush(string scheduleBgColor)
        {
            SolidColorBrush brush = new SolidColorBrush(InverseBlockColor(scheduleBgColor, 0x24));
            brush.Freeze();
            return brush;
        }

        /// <summary>日程勾选框勾色：同一反色判定，但用实色，避免 14% 透明看不见。</summary>
        public static Brush ScheduleCheckBrush(string scheduleBgColor)
        {
            SolidColorBrush brush = new SolidColorBrush(InverseBlockColor(scheduleBgColor, 0xFF));
            brush.Freeze();
            return brush;
        }

        /// <summary>标题栏搜索条底色：按窗口背景纯色反色，展开 25%、聚焦 32%。</summary>
        public static Brush SearchFillBrush(bool focused)
        {
            SolidColorBrush brush = new SolidColorBrush(
                InverseBlockColor(null, focused ? (byte)0x52 : (byte)0x40));
            brush.Freeze();
            return brush;
        }

        private static Color InverseBlockColor(string background, byte alpha)
        {
            Color color;
            if (!string.IsNullOrWhiteSpace(background))
            {
                color = ParseColor(background);
                if (color.A == 0)
                {
                    color = ParseColor(AppServices.Settings != null
                        ? AppServices.Settings.WindowBgColor
                        : "#FF202020");
                }
            }
            else
            {
                color = ParseColor(AppServices.Settings != null
                    ? AppServices.Settings.WindowBgColor
                    : "#FF202020");
            }

            bool isLight = IsLightBackground(color);
            return isLight
                ? Color.FromArgb(alpha, 0, 0, 0)
                : Color.FromArgb(alpha, 255, 255, 255);
        }

        /// <summary>
        /// 今日高亮渐变工厂：月历和周历只在渐隐终点上不同，其余 stops 完全一致。
        /// </summary>
        private static LinearGradientBrush CreateTodayGlowBrush(Color todayGlow, double fadeStop)
        {
            LinearGradientBrush brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.0, 0.0),
                EndPoint = new Point(0.0, 1.0)
            };
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(89, todayGlow.R, todayGlow.G, todayGlow.B), 0.0));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0, todayGlow.R, todayGlow.G, todayGlow.B), fadeStop));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0, todayGlow.R, todayGlow.G, todayGlow.B), 1.0));
            return brush;
        }

        /// <summary>
        /// 「今日色」圆底上的日期数字颜色：在黑 / 白之间取与圆底<b>对比度更高</b>的一方。
        /// 明暗由 IsLightBackground 判定，和反色块共用同一个入口。
        /// 深色字取近黑 #101012，与窗口底色同系，观感更统一。
        /// </summary>
        private static Color TodayTextColor(Color todayColor)
        {
            return IsLightBackground(todayColor)
                ? Color.FromRgb(0x10, 0x10, 0x12)
                : Color.FromRgb(0xFF, 0xFF, 0xFF);
        }

        /// <summary>
        /// 全程序唯一的明暗判定入口：纯黑与纯白谁在该底色上对比度更高。
        /// 等价于相对亮度 &gt;= 0.1791；不要在这里再引入 0.5 之类的第二阈值。
        /// </summary>
        public static bool IsLightBackground(Color background)
        {
            double luminance = RelativeLuminance(background);
            double contrastWithWhite = 1.05 / (luminance + 0.05);
            double contrastWithBlack = (luminance + 0.05) / 0.05;
            return contrastWithBlack >= contrastWithWhite;
        }

        /// <summary>WCAG 相对亮度：通道先做 sRGB→线性化再加权（与「悬停色块演示」同一算法）。</summary>
        private static double RelativeLuminance(Color color)
        {
            return 0.2126 * LinearChannel(color.R)
                 + 0.7152 * LinearChannel(color.G)
                 + 0.0722 * LinearChannel(color.B);
        }

        private static double LinearChannel(byte value)
        {
            double x = value / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
    }
}
