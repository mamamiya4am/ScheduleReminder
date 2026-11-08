using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScheduleReminder.Services;
using ScheduleReminder.Tools;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 小工具集合面板：挂在主界面左列「小工具」按钮上，点击后在左上角出现，
    /// 鼠标离开、失焦或点击外部时关闭；内容按 ToolRegistry 注册顺序自动生成。
    /// 新增小工具不需要改这里——只要在启动时注册，它就会自己出现在面板里。
    /// </summary>
    public partial class ToolBoxFlyout : UserControl
    {
        public ToolBoxFlyout()
        {
            InitializeComponent();
        }

        /// <summary>点击按钮打开：左上角与按钮左上角重合。</summary>
        public void ShowFor(FrameworkElement anchor)
        {
            if (anchor == null)
            {
                return;
            }
            BuildItems();

            Host.PlacementTarget = anchor;
            Host.IsOpen = true;
        }

        /// <summary>面板当前是否展开（供主界面按钮做开关切换）。</summary>
        public bool IsOpen
        {
            get { return Host.IsOpen; }
        }

        /// <summary>收起面板。</summary>
        public void ClosePanel()
        {
            Host.IsOpen = false;
        }

        private void BuildItems()
        {
            List<ToolEntry> rows = new List<ToolEntry>();
            IList<ITool> tools = ToolRegistry.GetTools();
            for (int i = 0; i < tools.Count; i++)
            {
                ITool tool = tools[i];
                if (tool == null)
                {
                    continue;
                }
                Geometry icon = string.IsNullOrEmpty(tool.IconKey)
                    ? null
                    : TryFindResource(tool.IconKey) as Geometry;
                rows.Add(new ToolEntry
                {
                    Id = tool.Id,
                    DisplayName = tool.DisplayName,
                    Icon = icon
                });
            }
            ToolList.ItemsSource = rows;
        }

        private void OnPopupOpened(object sender, EventArgs e)
        {
            // Popup 在独立视觉树里，不继承主窗口内容上的 ScaleTransform，
            // 这里按当前缩放档位自己补一份，避免非 100% 档下弹出层比界面小一圈。
            double scale = ThemeService.UiScale;
            if (scale <= 0)
            {
                scale = 1.0;
            }
            PanelRoot.LayoutTransform = new ScaleTransform(scale, scale);
            Keyboard.Focus(PanelRoot);
        }

        private void OnPopupClosed(object sender, EventArgs e)
        {
            // 关闭后释放面板创建的临时对象，避免重复打开累积引用。
            ToolList.ItemsSource = null;
            PanelRoot.LayoutTransform = null;
            Host.PlacementTarget = null;
        }

        private void OnPanelMouseLeave(object sender, MouseEventArgs e)
        {
            ClosePanel();
        }

        private void OnPanelLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // 焦点在面板内部移动（例如点工具按钮）不关闭；焦点到面板外才关闭。
            if (IsInsidePanel(e.NewFocus as DependencyObject))
            {
                return;
            }
            ClosePanel();
        }

        private bool IsInsidePanel(DependencyObject element)
        {
            while (element != null)
            {
                if (ReferenceEquals(element, PanelRoot))
                {
                    return true;
                }

                DependencyObject parent = element is Visual
                    ? VisualTreeHelper.GetParent(element)
                    : null;
                element = parent ?? LogicalTreeHelper.GetParent(element);
            }
            return false;
        }

        private void OnToolClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }
            string id = button.Tag as string;
            ClosePanel();
            ToolHost.Open(id);
        }

        private void OnPanelKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                ClosePanel();
                e.Handled = true;
            }
        }

        /// <summary>面板一格的显示数据：标识用于打开，名称用于文字，图标为已解析好的几何图形。</summary>
        public sealed class ToolEntry
        {
            public string Id { get; set; }

            public string DisplayName { get; set; }

            public Geometry Icon { get; set; }
        }
    }
}
