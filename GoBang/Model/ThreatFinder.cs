namespace GoBang.Model;

/// <summary>
/// 棋型威胁分析：找出某方下一步能形成五连（获胜点）和四连（可成四的点）的空位。
/// </summary>
public static class ThreatFinder
{
    private static readonly (int Dr, int Dc)[] Directions =
    {
        (0, 1), (1, 0), (1, 1), (1, -1)
    };

    /// <summary>
    /// 假设 stone 落在每个空位，统计其四个方向形成的最大连子数：
    /// 连成 5 及以上记为获胜点，连成 4 且至少一端开放记为可成四的点。
    /// </summary>
    public static (List<(int Row, int Col)> WinPoints, List<(int Row, int Col)> FourPoints) FindFor(
        Board board, Stone stone)
    {
        var winPoints = new List<(int Row, int Col)>();
        var fourPoints = new List<(int Row, int Col)>();

        for (int row = 0; row < Board.Size; row++)
        {
            for (int col = 0; col < Board.Size; col++)
            {
                if (!board.IsEmpty(row, col)) continue;

                int maxCount = 0;
                bool maxOpen = false;

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

                    if (count > maxCount)
                    {
                        maxCount = count;
                        maxOpen = openEnds > 0;
                    }
                    else if (count == maxCount && openEnds > 0)
                    {
                        maxOpen = true;
                    }
                }

                if (maxCount >= 5)
                    winPoints.Add((row, col));
                else if (maxCount == 4 && maxOpen)
                    fourPoints.Add((row, col));
            }
        }

        return (winPoints, fourPoints);
    }
}
