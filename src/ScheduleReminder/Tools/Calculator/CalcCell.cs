using System.ComponentModel;

namespace ScheduleReminder.Tools.Calculator
{
    /// <summary>
    /// 级联单元基类：一个单元就是一行——要么是数字框，要么是运算符下拉。
    /// 值变化会发通知，引擎订阅后负责「生长 / 截断 / 重算」。
    /// </summary>
    public abstract class CalcCell : INotifyPropertyChanged
    {
        private string _value;
        private bool _isFirstNumber;

        public event PropertyChangedEventHandler PropertyChanged;

        protected CalcCell(string value)
        {
            _value = value == null ? string.Empty : value;
        }

        /// <summary>单元的值：数字框是数字文本，运算符下拉是运算符（空串表示未选择）。</summary>
        public string Value
        {
            get { return _value; }
            set
            {
                string next = value == null ? string.Empty : value;
                if (_value == next)
                {
                    return;
                }
                _value = next;
                RaisePropertyChanged("Value");
            }
        }

        /// <summary>true 表示运算符单元，false 表示数字单元。</summary>
        public virtual bool IsOperator
        {
            get { return false; }
        }

        /// <summary>是否是第一个数字行（首行显示「清空」，其余行显示「Del」）。</summary>
        public bool IsFirstNumber
        {
            get { return _isFirstNumber; }
            set
            {
                if (_isFirstNumber == value)
                {
                    return;
                }
                _isFirstNumber = value;
                RaisePropertyChanged("IsFirstNumber");
            }
        }

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    /// <summary>数字单元（偶数位）。</summary>
    public sealed class NumberCell : CalcCell
    {
        public NumberCell(string value) : base(value)
        {
        }
    }

    /// <summary>运算符单元（奇数位）。</summary>
    public sealed class OperatorCell : CalcCell
    {
        public OperatorCell(string value) : base(value)
        {
        }

        public override bool IsOperator
        {
            get { return true; }
        }
    }
}
