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

        public uint k;
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

        public BinomialLadderParameters(StreamWriter writer, uint k, double f_c, double f_u, double? f_d = null)
        {
            this.writer = writer;
            this.k = k;
            Write("k", k);
            this.f_c = f_c;
            Write("f_c", f_c);
            this.f_u = f_u;
            Write("f_u", f_u);
            this.f_d = f_d ?? Math.Exp( (Math.Log(f_c) + Math.Log(f_u)) / 2d);
            Write("f_d", this.f_d);

            heights = Enumerable.Range(0, (int) k + 1).Select(i => (uint) i).ToArray();
            Write("heights", string.Join(",",heights));
            writer.WriteLine();

            n = (uint) ( ((double)(2*(k-2))) / this.f_d );
            n = 1u << (int) Math.Round(Math.Log((double) n, 2d));
            Write("n", n.ToString());
            double half_n = ((double) n)/2d;

            // Calculate probabilities of rise/fall assuming step is taken for a different key 
            r = heights.Select(h => ((double) k - h)/half_n).ToArray();
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
            theta = new double[k];
            theta[k - 1] = (1d - stay[k])/rise[k - 1];
            for (int h = ((int) k) - 2; h > 0; h--)
                theta[h] = (1d - ((fall[h + 2]/theta[h + 1]) + stay[h + 1]))/rise[h];
            Write("theta_h", theta);

            // Calculate equilibrium probabilities in poisson model (random arrival with probability f)
            // First, ignore scale by calculating all probabilities as multiple of x_rel[k]
            double[] p_rel = new double[k+1];
            p_rel[k] = 1d;
            for (int h = ((int) k) - 1; h > 0; h--)
                p_rel[h] = theta[h]*p_rel[h+1];
            Write("p_poisson_rel_h", p_rel);
            // Scale down so that the collective probabilities add to 1
            double p_poisson_k = 1d/p_rel.Sum();
            p_poisson = p_rel.Select(x_rel_h => x_rel_h * p_poisson_k).ToArray();
            Write("p_poisson_h", p_poisson);
            writer.WriteLine();


        }


        public void WriteFixedFrequencyDetectionProbabilities(string path, double f)
        {
            using (StreamWriter detectDataWriter = new StreamWriter(path + ".csv"))
            using (StreamWriter detectLogWriter = new StreamWriter(path + "_log.csv"))
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
                uint stuck_at_k = k + 1;
                double[] p = heights.Select(h => binomChoose(k,h)*Math.Pow(0.5d,k))
                    .Concat(new double[] { 0 }) // For the stuck at k (k+1) element
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
                        for (int h = 0; h <= k; h++)
                            pNext[h] = ((h > 0) ? fp_rise[h - 1]*p[h - 1] : 0) +
                                       ((h < k) ? fp_fall[h + 1]*p[h + 1] : 0) +
                                       (fp_stay[h]*p[h]);
                    }
                    else
                    {
                        observations++;
                        pNext[0] = 0;
                        for (int h = 1; h <= k; h++)
                            pNext[h] = p[h - 1];
                        pNext[stuck_at_k] += p[k];
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
            double f_c = 1d / MillionD; // one in a million
            double f_u = 1d/(50d*MillionD);
            string basePath = @"..\..\..\";
            StreamWriter writer = new StreamWriter(basePath + "params_" + k + "_" + Math.Round(1/f_c) + "=" + Math.Round(1/f_u) + ".csv");
            BinomialLadderParameters blp =
                new BinomialLadderParameters(writer, k, f_c, f_u);
            Tuple<string, double>[] Probabilities = new Tuple<string, double>[]
            {
                new Tuple<string, double>("OneInFiftyMillion", f_u),
                new Tuple<string, double>("OneInTwentyMillion", 1d / (20d * MillionD)),
                new Tuple<string, double>("OneInOneMillion",  1d / MillionD),
            };
            Parallel.ForEach(Probabilities, p =>
                blp.WriteFixedFrequencyDetectionProbabilities(basePath + p.Item1, p.Item2)
            );
        }
    }
}
