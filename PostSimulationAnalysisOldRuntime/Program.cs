using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PostSimulationAnalysisOldRuntime
{


    public static class Program
    {

        private static Trial[] LoadDataParallel(string path)
        {
            BlockingCollection<string> inputLines = new BlockingCollection<string>();
            ConcurrentBag<Trial> trials = new ConcurrentBag<Trial>();

            Parallel.Invoke(
                () =>
                {
                    int counter = 0;
                    using (StreamReader file = new StreamReader(path))
                    {
                        // Skip header
                        file.ReadLine();
                        string line;
                        while ((line = file.ReadLine()) != null)
                        {
                            inputLines.Add(line);
                            if (++counter >= 100000)
                            {
                                counter = 0;
                                Console.Out.WriteLine("Trial lines Read: {0}", inputLines.Count);
                            }
                        }
                        inputLines.CompleteAdding();
                    }

                }, () => Parallel.ForEach(inputLines.GetConsumingPartitioner(), (line) =>
                {
                    string[] fields = line.Trim().Split(new char[] {','});
                    if (fields.Length >= 11)
                    {
                        Trial trial = new Trial(fields);
                        trials.Add(trial);
                    }
                    int trialsCount = trials.Count;
                    if (trialsCount % 100000 == 0)
                        Console.Out.WriteLine("Trial lines processed: {0}", trialsCount);
                })

                    )
                    ;
            return trials.ToArray();
        }

        private static List<Trial> LoadData(string path)
        {
            List<Trial> trials = new List<Trial>();
            int counter = 0;
            using (StreamReader file = new StreamReader(path))
            {
                // Skip header
                file.ReadLine();

                string line;
                while ((line = file.ReadLine()) != null)
                {
                    string[] fields = line.Trim().Split(new char[] { ',' });
                    if (fields.Length >= 11)
                        trials.Add(new Trial(fields));
                    if (++counter >= 10000)
                    {
                        counter = 0;
                        Console.Out.WriteLine("Loaded: {0}", trials.Count);
                    }
                }
            }
            return trials;
        }



        public static void Main()   // string[] args
        {
            string path = @"E:\Experiment_2_3_17_50_5m_avoid\";
            Trial[] trials = LoadDataParallel(path + "data.txt");

            Trial[] trialsWithCorrectPassword = trials.Where(t => t.IsPasswordCorrect).ToArray();
            Trial[] trialsUsersCorrectPassword = trialsWithCorrectPassword.Where(t => !t.IsFromAttacker || !t.IsAGuess).ToArray();
            Trial[] trialsGuessesCorrectPassword = trialsWithCorrectPassword.Where(t => t.IsFromAttacker && t.IsAGuess).ToArray();
            int numConditions = trialsWithCorrectPassword.First().scoreForEachCondition.Length;

            //long numCorrectBenignFromAttackersIPpool = trialsUsersCorrectPassword.LongCount(t => t.IsIpInAttackersPool);
            //long numCorrectBenignFromProxy = trialsUsersCorrectPassword.LongCount(t => t.IsClientAProxyIP);
            //long numCorrectBenignBoth = trialsUsersCorrectPassword.LongCount(t => t.IsIpInAttackersPool && t.IsClientAProxyIP);
            long numCorrectGuessesFromProxy = trialsGuessesCorrectPassword.LongCount(t => t.IsClientAProxyIP);
            long numCorrectGuessesFromBenignIpPool = trialsGuessesCorrectPassword.LongCount(t => t.IsIpInBenignPool);
            long numCorrectGuessesFromBoth = trialsGuessesCorrectPassword.LongCount(t => t.IsIpInBenignPool && t.IsClientAProxyIP);

            for (int conditionNumber = 0; conditionNumber < numConditions; conditionNumber++)
            {
                using (StreamWriter writer = new StreamWriter(path + "PointsFor_" + conditionNumber.ToString() + ".csv"))
                {
                    writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}",
                        "False +",
                        "False + users",
                        "False + IPs",
                        "False -",
                        "True +",
                        "True -",
                        "FP Rate",
                        "TP Rate",
                        "Precision",
                        "Recall",
                        "FPwAttackerIP",
                        "FPwProxy",
                        "FPwBoth",
                        "FNwBenignIP",
                        "FNwProxy",
                        "FNwBoth",
                        "Threshold");

                    //List<Trial> originalMalicious = new List<Trial>(trialsGuessesCorrectPassword);
                    //List<Trial> originalBenign = new List<Trial>(trialsUsersCorrectPassword);
                    //originalMalicious.Sort((a, b) => -a.CompareTo(b, conditionNumber));
                    //originalBenign.Sort((a, b) => -a.CompareTo(b, conditionNumber));
                    //Queue<Trial> malicious = new Queue<Trial>(originalMalicious);
                    //Queue<Trial> benign = new Queue<Trial>(originalBenign);


                    Console.Out.WriteLine("Writing Condition: {0}", conditionNumber);
                    int c = conditionNumber;
                    Queue<Trial> malicious = new Queue<Trial>(trialsGuessesCorrectPassword.AsParallel().OrderBy((x) => -x.scoreForEachCondition[c]));
                    Console.Out.WriteLine("Sort 1 completed");
                    Queue<Trial> benign = new Queue<Trial>(trialsUsersCorrectPassword.AsParallel().OrderBy((x) => -x.scoreForEachCondition[c]));
                    Console.Out.WriteLine("Sort 2 completed");
                    int originalMaliciousCount = malicious.Count;
                    int originalBenignCount = benign.Count;

                    int falseNegatives = malicious.Count;
                    int falsePositives = 0;
                    int falsePositivesWithProxy = 0;
                    int falsePositivesWithAttackerIp = 0;
                    int falsePositivesWithProxyAndAttackerIp = 0;
                    long falseNegativeWithProxy = numCorrectGuessesFromProxy;
                    long falseNegativeBenignIp = numCorrectGuessesFromBenignIpPool;
                    long falseNegativeBenignProxyIp = numCorrectGuessesFromBoth;
                    HashSet<string> falsePositiveUsers = new HashSet<string>();
                    HashSet<string> falsePositiveIPs = new HashSet<string>();
                    //int falseNegativeFromDefendersIpPool;


                    double blockThreshold = malicious.Peek().GetScoreForCondition(conditionNumber);
                    List<ROCPoint> rocPoints = new List<ROCPoint>
                    {
                        new ROCPoint(0, 0, 0, 0, originalMaliciousCount, originalBenignCount,
                        falsePositivesWithAttackerIp, falsePositivesWithProxy, falsePositivesWithProxyAndAttackerIp,
                        falseNegativeBenignIp, falseNegativeWithProxy, falseNegativeBenignProxyIp,
                        blockThreshold)
                    };
                    while (malicious.Count > 0)
                    {
                        // Remove all malicious requests above this new threshold
                        while (malicious.Count > 0 && malicious.Peek().GetScoreForCondition(conditionNumber) >= blockThreshold)
                        {
                            Trial t = malicious.Dequeue();
                            falseNegatives--;
                            if (t.IsClientAProxyIP)
                                falseNegativeWithProxy--;
                            if (t.IsIpInBenignPool)
                                falseNegativeBenignIp--;
                            if (t.IsIpInBenignPool && t.IsClientAProxyIP)
                                falseNegativeBenignProxyIp--;
                        }

                        // Remove all benign requests above this new threshold
                        while (benign.Count > 0 && benign.Peek().GetScoreForCondition(conditionNumber) >= blockThreshold)
                        {
                            Trial t = benign.Dequeue();
                            falsePositives++;
                            falsePositiveUsers.Add(t.UserID);
                            falsePositiveIPs.Add(t.ClientIP);
                            if (t.IsIpInAttackersPool)
                                falsePositivesWithAttackerIp++;
                            if (t.IsClientAProxyIP)
                                falsePositivesWithProxy++;
                            if (t.IsIpInAttackersPool && t.IsClientAProxyIP)
                                falsePositivesWithProxyAndAttackerIp++;
                        }

                        rocPoints.Add(new ROCPoint(
                            falsePositives,  //originalBenign.Count - benign.Count,
                            falsePositiveUsers.Count(),
                            falsePositiveIPs.Count(),
                            //falseNegatives, //
                            originalMaliciousCount - malicious.Count,
                            malicious.Count,
                            benign.Count,
                            falsePositivesWithAttackerIp, falsePositivesWithProxy, falsePositivesWithProxyAndAttackerIp,
                            falseNegativeBenignIp, falseNegativeWithProxy, falseNegativeBenignProxyIp,
                            blockThreshold));

                        // Identify next threshold
                        if (malicious.Count > 0)
                            blockThreshold = malicious.Peek().GetScoreForCondition(conditionNumber);
                    }

                    List<ROCPoint> finalROCPoints = new List<ROCPoint>();
                    Queue<ROCPoint> rocPointQueue = new Queue<ROCPoint>(rocPoints);
                    while (rocPointQueue.Count > 0)
                    {
                        ROCPoint point = rocPointQueue.Dequeue();
                        if (rocPointQueue.Count == 0 || rocPointQueue.Peek().FalsePositives > point.FalsePositives)
                            finalROCPoints.Add(point);
                    }

                    foreach (ROCPoint point in finalROCPoints)
                    {
                        writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}",
                            point.FalsePositives,
                            point.FalsePositiveUniqueUsers,
                            point.FalsePositiveUniqueIPs,
                            point.FalseNegatives, point.TruePositives, point.TrueNegatives,
                            point.FalsePositiveRate, point.TruePositiveRate, point.Precision, point.Recall,
                            point.FalsePositivesWithAttackerIp, point.FalsePositivesWithProxy, point.FalsePositivesWithProxyAndAttackerIp,
                            point.FalseNegativesWithBenignIp, point.FalseNegativesWithProxy, point.FalseNegativesWithBenignProxyIp,
                            point.BlockingThreshold);
                    }
                    writer.Flush();


                }
            }
        }
    }
}
