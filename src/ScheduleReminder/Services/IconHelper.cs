using System;
using System.Drawing;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 图标加载：Resources\icon.ico 同时供 系统托盘（System.Drawing.Icon）与 窗口标题栏（WPF ImageSource）使用。
    /// .ico 内含多尺寸，托盘 / 任务栏 / 标题栏都会自动挑最合适的一档，比旧版 PNG 转图标更清晰。
    /// 注意：必须长期持有 Icon / ImageSource 引用，否则托盘句柄失效、图标变空白。
    /// </summary>
    public static class IconHelper
    {
        private static Icon _icon;
        private static System.Windows.Media.ImageSource _imageSource;

        public static Icon GetIcon()
        {
            if (_icon != null)
            {
                return _icon;
            }
            _icon = new Icon(AppPaths.IconPath);
            return _icon;
        }

        public static System.Windows.Media.ImageSource GetImageSource()
        {
            if (_imageSource != null)
            {
                return _imageSource;
            }
            System.Windows.Media.Imaging.BitmapImage image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            // 任务栏图标用 512×512 的 PNG（避免任务栏对 PNG 压缩帧 ICO 的兼容问题导致显小）；
            // 托盘与 EXE 文件图标仍由 GetIcon() / Win32Icon 使用 icon.ico。
            image.UriSource = new Uri(AppPaths.PngIconPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            _imageSource = image;
            return _imageSource;
        }
    }
}
