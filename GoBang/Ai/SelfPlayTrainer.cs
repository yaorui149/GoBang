using GoBang.Model;

namespace GoBang.Ai;

/// <summary>
/// 自我对弈 + 监督学习：
/// 未训练时用启发式 AI 对弈产生示范棋谱，之后用「网络 + 少量启发式」对弈，
/// 以每局胜者的落子作为监督标签训练策略网络，逐轮迭代提升。
/// </summary>
public static class SelfPlayTrainer
{
    private const double HeuristicMixRate = 0.3;

    public static PolicyNetwork Train(
        PolicyNetwork network,
        int rounds,
        int gamesPerRound,
        int epochs,
        Action<string>? report,
        CancellationToken cancellationToken)
    {
        for (int round = 1; round <= rounds; round++)
        {
            var samples = new List<TrainingSample>();

            for (int game = 1; game <= gamesPerRound; game++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                samples.AddRange(PlayGame(network, heuristicOnly: !network.IsTrained));
                report?.Invoke($"训练中 · 第 {round}/{rounds} 轮 · 第 {game}/{gamesPerRound} 局");
            }

            cancellationToken.ThrowIfCancellationRequested();

            double loss = network.Train(samples, epochs, learningRate: 0.05f);
            report?.Invoke($"第 {round}/{rounds} 轮完成 · 样本 {samples.Count} · loss {loss:F3}");
        }

        return network;
    }

    /// <summary>
    /// 下一整局：胜方的实际落子作为主标签；其余局面用启发式教师的推荐补齐，
    /// 既保留自我对弈的胜负信息，又保证每个局面都有监督信号。
    /// </summary>
    private static List<TrainingSample> PlayGame(PolicyNetwork network, bool heuristicOnly)
    {
        var board = new Board();
        var records = new List<(Stone Side, float[] Input, int Actual, int Teacher)>();
        Stone turn = Stone.Black;
        Stone? winner = null;

        while (true)
        {
            if (Decide(board, turn, network, heuristicOnly) is not { } move) break;

            (int Row, int Col) teacher = SimpleAi.ChooseMove(board, turn) ?? move;
            records.Add((turn,
                NeuralAi.Encode(board, turn),
                move.Row * Board.Size + move.Col,
                teacher.Row * Board.Size + teacher.Col));

            board.Place(move.Row, move.Col, turn);

            if (board.FindWinLine(move.Row, move.Col) != null)
            {
                winner = turn;
                break;
            }

            if (board.IsFull) break;

            turn = turn.Opponent();
        }

        var samples = new List<TrainingSample>(records.Count);
        foreach ((Stone side, float[] input, int actual, int teacher) in records)
        {
            bool useActual = winner != null && side == winner;
            samples.Add(new TrainingSample(input, useActual ? actual : teacher));
        }

        return samples;
    }

    private static (int Row, int Col)? Decide(Board board, Stone turn, PolicyNetwork network, bool heuristicOnly)
    {
        if (!heuristicOnly && Random.Shared.NextDouble() > HeuristicMixRate)
        {
            if (NeuralAi.ChooseMove(board, turn, network, explore: true) is { } move)
                return move;
        }

        return SimpleAi.ChooseMove(board, turn);
    }
}
