using System;
using System.Windows;
using System.Windows.Media;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>屏幕工作区定位（光标所在屏幕，单位统一换算为 WPF 的 DIP）。</summary>
    public static class ScreenHelper
    {
        /// <summary>
        /// 返回提醒浮层应出现的屏幕工作区（DIP）。
        /// 规则：**只要存在副屏，就永远在副屏弹出**；没有副屏才用主屏。
        /// 定位不依赖「鼠标当前在哪个屏幕」（那会导致提醒随鼠标乱跑）。
        /// </summary>
        public static Rect GetWorkArea()
        {
            try
            {
                System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
                if (screens != null && screens.Length > 1)
                {
                    for (int i = 0; i < screens.Length; i++)
                    {
                        if (!screens[i].Primary)
                        {
                            return ToDip(screens[i].WorkingArea);
                        }
                    }
                }
                if (screens != null && screens.Length > 0)
                {
                    return ToDip(screens[0].WorkingArea);
                }
                return SystemParameters.WorkArea;
            }
            catch (Exception ex)
            {
                LogWriter.Write("获取屏幕工作区失败，回退系统工作区", ex);
                return SystemParameters.WorkArea;
            }
        }

        /// <summary>是否存在副屏（用于设置窗提示与日志）。</summary>
        public static bool HasSecondaryScreen()
        {
            try
            {
                System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
                return screens != null && screens.Length > 1;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>把 WinForms 的物理像素矩形换算为 DIP 矩形。</summary>
        public static Rect ToDip(System.Drawing.Rectangle area)
        {
            Matrix transform = GetTransformFromDevice();
            Point topLeft = transform.Transform(new Point(area.Left, area.Top));
            Point bottomRight = transform.Transform(new Point(area.Right, area.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        public static Matrix GetTransformFromDevice()
        {
            try
            {
                if (Application.Current != null && Application.Current.MainWindow != null)
                {
                    PresentationSource source = PresentationSource.FromVisual(Application.Current.MainWindow);
                    if (source != null && source.CompositionTarget != null)
                    {
                        return source.CompositionTarget.TransformFromDevice;
                    }
                }
            }
            catch (Exception)
            {
                // 忽略：回退 1:1
            }
            return Matrix.Identity;
        }
    }
}
