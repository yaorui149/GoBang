using System.IO;
using GoBang.Model;

namespace GoBang.Ai;

/// <summary>一条监督学习样本：当前行棋方视角的棋盘平面 + 目标落子索引。</summary>
public readonly record struct TrainingSample(float[] Input, int MoveIndex);

/// <summary>
/// 单隐藏层策略网络：输入 15×15×2 平面，输出 225 个落子概率。
/// 手写前向/反向传播，不依赖第三方库。
/// </summary>
public sealed class PolicyNetwork
{
    private const int FileMagic = 0x47425031; // "GBP1"

    public const int BoardSize = Board.Size;
    public const int InputSize = BoardSize * BoardSize * 2;
    public const int OutputSize = BoardSize * BoardSize;
    private const int HiddenSize = 128;

    private readonly float[] _w1 = new float[InputSize * HiddenSize];
    private readonly float[] _b1 = new float[HiddenSize];
    private readonly float[] _w2 = new float[HiddenSize * OutputSize];
    private readonly float[] _b2 = new float[OutputSize];

    private readonly float[] _hidden = new float[HiddenSize];
    private readonly float[] _logits = new float[OutputSize];
    private readonly float[] _probs = new float[OutputSize];
    private readonly float[] _dLogits = new float[OutputSize];
    private readonly float[] _dHidden = new float[HiddenSize];
    private readonly Random _random = new();

    public bool IsTrained { get; private set; }

    public int TrainedRounds { get; private set; }

    public double LastLoss { get; private set; }

    public PolicyNetwork() => InitializeWeights();

    /// <summary>用交叉熵损失做小批量随机梯度下降，返回本次平均 loss。</summary>
    public double Train(IReadOnlyList<TrainingSample> samples, int epochs, float learningRate)
    {
        if (samples.Count == 0) return 0;

        var order = new int[samples.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;

        double totalLoss = 0;
        int counted = 0;

        for (int epoch = 0; epoch < epochs; epoch++)
        {
            Shuffle(order);
            float lr = learningRate * (1f - 0.5f * epoch / epochs);

            foreach (int index in order)
            {
                TrainingSample sample = samples[index];
                float[] probs = Forward(sample.Input);

                totalLoss -= Math.Log(Math.Max(probs[sample.MoveIndex], 1e-6));
                counted++;

                Array.Copy(probs, _dLogits, OutputSize);
                _dLogits[sample.MoveIndex] -= 1;

                Array.Clear(_dHidden);
                for (int h = 0; h < HiddenSize; h++)
                {
                    float v = _hidden[h];
                    if (v <= 0) continue;

                    int offset = h * OutputSize;
                    float sum = 0;
                    for (int o = 0; o < OutputSize; o++)
                        sum += _w2[offset + o] * _dLogits[o];
                    _dHidden[h] = sum;
                }

                for (int h = 0; h < HiddenSize; h++)
                {
                    float v = _hidden[h];
                    if (v == 0) continue;

                    int offset = h * OutputSize;
                    for (int o = 0; o < OutputSize; o++)
                        _w2[offset + o] -= lr * _dLogits[o] * v;
                }
                for (int o = 0; o < OutputSize; o++)
                    _b2[o] -= lr * _dLogits[o];

                for (int i = 0; i < InputSize; i++)
                {
                    float v = sample.Input[i];
                    if (v == 0) continue;

                    int offset = i * HiddenSize;
                    for (int h = 0; h < HiddenSize; h++)
                        _w1[offset + h] -= lr * _dHidden[h] * v;
                }
                for (int h = 0; h < HiddenSize; h++)
                    _b1[h] -= lr * _dHidden[h];
            }
        }

        IsTrained = true;
        TrainedRounds++;
        LastLoss = totalLoss / Math.Max(counted, 1);
        return LastLoss;
    }

    /// <summary>前向推理，返回内部概率数组（只读、下次调用会被覆盖）。</summary>
    public float[] Predict(float[] input) => Forward(input);

    public PolicyNetwork Clone()
    {
        var copy = new PolicyNetwork();
        Array.Copy(_w1, copy._w1, _w1.Length);
        Array.Copy(_b1, copy._b1, _b1.Length);
        Array.Copy(_w2, copy._w2, _w2.Length);
        Array.Copy(_b2, copy._b2, _b2.Length);
        copy.IsTrained = IsTrained;
        copy.TrainedRounds = TrainedRounds;
        copy.LastLoss = LastLoss;
        return copy;
    }

    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(FileMagic);
        writer.Write(IsTrained);
        writer.Write(TrainedRounds);
        writer.Write(LastLoss);
        WriteArray(writer, _w1);
        WriteArray(writer, _b1);
        WriteArray(writer, _w2);
        WriteArray(writer, _b2);
    }

    public static PolicyNetwork? Load(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (reader.ReadInt32() != FileMagic) return null;

            var network = new PolicyNetwork();
            network.IsTrained = reader.ReadBoolean();
            network.TrainedRounds = reader.ReadInt32();
            network.LastLoss = reader.ReadDouble();

            if (!ReadArray(reader, network._w1) ||
                !ReadArray(reader, network._b1) ||
                !ReadArray(reader, network._w2) ||
                !ReadArray(reader, network._b2))
                return null;

            return network;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private float[] Forward(float[] input)
    {
        Array.Copy(_b1, _hidden, HiddenSize);

        for (int i = 0; i < InputSize; i++)
        {
            float v = input[i];
            if (v == 0) continue;

            int offset = i * HiddenSize;
            for (int h = 0; h < HiddenSize; h++)
                _hidden[h] += v * _w1[offset + h];
        }

        for (int h = 0; h < HiddenSize; h++)
            if (_hidden[h] < 0) _hidden[h] = 0;

        Array.Copy(_b2, _logits, OutputSize);
        for (int h = 0; h < HiddenSize; h++)
        {
            float v = _hidden[h];
            if (v == 0) continue;

            int offset = h * OutputSize;
            for (int o = 0; o < OutputSize; o++)
                _logits[o] += v * _w2[offset + o];
        }

        Softmax(_logits, _probs);
        return _probs;
    }

    private static void Softmax(float[] logits, float[] result)
    {
        float max = float.NegativeInfinity;
        for (int i = 0; i < logits.Length; i++)
            if (logits[i] > max) max = logits[i];

        double sum = 0;
        for (int i = 0; i < logits.Length; i++)
        {
            double value = Math.Exp(logits[i] - max);
            result[i] = (float)value;
            sum += value;
        }

        float inverse = (float)(1.0 / Math.Max(sum, 1e-9));
        for (int i = 0; i < result.Length; i++)
            result[i] *= inverse;
    }

    private void InitializeWeights()
    {
        float scale1 = MathF.Sqrt(2f / InputSize);
        for (int i = 0; i < _w1.Length; i++)
            _w1[i] = (float)(_random.NextDouble() * 2 - 1) * scale1;

        float scale2 = MathF.Sqrt(2f / HiddenSize);
        for (int i = 0; i < _w2.Length; i++)
            _w2[i] = (float)(_random.NextDouble() * 2 - 1) * scale2;
    }

    private void Shuffle(int[] order)
    {
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
    }

    private static void WriteArray(BinaryWriter writer, float[] values)
    {
        writer.Write(values.Length);
        foreach (float value in values) writer.Write(value);
    }

    private static bool ReadArray(BinaryReader reader, float[] target)
    {
        int length = reader.ReadInt32();
        if (length != target.Length) return false;

        for (int i = 0; i < length; i++)
            target[i] = reader.ReadSingle();

        return true;
    }
}
