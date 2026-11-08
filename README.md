# ScheduleReminder（日程提醒）

一款极轻量化且无需联网的日程记录和提醒的 Windows 桌面工具。

事情可以只记一次，也可以按周、按月、按年重复，重复能设截止日期。到点会在屏幕右下角弹出置顶浮窗并响提示音，不容易漏掉会议、缴费、复诊这类容易忘的事；还能设提前几分钟提醒。

日程可以按标签分类、各自配色，在月历上按天聚合显示，过去和未来的日期都能翻回去查。工作与生活的事项放在一处安排。

技术栈 C# / .NET Framework 4.8 / WPF，零 NuGet 依赖；数据全部存本地 JSON，不联网、不需要账号。农历、节气、法定节假日由 BCL 的 `ChineseLunisolarCalendar` 加内置节气表推算，直接标在月历上。

本项目采用 MIT 许可证，详见 [LICENSE](LICENSE)。

## 一、下载与运行

本仓库目前提供源代码，可按「三、构建方式」自行编译；打包好的版本以后会放到本仓库 Releases。拿到程序包后，解压到任意有写权限的目录即可运行，不需要安装。

运行前请确认两点。目标机需已安装 .NET Framework 4.8（Windows 10 1903 及以上、Windows 11 自带；Windows 7 SP1 与 8.1 需要先装一次）。不要解压到 `C:\Program Files` 等只读位置，日程数据写在 exe 同目录的 `data\` 下，需要写权限。

程序未做代码签名，首次运行时 Windows 会提示「Windows 已保护你的电脑」。点「更多信息」→「仍要运行」即可，这不是病毒告警。

点窗口右上角的 X 是最小化到托盘，不是退出。要真正退出，请用托盘图标右键 → 退出。

## 二、工程结构

```
ScheduleReminder.sln
src\ScheduleReminder\
├─ ScheduleReminder.csproj        # net48 + UseWPF + UseWindowsForms，无 NuGet
├─ app.manifest                   # 单实例等声明
├─ App.xaml / App.xaml.cs         # 入口：服务初始化、托盘、提醒调度器启动
├─ Domain\                        # 纯规则层，零界面依赖
│                                 #   Schedule / ScheduleOccurrence / RecurrenceService
│                                 #   ScheduleRules / SolarTermTable / FestivalCalculator / TagTemplate
├─ Converters\                    # 颜色 / 空值 / 行高 等 XAML 转换器
├─ Models\                        # ViewModels / AppSettings / DateUtil
├─ Services\                      # 仓储 / 日历 / 提醒调度 / 声音 / 音效库 / 托盘 / 主题
│                                 #   / 屏幕 / 取色器辅助 / 开机自启 / 日志 / 备份 / 恢复
├─ Views\                         # MainWindow / HoverPreviewWindow / DetailWindow
│                                 #   EditWindow / CreateWindow / ReminderWindow / SettingsWindow
│                                 #   StatisticsWindow / TagTemplateWindow / MessageDialog
│                                 #   ColorPickerPopup / ToolBoxFlyout
│                                 #   CalculatorWindow / StopwatchWindow / UnitConverterWindow / WorldClockWindow
├─ Resources\icon.ico             # 窗口 / 托盘 / EXE 文件图标
├─ Resources\icon_taskbar.png     # 任务栏高清图标
├─ data\holidays.json             # 随包节假日数据（程序只读 data\）
├─ data_template\holidays.json    # 旧模板，保留作参考；发布使用 data\holidays.json
└─ sound\                         # 内置提示音（发布时复制到 data\sound\）
tools\                            # 节气生成脚本（开发期辅助，不参与构建）
```

## 三、构建方式

需要本机安装 **.NET Framework 4.8 运行时/引用程序集** 与 **Visual Studio 2019+**（或独立 MSBuild + .NET Framework 4.8 Targeting Pack）。

用 Visual Studio 打开 `ScheduleReminder.sln`，配置管理器选 `Release | Any CPU`（或 `Debug`），生成解决方案。也可以在命令行执行：

```
msbuild ScheduleReminder.sln /t:Restore,Build
```

目标框架为 `.NET Framework 4.8`（框架依赖部署，不内嵌运行时），覆盖 Windows 7 SP1 / Windows 10 / Windows 11。目标机未装 4.8 时程序无法启动，Windows 会弹出提示要求安装 .NET Framework 4.8。

## 四、一键发布

双击 `build\publish.bat`（或在命令行执行）：

- 以 `Release` 执行 `dotnet publish`；
- 自动整理到 `publish\ScheduleReminder\`：exe + 配置 + `Resources\icon.ico` + `Resources\icon_taskbar.png` + `data\holidays.json` + `data\sound\`（内置音效）；
- 发布脚本会校验图标、节假日和至少一个音效文件；
- 把整个文件夹拷到任意位置即可运行（框架依赖，目标机需 .NET Framework 4.8）；
- 日程数据保存在同目录 `data\`，跟着文件夹一起走。

## 五、已知限制

- 程序未做代码签名，首次运行会被 SmartScreen 拦截，放行方法见第一节。
- 用户自定义节假日覆盖层（`holidays_override.json`）尚未实现。
- 框架依赖部署，目标机必须已安装 .NET Framework 4.8。
- 日程数据存在程序目录 `data\`，因此不能安装在 `Program Files` 等只读位置。
