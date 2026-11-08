using System;
using System.Globalization;
using System.IO;
using ScheduleReminder.Models;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 背景图片的**内部仓库**：用户选中的图片一律复制到 data\background\ 下并改成唯一名，
    /// 程序只引用这份内部副本——原文件之后被移动 / 改名 / 删除都不再影响背景。
    ///
    /// 两个要点：
    /// 1) **唯一名**（bg_年月日_时分秒_毫秒 + 原扩展名）：主题层的背景图缓存只比对路径，
    ///    固定名会让"换图后仍显示旧图"；唯一名天然绕开这个坑。
    /// 2) **目录即镜像**：每次设置 / 清空之后，目录里只保留当前正在用的那一张，其余 bg_* 一律清掉，
    ///    因此"换图替换旧图"、"清空时删除图片"都不需要额外的状态记录。
    ///
    /// 安全约束：清理只处理**本目录内**、且文件名带 bg_ 前缀的文件，绝不触碰目录外的任何文件。
    /// </summary>
    public static class BackgroundImageStore
    {
        private const string FilePrefix = "bg_";

        /// <summary>内部目录（data\background）。</summary>
        public static string DirectoryPath
        {
            get { return AppPaths.BackgroundDirectory; }
        }

        /// <summary>把「设置里存的文件名」拼成当前完整路径；空值返回 null。
        /// 只认文件名：传入绝对路径时只取文件名部分，避免拼出非法路径。</summary>
        public static string ResolveFullPath(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
            {
                return null;
            }
            return Path.Combine(DirectoryPath, Path.GetFileName(stored));
        }

        /// <summary>
        /// 把外部图片复制进内部目录并改名，返回内部**文件名**（设置里只存它）；失败返回 null
        /// （调用方不要写设置，保持原样并提示）。
        /// </summary>
        public static string Import(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                return null;
            }
            try
            {
                if (!Directory.Exists(DirectoryPath))
                {
                    Directory.CreateDirectory(DirectoryPath);
                }
                string extension = Path.GetExtension(sourcePath);
                if (string.IsNullOrEmpty(extension))
                {
                    extension = ".png";
                }
                string name = FilePrefix
                    + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
                    + extension.ToLowerInvariant();
                string destination = Path.Combine(DirectoryPath, name);
                File.Copy(sourcePath, destination, true);
                return name;
            }
            catch (Exception ex)
            {
                LogWriter.Write("背景图片复制失败：" + sourcePath, ex);
                return null;
            }
        }

        /// <summary>
        /// 目录即镜像：删除内部目录里所有 bg_* 文件，只保留 keepPath 这一张（传 null = 全部删除）。
        /// 单个文件删不掉（被占用等）只记日志，不影响其它文件与调用方流程。
        /// </summary>
        public static void CleanupExcept(string keepPath)
        {
            try
            {
                if (!Directory.Exists(DirectoryPath))
                {
                    return;
                }
                // keepPath 可能是设置里存的文件名，也可能是旧值的绝对路径；
                // 一律按内部目录解析，避免把正在用的图当成「多余副本」删掉。
                string keep = ResolveFullPath(keepPath);
                if (keep != null)
                {
                    keep = Path.GetFullPath(keep);
                }
                string[] files = Directory.GetFiles(DirectoryPath, FilePrefix + "*");
                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    if (keep != null && string.Equals(Path.GetFullPath(file), keep, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        LogWriter.Write("背景图片清理失败：" + file, ex);
                    }
                }
            }
            catch (Exception ex)
            {
                LogWriter.Write("背景图片目录清理失败", ex);
            }
        }

    }
}
