using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
// ReSharper disable InconsistentNaming

namespace Binomial_Ladder_Sketch_Parameter_Calculator
{
    public class BinomialLadderParameters
    {
        public uint H;
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


        //public static double[] BLFDistribution(uint H, double N)
        //{
        //    double[] dist = new double[H + 1];
        //    double n = N/2d;
        //    double[] preproducts = new double[(H / 2) + 1];
        //    double[] preproducts2 = new double[(H / 2) + 1];
        //    double[] preproducts3 = new double[H+1];
        //    {
        //        double preprod = 1; double preprod2 = 1; double preprod3 = 1;
        //        for (uint h = 0; h <= preproducts.Length; h++)
        //        {
        //            preproducts[h] = preprod *= (n - (h - 1)) * (n - (h - 1)) * h / (N - (h - 1));
        //            preproducts2[h] = preprod2 *= (n - (h - 1)) / (N - (h - 1));
        //            preproducts3[h] = preprod3 *= h / (N - (h - 1));
        //        }
        //        for (int i = preproducts3.Length; i >= 0; i--)
        //            preproducts3[i] = preprod3 *= i / (N - (i - 1));

        //        for (uint h = 0; h <= H; h++)
        //    {
        //        double product = 1;
        //        uint first_stop = Math.Min(h, H - h);
        //        uint second_stop = Math.Max(h, H - h);
        //        if (first_stop > 0)
        //        {
        //            product = preproducts2[first_stop - 1]*
        //                      preproducts[second_stop - 1]/preproducts[first_stop - 1];
        //        }
        //        else
        //            product = preproducts[second_stop];

        //        for (uint i=1; i <= H; i++)
        //        if (i <= first_stop)
        //                product *= (n - (i - 1)) / (N - (i - 1));
        //        else if (i <= second_stop)
        //                product *= (n - (i - 1))/ (N - (i - 1));
        //        else
        //                product *= i/(N - (i - 1));
        //        dist[h] = dist[H - h] = product;
        //    }
        //    return dist;
        //}

        public BinomialLadderParameters(//StreamWriter writer, 
            uint H, double? f_c = null, double? f_u = null, double? f_d = null, uint? n = null)
        {
            this.H = H;            

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
                this.n = (uint) (((double) (2*(H - 2)))/f_d.Value);
                this.n = 1u << (int) Math.Round(Math.Log((double) this.n, 2d));
            }
            double half_n = ((double) this.n)/2d;

            // Calculate probabilities of rise/fall assuming step is taken for a different key 
            r = heights.Select(h => ((double) H - h)/half_n).ToArray();
            d = heights.Select(h => ((double)h) / half_n).ToArray();
        }


        public void SimulateStochastic(string path, double f, uint steps_to_track, bool stickyMode)
        {
            using (StreamWriter detectDataWriter = new StreamWriter(path + ".csv"))
            {
                double p_stepup = f;
                int num_heights = stickyMode ? heights.Length + 1 : heights.Length;
                int max_height_via_collision = heights.Length - 1;
                int max_height_via_step = stickyMode ? num_heights - 1 : max_height_via_collision;
                double[] p_collisionup = heights.Select(h => ((1 - f)*(r[h])*(1 - d[h]))).ToArray();
                double[] p_collisiondown = heights.Select(h => (1 - f)*(1 - r[h])*d[h]).ToArray();
                
                // Initialize to the binomial distribution
                double[,] p = new double[steps_to_track + 2, num_heights];
                double[,] pNext = new double[steps_to_track + 2, num_heights];
                //double[] dist = BLFDistribution(H, n);
                for (uint i = 0; i < heights.Length; i++)
                    pNext[0, i] = p[0, i] = binomChoose(H, i)*Math.Pow(0.5d, H);
                //pNext[0, i] = p[0, i] = dist[i]; // binomChoose(H, i)*Math.Pow(0.5d, H);

                uint iterations_per_expected_step = (uint) Math.Ceiling(1/f);

                if (stickyMode)
                {
                    detectDataWriter.WriteLine("{0},{1}", "Step", "p(detected)");
                }
                else
                {
                    detectDataWriter.WriteLine("{0},{1},{2}", "Step",
                        string.Join(",", heights.Select(height => "p(h=" + height + ")")),
                        string.Join(",", heights.Select(height => "p(h>=" + height + ")")));
                }

                for (uint expected_steps_completed = 0;
                    expected_steps_completed < steps_to_track;
                    expected_steps_completed++)
                {
                    for (uint iteration = 0; iteration < iterations_per_expected_step; iteration++)
                    {
                        // Copy pNext to P
                        for (int step = 0; step <= steps_to_track + 1; step++)
                        {
                            for (int height = 0; height < num_heights; height++)
                            {
                                p[step, height] = pNext[step, height];
                            }
                        }

                        for (int height = 0; height <= max_height_via_step; height++)
                        {
                            int new_height_if_taking_step = Math.Min(height+1, max_height_via_step);
                            double p_collision_up = height < p_collisionup.Length ? p_collisionup[height] : 0;
                            double p_collision_down = height < p_collisionup.Length ? p_collisiondown[height] : 0;
                            for (int step = 0; step <= steps_to_track; step++)
                            {
                                double startingAmount = p[step, height];
                                double totalAmountMoved = 0;

                                if (step <= steps_to_track)
                                {
                                    // Account for steps for the element of interest causing a move up
                                    double amountMovedForStep = startingAmount*p_stepup;
                                    pNext[step + 1, new_height_if_taking_step] += amountMovedForStep;
                                    totalAmountMoved += amountMovedForStep;
                                }

                                if (height < max_height_via_collision)
                                {
                                    // Account for collisions up
                                    double amountMovedForCollisionUp = startingAmount*p_collision_up;
                                    pNext[step, height + 1] += amountMovedForCollisionUp;
                                    totalAmountMoved += amountMovedForCollisionUp;
                                }

                                if (height > 0)
                                {
                                    // Account for collisions down
                                    double amountMovedForCollisionDown = startingAmount*p_collision_down;
                                    pNext[step, height - 1] += amountMovedForCollisionDown;
                                    totalAmountMoved += amountMovedForCollisionDown;
                                }

                                pNext[step, height] -= totalAmountMoved;
                            }
                        }
                    }

                    double[] at_height = new double[num_heights];
                    for (int height = 0; height < num_heights; height++)
                    {
                        at_height[height] = p[expected_steps_completed, height];
                    }
                    double adjustment = 1d / at_height.Sum();
                    double[] at_height_adjusted = at_height.Select(x => x * adjustment).ToArray();
                    if (stickyMode)
                    {
                        // Those detected are the ones already detected (height one above the ladder to indicate stuck-at position)
                        // and the one at the top of the ladder when the sep occurred (height at top of the ladder)
                        double amount_detected = at_height_adjusted.Reverse().Take(2).Sum();
                        detectDataWriter.WriteLine("{0},{1}", expected_steps_completed+1, amount_detected);
                    }
                    else
                    {
                        double sum = 0;
                        double[] at_or_above = new double[at_height_adjusted.Length];
                        for (int height = heights.Length - 1; height >= 0; height--)
                        {
                            sum += at_height_adjusted[height];
                            at_or_above[height] = sum;
                        }
                        detectDataWriter.WriteLine("{0},{1},{2}", expected_steps_completed+1,
                            string.Join(",", at_height_adjusted.Select( x=> x.ToString("E12"))),
                            string.Join(",", at_or_above.Select(x => x.ToString("E12")))
                            );
                    }
                    detectDataWriter.Flush();
                }
            }
        }


        //public void WriteDetectionProbabilitiesForEquidistantArrivals(string path, double f, bool stickyMode)
        //{
        //    using (StreamWriter detectDataWriter = new StreamWriter(path + ".csv"))
        //    {
        //        detectDataWriter.WriteLine("Observations,Total Steps,P(detection)");
        //        double[] fp_rise = heights.Select(h => (r[h])*(1 - d[h])).ToArray();
        //        double[] fp_fall = heights.Select(h => (1 - r[h])*d[h]).ToArray();
        //        double[] fp_stay = heights.Select(h => 1 - (fp_rise[h] + fp_fall[h])).ToArray();

        //        // Initialize to the binomial distribution
        //        uint observations = 0;
        //        double[] p = heights.Select(h => binomChoose(H,h)*Math.Pow(0.5d,H))
        //            .Concat(new double[] { 0 }) // For the stuck at H (H+1) element
        //            .ToArray();
        //        double[] pNext = new double[p.Length];
        //        ulong f_inverse = (ulong) (1/f);
        //        double detected = 0;


        //        for (ulong i = 0; i <= (100ul*1000ul*1000ul*1000ul); i++)
        //        {
        //            if ((i%f_inverse) != 0)
        //            {
        //                for (int h = 0; h <= H; h++)
        //                {
        //                    pNext[h] = ((h > 0) ? (fp_rise[h - 1]*p[h - 1]) : 0) +
        //                               ((h < H) ? (fp_fall[h + 1]*p[h + 1]) : 0) +
        //                               (fp_stay[h]*p[h]);
        //                }
        //            }
        //            else
        //            {
        //                observations++;

        //                if (!stickyMode)
        //                {
        //                    detected = 0;
        //                }
        //                for (uint h = threshold; h <= H; h++)
        //                {
        //                    detected += p[h];
        //                    if (stickyMode)
        //                    {
        //                        p[h] = 0;
        //                    }
        //                }
        //                pNext[0] = 0;
        //                for (int h = 1; h <= H; h++)
        //                    pNext[h] = p[h - 1];
        //                if (!stickyMode)
        //                {
        //                    pNext[H] += p[H];
        //                }
        //                detectDataWriter.WriteLine("{0},{1},{2}", observations, i, detected.ToString("E12"));
        //                detectDataWriter.Flush();
        //            }

        //            // Swap p and pNext
        //            double[] temp = pNext;
        //            pNext = p;
        //            p = temp;
        //        }
        //    }
        //}

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
            BinomialLadderParameters blf64 =
                new BinomialLadderParameters(64, n: n);
            BinomialLadderParameters blf48 =
                new BinomialLadderParameters(48, n: n);
            //            BinomialLadderParameters blfPIN = 
            //                new BinomialLadderParameters(1 << 19, 1 << 20);

            //double[] dist = BinomialLadderParameters.BLFDistribution(1 << 19, 1 << 20);
            //double[] cum = new double[dist.Length];
            //double total = 0;
            //for (int h = dist.Length - 1; h >= 0; h--)
            //    cum[h] = total += dist[h];
            //double middle = dist[(1 << 19)];
            //double plus100 = dist[(1 << 19) + 100];
            //double plus1000 = dist[(1 << 19) + 1000];

            //BinomialLadderParameters blf20 =
            //    new BinomialLadderParameters(48, n: n);
            //blf20.SimulateStochastic(basePath + "BLF_20_Detect_Twenty_Sticky", .05, 100, true);
            //blf20.SimulateStochastic(basePath + "BLF_20_Detect_Twenty_Perpetual", .05, 100, false);

            //blf64.SimulateStochastic(basePath + "BLF_48_DetectStochastic_OneMillion", (1d / MillionD), 60, false);

            //blfPIN.SimulateStochastic(basePath + "BLF_PIN20_Perpetual_Hundred", (1d / 5000), );

            Task[] tasks = new Task[]
            {
                Task.Run(() => blf64.SimulateStochastic(basePath + "BLF_64_Perpetual_Million", (1d / MillionD), 120, false)),
                Task.Run(() => blf64.SimulateStochastic(basePath + "BLF_64_Sticky_Million", (1d / MillionD), 120, true)),
                Task.Run(() => blf64.SimulateStochastic(basePath + "BLF_64_Perpetual_FiftyMillion", (1d / (50d*MillionD)), 65, false)),
                Task.Run(() => blf64.SimulateStochastic(basePath + "BLF_64_Sticky_FiftyMillion", (1d / (50d*MillionD)), 65, true)),
                Task.Run(() => blf48.SimulateStochastic(basePath + "BLF_48_Perpetual_Million", (1d / MillionD), 100, false)),
                Task.Run(() => blf48.SimulateStochastic(basePath + "BLF_48_Sticky_Million", (1d / MillionD), 100, true)),
                Task.Run(() => blf48.SimulateStochastic(basePath + "BLF_48_Perpetual_FiftyMillion", (1d / (50d*MillionD)), 65, false)),
                Task.Run(() => blf48.SimulateStochastic(basePath + "BLF_48_Sticky_FiftyMillion", (1d / (50d*MillionD)), 65, true)),
            };

            Task.WaitAll(tasks);
        }
    }
}
