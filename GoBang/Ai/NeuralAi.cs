using GoBang.Model;

namespace GoBang.Ai;

/// <summary>神经网络落子：把棋盘编码成行棋方视角的平面，取概率最高的合法点。</summary>
public static class NeuralAi
{
    /// <summary>编码为 15×15×2 平面：第 0 层己方棋子，第 1 层对方棋子。</summary>
    public static float[] Encode(Board board, Stone me)
    {
        var input = new float[PolicyNetwork.InputSize];
        Stone opponent = me.Opponent();
        int planeSize = Board.Size * Board.Size;

        for (int row = 0; row < Board.Size; row++)
        {
            for (int col = 0; col < Board.Size; col++)
            {
                Stone stone = board[row, col];
                if (stone == Stone.None) continue;

                int index = row * Board.Size + col;
                if (stone == me) input[index] = 1f;
                else if (stone == opponent) input[planeSize + index] = 1f;
            }
        }

        return input;
    }

    /// <summary>
    /// 落子决策：探索模式直接用网络概率采样（供自对弈训练）；
    /// 对弈模式先做战术短路（成五 / 堵五 / 防活三），再以「网络概率 + 启发式评分」选点。
    /// </summary>
    public static (int Row, int Col)? ChooseMove(Board board, Stone me, PolicyNetwork network, bool explore = false)
    {
        float[] input = Encode(board, me);
        float[] probs = network.Predict(input);

        if (explore)
        {
            double total = 0;
            for (int i = 0; i < probs.Length; i++)
                if (board.IsEmpty(i / Board.Size, i % Board.Size)) total += probs[i];

            if (total <= 1e-9) return null;

            double threshold = Random.Shared.NextDouble() * total;
            double cumulative = 0;
            for (int i = 0; i < probs.Length; i++)
            {
                if (!board.IsEmpty(i / Board.Size, i % Board.Size)) continue;

                cumulative += probs[i];
                if (threshold <= cumulative) return (i / Board.Size, i % Board.Size);
            }
        }

        // 战术层：先赢、再堵、再防对方成四
        (List<(int Row, int Col)> myWins, List<(int Row, int Col)> myFours) =
            ThreatFinder.FindFor(board, me);
        if (myWins.Count > 0) return PickByProbability(board, myWins, probs, preferCenter: true);

        (List<(int Row, int Col)> oppWins, List<(int Row, int Col)> oppFours) =
            ThreatFinder.FindFor(board, me.Opponent());
        if (oppWins.Count > 0) return PickByProbability(board, oppWins, probs, preferCenter: true);

        // 进攻优先：自己先成四逼对方应对，其次再堵对方活三
        if (myFours.Count > 0) return PickByProbability(board, myFours, probs, preferCenter: true);
        if (oppFours.Count > 0) return PickByProbability(board, oppFours, probs, preferCenter: true);

        // 常规局面：网络概率与启发式评分混合
        (int Row, int Col)? best = null;
        double bestScore = double.NegativeInfinity;

        for (int i = 0; i < probs.Length; i++)
        {
            int row = i / Board.Size;
            int col = i % Board.Size;
            if (!board.IsEmpty(row, col)) continue;

            double attack = SimpleAi.ScorePoint(board, row, col, me);
            double defend = SimpleAi.ScorePoint(board, row, col, me.Opponent());
            double score = probs[i] * 2.0 + attack / 15000.0 + defend / 30000.0;

            if (score > bestScore)
            {
                bestScore = score;
                best = (row, col);
            }
        }

        return best;
    }

    private static (int Row, int Col) PickByProbability(
        Board board,
        List<(int Row, int Col)> candidates,
        float[] probs,
        bool preferCenter)
    {
        double bestScore = double.NegativeInfinity;
        (int Row, int Col) best = candidates[0];
        double center = (Board.Size - 1) / 2.0;

        foreach ((int row, int col) in candidates)
        {
            if (!board.IsEmpty(row, col)) continue;

            double score = probs[row * Board.Size + col];
            if (preferCenter)
                score += (Board.Size - Math.Abs(row - center) - Math.Abs(col - center)) * 0.0005;

            if (score > bestScore)
            {
                bestScore = score;
                best = (row, col);
            }
        }

        return best;
    }
}
