using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GoBang.Ai;
using GoBang.Model;

namespace GoBang;

public partial class MainWindow : Window
{
    private static readonly Brush BlackStoneBrush = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
    private static readonly Brush WhiteStoneBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xF2, 0xE8));

    private const int TrainingRounds = 3;
    private const int GamesPerRound = 100;
    private const int TrainEpochs = 8;

    private static readonly string ModelPath =
        Path.Combine(AppContext.BaseDirectory, "models", "policy.bin");

    private readonly Board _board = new();
    private Stone _turn = Stone.Black;
    private bool _gameOver;
    private bool _aiThinking;
    private bool _ready;
    private bool _training;
    private int _gameVersion;
    private PolicyNetwork _network = new();
    private CancellationTokenSource? _trainCts;

    public MainWindow()
    {
        InitializeComponent();
        BoardView.CellClicked += OnCellClicked;
        _network = PolicyNetwork.Load(ModelPath) ?? new PolicyNetwork();
        _ready = true;
        ResetGame();
        UpdateAiStatus();
    }

    private bool IsPve => PveRadio.IsChecked == true;

    private Stone HumanStone => WhiteSideRadio.IsChecked == true ? Stone.White : Stone.Black;

    private Stone AiStone => HumanStone.Opponent();

    private void OnCellClicked(int row, int col)
    {
        if (!_ready || _training || _gameOver || _aiThinking) return;
        if (IsPve && _turn != HumanStone) return;
        if (!_board.IsEmpty(row, col)) return;

        Place(row, col);
    }

    private void Place(int row, int col)
    {
        _board.Place(row, col, _turn);
        RebuildLog();

        if (_board.FindWinLine(row, col) is { } winLine)
        {
            _gameOver = true;
            BoardView.WinLine = winLine;
            UpdateStatus($"{_turn.ToDisplayName()} 获胜！");
            return;
        }

        if (_board.IsFull)
        {
            _gameOver = true;
            UpdateStatus("棋盘已满，平局");
            return;
        }

        _turn = _turn.Opponent();
        UpdateStatus();
        StartAiIfNeeded();
    }

    private async void StartAiIfNeeded()
    {
        if (!IsPve || _training || _gameOver || _turn != AiStone) return;

        int version = _gameVersion;
        _aiThinking = true;
        UpdateStatus("电脑思考中…");

        try
        {
            await Task.Delay(350);
            if (version != _gameVersion || _training || _gameOver || _turn != AiStone) return;

            if (ChooseAiMove() is { } move)
                Place(move.Row, move.Col);
        }
        finally
        {
            if (version == _gameVersion)
            {
                _aiThinking = false;
                if (!_gameOver) UpdateStatus();
            }
        }
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => ResetGame();

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ResetGame();
    }

    private void HumanSide_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ResetGame();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _training || _aiThinking || _board.MoveCount == 0) return;

        _gameOver = false;
        BoardView.WinLine = null;

        if (IsPve)
        {
            // 撤销到人类最后一次落子之前
            while (_board.MoveCount > 0)
            {
                Move? last = _board.Undo();
                if (last is { } move && move.Stone == HumanStone) break;
            }
        }
        else
        {
            _board.Undo();
        }

        _turn = _board.MoveCount == 0 ? Stone.Black : _board.History[^1].Stone.Opponent();

        RebuildLog();
        UpdateStatus();

        // 人类执白时可能撤成空盘，需要让电脑重新先行
        if (!_training) StartAiIfNeeded();
    }

    private void ResetGame()
    {
        _board.Clear();
        _turn = Stone.Black;
        _gameOver = false;
        _aiThinking = false;
        _gameVersion++;

        BoardView.GameBoard = _board;
        BoardView.WinLine = null;
        BoardView.HintPoints = null;

        UpdateSideUi();
        RebuildLog();
        UpdateStatus();
        if (!_training) StartAiIfNeeded();
    }

    private void UpdateSideUi()
    {
        BlackSideRadio.IsEnabled = IsPve;
        WhiteSideRadio.IsEnabled = IsPve;

        SideHintText.Text = IsPve
            ? HumanStone == Stone.Black
                ? "黑棋先手，由你先落子"
                : "白棋后手，电脑执黑先行"
            : "双人模式：双方各执一色";
    }

    private void UpdateStatus(string? message = null)
    {
        if (!_ready) return;

        if (message != null)
        {
            StatusText.Text = message;
        }
        else if (IsPve)
        {
            StatusText.Text = _turn == HumanStone
                ? $"轮到你了（你执 {HumanStone.ToDisplayName()}）"
                : "电脑思考中…";
        }
        else
        {
            StatusText.Text = $"轮到 {_turn.ToDisplayName()}";
        }

        TurnDot.Fill = _gameOver
            ? Brushes.Transparent
            : _turn == Stone.Black ? BlackStoneBrush : WhiteStoneBrush;

        MoveText.Text = $"第 {_board.MoveCount} 手";

        BoardView.PreviewStone = _gameOver || (IsPve && _turn == AiStone)
            ? Stone.None
            : _turn;

        RefreshHints();
    }

    /// <summary>提示当前棋手：对手下一手能成四、成五的点，红色呼吸闪烁。</summary>
    private void RefreshHints()
    {
        if (_gameOver || _board.MoveCount == 0 || (IsPve && _turn == AiStone))
        {
            BoardView.HintPoints = null;
            return;
        }

        (List<(int Row, int Col)> winPoints, List<(int Row, int Col)> fourPoints) =
            ThreatFinder.FindFor(_board, _turn.Opponent());

        List<(int Row, int Col)> hints = [.. winPoints, .. fourPoints];
        BoardView.HintPoints = hints.Count == 0 ? null : hints;
    }

    private (int Row, int Col)? ChooseAiMove()
    {
        if (_network.IsTrained && NeuralAi.ChooseMove(_board, AiStone, _network) is { } move)
            return move;

        return SimpleAi.ChooseMove(_board, AiStone);
    }

    private void UpdateAiStatus()
    {
        AiStatusText.Text = _network.IsTrained
            ? $"AI：神经网络（{_network.TrainedRounds} 轮 · loss {_network.LastLoss:F3}）"
            : "AI：启发式（尚未训练）";
    }

    private async void Train_Click(object sender, RoutedEventArgs e)
    {
        if (_training)
        {
            _trainCts?.Cancel();
            return;
        }

        _training = true;
        _trainCts = new CancellationTokenSource();
        CancellationToken token = _trainCts.Token;
        TrainButton.Content = "取消训练";

        ResetGame(); // 训练期间清空并锁定棋盘

        PolicyNetwork start = _network.Clone();
        bool succeeded = false;

        try
        {
            PolicyNetwork trained = await Task.Run(() => SelfPlayTrainer.Train(
                start,
                TrainingRounds,
                GamesPerRound,
                TrainEpochs,
                message => Dispatcher.BeginInvoke(new Action(() => AiStatusText.Text = message)),
                token), token);

            _network = trained;
            succeeded = TrySaveModel();
        }
        catch (OperationCanceledException)
        {
            AiStatusText.Text = "训练已取消，继续使用原模型";
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "训练失败：" + ex.Message;
        }
        finally
        {
            _training = false;
            _trainCts?.Dispose();
            _trainCts = null;
            TrainButton.Content = "训练 AI";

            if (succeeded) UpdateAiStatus();
            UpdateStatus();
            StartAiIfNeeded();
        }
    }

    private bool TrySaveModel()
    {
        try
        {
            _network.Save(ModelPath);
            return true;
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "模型保存失败：" + ex.Message;
            return false;
        }
    }

    private void RebuildLog()
    {
        var builder = new StringBuilder();
        int index = 0;

        foreach (Move move in _board.History)
        {
            builder.Append($"{++index,2}. {move.Stone.ToDisplayName()} {CoordName(move.Row, move.Col)}\n");
        }

        LogText.Text = builder.ToString();
        Dispatcher.BeginInvoke(new Action(LogScroll.ScrollToEnd), DispatcherPriority.Background);
    }

    /// <summary>棋盘坐标：列 A~O，行从下往上 1~15（顶部为 15）。</summary>
    private static string CoordName(int row, int col) => $"{(char)('A' + col)}{Board.Size - row}";
}
