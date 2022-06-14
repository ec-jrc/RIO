using System;

namespace RIO
{
    /// <summary>
    /// Utility class to collect <see cref="double"/> values and derive statistical data from them.
    /// </summary>
    public class Stats
    {
        int n;
        double average = 0, M2 = 0, last;
        /// <summary>
        /// Average of all the samples added so far.
        /// </summary>
        public double Average { get { return average; } }
        /// <summary>
        /// Measures how far the set of numbers is spread out from their average value.
        /// </summary>
        public double Variance { get { return n > 1 ? M2 / (n - 1) : double.NaN; } }
        /// <summary>
        /// Number of the samples added to the collection.
        /// </summary>
        public int Quantity { get { return n; } }
        /// <summary>
        /// The last sample added to the collection.
        /// </summary>
        public double Last => last;
        /// <summary>
        /// Add a sample to the collection and updates the aggregations accordingly.
        /// </summary>
        /// <param name="sample">The new value.</param>
        public void Add(double sample)
        {
            last = sample;
            n++;
            double delta = (sample - average);
            average += delta / n;
            M2 += delta * (sample - average);
        }
        /// <summary>
        /// Remove a sample from the collection and updates the aggregations accordingly.
        /// </summary>
        /// <param name="sample">The value will be removed, even if not exactly that value was added previously.</param>
        public void Remove(double sample)
        {
            n--;
            double delta = average - sample;
            average += delta / n;
            M2 -= delta * (sample - average);
        }
        /// <summary>
        /// Distance of the sample from the average.
        /// </summary>
        /// <param name="sample">A value that will not be added to the collection.</param>
        /// <returns>The difference from the <see cref="Average"/> without sign.</returns>
        public double Sigma(double sample)
        {
            return Math.Abs(sample - average);
        }
        /// <summary>
        /// Clear all data and reset the instance.
        /// </summary>
        public void Clear()
        {
            n = 0;
            average = 0;
            M2 = 0;
        }
    }
}
