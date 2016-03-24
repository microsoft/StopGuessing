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
        private StreamWriter writer;

        public uint H;
        public uint threshold;
        public uint n;
        public double f_c;
        public double f_u;
        public double f_d;
        public uint[] heights;
        public double[] r,d;
        public double[] theta;

        public double[] p_poisson;

        public void Write(string label, string value)
        {
            writer.WriteLine("{0},{1}", label, value);
            writer.Flush();
        }

        public void Write(string label, double value)
        {
            Write(label, value.ToString("E5"));
        }

        public void Write(string label, double[] array)
        {
            Write(label, string.Join(",", array.Select(x => x.ToString("E5"))));
        }

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

        public BinomialLadderParameters(StreamWriter writer, uint H, uint threshold, double f_c, double f_u, double? f_d = null)
        {
            this.writer = writer;
            this.H = H;            
            Write("H", H);
            this.threshold = threshold;
            Write("threshold", threshold);
            this.f_c = f_c;
            Write("f_c", f_c);
            this.f_u = f_u;
            Write("f_u", f_u);
            this.f_d = f_d ?? Math.Exp( (Math.Log(f_c) + Math.Log(f_u)) / 2d);
            Write("f_d", this.f_d);

            heights = Enumerable.Range(0, (int) H + 1).Select(i => (uint) i).ToArray();
            Write("heights", string.Join(",",heights));
            writer.WriteLine();

            n = (uint) ( ((double)(2*(threshold-2))) / this.f_d );
            n = 1u << (int) Math.Round(Math.Log((double) n, 2d));
            Write("n", n.ToString());
            double half_n = ((double) n)/2d;

            // Calculate probabilities of rise/fall assuming step is taken for a different key 
            r = heights.Select(h => ((double) H - h)/half_n).ToArray();
            Write("r_h", r);
            d = heights.Select(h => ((double)h) / half_n).ToArray();
            Write("d_h", d);
            writer.WriteLine();

            // Calculate movement probabilities for key at undesirable frequency
            double f = f_u;
            double[] rise = heights.Select(h => f + (1 - f) * (r[h]) * (1 - d[h])).ToArray();
            double[] fall = heights.Select(h => (1 - f) * (1-r[h]) * d[h]).ToArray();
            double[] stay = heights.Select(h => (1 - f) * ( ((1-r[h]) * (1-d[h])) + (r[h] * d[h]) )).ToArray();
            Write("rise_h", rise);
            Write("fall_h", fall);
            Write("stay_h", stay);
            writer.WriteLine();


            // Calculate theta relationships between probabilties
            theta = new double[H];
            theta[H - 1] = (1d - stay[H])/rise[H - 1];
            for (int h = ((int) H) - 2; h > 0; h--)
                theta[h] = (1d - ((fall[h + 2]/theta[h + 1]) + stay[h + 1]))/rise[h];
            Write("theta_h", theta);

            // Calculate equilibrium probabilities in poisson model (random arrival with probability f)
            // First, ignore scale by calculating all probabilities as multiple of x_rel[H]
            double[] p_rel = new double[H+1];
            p_rel[H] = 1d;
            for (int h = ((int) H) - 1; h > 0; h--)
                p_rel[h] = theta[h]*p_rel[h+1];
            Write("p_poisson_rel_h", p_rel);
            // Scale down so that the collective probabilities add to 1
            double p_poisson_k = 1d/p_rel.Sum();
            p_poisson = p_rel.Select(x_rel_h => x_rel_h * p_poisson_k).ToArray();
            Write("p_poisson_h", p_poisson);
            writer.WriteLine();


        }


        public void WriteFixedFrequencyDetectionProbabilities(string path, double f, bool measureIfEverDetected)
        {
            using (StreamWriter detectDataWriter = new StreamWriter(path + "_" + H + "_" + threshold + ".csv"))
            using (StreamWriter detectLogWriter = new StreamWriter(path + "_" + H + "_" + threshold + "_log.csv"))
            {
                detectDataWriter.WriteLine("Observations,Total Steps,P(detection)");
                double[] fp_rise = heights.Select(h => (r[h])*(1 - d[h])).ToArray();
                double[] fp_fall = heights.Select(h => (1 - r[h])*d[h]).ToArray();
                double[] fp_stay = heights.Select(h => 1 - (fp_rise[h] + fp_fall[h])).ToArray();
                detectLogWriter.WriteLine("fp_rise_h, {0}", string.Join(",", fp_rise.Select( x => x.ToString("E5")) ) );
                detectLogWriter.WriteLine("fp_fall_h, {0}", string.Join(",", fp_fall.Select( x => x.ToString("E5"))));
                detectLogWriter.WriteLine("fp_stay_h, {0}", string.Join(",", fp_stay.Select( x => x.ToString("E5"))));

                // Initialize to the binomial distribution
                uint observations = 0;
                uint stuck_at_k = H + 1;
                double[] p = heights.Select(h => binomChoose(H,h)*Math.Pow(0.5d,H))
                    .Concat(new double[] { 0 }) // For the stuck at H (H+1) element
                    .ToArray();
                double[] pNext = new double[p.Length];
                ulong f_inverse = (ulong) (1/f);

                detectLogWriter.WriteLine("occur,all steps, {0},{1}", string.Join(",", heights), ", stuck");
                detectLogWriter.WriteLine("0,0,{0}", string.Join(",", p.Select(x => x.ToString("E5"))));

                for (ulong i = 0; i <= (100ul*1000ul*1000ul*1000ul); i++)
                {
                    pNext[stuck_at_k] = p[stuck_at_k];
                    if ((i%f_inverse) != 0)
                    {
                        for (int h = 0; h <= H; h++)
                            pNext[h] = ((h > 0) ? fp_rise[h - 1]*p[h - 1] : 0) +
                                       ((h < H) ? fp_fall[h + 1]*p[h + 1] : 0) +
                                       (fp_stay[h]*p[h]);
                    }
                    else
                    {
                        observations++;
                        
                        double detected = 0;
                        for (uint h = threshold; h <= H; h++)
                            detected += p[h];
                        pNext[0] = 0;
                        if (measureIfEverDetected)
                        {
                            for (int h = 1; h <= threshold; h++)
                                pNext[h] = p[h - 1];
                            for (uint h = threshold + 1; h <= H; h++)
                            {
                                pNext[h] = 0;
                            }
                            pNext[stuck_at_k] += detected;
                        }
                        else
                        {
                            for (int h = 1; h <= H; h++)
                                pNext[h] = p[h - 1];
                            pNext[H] += p[H];
                            pNext[stuck_at_k] = detected;
                        }
                        detectLogWriter.WriteLine("{0},{1},{2}",observations, i, string.Join(",", pNext.Select(x => x.ToString("E5"))));
                        detectDataWriter.WriteLine("{0},{1},{2}", observations, i, pNext[stuck_at_k].ToString("E5"));
                        detectLogWriter.Flush();
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
            uint k = 48;
            double requestFraction = 1024d;
            double f_c = requestFraction / MillionD; // one in a million
            double f_u = requestFraction / (50d*MillionD);
            string basePath = @"..\..\..\";
            StreamWriter writer = new StreamWriter(basePath + "params_" + k + "_" + Math.Round(1/f_c) + "=" + Math.Round(1/f_u) + ".csv");
            BinomialLadderParameters blp =
                new BinomialLadderParameters(writer, k, k-4, f_c, f_u);
            
            Tuple<string, double, bool>[] Probabilities = new Tuple<string, double, bool>[]
            {
                //new Tuple<string, double, bool>("DOneInFiftyMillionAny", f_u, true),
                //new Tuple<string, double, bool>("DOneInTwentyMillionAny",requestFraction / (20d * MillionD), true),
                //new Tuple<string, double, bool>("DOneInOneMillionAny",f_c, true),
                new Tuple<string, double, bool>("DOneInFiftyMillionCurrent", f_u, false),
                new Tuple<string, double, bool>("DOneInTwentyMillionCurrent",requestFraction / (20d * MillionD), false),
                new Tuple<string, double, bool>("DOneInOneMillionCurrent",f_c, false),
            };
            Parallel.ForEach(Probabilities, p =>
                blp.WriteFixedFrequencyDetectionProbabilities(basePath + p.Item1, p.Item2, p.Item3)
            );
        }
    }
}
