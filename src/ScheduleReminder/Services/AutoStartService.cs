using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 开机自启：在开始菜单「启动」文件夹写 / 删 .lnk 快捷方式，不写注册表 Run（P0-9）。
    /// 快捷方式用后期绑定 COM（WScript.Shell）创建，避免引用 Microsoft.CSharp / 引入第三方库。
    /// </summary>
    public static class AutoStartService
    {
        private const string LinkName = "ScheduleReminder.lnk";

        /// <summary>快捷方式完整路径（启动文件夹下）；拿不到返回空串。</summary>
        public static string GetShortcutPath()
        {
            try
            {
                string startDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (string.IsNullOrEmpty(startDir))
                {
                    return string.Empty;
                }
                return Path.Combine(startDir, LinkName);
            }
            catch (Exception ex)
            {
                LogWriter.Write("获取启动文件夹路径失败", ex);
                return string.Empty;
            }
        }

        /// <summary>是否已开启开机自启（按 .lnk 是否存在判断）。</summary>
        public static bool IsEnabled()
        {
            try
            {
                return File.Exists(GetShortcutPath());
            }
            catch (Exception ex)
            {
                LogWriter.Write("读取开机自启状态失败", ex);
                return false;
            }
        }

        /// <summary>开启 / 关闭开机自启；失败不抛异常，仅记录并返回 false。</summary>
        public static bool SetEnabled(bool enabled)
        {
            try
            {
                string path = GetShortcutPath();
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }
                if (enabled)
                {
                    if (File.Exists(path))
                    {
                        return true;
                    }
                    string exe = ExePath();
                    if (string.IsNullOrEmpty(exe))
                    {
                        return false;
                    }
                    object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                    if (shell == null)
                    {
                        return false;
                    }
                    object shortcut = shell.GetType().InvokeMember(
                        "CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                    shortcut.GetType().InvokeMember(
                        "TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exe });
                    shortcut.GetType().InvokeMember(
                        "WorkingDirectory", BindingFlags.SetProperty, null, shortcut,
                        new object[] { Path.GetDirectoryName(exe) });
                    shortcut.GetType().InvokeMember(
                        "Description", BindingFlags.SetProperty, null, shortcut, new object[] { "日程提醒" });
                    shortcut.GetType().InvokeMember(
                        "Save", BindingFlags.InvokeMethod, null, shortcut, null);
                    Marshal.ReleaseComObject(shortcut);
                    Marshal.ReleaseComObject(shell);
                    return true;
                }
                else
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogWriter.Write("设置开机自启失败", ex);
                DialogService.Alert("开机自启", "设置开机自启失败：" + ex.Message);
                return false;
            }
        }

        private static string ExePath()
        {
            try
            {
                Assembly asm = Assembly.GetEntryAssembly();
                if (asm != null && !string.IsNullOrEmpty(asm.Location))
                {
                    return asm.Location;
                }
            }
            catch (Exception ex)
            {
                LogWriter.Write("取 EXE 路径失败", ex);
            }
            try
            {
                return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            }
            catch (Exception ex)
            {
                LogWriter.Write("取 EXE 路径失败", ex);
                return string.Empty;
            }
        }
    }
}
