using GoBang.Model;

namespace GoBang.Ai;

/// <summary>
/// 简易启发式 AI：只在已有棋子周围找候选点，为每个候选点算「进攻分 + 防守分」。
/// 成五、堵五优先处理，其余取综合最高分。
/// </summary>
public static class SimpleAi
{
    public const double WinScore = 1_000_000;

    private static readonly (int Dr, int Dc)[] Directions =
    {
        (0, 1), (1, 0), (1, 1), (1, -1)
    };

    public static (int Row, int Col)? ChooseMove(Board board, Stone me)
    {
        if (board.MoveCount == 0)
            return (Board.Size / 2, Board.Size / 2);

        Stone opponent = me.Opponent();
        (int Row, int Col)? blockMove = null;
        (int Row, int Col)? best = null;
        double bestScore = double.NegativeInfinity;

        foreach ((int row, int col) in CollectCandidates(board))
        {
            double attack = Evaluate(board, row, col, me);
            if (attack >= WinScore) return (row, col);

            double defend = Evaluate(board, row, col, opponent);
            if (defend >= WinScore)
            {
                blockMove ??= (row, col);
                continue;
            }

            double score = attack + defend * 0.9 + CenterBias(row, col);
            if (score > bestScore)
            {
                bestScore = score;
                best = (row, col);
            }
        }

        return blockMove ?? best;
    }

    /// <summary>候选点 = 已有棋子两格范围内的空位，避免逐格扫描整盘。</summary>
    private static List<(int Row, int Col)> CollectCandidates(Board board)
    {
        var candidates = new HashSet<(int Row, int Col)>();

        foreach (Move move in board.History)
        {
            for (int dr = -2; dr <= 2; dr++)
            {
                for (int dc = -2; dc <= 2; dc++)
                {
                    int row = move.Row + dr;
                    int col = move.Col + dc;
                    if (Board.InBounds(row, col) && board.IsEmpty(row, col))
                        candidates.Add((row, col));
                }
            }
        }

        if (candidates.Count == 0)
        {
            for (int row = 0; row < Board.Size; row++)
                for (int col = 0; col < Board.Size; col++)
                    if (board.IsEmpty(row, col)) candidates.Add((row, col));
        }

        return new List<(int Row, int Col)>(candidates);
    }

    /// <summary>假设在 (row, col) 放一枚 stone，统计四个方向形成的棋型分数。</summary>
    private static double Evaluate(Board board, int row, int col, Stone stone)
    {
        double total = 0;

        foreach ((int dr, int dc) in Directions)
        {
            int count = 1;
            int openEnds = 0;

            int r = row + dr, c = col + dc;
            while (Board.InBounds(r, c) && board[r, c] == stone)
            {
                count++;
                r += dr;
                c += dc;
            }
            if (Board.InBounds(r, c) && board[r, c] == Stone.None) openEnds++;

            r = row - dr;
            c = col - dc;
            while (Board.InBounds(r, c) && board[r, c] == stone)
            {
                count++;
                r -= dr;
                c -= dc;
            }
            if (Board.InBounds(r, c) && board[r, c] == Stone.None) openEnds++;

            total += ShapeScore(count, openEnds);
        }

        return total;
    }

    private static double ShapeScore(int count, int openEnds)
    {
        if (count >= 5) return WinScore;
        if (openEnds == 0) return 0;

        return count switch
        {
            4 => openEnds == 2 ? 100_000 : 15_000,
            3 => openEnds == 2 ? 8_000 : 800,
            2 => openEnds == 2 ? 600 : 80,
            _ => openEnds == 2 ? 40 : 10
        };
    }

    /// <summary>评估某方在 (row, col) 落子的棋型分（神经网络与提示功能共用）。</summary>
    public static double ScorePoint(Board board, int row, int col, Stone stone) =>
        Evaluate(board, row, col, stone);

    private static double CenterBias(int row, int col)
    {
        double center = (Board.Size - 1) / 2.0;
        return (Board.Size - Math.Abs(row - center) - Math.Abs(col - center)) * 0.6;
    }
}
