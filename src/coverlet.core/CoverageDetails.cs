using System;

namespace Coverlet.Core
{
    public class CoverageDetails
    {
        private double _averageModulePercent;
        public double Covered { get; set; }
        public int Total { get; set; }
        public double AverageModulePercent
        {
            get { return Math.Floor(_averageModulePercent * 100) / 100; }
            set { _averageModulePercent = value; }
        }

        public double Percent => Total == 0 ? 100D : Math.Floor((Covered / Total) * 10000) / 100;
    }
}