using System.ComponentModel;

namespace ScheduleReminder.Tools.WorldClock
{
    /// <summary>
    /// 世界时钟列表中的一行。行对象常驻，整分刷新时只改时刻、日期、时差三个文本，
    /// 不重建列表——这样不会打断滚动位置，也不会丢掉悬停与焦点状态。
    /// </summary>
    public sealed class CityRow : INotifyPropertyChanged
    {
        private string _timeText = "--:--";
        private string _dateText = string.Empty;
        private string _differenceText = string.Empty;

        /// <summary>城市名，同时是这一行的标识（删除与查重用它）。</summary>
        public string Name { get; set; }

        /// <summary>国家名（中文）。</summary>
        public string Country { get; set; }

        /// <summary>当地时刻，显示到分。</summary>
        public string TimeText
        {
            get { return _timeText; }
            set
            {
                if (_timeText != value)
                {
                    _timeText = value;
                    Raise("TimeText");
                }
            }
        }

        /// <summary>当地日期，如「10月20日 · 周二」。</summary>
        public string DateText
        {
            get { return _dateText; }
            set
            {
                if (_dateText != value)
                {
                    _dateText = value;
                    Raise("DateText");
                }
            }
        }

        /// <summary>与本地的时差，如「+9 小时」「−12 小时」。</summary>
        public string DifferenceText
        {
            get { return _differenceText; }
            set
            {
                if (_differenceText != value)
                {
                    _differenceText = value;
                    Raise("DifferenceText");
                }
            }
        }

        private bool _canMoveUp;

        /// <summary>上移可用（首行置灰禁用）。</summary>
        public bool CanMoveUp
        {
            get { return _canMoveUp; }
            set
            {
                if (_canMoveUp != value)
                {
                    _canMoveUp = value;
                    Raise("CanMoveUp");
                }
            }
        }

        private bool _canMoveDown;

        /// <summary>下移可用（末行置灰禁用）。</summary>
        public bool CanMoveDown
        {
            get { return _canMoveDown; }
            set
            {
                if (_canMoveDown != value)
                {
                    _canMoveDown = value;
                    Raise("CanMoveDown");
                }
            }
        }

        private bool _isLast;

        /// <summary>是否为末行（末行不画分割线）。</summary>
        public bool IsLast
        {
            get { return _isLast; }
            set
            {
                if (_isLast != value)
                {
                    _isLast = value;
                    Raise("IsLast");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
