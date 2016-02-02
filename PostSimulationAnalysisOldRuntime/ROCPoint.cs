using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PostSimulationAnalysisOldRuntime
{
    public class ROCPoint
    {
        public int FalsePositives;
        public int FalsePositiveUniqueUsers;
        public int TruePositives;
        public int FalseNegatives;
        public int TrueNegatives;
        public double BlockingThreshold;
        public int FalsePositivesWithProxy;
        public int FalsePositivesWithAttackerIp;
        public int FalsePositivesWithProxyAndAttackerIp;
        public long FalseNegativesWithProxy;
        public long FalseNegativesWithBenignIp;
        public long FalseNegativesWithBenignProxyIp;

        public ROCPoint(int falsePositives, int falsePositiveUniqueUsers, int truePositives, int falseNegatives, int trueNegatives,
            int falsePositivesWithAttackerIp,
            int falsePositivesWithProxy,
            int falsePositivesWithProxyAndAttackerIp,
            long falseNegativesWithBenignIp,
            long falseNegativesWithProxy,
            long falseNegativesWithBenignProxyIp,
            double blockingThreshold)
        {
            FalsePositives = falsePositives;
            FalsePositiveUniqueUsers = falsePositiveUniqueUsers;
            TruePositives = truePositives;
            FalseNegatives = falseNegatives;
            TrueNegatives = trueNegatives;
            FalsePositivesWithProxy = falsePositivesWithProxy;
            FalsePositivesWithAttackerIp = falsePositivesWithAttackerIp;
            FalsePositivesWithProxyAndAttackerIp = falsePositivesWithProxyAndAttackerIp;
            FalseNegativesWithProxy = falseNegativesWithProxy;
            FalseNegativesWithBenignIp = falseNegativesWithBenignIp;
            FalseNegativesWithBenignProxyIp = falseNegativesWithBenignProxyIp;
            BlockingThreshold = blockingThreshold;
        }

        public static double fraction(int numerator, int denominator)
        {
            return ((double)numerator / (double)denominator);
        }


        public double FalsePositiveRate => fraction(FalsePositives, FalsePositives + TrueNegatives);
        public double TruePositiveRate => fraction(TruePositives, TruePositives + FalseNegatives);
        public double Precision => fraction(TruePositives, TruePositives + FalsePositives);
        public double Recall => fraction(TruePositives, TruePositives + FalseNegatives);

    }
}
