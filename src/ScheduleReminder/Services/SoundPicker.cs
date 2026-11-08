using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 音效下拉：全程序唯一、共用的铃声选择器组件，供「新建日程窗 / 设置窗 / 标签编辑窗」
    /// 三处调用，行为与最初「系统设置」窗里的音效选择器**完全一致**：
    ///
    ///   ① 选项 = data\sound\ 下的全部音效文件 + 永远在最后的「-」（- = 不播放声音）；
    ///   ② 回显：selectedName 能解析到就选中它，文件已被删则回落到最后的「-」；
    ///   ③ 展开下拉时按磁盘现状刷新（用户新加了音效立刻可见；没变化就不动，避免闪烁）；
    ///   ④ 试听：悬停 / 键盘高亮某条 → 立即试听该条，同一条不重复连播；
    ///   ⑤ 兜底：展开后停留满 2 秒 → 试听一次当前选中项；
    ///   ⑥ 选中回调：用户在下拉展开态下改选 → 触发 onSelected（仅展开态，避免刷新时误触发）；
    ///      设置窗用它来「选中即存盘 + 让新铃声立即生效」，新建日程窗不需要就不传。
    ///
    /// 用法：
    ///   private SoundPicker _soundPicker;                                       // 字段
    ///   _soundPicker = SoundPicker.Load(SoundCombo, savedName);                 // 构造时一次（无回调）
    ///   _soundPicker = SoundPicker.Load(SoundCombo, savedName, OnSoundSelected);// 带「选中即处理」回调
    ///   string name = _soundPicker.SelectedName();                              // 取值
    ///   _soundPicker.Select(name);                                              // 按文件名选中（套用标签预设用）
    ///   _soundPicker.Refresh();                                                 // 重新扫描文件夹（保留当前选择）
    ///   _soundPicker.Dispose();                                                 // 关窗时调用
    /// </summary>
    public sealed class SoundPicker : IDisposable
    {
        /// <summary>「停留满 2 秒」的试听延时，与设置窗一致。</summary>
        private static readonly TimeSpan PreviewDelay = TimeSpan.FromSeconds(2);

        private readonly ComboBox _combo;
        private readonly DispatcherTimer _timer;

        /// <summary>选中即回调（仅下拉展开态下用户改选时触发）；不需要就不传。</summary>
        private readonly Action<string> _onSelected;

        /// <summary>下拉是否处于展开态（只有展开时才允许试听 / 触发回调）。</summary>
        private bool _dropdownOpen;

        /// <summary>上一次已试听的条目，用于「同一条不重复连播」。</summary>
        private ComboBoxItem _lastPreviewItem;

        private bool _disposed;

        private SoundPicker(ComboBox combo, Action<string> onSelected)
        {
            _combo = combo;
            _onSelected = onSelected;
            _timer = new DispatcherTimer { Interval = PreviewDelay };
            _timer.Tick += OnPreviewTick;

            _combo.DropDownOpened += OnDropDownOpened;
            _combo.DropDownClosed += OnDropDownClosed;
            _combo.SelectionChanged += OnSelectionChanged;
        }

        /// <summary>
        /// 重建音效下拉并挂上试听机制，返回实例供关窗时 Dispose。
        /// selectedName 为要回显的文件名；为空或文件已不存在都回落到最后的「-」。
        /// onSelected 可选：用户在下拉展开态下改选时回调（设置窗用来即时存盘 / 生效）。
        /// </summary>
        public static SoundPicker Load(ComboBox combo, string selectedName, Action<string> onSelected = null)
        {
            if (combo == null)
            {
                return null;
            }
            SoundPicker picker = new SoundPicker(combo, onSelected);
            picker.UnhookOldItems();
            picker.Fill(selectedName);
            return picker;
        }

        /// <summary>重新扫描 sound 目录并重建列表，保留当前选择（等价于设置窗原 LoadSoundCombo(SelectedName())）。</summary>
        public void Refresh()
        {
            if (_disposed)
            {
                return;
            }
            Fill(SelectedName());
        }

        /// <summary>
        /// 按文件名选中某项（供「套用标签预设」调用）：按磁盘现状重建列表并落到该文件；
        /// 传空 / 文件已不存在都回落到最后的「-」。不触碰下拉开合与试听机制。
        /// </summary>
        public void Select(string selectedName)
        {
            if (_disposed)
            {
                return;
            }
            Fill(selectedName);
        }

        /// <summary>取当前选中音效；没有选中时按「-」（不播放声音）处理。</summary>
        public string SelectedName()
        {
            ComboBoxItem item = _combo == null ? null : _combo.SelectedItem as ComboBoxItem;
            return item != null && item.Tag != null
                ? item.Tag.ToString()
                : SoundLibrary.NoneOption;
        }

        // ---------------- 列表填充（对应设置窗 LoadSoundCombo） ----------------

        private void Fill(string selectedName)
        {
            try
            {
                _combo.Items.Clear();

                List<string> names = SoundLibrary.ListNames();
                for (int i = 0; i < names.Count; i++)
                {
                    ComboBoxItem item = new ComboBoxItem();
                    item.Content = names[i];
                    item.Tag = names[i];
                    HookSoundItem(item);
                    _combo.Items.Add(item);
                }

                ComboBoxItem none = new ComboBoxItem();
                none.Content = SoundLibrary.NoneOption;
                none.Tag = SoundLibrary.NoneOption;
                HookSoundItem(none);
                _combo.Items.Add(none);

                // 回显：能解析到就选中对应项（按文件名比对），否则落在最后的「-」
                string want = SoundLibrary.NoneOption;
                string resolved = SoundLibrary.ResolvePath(selectedName);
                if (resolved != null)
                {
                    want = System.IO.Path.GetFileName(resolved);
                }
                int index = _combo.Items.Count - 1;
                for (int i = 0; i < _combo.Items.Count; i++)
                {
                    ComboBoxItem item = _combo.Items[i] as ComboBoxItem;
                    if (item != null
                        && string.Equals(item.Tag as string, want, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
                _combo.SelectedIndex = index;
            }
            catch (Exception ex)
            {
                LogWriter.Write("加载音效列表失败", ex);
            }
        }

        // ---------------- 试听机制（对应设置窗的展开刷新 / 高亮试听 / 2 秒兜底） ----------------

        private void OnDropDownOpened(object sender, EventArgs e)
        {
            // 用户可能刚往文件夹里加了新音效：展开时按磁盘现状刷新（没变化就不动，避免闪烁）
            List<string> names = SoundLibrary.ListNames();
            bool changed = names.Count + 1 != _combo.Items.Count;
            if (!changed)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    ComboBoxItem item = _combo.Items[i] as ComboBoxItem;
                    if (item == null
                        || !string.Equals(item.Tag as string, names[i], StringComparison.OrdinalIgnoreCase))
                    {
                        changed = true;
                        break;
                    }
                }
            }
            if (changed)
            {
                Fill(SelectedName());
            }
            _dropdownOpen = true;
            RestartPreview();
        }

        private void OnDropDownClosed(object sender, EventArgs e)
        {
            _dropdownOpen = false;
            _lastPreviewItem = null;
            _timer.Stop();
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_dropdownOpen)
            {
                return;
            }
            // 展开态下用户改选：先试听，再回调（设置窗用于存盘 / 立即生效）
            RestartPreview();
            if (_onSelected != null)
            {
                _onSelected(SelectedName());
            }
        }

        private void RestartPreview()
        {
            _timer.Stop();
            _timer.Start();
        }

        /// <summary>停留满 2 秒 → 试听一次当前选中项（只放一次，不循环）。</summary>
        private void OnPreviewTick(object sender, EventArgs e)
        {
            _timer.Stop();
            if (AppServices.Sound == null)
            {
                return;
            }
            AppServices.Sound.Preview(SelectedName());
        }

        /// <summary>
        /// 给音效项挂「高亮变化」监听：下拉展开时 ComboBox 会捕获鼠标，普通鼠标事件可能被拦截，
        /// 而列表项高亮（IsHighlighted）始终可靠——悬停 / 滚动经过 / 键盘移动都会改它，谁变就试听谁。
        /// </summary>
        private void HookSoundItem(ComboBoxItem item)
        {
            if (item == null)
            {
                return;
            }
            item.MouseEnter += OnItemMouseEnter; // 双保险：路由未被拦截时也能触发
            DependencyPropertyDescriptor
                .FromProperty(ComboBoxItem.IsHighlightedProperty, typeof(ComboBoxItem))
                .AddValueChanged(item, OnItemHighlightChanged);
        }

        /// <summary>某条音效获得高亮 → 立即试听该条。</summary>
        private void OnItemHighlightChanged(object sender, EventArgs e)
        {
            PreviewItem(sender as ComboBoxItem);
        }

        /// <summary>鼠标悬停某条音效（路由未被捕获拦截时生效）。</summary>
        private void OnItemMouseEnter(object sender, MouseEventArgs e)
        {
            PreviewItem(sender as ComboBoxItem);
        }

        /// <summary>试听指定条目：仅下拉展开时生效；同一条不重复连播。</summary>
        private void PreviewItem(ComboBoxItem item)
        {
            if (item == null || !_dropdownOpen || AppServices.Sound == null || item.Tag == null)
            {
                return;
            }
            if (item == _lastPreviewItem)
            {
                return;
            }
            _lastPreviewItem = item;
            // 取消还在排队的「展开后 2 秒试听」，避免离开后还响一声
            _timer.Stop();
            AppServices.Sound.Preview(item.Tag.ToString());
        }

        // ---------------- 收尾 ----------------

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            _timer.Stop();
            _timer.Tick -= OnPreviewTick;

            UnhookOldItems();

            if (_combo != null)
            {
                _combo.DropDownOpened -= OnDropDownOpened;
                _combo.DropDownClosed -= OnDropDownClosed;
                _combo.SelectionChanged -= OnSelectionChanged;
            }
        }

        /// <summary>解除条目上的高亮监听，避免重建下拉时句柄累积。</summary>
        private void UnhookOldItems()
        {
            if (_combo == null)
            {
                return;
            }
            DependencyPropertyDescriptor descriptor =
                DependencyPropertyDescriptor.FromProperty(ComboBoxItem.IsHighlightedProperty, typeof(ComboBoxItem));
            foreach (object o in _combo.Items)
            {
                ComboBoxItem item = o as ComboBoxItem;
                if (item != null)
                {
                    descriptor.RemoveValueChanged(item, OnItemHighlightChanged);
                }
            }
            _lastPreviewItem = null;
        }
    }
}