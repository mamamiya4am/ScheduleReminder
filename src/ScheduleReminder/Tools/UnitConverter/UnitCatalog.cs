using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ScheduleReminder.Tools.UnitConverter
{
    /// <summary>单个单位：线性类别用系数换算，温度类别用摄氏中转函数换算。</summary>
    public sealed class UnitDefinition
    {
        /// <summary>单位名，如「毫米」「摄氏度」。</summary>
        public string Name { get; private set; }

        /// <summary>线性换算系数：以类别基准单位为 1 的相对值，如「毫米」为 0.001（基准 = 米）。</summary>
        public double Factor { get; private set; }

        /// <summary>温度专用：把该单位的值转成摄氏基准；非温度类别为 null。</summary>
        public Func<double, double> ToBase { get; private set; }

        /// <summary>温度专用：把摄氏基准值转成该单位；非温度类别为 null。</summary>
        public Func<double, double> FromBase { get; private set; }

        private UnitDefinition()
        {
        }

        /// <summary>线性单位：值 × 系数 = 基准值，基准值 ÷ 系数 = 值。</summary>
        public static UnitDefinition Linear(string name, double factor)
        {
            return new UnitDefinition
            {
                Name = name,
                Factor = factor,
                ToBase = null,
                FromBase = null
            };
        }

        /// <summary>温度单位：以摄氏度为中转，两个函数必须成对提供。</summary>
        public static UnitDefinition Temperature(string name, Func<double, double> toBase, Func<double, double> fromBase)
        {
            return new UnitDefinition
            {
                Name = name,
                Factor = 1.0,
                ToBase = toBase,
                FromBase = fromBase
            };
        }
    }

    /// <summary>一个换算类别：名称 + 是否温度 + 该类全部单位（顺序即下拉与结果列表顺序）。</summary>
    public sealed class UnitCategory
    {
        /// <summary>类别名，如「长度」「温度」。</summary>
        public string Name { get; private set; }

        /// <summary>是否为温度类别（温度需要绝对零度钳制，并按摄氏中转）。</summary>
        public bool IsTemperature { get; private set; }

        /// <summary>该类全部单位。</summary>
        public IList<UnitDefinition> Units { get; private set; }

        public static UnitCategory Linear(string name, IList<UnitDefinition> units)
        {
            return new UnitCategory { Name = name, IsTemperature = false, Units = units };
        }

        public static UnitCategory Temperature(string name, IList<UnitDefinition> units)
        {
            return new UnitCategory { Name = name, IsTemperature = true, Units = units };
        }
    }

    /// <summary>
    /// 单位表与换算，纯逻辑、不依赖 WPF，与原型《单位换算窗.html》逐项一致。
    /// 规则：非温度类别走系数，温度类别走摄氏中转；温度中转后钳制到 −273.15（低于绝对零度静默钳制，不提示、不限制输入）。
    /// </summary>
    public static class UnitCatalog
    {
        /// <summary>绝对零度（摄氏）：温度类别中转值的下界。</summary>
        public const double AbsoluteZeroCelsius = -273.15;

        private static readonly List<UnitCategory> CategoriesBacking = Build();

        /// <summary>八个类别，顺序为长度、重量、面积、体积、温度、时间、数据存储、速度。</summary>
        public static IList<UnitCategory> Categories
        {
            get { return CategoriesBacking; }
        }

        /// <summary>把某个单位的值换算到类别基准值；温度走 toBase，其余乘以系数。</summary>
        public static double ToBase(UnitDefinition unit, double value)
        {
            if (unit == null)
            {
                return value;
            }
            if (unit.ToBase != null)
            {
                return unit.ToBase(value);
            }
            return value * unit.Factor;
        }

        /// <summary>把类别基准值换算到某个单位；温度走 fromBase，其余除以系数。</summary>
        public static double FromBase(UnitDefinition unit, double baseValue)
        {
            if (unit == null)
            {
                return baseValue;
            }
            if (unit.FromBase != null)
            {
                return unit.FromBase(baseValue);
            }
            return baseValue / unit.Factor;
        }

        /// <summary>按单位名在当前类别里找单位；找不到返回该类别首项（与原型 findUnit 的兜底一致）。</summary>
        public static UnitDefinition FindUnit(UnitCategory category, string name)
        {
            if (category == null || category.Units == null || category.Units.Count == 0)
            {
                return null;
            }
            for (int i = 0; i < category.Units.Count; i++)
            {
                if (category.Units[i].Name == name)
                {
                    return category.Units[i];
                }
            }
            return category.Units[0];
        }

        /// <summary>
        /// 输入清洗（与原型 sanitize 同逻辑）：全角数字转半角，中文句号与全角点转小数点，
        /// 各类减号与破折号转负号，空格与逗号删除；只保留数字、小数点、负号；
        /// 负号只在首位且只有一个，小数点只保留第一个。
        /// </summary>
        public static string Sanitize(string raw)
        {
            string s = raw ?? string.Empty;

            StringBuilder normalized = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '０' && c <= '９')
                {
                    normalized.Append((char)(c - 0xFEE0));
                }
                else if (c == '。' || c == '．' || c == '｡')
                {
                    normalized.Append('.');
                }
                else if (c == '−' || c == '–' || c == '—' || c == '－' || c == '﹣')
                {
                    normalized.Append('-');
                }
                else if (c == ' ' || c == '\t' || c == ',' || c == '，')
                {
                    // 千分位与空格直接丢弃
                }
                else
                {
                    normalized.Append(c);
                }
            }
            s = normalized.ToString();

            StringBuilder kept = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '.' || c == '-')
                {
                    kept.Append(c);
                }
            }
            s = kept.ToString();

            bool negative = s.Length > 0 && s[0] == '-';
            s = s.Replace("-", string.Empty);

            int dot = s.IndexOf('.');
            if (dot >= 0)
            {
                s = s.Substring(0, dot + 1) + s.Substring(dot + 1).Replace(".", string.Empty);
            }

            return (negative ? "-" : string.Empty) + s;
        }

        /// <summary>
        /// 判断清洗后的文本是否还不足以换算：空串、只有一个负号、只有一个小数点都算无效。
        /// </summary>
        public static bool IsEmptyInput(string sanitized)
        {
            if (string.IsNullOrEmpty(sanitized))
            {
                return true;
            }
            return sanitized == "-" || sanitized == ".";
        }

        /// <summary>
        /// 数值格式化（与原型 fmt 同逻辑）：零显示「0」；绝对值不小于 1e15 用科学计数法（小数点后六位）；
        /// 安全整数范围内的整数原样显示并加千分位；其余按十二位有效数字、去尾零、整数部分加千分位；
        /// 非有限值显示「—」。
        /// </summary>
        public static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "—";
            }
            if (value == 0.0)
            {
                return "0";
            }

            double abs = Math.Abs(value);
            if (abs >= 1e15)
            {
                return value.ToString("0.000000e+00", CultureInfo.InvariantCulture);
            }

            if (Math.Floor(value) == value && abs <= 9007199254740991.0)
            {
                decimal whole = (decimal)abs;
                return (value < 0 ? "-" : string.Empty) +
                       GroupDigits(whole.ToString("0", CultureInfo.InvariantCulture));
            }

            string text = value.ToString("G12", CultureInfo.InvariantCulture);
            if (text.IndexOf('E') >= 0 || text.IndexOf('e') >= 0)
            {
                // G12 在指数小于 −4 时就改用科学计数法，而原型 toPrecision(12) 要到 1e-6 以下才改。
                // 指数落在 [−6, 12) 的值改回定点写，避免「1 平方毫米 = 1.000000e-06 平方米」这种反直觉显示。
                int exponent = (int)Math.Floor(Math.Log10(abs));
                if (exponent >= -6 && exponent < 12)
                {
                    int decimals = 11 - exponent;
                    if (decimals < 0)
                    {
                        decimals = 0;
                    }
                    text = value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture),
                                          CultureInfo.InvariantCulture);
                }
                else
                {
                    return value.ToString("0.000000e+00", CultureInfo.InvariantCulture);
                }
            }

            if (text.IndexOf('.') >= 0)
            {
                text = text.TrimEnd('0').TrimEnd('.');
            }
            bool negative = text.StartsWith("-", StringComparison.Ordinal);
            if (negative)
            {
                text = text.Substring(1);
            }

            string[] parts = text.Split('.');
            parts[0] = GroupDigits(parts[0]);
            return (negative ? "-" : string.Empty) + string.Join(".", parts);
        }

        /// <summary>整数部分按三位加千分位逗号。</summary>
        private static string GroupDigits(string digits)
        {
            if (string.IsNullOrEmpty(digits) || digits.Length <= 3)
            {
                return digits;
            }
            int lead = digits.Length % 3;
            if (lead == 0)
            {
                lead = 3;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(digits.Substring(0, lead));
            for (int i = lead; i < digits.Length; i += 3)
            {
                sb.Append(',').Append(digits.Substring(i, 3));
            }
            return sb.ToString();
        }

        private static List<UnitCategory> Build()
        {
            List<UnitCategory> list = new List<UnitCategory>();

            list.Add(UnitCategory.Linear("长度", new List<UnitDefinition>
            {
                UnitDefinition.Linear("毫米", 0.001),
                UnitDefinition.Linear("厘米", 0.01),
                UnitDefinition.Linear("分米", 0.1),
                UnitDefinition.Linear("米", 1.0),
                UnitDefinition.Linear("千米", 1000.0),
                UnitDefinition.Linear("英寸", 0.0254),
                UnitDefinition.Linear("英尺", 0.3048),
                UnitDefinition.Linear("码", 0.9144),
                UnitDefinition.Linear("英里", 1609.344),
                UnitDefinition.Linear("海里", 1852.0),
                UnitDefinition.Linear("里（市里）", 500.0),
                UnitDefinition.Linear("尺（市尺）", 1.0 / 3.0),
                UnitDefinition.Linear("寸（市寸）", 1.0 / 30.0)
            }));

            list.Add(UnitCategory.Linear("重量", new List<UnitDefinition>
            {
                UnitDefinition.Linear("毫克", 0.000001),
                UnitDefinition.Linear("克", 0.001),
                UnitDefinition.Linear("千克", 1.0),
                UnitDefinition.Linear("公斤", 1.0),
                UnitDefinition.Linear("吨", 1000.0),
                UnitDefinition.Linear("斤（市斤）", 0.5),
                UnitDefinition.Linear("两（市两）", 0.05),
                UnitDefinition.Linear("钱（市钱）", 0.005),
                UnitDefinition.Linear("磅", 0.45359237),
                UnitDefinition.Linear("盎司", 0.028349523125),
                UnitDefinition.Linear("克拉", 0.0002)
            }));

            list.Add(UnitCategory.Linear("面积", new List<UnitDefinition>
            {
                UnitDefinition.Linear("平方毫米", 0.000001),
                UnitDefinition.Linear("平方厘米", 0.0001),
                UnitDefinition.Linear("平方米", 1.0),
                UnitDefinition.Linear("平方千米", 1000000.0),
                UnitDefinition.Linear("公顷", 10000.0),
                UnitDefinition.Linear("亩", 2000.0 / 3.0),
                UnitDefinition.Linear("平方英寸", 0.00064516),
                UnitDefinition.Linear("平方英尺", 0.09290304),
                UnitDefinition.Linear("英亩", 4046.8564224),
                UnitDefinition.Linear("平方英里", 2589988.110336)
            }));

            list.Add(UnitCategory.Linear("体积", new List<UnitDefinition>
            {
                UnitDefinition.Linear("毫升", 0.001),
                UnitDefinition.Linear("升", 1.0),
                UnitDefinition.Linear("立方厘米", 0.001),
                UnitDefinition.Linear("立方米", 1000.0),
                UnitDefinition.Linear("立方英尺", 28.316846592),
                UnitDefinition.Linear("加仑（美）", 3.785411784),
                UnitDefinition.Linear("加仑（英）", 4.54609),
                UnitDefinition.Linear("品脱（美）", 0.473176473),
                UnitDefinition.Linear("液量盎司（美）", 0.0295735295625)
            }));

            list.Add(UnitCategory.Temperature("温度", new List<UnitDefinition>
            {
                UnitDefinition.Temperature("摄氏度", v => v, v => v),
                UnitDefinition.Temperature("华氏度", v => (v - 32.0) * 5.0 / 9.0, v => v * 9.0 / 5.0 + 32.0),
                UnitDefinition.Temperature("开尔文", v => v - 273.15, v => v + 273.15)
            }));

            list.Add(UnitCategory.Linear("时间", new List<UnitDefinition>
            {
                UnitDefinition.Linear("毫秒", 0.001),
                UnitDefinition.Linear("秒", 1.0),
                UnitDefinition.Linear("分", 60.0),
                UnitDefinition.Linear("小时", 3600.0),
                UnitDefinition.Linear("天", 86400.0),
                UnitDefinition.Linear("周", 604800.0),
                UnitDefinition.Linear("月（30 天）", 2592000.0),
                UnitDefinition.Linear("年（365 天）", 31536000.0)
            }));

            list.Add(UnitCategory.Linear("数据存储", new List<UnitDefinition>
            {
                UnitDefinition.Linear("比特", 0.125),
                UnitDefinition.Linear("字节", 1.0),
                UnitDefinition.Linear("KB（1024）", 1024.0),
                UnitDefinition.Linear("MB（1024）", 1048576.0),
                UnitDefinition.Linear("GB（1024）", 1073741824.0),
                UnitDefinition.Linear("TB（1024）", 1099511627776.0)
            }));

            list.Add(UnitCategory.Linear("速度", new List<UnitDefinition>
            {
                UnitDefinition.Linear("米/秒", 1.0),
                UnitDefinition.Linear("千米/小时", 1.0 / 3.6),
                UnitDefinition.Linear("英里/小时", 0.44704),
                UnitDefinition.Linear("英尺/秒", 0.3048),
                UnitDefinition.Linear("节", 1852.0 / 3600.0)
            }));

            return list;
        }
    }
}
