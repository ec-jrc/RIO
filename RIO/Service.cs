//using Microsoft.Azure.Devices.Client;
using RIO;

namespace RIO
{
    internal class Service : IMeasurable
    {
        public IMetrics Metrics { get; set; }
    }
}