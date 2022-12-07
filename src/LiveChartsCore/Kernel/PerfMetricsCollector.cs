// The MIT License(MIT)
//
// Copyright(c) 2021 Alberto Rodriguez Orozco & LiveCharts Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Linq;
using System.Text;

namespace LiveChartsCore.Kernel;
internal class PerfMetricsCollector
{
    private readonly FastCircularBuffer<long> _drawLockTicks = new(5, 0); // 32 items
    private readonly FastCircularBuffer<long> _drawTicks = new(5, (long)2e4); // 32 items, 2 ms default average
    private readonly FastCircularBuffer<long> _measureTicks = new(5, (long)3e4); // 32 items, 3 ms default average
    private readonly FastCircularBuffer<long> _measureVisualsTicks = new(5, (long)2e4); // 32 items, 2 ms default average
    private readonly StringBuilder _strBuilder = new();

    public void AddDrawLockTime(long ticks)
    {
        _drawLockTicks.Append(ticks);
    }

    public void AddDrawTime(long ticks)
    {
        _drawTicks.Append(ticks);
    }

    public void AddMeasureTime(long ticks)
    {
        _measureTicks.Append(ticks);
    }

    public void AddMeasureVisualsTime(long ticks)
    {
        _measureVisualsTicks.Append(ticks);
    }

    public string GetFormattedString(bool singleLine)
    {
        var sb = _strBuilder;

        _ = sb.AppendFormat("Draw lock time: {0:F4}", _drawLockTicks.Data.Average() / 1e4);
        _ = singleLine ? sb.Append("; ") : sb.AppendLine();
        _ = sb.AppendFormat("Draw time: {0:F4}", _drawTicks.Data.Average() / 1e4);
        _ = singleLine ? sb.Append("; ") : sb.AppendLine();
        _ = sb.AppendFormat("Measure time: {0:F4}", _measureTicks.Data.Average() / 1e4);
        _ = singleLine ? sb.Append("; ") : sb.AppendLine();
        _ = sb.AppendFormat("Measure visuals time: {0:F4}", _measureVisualsTicks.Data.Average() / 1e4);

        var res = sb.ToString();
        _ = sb.Clear();
        return res;
    }

    private class FastCircularBuffer<T>
    {
        private readonly int _mask;
        private int _nextIndex;

        public T[] Data { get; private set; }

        public FastCircularBuffer(int sizeDegree, T defaultValue)
        {
            var size = 1 << sizeDegree;
            _mask = size - 1;
            Data = new T[size];
            _nextIndex = 0;

            for (var i = 0; i < size; i++)
                Data[i] = defaultValue;
        }

        public void Append(T value)
        {
            Data[_nextIndex] = value;
            _nextIndex = (_nextIndex + 1) & _mask;
        }
    }
}
