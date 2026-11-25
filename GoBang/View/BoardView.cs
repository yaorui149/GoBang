using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GoBang.Model;

namespace GoBang.View;

/// <summary>
/// 自绘棋盘控件：负责绘制和鼠标交互，不含对局规则。
/// </summary>
public sealed class BoardView : FrameworkElement
{
    private const double EdgePadding = 30;
    private const double MinCell = 14;

    private static readonly Brush BackgroundBrush =
        new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x23));

    private static readonly Brush WoodBrush = new LinearGradientBrush(
        Color.FromRgb(0xEF, 0xC9, 0x8B),
        Color.FromRgb(0xD8, 0xA8, 0x60),
        45);

    private static readonly Brush StarBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E));
    private static readonly Brush LastMoveBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0x4A, 0x3B));

    // 画刷和画笔预先创建并冻结，避免每次重绘都新建对象
    private static readonly Brush BoardShadowBrush = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0));
    private static readonly Brush StoneShadowBrush = new SolidColorBrush(Color.FromArgb(0x46, 0, 0, 0));

    private static readonly Brush BlackStoneBrush = CreateStoneBrush(
        Color.FromRgb(0x6A, 0x6A, 0x6A), Color.FromRgb(0x05, 0x05, 0x05));
    private static readonly Brush WhiteStoneBrush = CreateStoneBrush(
        Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xC6, 0xC0, 0xB0));
    private static readonly Pen BlackStonePen = CreatePen(Color.FromRgb(0x05, 0x05, 0x05), 0.8);
    private static readonly Pen WhiteStonePen = CreatePen(Color.FromRgb(0xC6, 0xC0, 0xB0), 0.8);

    private static readonly Pen GridPen = CreatePen(Color.FromArgb(0x9A, 0x4A, 0x33, 0x1E), 1);
    private static readonly Pen BorderPen = CreatePen(Color.FromArgb(0xD0, 0x3A, 0x26, 0x14), 1.6);
    private static readonly Pen GuidePen = CreatePen(Color.FromArgb(0x55, 0x6B, 0x4A, 0x2B), 1);
    private static readonly Pen WinPen = CreatePen(Color.FromArgb(0xCC, 0xE2, 0x4A, 0x3B), 3);
    private static readonly Pen WinRingPen = CreatePen(Color.FromArgb(0xF0, 0xF2, 0xB0, 0x3A), 2.4);

    private static readonly (int Row, int Col)[] StarPoints =
    {
        (3, 3), (3, 11), (7, 7), (11, 3), (11, 11)
    };

    private readonly DispatcherTimer _hintTimer;
    private Board _board = new();
    private Stone _preview = Stone.None;
    private IReadOnlyList<(int Row, int Col)>? _winLine;
    private IReadOnlyList<(int Row, int Col)>? _hintPoints;
    private (int Row, int Col)? _hover;
    private double _hintPhase;
    private bool _hintRising = true;

    public BoardView()
    {
        _hintTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(45)
        };
        _hintTimer.Tick += (_, _) =>
        {
            AdvanceHintPhase();
            InvalidateVisual();
        };
    }

    public event Action<int, int>? CellClicked;

    public Board GameBoard
    {
        get => _board;
        set
        {
            _board = value;
            _hover = null;
            InvalidateVisual();
        }
    }

    /// <summary>鼠标悬停时预览的棋子颜色，None 表示不显示。</summary>
    public Stone PreviewStone
    {
        get => _preview;
        set
        {
            if (_preview == value) return;
            _preview = value;
            InvalidateVisual();
        }
    }

    public IReadOnlyList<(int Row, int Col)>? WinLine
    {
        get => _winLine;
        set
        {
            _winLine = value;
            InvalidateVisual();
        }
    }

    /// <summary>需要闪烁提示的威胁点（对手可成四、可成五的位置）。</summary>
    public IReadOnlyList<(int Row, int Col)>? HintPoints
    {
        set
        {
            _hintPoints = value is { Count: > 0 } ? value : null;

            if (_hintPoints == null)
            {
                _hintTimer.Stop();
                _hintPhase = 0;
                _hintRising = true;
            }
            else if (!_hintTimer.IsEnabled)
            {
                _hintTimer.Start();
            }

            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        // 铺满整个区域，同时保证控件可命中鼠标
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        (double cell, double x0, double y0) = Layout();
        double span = cell * (Board.Size - 1);
        double radius = cell * 0.43;

        var boardRect = new Rect(x0 - cell, y0 - cell, span + cell * 2, span + cell * 2);
        dc.DrawRoundedRectangle(
            BoardShadowBrush, null,
            new Rect(boardRect.X + 4, boardRect.Y + 7, boardRect.Width, boardRect.Height), 12, 12);
        dc.DrawRoundedRectangle(WoodBrush, null, boardRect, 12, 12);

        for (int i = 0; i < Board.Size; i++)
        {
            double offset = i * cell;
            dc.DrawLine(GridPen, new Point(x0, y0 + offset), new Point(x0 + span, y0 + offset));
            dc.DrawLine(GridPen, new Point(x0 + offset, y0), new Point(x0 + offset, y0 + span));
        }
        dc.DrawRectangle(null, BorderPen, new Rect(x0, y0, span, span));

        foreach ((int r, int c) in StarPoints)
            dc.DrawEllipse(StarBrush, null, new Point(x0 + c * cell, y0 + r * cell), cell * 0.09, cell * 0.09);

        // 只遍历已落子的棋谱，而不是检查 225 个格子
        foreach (Move move in _board.History)
            DrawStone(dc, x0 + move.Col * cell, y0 + move.Row * cell, radius, move.Stone);

        if (_board.MoveCount > 0)
        {
            Move last = _board.History[^1];
            dc.DrawEllipse(LastMoveBrush, null,
                new Point(x0 + last.Col * cell, y0 + last.Row * cell), radius * 0.19, radius * 0.19);
        }

        if (_hover is { } hover && _preview != Stone.None && _board.IsEmpty(hover.Row, hover.Col))
        {
            double cx = x0 + hover.Col * cell;
            double cy = y0 + hover.Row * cell;
            dc.DrawLine(GuidePen, new Point(x0, cy), new Point(x0 + span, cy));
            dc.DrawLine(GuidePen, new Point(cx, y0), new Point(cx, y0 + span));
            DrawStone(dc, cx, cy, radius, _preview, 0.45);
        }

        // 威胁点：颜色由橙渐红，透明度与半径随呼吸相位变化
        if (_hintPoints != null)
        {
            Color color = HintColor(_hintPhase);
            byte alpha = (byte)(90 + 130 * _hintPhase);
            double ringRadius = radius * (0.95 + 0.2 * _hintPhase);

            var fill = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.4), color.R, color.G, color.B));
            var ring = new Pen(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), 2.2);
            fill.Freeze();
            ring.Freeze();

            foreach ((int r, int c) in _hintPoints)
                dc.DrawEllipse(fill, ring, new Point(x0 + c * cell, y0 + r * cell), ringRadius, ringRadius);
        }

        if (_winLine is { Count: > 0 })
        {
            (int Row, int Col) first = _winLine[0];
            (int Row, int Col) last = _winLine[^1];
            dc.DrawLine(WinPen,
                new Point(x0 + first.Col * cell, y0 + first.Row * cell),
                new Point(x0 + last.Col * cell, y0 + last.Row * cell));

            foreach ((int r, int c) in _winLine)
                dc.DrawEllipse(null, WinRingPen,
                    new Point(x0 + c * cell, y0 + r * cell), radius * 1.1, radius * 1.1);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        (int Row, int Col)? hit = HitTest(e.GetPosition(this));
        (int Row, int Col)? next = hit is { } h && _board.IsEmpty(h.Row, h.Col) ? h : null;

        if (!Nullable.Equals(next, _hover))
        {
            _hover = next;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != null)
        {
            _hover = null;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (HitTest(e.GetPosition(this)) is { } hit)
            CellClicked?.Invoke(hit.Row, hit.Col);
    }

    private (double Cell, double X0, double Y0) Layout()
    {
        int gaps = Board.Size - 1;
        double available = Math.Min(ActualWidth, ActualHeight) - EdgePadding * 2;
        double cell = Math.Max(MinCell, available / gaps);
        double span = cell * gaps;
        return (cell, (ActualWidth - span) / 2, (ActualHeight - span) / 2);
    }

    private (int Row, int Col)? HitTest(Point point)
    {
        (double cell, double x0, double y0) = Layout();

        int col = (int)Math.Round((point.X - x0) / cell);
        int row = (int)Math.Round((point.Y - y0) / cell);
        if (!Board.InBounds(row, col)) return null;

        double dx = point.X - (x0 + col * cell);
        double dy = point.Y - (y0 + row * cell);
        if (Math.Sqrt(dx * dx + dy * dy) > cell * 0.5) return null;

        return (row, col);
    }

    private static void DrawStone(DrawingContext dc, double cx, double cy, double radius, Stone stone, double opacity = 1)
    {
        if (opacity < 1) dc.PushOpacity(opacity);

        dc.DrawEllipse(StoneShadowBrush, null,
            new Point(cx + radius * 0.1, cy + radius * 0.14), radius, radius);

        bool isBlack = stone == Stone.Black;
        dc.DrawEllipse(
            isBlack ? BlackStoneBrush : WhiteStoneBrush,
            isBlack ? BlackStonePen : WhiteStonePen,
            new Point(cx, cy), radius, radius);

        if (opacity < 1) dc.Pop();
    }

    private static Brush CreateStoneBrush(Color center, Color edge)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.38, 0.34),
            GradientOrigin = new Point(0.32, 0.28),
            RadiusX = 0.75,
            RadiusY = 0.75
        };
        brush.GradientStops.Add(new GradientStop(center, 0));
        brush.GradientStops.Add(new GradientStop(edge, 1));
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private void AdvanceHintPhase()
    {
        const double step = 0.05;

        if (_hintRising)
        {
            _hintPhase += step;
            if (_hintPhase >= 1)
            {
                _hintPhase = 1;
                _hintRising = false;
            }
        }
        else
        {
            _hintPhase -= step;
            if (_hintPhase <= 0)
            {
                _hintPhase = 0;
                _hintRising = true;
            }
        }
    }

    /// <summary>呼吸相位 0 → 1 时颜色由橙 (255,154,60) 渐变到红 (255,36,36)。</summary>
    private static Color HintColor(double phase)
    {
        byte g = (byte)(154 + (36 - 154) * phase);
        byte b = (byte)(60 + (36 - 60) * phase);
        return Color.FromRgb(0xFF, g, b);
    }
}
