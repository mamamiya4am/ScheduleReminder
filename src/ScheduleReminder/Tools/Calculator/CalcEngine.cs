using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ScheduleReminder.Tools.Calculator
{
    /// <summary>
    /// 级联计算器引擎：纯逻辑，不依赖 WPF，可单独做单元测试。
    /// 规则逐条对应设计基准《计算器窗.html》：
    /// 严格从左到右求值；根号是二元运算符（右侧数字是根次数）；幂与根号是终止运算符。
    /// </summary>
    public sealed class CalcEngine : INotifyPropertyChanged
    {
        /// <summary>根次数小于等于零时的错误文案。</summary>
        public const string ErrorRootDegree = "根次数必须为正数";

        /// <summary>除数为零时的错误文案。</summary>
        public const string ErrorDivideByZero = "除数不能为 0";

        private const string OpAdd = "+";
        private const string OpSubtract = "-";
        private const string OpMultiply = "*";
        private const string OpDivide = "/";
        private const string OpPower = "^";
        private const string OpRoot = "√";

        private static readonly string[] SuperscriptDigits =
        {
            "⁰", "¹", "²", "³", "⁴", "⁵", "⁶", "⁷", "⁸", "⁹"
        };

        private readonly ObservableCollection<CalcCell> _cells = new ObservableCollection<CalcCell>();

        private string _expressionText = "—";
        private string _valueText = "0";
        private bool _hasError;
        private bool _selfCheckOk = true;

        // 自检用的分词器状态
        private List<string> _tokens = new List<string>();
        private int _pos;

        // 程序内部改值时置 true：只重算、不生长（对应原型里直接改 cells 值不走输入监听）
        private bool _suppressGrow;

        public event PropertyChangedEventHandler PropertyChanged;

        public CalcEngine()
        {
            Reset();
        }

        /// <summary>级联单元集合：偶数位是数字、奇数位是运算符，末位必为数字。</summary>
        public ObservableCollection<CalcCell> Cells
        {
            get { return _cells; }
        }

        /// <summary>表达式行文本（错误态时为错误文案）。</summary>
        public string ExpressionText
        {
            get { return _expressionText; }
        }

        /// <summary>结果行文本（错误态时为「—」）。</summary>
        public string ValueText
        {
            get { return _valueText; }
        }

        /// <summary>是否处于错误态。</summary>
        public bool HasError
        {
            get { return _hasError; }
        }

        /// <summary>内部自检：显示串独立求值是否等于顺序求值结果（不向用户展示）。</summary>
        public bool SelfCheckOk
        {
            get { return _selfCheckOk; }
        }

        // ============ 对外操作 ============

        /// <summary>重置为最小三段全空结构：空数字、空运算符、空数字。</summary>
        public void Reset()
        {
            for (int i = _cells.Count - 1; i >= 0; i--)
            {
                RemoveCellAt(i);
            }
            AddCell(new NumberCell(string.Empty));
            AddCell(new OperatorCell(string.Empty));
            AddCell(new NumberCell(string.Empty));
            Evaluate();
        }

        /// <summary>
        /// 删除一个数字行：非末位连带删掉其下方运算符行（两行一组）；
        /// 末位只删这一行，并把最后一个运算符置为无符号（不补空单元）。
        /// </summary>
        public void RemoveNumber(NumberCell cell)
        {
            if (cell == null)
            {
                return;
            }
            int i = _cells.IndexOf(cell);
            if (i < 0)
            {
                return;
            }
            if (i + 1 < _cells.Count)
            {
                RemoveCellAt(i);
                RemoveCellAt(i);
            }
            else
            {
                RemoveCellAt(i);
                if (i - 1 >= 0)
                {
                    CalcCell up = _cells[i - 1];
                    if (up != null && up.IsOperator)
                    {
                        // 走正常 Value：会发通知，界面上的下拉框才会回到「运算符」；
                        // 用 _suppressGrow 挡住生长，避免又被追加出一个新数字行
                        _suppressGrow = true;
                        up.Value = string.Empty;
                        _suppressGrow = false;
                    }
                }
            }
            Normalize();
            Evaluate();
        }

        /// <summary>重算：任何单元变化后都会自动调用，也可外部手动触发。</summary>
        public void Evaluate()
        {
            RefreshFlags();

            List<double> vals = new List<double>();
            List<string> ops = new List<string>();

            for (int i = 0; i < _cells.Count; i += 2)
            {
                CalcCell numCell = _cells[i];
                string raw = numCell == null ? string.Empty : Trim(numCell.Value);
                if (raw.Length == 0)
                {
                    break;
                }
                double v;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                {
                    break;
                }
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    break;
                }
                vals.Add(v);

                string op = string.Empty;
                if (i + 1 < _cells.Count && _cells[i + 1] != null)
                {
                    op = Trim(_cells[i + 1].Value);
                }
                if (op.Length == 0)
                {
                    break;
                }
                if (i + 2 >= _cells.Count)
                {
                    break;
                }
                CalcCell right = _cells[i + 2];
                if (right == null || Trim(right.Value).Length == 0)
                {
                    break;
                }
                ops.Add(op);
            }

            if (vals.Count == 0)
            {
                SetResult("—", "0", false);
                return;
            }

            double r = vals[0];
            for (int k = 0; k < ops.Count; k++)
            {
                double b = vals[k + 1];
                string op = ops[k];
                if (op == OpAdd)
                {
                    r = r + b;
                }
                else if (op == OpSubtract)
                {
                    r = r - b;
                }
                else if (op == OpMultiply)
                {
                    r = r * b;
                }
                else if (op == OpPower)
                {
                    r = Math.Pow(r, b);
                }
                else if (op == OpRoot)
                {
                    if (b <= 0)
                    {
                        SetResult(ErrorRootDegree, "—", true);
                        return;
                    }
                    r = Math.Pow(r, 1.0 / b);
                }
                else if (op == OpDivide)
                {
                    if (b == 0)
                    {
                        SetResult(ErrorDivideByZero, "—", true);
                        return;
                    }
                    r = r / b;
                }
            }

            string display = BuildDisplay(vals, ops);
            SetResult(display + " =", Format(r), false);

            bool ok = SelfCheck(display, r);
            if (_selfCheckOk != ok)
            {
                _selfCheckOk = ok;
                RaisePropertyChanged("SelfCheckOk");
            }
        }

        // ============ 内部：集合维护 ============

        private void AddCell(CalcCell cell)
        {
            cell.PropertyChanged += OnCellValueChanged;
            _cells.Add(cell);
        }

        private void RemoveCellAt(int index)
        {
            CalcCell cell = _cells[index];
            cell.PropertyChanged -= OnCellValueChanged;
            _cells.RemoveAt(index);
        }

        /// <summary>最小结构保护：至少保留三段（数字、运算符、数字）。</summary>
        private void Normalize()
        {
            while (_cells.Count < 3)
            {
                if (_cells.Count % 2 == 0)
                {
                    AddCell(new NumberCell(string.Empty));
                }
                else
                {
                    AddCell(new OperatorCell(string.Empty));
                }
            }
        }

        private void RefreshFlags()
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                _cells[i].IsFirstNumber = (i == 0);
            }
        }

        private void OnCellValueChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "Value")
            {
                return;
            }
            CalcCell cell = sender as CalcCell;
            if (cell == null)
            {
                return;
            }
            if (_suppressGrow)
            {
                // 程序内部改值（删除末位后把运算符置空）：只重算，不生长
                Evaluate();
                return;
            }
            if (cell.IsOperator)
            {
                int i = _cells.IndexOf(cell);
                if (i < 0)
                {
                    return;
                }
                if (IsTerminator(cell.Value))
                {
                    // 幂 / 根号是终止运算符：保留「本运算符 + 右侧一个数字」，其后全部截断
                    if (_cells.Count <= i + 1)
                    {
                        AddCell(new NumberCell(string.Empty));
                    }
                    while (_cells.Count > i + 2)
                    {
                        RemoveCellAt(_cells.Count - 1);
                    }
                }
                else
                {
                    Grow();
                }
            }
            else
            {
                Grow();
            }
            Evaluate();
        }

        /// <summary>
        /// 向下生长：末尾数字有值且其上方运算符已选定且非终止符 → 追加运算符行；
        /// 末尾运算符已选定且非终止符 → 追加数字行。
        /// </summary>
        private void Grow()
        {
            int n = _cells.Count;
            if (n == 0)
            {
                return;
            }
            CalcCell last = _cells[n - 1];
            if (!last.IsOperator)
            {
                if (Trim(last.Value).Length == 0)
                {
                    return;
                }
                if (n >= 2)
                {
                    CalcCell up = _cells[n - 2];
                    if (up.IsOperator)
                    {
                        if (up.Value.Length == 0)
                        {
                            return;
                        }
                        if (IsTerminator(up.Value))
                        {
                            return;
                        }
                    }
                }
                AddCell(new OperatorCell(string.Empty));
            }
            else
            {
                if (last.Value.Length == 0)
                {
                    return;
                }
                if (IsTerminator(last.Value))
                {
                    return;
                }
                AddCell(new NumberCell(string.Empty));
            }
        }

        // ============ 内部：显示与格式化 ============

        private string BuildDisplay(List<double> vals, List<string> ops)
        {
            string s = Format(vals[0]);
            bool hasPrev = false;
            int prevPrec = 0;
            for (int m = 0; m < ops.Count; m++)
            {
                int pc = Precedence(ops[m]);
                if (ops[m] == OpRoot)
                {
                    double bv = vals[m + 1];
                    if (IsInteger(bv) && bv > 0)
                    {
                        s = ToSuperscript(bv) + "√(" + s + ")";
                    }
                    else
                    {
                        s = "(" + s + ")^(1/" + Format(bv) + ")";
                    }
                }
                else if (ops[m] == OpPower)
                {
                    if (hasPrev && pc > prevPrec)
                    {
                        s = "(" + s + ")";
                    }

                    double exponent = vals[m + 1];
                    if (IsInteger(exponent) && exponent >= 0)
                    {
                        // 指数已经是上标时不再画 ^：2³，而不是 2 ^ ³。
                        s = s + SupOrPlain(exponent);
                    }
                    else
                    {
                        s = s + " ^ " + Format(exponent);
                    }
                    hasPrev = true;
                    prevPrec = pc;
                }
                else
                {
                    if (hasPrev && pc > prevPrec)
                    {
                        s = "(" + s + ")";
                    }
                    s = s + " " + Symbol(ops[m]) + " " + Format(vals[m + 1]);
                    hasPrev = true;
                    prevPrec = pc;
                }
            }
            return s;
        }

        private void SetResult(string expression, string value, bool hasError)
        {
            _expressionText = expression;
            _valueText = value;
            _hasError = hasError;
            RaisePropertyChanged("ExpressionText");
            RaisePropertyChanged("ValueText");
            RaisePropertyChanged("HasError");
        }

        /// <summary>数值格式化：整数原样、小数保留六位去尾零、非有限值显示「—」。</summary>
        public static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "—";
            }
            if (IsInteger(value))
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }
            double rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
            return rounded.ToString("0.######", CultureInfo.InvariantCulture);
        }

        public static bool IsInteger(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return false;
            }
            return value == Math.Floor(value);
        }

        /// <summary>非负整数转上标字符（幂的指数、根号的根次数）。</summary>
        public static string ToSuperscript(double value)
        {
            long n = (long)Math.Round(Math.Abs(value), 0);
            string digits = n.ToString(CultureInfo.InvariantCulture);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < digits.Length; i++)
            {
                char c = digits[i];
                if (c >= '0' && c <= '9')
                {
                    sb.Append(SuperscriptDigits[c - '0']);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string SupOrPlain(double value)
        {
            if (IsInteger(value) && value >= 0)
            {
                return ToSuperscript(value);
            }
            return Format(value);
        }

        public static string Symbol(string op)
        {
            if (op == OpAdd) { return "+"; }
            if (op == OpSubtract) { return "−"; }
            if (op == OpMultiply) { return "×"; }
            if (op == OpDivide) { return "÷"; }
            if (op == OpPower) { return "^"; }
            if (op == OpRoot) { return "√"; }
            return op;
        }

        public static int Precedence(string op)
        {
            if (op == OpAdd || op == OpSubtract) { return 1; }
            if (op == OpMultiply || op == OpDivide) { return 2; }
            if (op == OpPower || op == OpRoot) { return 3; }
            return 0;
        }

        public static bool IsTerminator(string op)
        {
            return op == OpPower || op == OpRoot;
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        // ============ 内部：自检（把显示出来的公式串独立重算一遍） ============

        private bool SelfCheck(string display, double expected)
        {
            string src = NormalizeRoots(display.Replace("×", "*").Replace("÷", "/").Replace("−", "-"));
            // 显示层为了可读性不再为整数指数写 ^；自检解析前把上标指数补回 ^。
            src = Regex.Replace(src, "([⁰¹²³⁴⁵⁶⁷⁸⁹]+)", "^$1");
            src = RestoreSuperscripts(src);
            _tokens = Tokenize(src);
            _pos = 0;
            double v = ParseExpr();
            if (double.IsNaN(v) && double.IsNaN(expected))
            {
                return true;
            }
            if (double.IsNaN(v) || double.IsNaN(expected))
            {
                return false;
            }
            return Math.Abs(v - expected) < 1e-9;
        }

        /// <summary>把标准根号记号「ⁿ√(…)」归一为「(…)^(1/n)」。</summary>
        private static string NormalizeRoots(string s)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                StringBuilder sb = new StringBuilder();
                int i = 0;
                while (i < s.Length)
                {
                    int start = i;
                    while (i < s.Length && IsSuperscript(s[i]))
                    {
                        i++;
                    }
                    if (i > start && i < s.Length && s[i] == '√' && i + 1 < s.Length && s[i + 1] == '(')
                    {
                        string n = RestoreSuperscripts(s.Substring(start, i - start));
                        int depth = 1;
                        int j = i + 2;
                        while (j < s.Length && depth > 0)
                        {
                            if (s[j] == '(')
                            {
                                depth++;
                            }
                            else if (s[j] == ')')
                            {
                                depth--;
                                if (depth == 0)
                                {
                                    break;
                                }
                            }
                            j++;
                        }
                        string inner = s.Substring(i + 2, j - (i + 2));
                        sb.Append('(').Append(inner).Append(")^(1/").Append(n).Append(')');
                        i = j + 1;
                        changed = true;
                    }
                    else
                    {
                        sb.Append(s.Substring(start, i - start));
                        if (i < s.Length)
                        {
                            sb.Append(s[i]);
                            i++;
                        }
                    }
                }
                s = sb.ToString();
            }
            return s;
        }

        private static bool IsSuperscript(char c)
        {
            for (int i = 0; i < SuperscriptDigits.Length; i++)
            {
                if (SuperscriptDigits[i].Length > 0 && SuperscriptDigits[i][0] == c)
                {
                    return true;
                }
            }
            return false;
        }

        private static string RestoreSuperscripts(string s)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                int idx = IndexOfSuperscript(s[i]);
                if (idx >= 0)
                {
                    sb.Append(idx.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        private static int IndexOfSuperscript(char c)
        {
            for (int i = 0; i < SuperscriptDigits.Length; i++)
            {
                if (SuperscriptDigits[i].Length > 0 && SuperscriptDigits[i][0] == c)
                {
                    return i;
                }
            }
            return -1;
        }

        private static List<string> Tokenize(string src)
        {
            List<string> tokens = new List<string>();
            MatchCollection matches = Regex.Matches(src, "\\d+(\\.\\d+)?|[+\\-*/()^]");
            for (int i = 0; i < matches.Count; i++)
            {
                tokens.Add(matches[i].Value);
            }
            return tokens;
        }

        private double ParseExpr()
        {
            double v = ParseTerm();
            while (_pos < _tokens.Count && (_tokens[_pos] == "+" || _tokens[_pos] == "-"))
            {
                string o = _tokens[_pos];
                _pos++;
                double r = ParseTerm();
                v = (o == "+") ? v + r : v - r;
            }
            return v;
        }

        private double ParseTerm()
        {
            double v = ParsePower();
            while (_pos < _tokens.Count && (_tokens[_pos] == "*" || _tokens[_pos] == "/"))
            {
                string o = _tokens[_pos];
                _pos++;
                double r = ParseFactor();
                if (o == "*")
                {
                    v = v * r;
                }
                else
                {
                    if (r == 0)
                    {
                        return double.NaN;
                    }
                    v = v / r;
                }
            }
            return v;
        }

        private double ParsePower()
        {
            double v = ParseFactor();
            while (_pos < _tokens.Count && _tokens[_pos] == "^")
            {
                _pos++;
                double r = ParseFactor();
                if (v < 0 && !IsInteger(r))
                {
                    return double.NaN;
                }
                v = Math.Pow(v, r);
            }
            return v;
        }

        private double ParseFactor()
        {
            if (_pos >= _tokens.Count)
            {
                return 0;
            }
            string t = _tokens[_pos];
            if (t == "-")
            {
                _pos++;
                return -ParseFactor();
            }
            if (t == "+")
            {
                _pos++;
                return ParseFactor();
            }
            if (t == "(")
            {
                _pos++;
                double v = ParseExpr();
                if (_pos < _tokens.Count && _tokens[_pos] == ")")
                {
                    _pos++;
                }
                return v;
            }
            double n;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out n))
            {
                _pos++;
                return n;
            }
            _pos++;
            return 0;
        }

        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
