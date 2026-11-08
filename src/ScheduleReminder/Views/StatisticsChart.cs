using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ScheduleReminder.Domain.Statistics;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 统计数据的自绘图表。只负责绘图与悬浮命中；
    /// 统计结果、色板、隐藏项均由 StatisticsWindow 传入。
    /// </summary>
    public sealed class StatisticsChart : FrameworkElement
    {
        private StatisticsResult _result;
        private Dictionary<string, Brush> _seriesBrushes = new Dictionary<string, Brush>(StringComparer.Ordinal);
        private HashSet<string> _hiddenKeys = new HashSet<string>(StringComparer.Ordinal);
        private Brush _totalBrush = Brushes.White;
        private Brush _axisBrush = Brushes.Gray;
        private Brush _gridBrush = Brushes.DimGray;
        private Brush _textBrush = Brushes.White;
        private Brush _textSecondaryBrush = Brushes.Gray;
        private Brush _tooltipBrush = Brushes.Black;
        private Typeface _typeface = new Typeface("Microsoft YaHei UI");
        private List<PlotColumn> _columns = new List<PlotColumn>();
        private Rect _plot;
        private int _hoverColumn = -1;

        public StatisticsChart()
        {
            SnapsToDevicePixels = true;
            ClipToBounds = true;
            Focusable = false;
        }

        public StatisticsResult Result
        {
            get { return _result; }
            set
            {
                _result = value;
                _hoverColumn = -1;
                InvalidateVisual();
            }
        }

        public bool ShowTotal { get; set; }

        public void SetPalette(Dictionary<string, Brush> seriesBrushes, Brush totalBrush)
        {
            _seriesBrushes = seriesBrushes ?? new Dictionary<string, Brush>(StringComparer.Ordinal);
            _totalBrush = totalBrush ?? Brushes.White;
            InvalidateVisual();
        }

        public void SetChartBrushes(Brush axis, Brush grid, Brush text, Brush textSecondary, Brush tooltip)
        {
            _axisBrush = axis ?? Brushes.Gray;
            _gridBrush = grid ?? Brushes.DimGray;
            _textBrush = text ?? Brushes.White;
            _textSecondaryBrush = textSecondary ?? Brushes.Gray;
            _tooltipBrush = tooltip ?? Brushes.Black;
            InvalidateVisual();
        }

        public void SetTypeface(Typeface typeface)
        {
            if (typeface != null)
            {
                _typeface = typeface;
            }
            InvalidateVisual();
        }

        public void SetHiddenKeys(HashSet<string> hiddenKeys)
        {
            _hiddenKeys = hiddenKeys ?? new HashSet<string>(StringComparer.Ordinal);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            _plot = new Rect(46, 12, Math.Max(10, ActualWidth - 62), Math.Max(10, ActualHeight - 48));
            if (_result == null || _result.Buckets == null || _result.Buckets.Count == 0)
            {
                _columns.Clear();
                return;
            }

            BuildColumns();
            DrawGridAndAxes(dc);
            // 总计线是参考线，先画在底层，避免压住数据线。
            if (ShowTotal)
            {
                DrawTotal(dc);
            }
            DrawSeries(dc);
            DrawHover(dc);
        }

        private void BuildColumns()
        {
            _columns.Clear();
            int bucketCount = _result.Buckets.Count;
            int maxColumns = Math.Max(2, (int)(_plot.Width / 7.0));
            int groupSize = bucketCount > maxColumns * 2
                ? (int)Math.Ceiling(bucketCount / (double)maxColumns)
                : 1;

            for (int start = 0; start < bucketCount; start += groupSize)
            {
                int end = Math.Min(bucketCount - 1, start + groupSize - 1);
                PlotColumn column = new PlotColumn();
                column.StartIndex = start;
                column.EndIndex = end;
                column.Values = new Dictionary<string, int>(StringComparer.Ordinal);
                column.Label = _result.Buckets[start].Label;
                if (end > start)
                {
                    column.Label = _result.Buckets[start].Label + "–" + _result.Buckets[end].Label;
                }

                for (int i = start; i <= end; i++)
                {
                    StatisticsBucket bucket = _result.Buckets[i];
                    foreach (KeyValuePair<string, int> pair in bucket.Values)
                    {
                        int old;
                        if (!column.Values.TryGetValue(pair.Key, out old) || pair.Value > old)
                        {
                            column.Values[pair.Key] = pair.Value;
                        }
                    }
                }

                double center = _plot.Left + ((start + end + 1) / 2.0) * (_plot.Width / bucketCount);
                column.X = center;
                column.Total = 0;
                foreach (KeyValuePair<string, int> pair in column.Values)
                {
                    column.Total += pair.Value;
                }
                _columns.Add(column);
            }
        }

        private void DrawGridAndAxes(DrawingContext dc)
        {
            int maxValue = 1;
            for (int i = 0; i < _columns.Count; i++)
            {
                PlotColumn column = _columns[i];
                for (int s = 0; s < _result.Series.Count; s++)
                {
                    if (_hiddenKeys.Contains(_result.Series[s].Key))
                    {
                        continue;
                    }
                    int value;
                    if (column.Values.TryGetValue(_result.Series[s].Key, out value) && value > maxValue)
                    {
                        maxValue = value;
                    }
                }
                if (ShowTotal && column.Total > maxValue)
                {
                    maxValue = column.Total;
                }
            }

            int step = NiceStep(maxValue / 4);
            int yMax = Math.Max(step, ((maxValue + step - 1) / step) * step);

            Pen gridPen = MakePen(_gridBrush, 1);
            Pen axisPen = MakePen(_axisBrush, 1);

            // 与周视图的普通分隔线保持一致：同一底色，10% 透明度；只淡化横线，不淡化刻度文字。
            dc.PushOpacity(0.1);
            for (int tick = 0; tick <= yMax; tick += step)
            {
                double y = _plot.Bottom - (tick / (double)yMax) * _plot.Height;
                dc.DrawLine(gridPen, new Point(_plot.Left, y), new Point(_plot.Right, y));
            }
            dc.Pop();

            for (int tick = 0; tick <= yMax; tick += step)
            {
                double y = _plot.Bottom - (tick / (double)yMax) * _plot.Height;
                DrawText(dc, tick.ToString(CultureInfo.InvariantCulture), 10,
                    new Point(_plot.Left - 8, y), _textSecondaryBrush, TextAlignment.Right, true);
            }

            dc.DrawLine(axisPen, new Point(_plot.Left, _plot.Top), new Point(_plot.Left, _plot.Bottom));
            dc.DrawLine(axisPen, new Point(_plot.Left, _plot.Bottom), new Point(_plot.Right, _plot.Bottom));

            int labelEvery = Math.Max(1, (int)Math.Ceiling(_columns.Count / 7.0));
            for (int i = 0; i < _columns.Count; i += labelEvery)
            {
                DrawText(dc, _columns[i].Label, 9.5, new Point(_columns[i].X, _plot.Bottom + 8),
                    _textSecondaryBrush, TextAlignment.Center, false);
            }
            if (_columns.Count > 1 && (_columns.Count - 1) % labelEvery != 0)
            {
                PlotColumn last = _columns[_columns.Count - 1];
                DrawText(dc, last.Label, 9.5, new Point(last.X, _plot.Bottom + 8),
                    _textSecondaryBrush, TextAlignment.Center, false);
            }

            _yMax = yMax;
        }

        private int _yMax = 1;

        private void DrawSeries(DrawingContext dc)
        {
            for (int s = 0; s < _result.Series.Count; s++)
            {
                StatisticsSeries series = _result.Series[s];
                if (_hiddenKeys.Contains(series.Key))
                {
                    continue;
                }

                Brush brush;
                if (!_seriesBrushes.TryGetValue(series.Key, out brush))
                {
                    brush = Brushes.Gray;
                }

                Pen pen = MakePen(brush, 1.5);
                PointCollection points = new PointCollection();
                for (int i = 0; i < _columns.Count; i++)
                {
                    int value;
                    _columns[i].Values.TryGetValue(series.Key, out value);
                    points.Add(new Point(_columns[i].X, ValueToY(value)));
                }

                if (points.Count == 1)
                {
                    dc.DrawEllipse(brush, null, points[0], 3.5, 3.5);
                }
                else
                {
                    for (int i = 1; i < points.Count; i++)
                    {
                        dc.DrawLine(pen, points[i - 1], points[i]);
                    }
                }

                if (_result.Granularity == StatisticsGranularity.Day && _columns.Count <= 14)
                {
                    for (int i = 0; i < points.Count; i++)
                    {
                        dc.DrawEllipse(brush, null, points[i], 2.4, 2.4);
                    }
                }
            }
        }

        private void DrawTotal(DrawingContext dc)
        {
            Pen pen = MakePen(_totalBrush, 2.5);
            PointCollection points = new PointCollection();
            for (int i = 0; i < _columns.Count; i++)
            {
                points.Add(new Point(_columns[i].X, ValueToY(_columns[i].Total)));
            }
            if (points.Count == 1)
            {
                dc.DrawEllipse(_totalBrush, null, points[0], 3.5, 3.5);
                return;
            }
            for (int i = 1; i < points.Count; i++)
            {
                dc.DrawLine(pen, points[i - 1], points[i]);
            }
        }

        private void DrawHover(DrawingContext dc)
        {
            if (_hoverColumn < 0 || _hoverColumn >= _columns.Count)
            {
                return;
            }

            PlotColumn column = _columns[_hoverColumn];
            Pen pen = new Pen(_textSecondaryBrush, 1);
            pen.DashStyle = new DashStyle(new double[] { 2, 4 }, 0);
            pen.Freeze();
            dc.DrawLine(pen, new Point(column.X, _plot.Top), new Point(column.X, _plot.Bottom));

            List<string> lines = new List<string>();
            lines.Add(column.Label);
            for (int i = 0; i < _result.Series.Count; i++)
            {
                StatisticsSeries series = _result.Series[i];
                if (_hiddenKeys.Contains(series.Key))
                {
                    continue;
                }
                int value;
                column.Values.TryGetValue(series.Key, out value);
                if (value > 0 || !series.IsUntagged)
                {
                    lines.Add(series.DisplayName + "  " + value.ToString(CultureInfo.InvariantCulture));
                }
            }
            lines.Add("合计  " + column.Total.ToString(CultureInfo.InvariantCulture));

            double lineHeight = 17;
            double width = 170;
            double height = 12 + lines.Count * lineHeight;
            double left = column.X + 12;
            if (left + width > ActualWidth - 4)
            {
                left = column.X - width - 12;
            }
            if (left < 4)
            {
                left = 4;
            }
            double top = _plot.Top + 8;
            if (top + height > _plot.Bottom)
            {
                top = Math.Max(_plot.Top, _plot.Bottom - height);
            }

            Rect box = new Rect(left, top, width, height);
            dc.DrawRoundedRectangle(_tooltipBrush, null, box, 5, 5);

            // 淡细边框：沿用图表的次级文字色，降低透明度，避免抢过卡片内容。
            Pen borderPen = new Pen(_axisBrush, 1);
            borderPen.Freeze();
            dc.PushOpacity(0.35);
            dc.DrawRoundedRectangle(null, borderPen, box, 5, 5);
            dc.Pop();

            for (int i = 0; i < lines.Count; i++)
            {
                Brush brush = i == 0 ? _textBrush : _textSecondaryBrush;
                DrawText(dc, lines[i], 10.5, new Point(box.Left + 8, box.Top + 7 + i * lineHeight),
                    brush, TextAlignment.Left, false);
            }
        }

        private double ValueToY(int value)
        {
            if (_yMax <= 0)
            {
                _yMax = 1;
            }
            double ratio = Math.Max(0, Math.Min(1, value / (double)_yMax));
            return _plot.Bottom - ratio * _plot.Height;
        }

        private static int NiceStep(int rough)
        {
            if (rough <= 1) return 1;
            int magnitude = 1;
            while (magnitude * 10 <= rough) magnitude *= 10;
            int normalized = rough / magnitude;
            int step;
            if (normalized <= 1) step = 1;
            else if (normalized <= 2) step = 2;
            else if (normalized <= 5) step = 5;
            else step = 10;
            return step * magnitude;
        }

        private static Pen MakePen(Brush brush, double thickness)
        {
            Pen pen = new Pen(brush, thickness);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.LineJoin = PenLineJoin.Round;
            pen.Freeze();
            return pen;
        }

        private void DrawText(
            DrawingContext dc, string text, double size, Point point,
            Brush brush, TextAlignment alignment, bool rightAligned)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            FormattedText formatted = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                _typeface,
                size,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double x = point.X;
            if (alignment == TextAlignment.Center)
            {
                x -= formatted.Width / 2;
            }
            else if (rightAligned)
            {
                x -= formatted.Width;
            }
            dc.DrawText(formatted, new Point(x, point.Y - formatted.Height / 2));
        }

        /// <summary>
        /// 让整个图表矩形都参与命中测试，而不是只有实际画出的线条可命中。
        /// 否则 OnMouseMove 只会在鼠标压到折线/网格线时触发，表现成“必须悬浮在折线上”。
        /// </summary>
        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            Point point = hitTestParameters.HitPoint;
            if (point.X >= 0 && point.Y >= 0
                && point.X <= ActualWidth && point.Y <= ActualHeight)
            {
                return new PointHitTestResult(this, point);
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_columns.Count == 0)
            {
                return;
            }
            Point p = e.GetPosition(this);
            if (!_plot.Contains(p))
            {
                if (_hoverColumn != -1)
                {
                    _hoverColumn = -1;
                    InvalidateVisual();
                }
                return;
            }

            int nearest = 0;
            double distance = double.MaxValue;
            for (int i = 0; i < _columns.Count; i++)
            {
                double d = Math.Abs(_columns[i].X - p.X);
                if (d < distance)
                {
                    distance = d;
                    nearest = i;
                }
            }
            if (_hoverColumn != nearest)
            {
                _hoverColumn = nearest;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverColumn != -1)
            {
                _hoverColumn = -1;
                InvalidateVisual();
            }
        }

        private sealed class PlotColumn
        {
            public int StartIndex;
            public int EndIndex;
            public double X;
            public int Total;
            public string Label;
            public Dictionary<string, int> Values;
        }
    }
}
