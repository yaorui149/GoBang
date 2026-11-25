namespace GoBang.Model;

public enum Stone
{
    None = 0,
    Black = 1,
    White = 2
}

public static class StoneExtensions
{
    public static Stone Opponent(this Stone stone) =>
        stone == Stone.Black ? Stone.White : Stone.Black;

    public static string ToDisplayName(this Stone stone) => stone switch
    {
        Stone.Black => "● 黑方",
        Stone.White => "○ 白方",
        _ => "空"
    };
}

public readonly record struct Move(int Row, int Col, Stone Stone);

/// <summary>
/// 棋盘状态与规则判定，纯逻辑、不依赖 UI。
/// </summary>
public sealed class Board
{
    public const int Size = 15;

    private static readonly (int Dr, int Dc)[] Directions =
    {
        (0, 1), (1, 0), (1, 1), (1, -1)
    };

    private readonly Stone[,] _cells = new Stone[Size, Size];
    private readonly List<Move> _history = new();

    public Stone this[int row, int col] => _cells[row, col];

    public IReadOnlyList<Move> History => _history;

    public int MoveCount => _history.Count;

    public bool IsFull => _history.Count == Size * Size;

    public static bool InBounds(int row, int col) =>
        row >= 0 && row < Size && col >= 0 && col < Size;

    public bool IsEmpty(int row, int col) =>
        InBounds(row, col) && _cells[row, col] == Stone.None;

    public void Place(int row, int col, Stone stone)
    {
        if (stone == Stone.None)
            throw new ArgumentException("不能放置空棋子。", nameof(stone));
        if (!IsEmpty(row, col))
            throw new InvalidOperationException($"({row}, {col}) 无法落子。");

        _cells[row, col] = stone;
        _history.Add(new Move(row, col, stone));
    }

    public Move? Undo()
    {
        if (_history.Count == 0) return null;

        Move last = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        _cells[last.Row, last.Col] = Stone.None;
        return last;
    }

    public void Clear()
    {
        Array.Clear(_cells);
        _history.Clear();
    }

    /// <summary>
    /// 判断 (row, col) 上的棋子是否形成五连，返回连线坐标；否则返回 null。
    /// 只从落子点沿四个方向检查，每侧最多延伸 4 格（判定五连够用），不扫描全盘。
    /// </summary>
    public List<(int Row, int Col)>? FindWinLine(int row, int col)
    {
        if (!InBounds(row, col)) return null;

        Stone stone = _cells[row, col];
        if (stone == Stone.None) return null;

        foreach ((int dr, int dc) in Directions)
        {
            int backward = CountSide(row, col, -dr, -dc, stone);
            int forward = CountSide(row, col, dr, dc, stone);
            if (backward + forward + 1 < 5) continue;

            var line = new List<(int Row, int Col)>(backward + forward + 1);
            for (int i = backward; i >= 1; i--)
                line.Add((row - dr * i, col - dc * i));
            line.Add((row, col));
            for (int i = 1; i <= forward; i++)
                line.Add((row + dr * i, col + dc * i));
            return line;
        }

        return null;
    }

    /// <summary>从 (row, col) 沿 (dr, dc) 方向最多数 4 枚连续同色棋子。</summary>
    private int CountSide(int row, int col, int dr, int dc, Stone stone)
    {
        int count = 0;
        for (int i = 1; i <= 4; i++)
        {
            int r = row + dr * i;
            int c = col + dc * i;
            if (!InBounds(r, c) || _cells[r, c] != stone) break;
            count++;
        }

        return count;
    }
}
