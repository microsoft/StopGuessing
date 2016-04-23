using System;
using System.Collections.Generic;
using System.Deployment.Internal;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// ReSharper disable InconsistentNaming

namespace Binomial_Ladder_Sketch_Parameter_Calculator
{
    class BinomialLadderParameters
    {
        //private StreamWriter writer;

        public uint H;
        public uint threshold;
        public uint n;
        public uint[] heights;
        public double[] r,d;

        public static double binomChoose(uint n, uint k)
        {
            double result = 1;
            for (uint i = 1; i <= k; i++)
            {
                result *= (double) (n - k + i);
                result /= (double) i;
            }
            return result;
        }

        public BinomialLadderParameters(//StreamWriter writer, 
            uint H, uint threshold, double? f_c = null, double? f_u = null, double? f_d = null, uint? n = null)
        {
            this.H = H;            
            this.threshold = threshold;

            heights = Enumerable.Range(0, (int) H + 1).Select(i => (uint) i).ToArray();

            if (n.HasValue)
            {
                this.n = n.Value;
            }
            else if (f_d.HasValue || (f_c.HasValue && f_u.HasValue))
            {
                if (!f_d.HasValue)
                {
                    f_d = Math.Exp((Math.Log(f_c.Value) + Math.Log(f_u.Value))/2d);
                }
                this.n = (uint) (((double) (2*(threshold - 2)))/f_d.Value);
                this.n = 1u << (int) Math.Round(Math.Log((double) this.n, 2d));
            }
            double half_n = ((double) this.n)/2d;

            // Calculate probabilities of rise/fall assuming step is taken for a different key 
            r = heights.Select(h => ((double) H - h)/half_n).ToArray();
            d = heights.Select(h => ((double)h) / half_n).ToArray();

            // Calculate movement probabilities for key at undesirable frequency
            //double f = f_u;
            //double[] rise = heights.Select(h => f + (1 - f) * (r[h]) * (1 - d[h])).ToArray();
            //double[] fall = heights.Select(h => (1 - f) * (1-r[h]) * d[h]).ToArray();
            //double[] stay = heights.Select(h => (1 - f) * ( ((1-r[h]) * (1-d[h])) + (r[h] * d[h]) )).ToArray();
            //Write("rise_h", rise);
            //Write("fall_h", fall);
            //Write("stay_h", stay);
            //writer.WriteLine();


            // Calculate theta relationships between probabilties
            //theta = new double[H];
            //theta[H - 1] = (1d - stay[H])/rise[H - 1];
            //for (int h = ((int) H) - 2; h > 0; h--)
            //    theta[h] = (1d - ((fall[h + 2]/theta[h + 1]) + stay[h + 1]))/rise[h];
            //Write("theta_h", theta);

            //// Calculate equilibrium probabilities in poisson model (random arrival with probability f)
            //// First, ignore scale by calculating all probabilities as multiple of x_rel[H]
            //double[] p_rel = new double[H+1];
            //p_rel[H] = 1d;
            //for (int h = ((int) H) - 1; h > 0; h--)
            //    p_rel[h] = theta[h]*p_rel[h+1];
            //Write("p_poisson_rel_h", p_rel);
            //// Scale down so that the collective probabilities add to 1
            //double p_poisson_k = 1d/p_rel.Sum();
            //p_poisson = p_rel.Select(x_rel_h => x_rel_h * p_poisson_k).ToArray();
            //Write("p_poisson_h", p_poisson);
            //writer.WriteLine();


        }
        public void WriteDetectionProbabilitiesForStochasticArrivals(string path, double f, bool stickyMode)
        {
            using (StreamWriter detectDataWriter = new StreamWriter(path + "_" + H + "_" + threshold + ".csv"))
            {
                detectDataWriter.WriteLine("Total Steps,P(detection)");
                double[] fp_rise = heights.Select(h => f + ( (1-f) * (r[h]) * (1 - d[h]) ) ).ToArray();
                double[] fp_fall = heights.Select(h => (1-f) * (1 - r[h]) * d[h] ).ToArray();
                double[] fp_stay = heights.Select(h => 1 - (fp_rise[h] + fp_fall[h])).ToArray();

                // Initialize to the binomial distribution
                double[] p = heights.Select(h => binomChoose(H, h) * Math.Pow(0.5d, H)).ToArray();
                double[] pNext = new double[p.Length];
                double detected = 0;

                for (ulong i = 0; i <= (100ul * 1000ul * 1000ul * 1000ul); i++)
                {
                    for (int h = 0; h <= H; h++)
                    {
                        pNext[h] = ((h > 0) ? (fp_rise[h - 1] * p[h - 1]) : 0) +
                                    ((h < H) ? (fp_fall[h + 1] * p[h + 1]) : 0) +
                                    (fp_stay[h] * p[h]);
                    }

                    if (!stickyMode)
                    {
                        detected = 0;
                    }
                    for (uint h = threshold; h <= H; h++)
                    {
                        detected += pNext[h];
                        if (stickyMode)
                        {
                            pNext[h] = 0;
                        }
                    }

                    if (i%1000000 == 0)
                    {
                        detectDataWriter.WriteLine("{0},{1}", i, detected.ToString("E12"));
                        detectDataWriter.Flush();
                    }

                    // Swap p and pNext
                    double[] temp = pNext;
                    pNext = p;
                    p = temp;
                }
            }
        }

        public void WriteDetectionProbabilitiesForEquidistantArrivals(string path, double f, bool stickyMode)
        {
            using (StreamWriter detectDataWriter = new StreamWriter(path + "_" + H + "_" + threshold + ".csv"))
            {
                detectDataWriter.WriteLine("Observations,Total Steps,P(detection)");
                double[] fp_rise = heights.Select(h => (r[h])*(1 - d[h])).ToArray();
                double[] fp_fall = heights.Select(h => (1 - r[h])*d[h]).ToArray();
                double[] fp_stay = heights.Select(h => 1 - (fp_rise[h] + fp_fall[h])).ToArray();

                // Initialize to the binomial distribution
                uint observations = 0;
                double[] p = heights.Select(h => binomChoose(H,h)*Math.Pow(0.5d,H))
                    .Concat(new double[] { 0 }) // For the stuck at H (H+1) element
                    .ToArray();
                double[] pNext = new double[p.Length];
                ulong f_inverse = (ulong) (1/f);
                double detected = 0;


                for (ulong i = 0; i <= (100ul*1000ul*1000ul*1000ul); i++)
                {
                    if ((i%f_inverse) != 0)
                    {
                        for (int h = 0; h <= H; h++)
                        {
                            pNext[h] = ((h > 0) ? (fp_rise[h - 1]*p[h - 1]) : 0) +
                                       ((h < H) ? (fp_fall[h + 1]*p[h + 1]) : 0) +
                                       (fp_stay[h]*p[h]);
                        }
                    }
                    else
                    {
                        observations++;

                        if (!stickyMode)
                        {
                            detected = 0;
                        }
                        for (uint h = threshold; h <= H; h++)
                        {
                            detected += p[h];
                            if (stickyMode)
                            {
                                p[h] = 0;
                            }
                        }
                        pNext[0] = 0;
                        for (int h = 1; h <= H; h++)
                            pNext[h] = p[h - 1];
                        if (!stickyMode)
                        {
                            pNext[H] += p[H];
                        }
                        detectDataWriter.WriteLine("{0},{1},{2}", observations, i, detected.ToString("E12"));
                        detectDataWriter.Flush();
                    }

                    // Swap p and pNext
                    double[] temp = pNext;
                    pNext = p;
                    p = temp;
                }
            }
        }

    }
    class Program
    {


        static void Main(string[] args)
        {
            double MillionD = 1000d*1000d;
            //uint H = 48;
            //uint threshold = H - 4;
            uint n = 1 << 29;
            double requestFraction = 1d;
            double f_c = requestFraction / MillionD; // one in a million
            double f_u = requestFraction / (50d*MillionD);
            string basePath = @"..\..\..\";
           // StreamWriter writer = new StreamWriter(basePath + "params_" + H + "_" + Math.Round(1/f_c) + "=" + Math.Round(1/f_u) + ".csv");
            BinomialLadderParameters blp4844 =
                new BinomialLadderParameters(48, 44, n: n);
            BinomialLadderParameters blp4848 =
                new BinomialLadderParameters(48, 48, n: n);

            Task[] tasks = new Task[]
            {
                Task.Run(() => blp4848.WriteDetectionProbabilitiesForEquidistantArrivals(
                    basePath + "SingleTriggerOneInOneMillionEq", 1d/MillionD, true)),
                Task.Run(() => blp4844.WriteDetectionProbabilitiesForEquidistantArrivals(
                    basePath + "PerpetualOneInOneMillionEq", 1d/MillionD, false)),
                Task.Run(() => blp4848.WriteDetectionProbabilitiesForEquidistantArrivals(
                    basePath + "SingleTriggerOneInFiftyMillionEq", 1d/(50d*MillionD), true)),
                Task.Run(() => blp4844.WriteDetectionProbabilitiesForEquidistantArrivals(
                    basePath + "PerpetualOneInFiftyMillionEq", 1d/(50d*MillionD), false)),
                Task.Run(() => blp4848.WriteDetectionProbabilitiesForStochasticArrivals(
                    basePath + "SingleTriggerOneInOneMillionStoch", 1d/MillionD, true)),
                Task.Run(() => blp4844.WriteDetectionProbabilitiesForStochasticArrivals(
                    basePath + "PerpetualOneInOneMillionStoch", 1d/MillionD, false)),
                Task.Run(() => blp4848.WriteDetectionProbabilitiesForStochasticArrivals(
                    basePath + "SingleTriggerOneInFiftyMillionStoch", 1d/(50d*MillionD), true)),
                Task.Run(() => blp4844.WriteDetectionProbabilitiesForStochasticArrivals(
                    basePath + "PerpetualOneInFiftyMillionStoch", 1d/(50d*MillionD), false))
            };

            Task.WaitAll(tasks);
        }
    }
}
