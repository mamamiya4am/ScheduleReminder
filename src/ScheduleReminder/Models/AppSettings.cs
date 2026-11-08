using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Models
{
    /// <summary>
    /// 应用设置（落盘在 程序目录\data\settings.json）。
    /// </summary>
    [DataContract]
    public class AppSettings
    {
        // ---------- 外观默认色（唯一来源：模型层定义。恢复默认 = 写回该值；渲染层兜底也取这里） ----------
        public const string DefaultGlobalTextColor = "#F2F2F2";
        public const string DefaultSystemBorderColor = "#FFFFFFFF";
        public const string DefaultTodayBorderColor = "#FFFFA726";
        public const string DefaultFocusBorderColor = "#FF6C8CFF";
        public const string DefaultControlFillColor = "#FF24242C";
        public const string DefaultWindowBgColor = "#FF101012";

        /// <summary>界面缩放：可选档位（与设置窗下拉完全一致，单一来源）与默认档。</summary>
        public static readonly int[] ValidScalePercents = { 80, 90, 100, 110, 120 };
        public const int DefaultUiScalePercent = 100;

        /// <summary>
        /// 窗口背景不透明度默认值（用户要求 90，见故障记录 OPT-006 修改意见 2）。
        /// 合法范围 10–100：低于 10 会让窗口背景 / 控件底色几乎完全透明，窗口无法拖拽与选中。
        /// </summary>
        public const int DefaultWindowBgOpacity = 90;
        public const int MinWindowBgOpacity = 10;
        public const int MaxWindowBgOpacity = 100;

        /// <summary>提醒浮层「基准尺寸」出厂默认与合法范围（最终显示尺寸 = 基准 × 界面大小档位系数）。</summary>
        public const double DefaultPopupWidth = 300;
        public const double DefaultPopupHeight = 176;

        /// <summary>
        /// 提醒浮层基准尺寸下限 = 出厂默认：防止存档异常（0 / 极小）导致浮层小到看不见。
        /// 注意：钳的是**基准值**，与「界面大小」档位（80%/90%/100%/110%/120%）互不干涉——
        /// 档位改的是乘在基准上的系数，钳基准不会限制档位缩放。
        /// </summary>
        public const double MinPopupWidth = DefaultPopupWidth;
        public const double MinPopupHeight = DefaultPopupHeight;
        public const double MaxPopupWidth = 600;
        public const double MaxPopupHeight = 400;

        // ---------- 悬停预览 ----------
        [DataMember(Name = "hoverDelaySeconds")]
        public double HoverDelaySeconds { get; set; }

        // ---------- 外观 ----------
        [DataMember(Name = "globalTextColor", EmitDefaultValue = false)]
        public string GlobalTextColor { get; set; }

        [DataMember(Name = "systemBorderColor", EmitDefaultValue = false)]
        public string SystemBorderColor { get; set; }

        [DataMember(Name = "todayBorderColor", EmitDefaultValue = false)]
        public string TodayBorderColor { get; set; }

        [DataMember(Name = "windowBgColor", EmitDefaultValue = false)]
        public string WindowBgColor { get; set; }

        /// <summary>
        /// 焦点边框颜色：控件获得焦点以及鼠标悬停/操作时，边框显示的颜色。
        /// null = 用默认强调色（蓝色 #FF6C8CFF）。
        /// </summary>
        [DataMember(Name = "focusBorderColor", EmitDefaultValue = false)]
        public string FocusBorderColor { get; set; }

        /// <summary>
        /// 控件填充色：按钮 / 输入框 / 下拉框等控件的底色。
        /// null = 用默认不透明深灰（#FF24242C），避免浅色文字在白底上不可见。
        /// </summary>
        [DataMember(Name = "controlFillColor", EmitDefaultValue = false)]
        public string ControlFillColor { get; set; }

        /// <summary>窗口背景图：只存 data\background\ 内的文件名，运行时由 AppPaths.BackgroundDirectory 拼完整路径。</summary>
        [DataMember(Name = "windowBgImage", EmitDefaultValue = false)]
        public string WindowBgImage { get; set; }

        [DataMember(Name = "windowBgStretch", EmitDefaultValue = false)]
        public string WindowBgStretch { get; set; }

        [DataMember(Name = "windowBgOpacity", EmitDefaultValue = false)]
        public int WindowBgOpacity { get; set; }

        [DataMember(Name = "customColors", EmitDefaultValue = false)]
        public List<string> CustomColors { get; set; }

        // ---------- 提示音 ----------
        [DataMember(Name = "soundFilePath", EmitDefaultValue = false)]
        public string SoundFilePath { get; set; }

        /// <summary>
        /// 界面整体缩放百分比。可选档位见 ValidScalePercents（与设置窗下拉一致），Normalize 会吸附到最近档。
        /// 作用：窗口尺寸、控件、字号、间距、浮层尺寸**全部等比缩放**（单一系数 uiScalePercent/100）。
        /// </summary>
        [DataMember(Name = "uiScalePercent")]
        public int UiScalePercent { get; set; }

        // ---------- 开机自启 ----------
        [DataMember(Name = "autoStart")]
        public bool AutoStart { get; set; }

        // ---------- 窗口位置记忆 ----------
        [DataMember(Name = "mainLeft", EmitDefaultValue = false)]
        public double MainLeft { get; set; }

        [DataMember(Name = "mainTop", EmitDefaultValue = false)]
        public double MainTop { get; set; }

        [DataMember(Name = "mainWidth", EmitDefaultValue = false)]
        public double MainWidth { get; set; }

        /// <summary>右侧待办面板是否收起（默认 false = 展开）。</summary>
        [DataMember(Name = "rightPanelCollapsed")]
        public bool RightPanelCollapsed { get; set; }

        /// <summary>AI 文件投递通道；默认关闭。</summary>
        [DataMember(Name = "aiIntegrationEnabled", EmitDefaultValue = false)]
        public bool AiIntegrationEnabled { get; set; }

        /// <summary>视图模式：month / week；默认 month。</summary>
        [DataMember(Name = "viewMode", EmitDefaultValue = false)]
        public string ViewMode { get; set; }

        [DataMember(Name = "mainHeight", EmitDefaultValue = false)]
        public double MainHeight { get; set; }

        // ---------- 新建窗 / 编辑窗尺寸记忆 ----------
        // 存的同样是「基准（100%）尺寸」= 实际尺寸 ÷ 缩放系数，避免下次打开重复放大。
        // 设计尺寸本身只写在各自 XAML 的 Width/Height 上，这里仅记录用户拉伸过的结果。
        // 0 = 「用户从未拉伸过」→ 打开时采用 XAML 里的设计尺寸（故此处不赋任何默认值）。
        [DataMember(Name = "createWidth", EmitDefaultValue = false)]
        public double CreateWidth { get; set; }

        [DataMember(Name = "createHeight", EmitDefaultValue = false)]
        public double CreateHeight { get; set; }

        [DataMember(Name = "editWidth", EmitDefaultValue = false)]
        public double EditWidth { get; set; }

        // ---------- 提醒浮层旧配置（仅为兼容旧 settings.json 保留；浮层已改为固定 300×176，运行时不再读写，仅 Normalize 做兼容校正） ----------
        [DataMember(Name = "popupOffsetRight")]
        public double PopupOffsetRight { get; set; }

        [DataMember(Name = "popupOffsetBottom")]
        public double PopupOffsetBottom { get; set; }

        [DataMember(Name = "popupWidth")]
        public double PopupWidth { get; set; }

        [DataMember(Name = "popupHeight")]
        public double PopupHeight { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            HoverDelaySeconds = 0.15;
            GlobalTextColor = DefaultGlobalTextColor;
            SystemBorderColor = DefaultSystemBorderColor;
            TodayBorderColor = DefaultTodayBorderColor;
            FocusBorderColor = DefaultFocusBorderColor;
            ControlFillColor = DefaultControlFillColor;
            WindowBgColor = DefaultWindowBgColor;
            AutoStart = false;
            WindowBgOpacity = DefaultWindowBgOpacity;
            PopupOffsetRight = 16;
            PopupOffsetBottom = 16;
            PopupWidth = DefaultPopupWidth;
            PopupHeight = DefaultPopupHeight;
            MainWidth = 0;
            MainHeight = 0;
            EditWidth = 0;
            WindowBgStretch = "UniformToFill";
            UiScalePercent = DefaultUiScalePercent;
            ViewMode = "month";
        }

        public AppSettings()
        {
            HoverDelaySeconds = 0.15;
            GlobalTextColor = DefaultGlobalTextColor;
            SystemBorderColor = DefaultSystemBorderColor;
            TodayBorderColor = DefaultTodayBorderColor;
            FocusBorderColor = DefaultFocusBorderColor;
            ControlFillColor = DefaultControlFillColor;
            WindowBgColor = DefaultWindowBgColor;
            AutoStart = false;
            WindowBgOpacity = DefaultWindowBgOpacity;
            WindowBgStretch = "UniformToFill";
            UiScalePercent = DefaultUiScalePercent;
            PopupOffsetRight = 16;
            PopupOffsetBottom = 16;
            PopupWidth = DefaultPopupWidth;
            PopupHeight = DefaultPopupHeight;
            MainWidth = 0;
            MainHeight = 0;
            EditWidth = 0;
            CustomColors = new List<string>();
            ViewMode = "month";
        }

        public void Normalize()
        {
            // 悬停延迟 / 背景透明度的合法性由设置窗输入时校验（数字过滤 + 失焦解析，非法回退已保存值）。
            // 界面缩放：取值吸附到可选档位，保证界面下拉永远能表示当前值。
            UiScalePercent = NearestValidScalePercent(UiScalePercent);
            // 颜色设置永不为空：缺失或为空时补上默认色（与其它设置的初始值机制一致）
            if (string.IsNullOrEmpty(GlobalTextColor))
            {
                GlobalTextColor = DefaultGlobalTextColor;
            }
            if (string.IsNullOrEmpty(SystemBorderColor))
            {
                SystemBorderColor = DefaultSystemBorderColor;
            }
            if (string.IsNullOrEmpty(TodayBorderColor))
            {
                TodayBorderColor = DefaultTodayBorderColor;
            }
            if (string.IsNullOrEmpty(FocusBorderColor))
            {
                FocusBorderColor = DefaultFocusBorderColor;
            }
            if (string.IsNullOrEmpty(ControlFillColor))
            {
                ControlFillColor = DefaultControlFillColor;
            }
            if (string.IsNullOrEmpty(WindowBgColor))
            {
                WindowBgColor = DefaultWindowBgColor;
            }
            if (string.IsNullOrEmpty(WindowBgStretch))
            {
                WindowBgStretch = "UniformToFill";
            }
            // 背景透明度钳制到 10–100（OPT-006）：保证落盘值不会低到让窗口无法拖拽 / 选中
            if (WindowBgOpacity < MinWindowBgOpacity || WindowBgOpacity > MaxWindowBgOpacity)
            {
                WindowBgOpacity = DefaultWindowBgOpacity;
            }
            // 提醒浮层基准尺寸钳制（只钳基准值，不影响「界面大小」档位缩放）：
            // 下限 = 出厂默认，避免异常存档让浮层小到看不见；上限避免异常存档把浮层撑满屏幕。
            if (PopupWidth < MinPopupWidth || PopupWidth > MaxPopupWidth)
            {
                PopupWidth = DefaultPopupWidth;
            }
            if (PopupHeight < MinPopupHeight || PopupHeight > MaxPopupHeight)
            {
                PopupHeight = DefaultPopupHeight;
            }
            if (CustomColors == null)
            {
                CustomColors = new List<string>();
            }
            if (!string.Equals(ViewMode, "week", StringComparison.OrdinalIgnoreCase))
            {
                ViewMode = "month";
            }
        }

        /// <summary>取最接近的可选缩放档位（保证模型取值永远是界面能表示的档位）。</summary>
        public static int NearestValidScalePercent(int percent)
        {
            int best = ValidScalePercents[0];
            int bestDiff = Math.Abs(percent - best);
            for (int i = 1; i < ValidScalePercents.Length; i++)
            {
                int diff = Math.Abs(percent - ValidScalePercents[i]);
                if (diff < bestDiff)
                {
                    best = ValidScalePercents[i];
                    bestDiff = diff;
                }
            }
            return best;
        }
    }
}
