using JetBlack.Core.Collections.Generic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RIO
{
    public enum Solution { Regression, Average }
    public class NewAnalysis
    {
        const double eps = 1E-10;

        private float addRMS, lastValue, rms, swh;
        private int usedPoints, alertValue;
        private double alertSignal, currentTime, lastTime;
        private DateTime nextTime;
        private bool empty = true, tuning = true;
        private CircularBuffer<float> swhBuffer;
        private int swhSamples, tuneCount = 0;
        readonly float SamplingPeriod;
        readonly int secPeriod;
        readonly int alertStep = 1;
        readonly Solution solution;
        readonly float ratioRMS, Threshold, Tmax300, Tmax30;
        readonly Queue<float> Values, ShortAverage, LongAverage, Differences;
        readonly Queue<double> TimeStamps;
        readonly Stats samples = new Stats();

        public float AddRMS { get => addRMS; set => addRMS = value; }
        public DateTime LastTime { get => DateTime.FromOADate(currentTime); }
        public float LastValue { get => lastValue; }
        public int UsedPoints { get => usedPoints; }
        public double AlertSignal { get => alertSignal; }
        public int Alert { get => alertValue; }
        public float RMS { get => rms; }
        public float Swh { get => swh; }
        public int Period { get => secPeriod; }
        public int ShortWindowSize { get; private set; } = 10;
        public int LongWindowSize { get; private set; } = 100;
        public DateTime NextTime { get => nextTime; set => nextTime = value; }
        public double ShortForecast { get; private set; } = 0;
        public double LongForecast { get; private set; } = 0;
        public NewAnalysis(int shortWindowSize, int longWindowSize, float ratio, float threshold,
        float period, Solution solution = Solution.Regression)
        {
            secPeriod = (int)period;
            this.solution = solution;
            if (secPeriod < 11)
                alertStep = 1;
            else if (secPeriod < 21)
                alertStep = 2;
            else if (secPeriod < 31)
                alertStep = 3;
            else if (secPeriod < 41)
                alertStep = 4;
            else alertStep = 5;
            SamplingPeriod = period / 86400;

            lastTime = double.MinValue;
            ratioRMS = ratio;
            Threshold = threshold;

            LongWindowSize = longWindowSize;
            ShortWindowSize = shortWindowSize;

            Tmax300 = period * LongWindowSize;
            Tmax30 = period * ShortWindowSize;

            Values = new Queue<float>(LongWindowSize);
            TimeStamps = new Queue<double>(LongWindowSize);
            ShortAverage = new Queue<float>(LongWindowSize);
            LongAverage = new Queue<float>(LongWindowSize);
            Differences = new Queue<float>(LongWindowSize);

            swhSamples = 3600 / secPeriod;
            swhBuffer = new CircularBuffer<float>(swhSamples);
        }

        public bool AddMeasure(DateTime timestamp, float sensorValue)
        {
            bool retValue = false;

            currentTime = timestamp.ToOADate();
            if (nextTime == DateTime.MinValue)
                nextTime = timestamp.AddSeconds(secPeriod - (timestamp.Second % secPeriod)).AddMilliseconds(-timestamp.Millisecond);
            if (currentTime < lastTime)
            {   // System clock changed
                Reset();
            }
            if (empty)
            {
                TimeStamps.Clear();
                Values.Clear();
                ShortAverage.Clear();
                LongAverage.Clear();
                Differences.Clear();

                lastTime = currentTime;
                empty = false;

                for (int idx = 0; idx <= LongWindowSize; idx++)
                {
                    TimeStamps.Enqueue(currentTime - (LongWindowSize - idx) * SamplingPeriod);
                    Values.Enqueue(sensorValue);
                    ShortAverage.Enqueue(0); // sensorValue;
                    LongAverage.Enqueue(0); //sensorValue;
                    Differences.Enqueue(0);
                }
                tuning = true;
                tuneCount = LongWindowSize;
            }

            samples.Add(sensorValue);
            if (timestamp > nextTime)
            {
                while (nextTime < timestamp)
                    nextTime = nextTime.AddSeconds(secPeriod);
                usedPoints = samples.Quantity;
                double Avg = samples.Average;
                samples.Clear();

                lastValue = (float)Avg;

                Values.Dequeue();
                TimeStamps.Dequeue();
                ShortAverage.Dequeue();
                LongAverage.Dequeue();
                Differences.Dequeue();

                Values.Enqueue(lastValue);
                TimeStamps.Enqueue(currentTime);

                if (tuning)
                {
                    tuning = (--tuneCount > 0);
                    ShortAverage.Enqueue((float)(ShortForecast = lastValue));
                    LongAverage.Enqueue((float)(LongForecast = lastValue));
                    Differences.Enqueue(0.0F);
                }
                else
                {
                    if (solution == Solution.Regression)
                    {
                        ShortForecast = Regression(TimeStamps.ToArray(),
                                                   Values.ToArray(),
                                                   Math.Max(tuneCount, LongWindowSize - ShortWindowSize),
                                                   LongWindowSize,
                                                   currentTime);
                        LongForecast = Regression(TimeStamps.ToArray(),
                                                   Values.ToArray(),
                                                   tuneCount,
                                                   LongWindowSize,
                                                   currentTime);
                    }
                    else
                    {
                        ShortForecast = Values.ToArray().Skip(Math.Max(tuneCount, LongWindowSize - ShortWindowSize)).Average();
                        LongForecast = Values.ToArray().Skip(tuneCount).Average();
                    }

                    try
                    {   // Significant Wave Height computation
                        float data = 2 * (float)Math.Abs(Avg - LongForecast);
                        swhBuffer.Enqueue(data);
                        swh = (float)swhBuffer.OrderByDescending(f => f).Take((int)(swhSamples / 3)).Average();
                    }
                    catch (Exception ex)
                    {
                        Manager.OnNotify("error", "{0}\n{1}", ex.Message, ex.StackTrace);
                    }

                    ShortAverage.Enqueue((float)ShortForecast);
                    LongAverage.Enqueue((float)LongForecast);
                    double difference = ShortForecast - LongForecast;

                    Differences.Enqueue((float)difference);
                    if (tuning)
                    {
                        alertSignal = 0;
                        alertValue = 0;
                    }
                    else
                    {
                        rms = RootMeanSquare(Differences.ToArray(), 0, LongWindowSize / 2);

                        alertSignal = Math.Abs(difference);
                        if (alertSignal > (rms * ratioRMS + Threshold /* addRMS */) && alertSignal > Threshold)
                            alertValue = Math.Min(10, alertValue + alertStep);
                        else
                            alertValue = Math.Max(0, alertValue - alertStep);
                    }
                }

                lastTime = currentTime;
                retValue = true;
            }

            return retValue;
        }
        double Regression(double[] x, float[] y, int min, int max, double xForecast)
        {
            int k;
            double a, b, c;
            double a11, a12, a13, a21, a22, a23, a31, a32, a33, c1, c2, c3;
            double sumx2 = 0, sumx1 = 0, sumy1 = 0;
            double sumx3 = 0, sumx4 = 0, sumx2y = 0, sumxy = 0;
            int np = 0;

            for (k = min; k <= max; k++)
            {
                double dx = (double)(x[k] - x[min]), dx2 = dx * dx;
                sumx4 += dx2 * dx2;
                sumx3 += dx2 * dx;
                sumx2 += dx2;
                sumx1 += dx;
                sumy1 += y[k];
                sumxy += dx * y[k];
                sumx2y += dx2 * y[k];
                np += 1;
            }

            c1 = sumx2y; c2 = sumxy; c3 = sumy1;

            a11 = sumx4; a12 = sumx3; a13 = sumx2;
            a21 = sumx3; a22 = sumx2; a23 = sumx1;
            a31 = sumx2; a32 = sumx1; a33 = np;

            double denom = Multi2(a11, a21, a31, a12, a22, a32, a13, a23, a33);
            if (Math.Abs(denom) > eps)
            {
                a = Multi2(c1, c2, c3, a12, a22, a32, a13, a23, a33) / denom;
                b = Multi2(a11, a21, a31, c1, c2, c3, a13, a23, a33) / denom;
                c = Multi2(a11, a21, a31, a12, a22, a32, c1, c2, c3) / denom;
                double v = xForecast - x[min];
                return a * v * v + b * v + c;
            }
            else
                return sumy1 / np;
        }
        private static double Multi2(double a11, double a21, double a31, double a12, double a22, double a32, double a13, double a23, double a33)
        {
            double multi0 = a11 * a22 * a33 + a12 * a23 * a31 + a13 * a21 * a32
            - a13 * a22 * a31 - a11 * a23 * a32 - a33 * a12 * a21;
            return multi0;
        }
        private static float RootMeanSquare(float[] data, int min, int max)
        {
            float avg = 0, rm = 0, den = max - min + 1;

            for (int idx = min; idx <= max; idx++)
                avg += data[idx] / den;
            for (int idx = min; idx <= max; idx++)
                rm += (data[idx] - avg) * (data[idx] - avg) / den;

            return (float)Math.Sqrt(rm);
        }
        public void Reset()
        {
            empty = true;
        }

        public void Dump(string txtBuffer)
        {
            double[] ts = TimeStamps.ToArray();
            float[] vs = Values.ToArray(), sa = ShortAverage.ToArray(), la = LongAverage.ToArray();
            List<string> lines = new List<string>(LongWindowSize + 5);
            lines.Add(string.Format("{0},{1}", LongWindowSize, LongWindowSize));
            for (int k = 0; k <= LongWindowSize; k++)
                lines.Add(string.Format("{0},{1},{2},{3}", ts[k], vs[k], sa[k], la[k]));
            File.WriteAllLines(txtBuffer, lines);
        }

        public void Recover(string txtBuffer)
        {
            if (File.Exists(txtBuffer))
            {
                string[] lines = File.ReadAllLines(txtBuffer);
                if (lines.Length == 0) return;

                string[] idxs = lines[0].Split(',');
                if (idxs.Length != 2) return;

                if (int.TryParse(idxs[0], out int index300) && int.TryParse(idxs[1], out int index1))
                {
                    int k = 0, skip = 1 + ((LongWindowSize < index300) ? index300 - LongWindowSize : 0);
                    foreach (string line in lines.Skip(skip))
                    {
                        string[] fields = line.Split(',');
                        if (fields.Length != 4) break;

                        if (double.TryParse(fields[0], out double timestamp) &&
                            float.TryParse(fields[1], out float val) &&
                            float.TryParse(fields[2], out float shortAverage) &&
                            float.TryParse(fields[3], out float longAverage))
                        {
                            TimeStamps.Enqueue(timestamp);
                            Values.Enqueue(val);
                            ShortAverage.Enqueue(shortAverage);
                            LongAverage.Enqueue(longAverage);
                        }
                        else
                            break;
                        lastTime = timestamp;

                        if (++k > LongWindowSize) break;
                    }
                }
                if ((DateTime.UtcNow - LastTime).TotalMinutes < 5)
                    Reset();
            }
        }
    }
}
