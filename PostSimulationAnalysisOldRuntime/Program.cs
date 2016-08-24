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

        private static Trial[] LoadData(string path)
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
                    string[] fields = line.Trim().Split(new char[] { '\t' });
                    if (fields.Length >= 23)
                        trials.Add(new Trial((uint)(trials.Count + 1), fields));
                    if (++counter >= 100000)
                    {
                        counter = 0;
                        Console.Out.WriteLine("Loaded: {0} thousand", trials.Count / 1000);
                    }
                }
            }
            return trials.ToArray();
        }



        public static void Main()   // string[] args
        {
            string runDirectoryPath = @"f:\OneDrive\StopGuessingData\Run_100000000_8_19_23_32";
            DirectoryInfo runDir = new DirectoryInfo(runDirectoryPath);
            foreach (DirectoryInfo testDir in runDir.EnumerateDirectories())
            {
                string path = testDir.FullName;
                Console.Out.WriteLine("Experiment: {0}", path);

                if (!File.Exists(path + @"\LegitimateAttemptsWithValidPasswords.txt") || !File.Exists(path + @"\AttackAttemptsWithValidPasswords.txt"))
                    continue;

                Trial[] trialsUsersCorrectPassword = LoadData(path + @"\LegitimateAttemptsWithValidPasswords.txt");
                Trial[] trialsGuessesCorrectPassword = LoadData(path + @"\AttackAttemptsWithValidPasswords.txt");

                int benignWithCookie = trialsUsersCorrectPassword.Count(r => r.DeviceCookieHadPriorSuccessfulLoginForThisAccount);

                //Trial[] trialsWithCorrectPassword = trials.Where(t => t.IsPasswordCorrect).ToArray();
                //Trial[] trialsUsersCorrectPassword = trials.Where(t => t.IsPasswordCorrect && !(t.IsFromAttacker && t.IsAGuess)).ToArray();
                //Trial[] trialsGuessesCorrectPassword = trials.Where(t => t.IsPasswordCorrect && (t.IsFromAttacker && t.IsAGuess)).ToArray();
                //int numConditions = 15;

                //long numCorrectBenignFromAttackersIPpool = trialsUsersCorrectPassword.LongCount(t => t.IsIpInAttackersPool);
                //long numCorrectBenignFromProxy = trialsUsersCorrectPassword.LongCount(t => t.IsClientAProxyIP);
                //long numCorrectBenignBoth = trialsUsersCorrectPassword.LongCount(t => t.IsIpInAttackersPool && t.IsClientAProxyIP);
                long numCorrectGuessesFromProxy = trialsGuessesCorrectPassword.LongCount(t => t.IsClientAProxyIP);
                long numCorrectGuessesFromBenignIpPool = trialsGuessesCorrectPassword.LongCount(t => t.IsIpInBenignPool);
                long numCorrectGuessesFromBoth = trialsGuessesCorrectPassword.LongCount(t => t.IsIpInBenignPool); // && t.IsClientAProxyIP
                List<Task> tasks = new List<Task>();

                ICondition[] conditions =  new ICondition[]
                {
                    new StopGuessingCondition("AllOn"),
                    new StopGuessingCondition("NoTypoDetection") {beta_typo = 0},
                    new StopGuessingCondition("NoRepeatCorrection") {repeat = 1},
                    new StopGuessingCondition("PhiIgnoresFrequency") {phi_frequent = 1},
                    new StopGuessingCondition("FixedThreshold") {T = 1},
                    new StopGuessingCondition("NoCookies") { cookies_off = true },
                    new StopGuessingCondition("NoAlpha") {alpha = 1},
                    new StopGuessingCondition("Control") {alpha = 1, beta_typo =1, beta_notypo=1, phi_frequent = 1, phi_infrequent = 1, T=1, repeat =1, gamma=0},
                    new StopGuessingCondition("ControlNoRepeats") {alpha = 1, beta_typo =1, beta_notypo=1, phi_frequent = 1, phi_infrequent = 1, T=1, repeat =0, gamma=0},
                    new PasswordFrequencyOnlyCondition(),
                    new AccountLoginFailuresOnlyCondition()
                };

                foreach (ICondition condition in conditions)
                {
                    Console.Out.WriteLine("Starting work on Condition: {0}", condition.Name);
                    List<ROCPoint> rocPoints = new List<ROCPoint>();

                    {
                        Queue<Trial> malicious =
                            new Queue<Trial>(
                                trialsGuessesCorrectPassword.AsParallel()
                                    .OrderByDescending((x) => condition.GetScore(x)));
                        Console.Out.WriteLine("Sort 1 completed for Condition {0}", condition.Name);
                        Queue<Trial> benign =
                            new Queue<Trial>(
                                trialsUsersCorrectPassword.AsParallel()
                                    .OrderByDescending((x) => condition.GetScore(x)));
                        Console.Out.WriteLine("Sort 2 completed for Condition {0}", condition.Name);
                        int originalMaliciousCount = malicious.Count;
                        int originalBenignCount = benign.Count;


                        int falsePositives = 0;
                        int falsePositivesWithProxy = 0;
                        int falsePositivesWithAttackerIp = 0;
                        int falsePositivesWithProxyAndAttackerIp = 0;
                        long falseNegativeWithProxy = numCorrectGuessesFromProxy;
                        long falseNegativeBenignIp = numCorrectGuessesFromBenignIpPool;
                        long falseNegativeBenignProxyIp = numCorrectGuessesFromBoth;
                        HashSet<int> falsePositiveUsers = new HashSet<int>();
                        HashSet<uint> falsePositiveIPs = new HashSet<uint>();
                        //int falseNegativeFromDefendersIpPool;


                        double blockThreshold = malicious.Count == 0 ? 0 : condition.GetScore(malicious.Peek());
                        rocPoints.Add(
                            new ROCPoint(0, 0, 0, 0, originalMaliciousCount, originalBenignCount,
                                falsePositivesWithAttackerIp, falsePositivesWithProxy,
                                falsePositivesWithProxyAndAttackerIp,
                                falseNegativeBenignIp, falseNegativeWithProxy, falseNegativeBenignProxyIp,
                                blockThreshold));

                        while (malicious.Count > 0)
                        {
                            // Remove all malicious requests above this new threshold
                            while (malicious.Count > 0 &&
                                   condition.GetScore(malicious.Peek()) >= blockThreshold)
                            {
                                Trial t = malicious.Dequeue();
                                if (t.IsClientAProxyIP)
                                    falseNegativeWithProxy--;
                                if (t.IsIpInBenignPool)
                                    falseNegativeBenignIp--;
                                if (t.IsIpInBenignPool && t.IsClientAProxyIP)
                                    falseNegativeBenignProxyIp--;
                            }

                            // Remove all benign requests above this new threshold
                            while (benign.Count > 0 &&
                                   condition.GetScore(benign.Peek()) >= blockThreshold)
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
                                falsePositives, //originalBenign.Count - benign.Count,
                                falsePositiveUsers.Count(),
                                falsePositiveIPs.Count(),
                                //falseNegatives, //
                                originalMaliciousCount - malicious.Count,
                                malicious.Count,
                                benign.Count,
                                falsePositivesWithAttackerIp, falsePositivesWithProxy,
                                falsePositivesWithProxyAndAttackerIp,
                                falseNegativeBenignIp, falseNegativeWithProxy, falseNegativeBenignProxyIp,
                                blockThreshold));

                            // Identify next threshold
                            if (malicious.Count > 0)
                                blockThreshold = condition.GetScore(malicious.Peek());
                        }
                    }

                    Console.Out.WriteLine("ROC Points identified for Condition {0}", condition.Name);

                    List<ROCPoint> finalROCPoints = new List<ROCPoint>();
                    Queue<ROCPoint> rocPointQueue = new Queue<ROCPoint>(rocPoints);
                    while (rocPointQueue.Count > 0)
                    {
                        ROCPoint point = rocPointQueue.Dequeue();
                        if (rocPointQueue.Count == 0 || rocPointQueue.Peek().FalsePositives > point.FalsePositives)
                            finalROCPoints.Add(point);
                    }

                    using (
                        StreamWriter writer = new StreamWriter(path + @"\PointsFor_" + condition.Name + ".csv")
                        )
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


                        foreach (ROCPoint point in finalROCPoints)
                        {
                            writer.WriteLine(
                                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}",
                                point.FalsePositives,
                                point.FalsePositiveUniqueUsers,
                                point.FalsePositiveUniqueIPs,
                                point.FalseNegatives, point.TruePositives, point.TrueNegatives,
                                point.FalsePositiveRate, point.TruePositiveRate, point.Precision, point.Recall,
                                point.FalsePositivesWithAttackerIp, point.FalsePositivesWithProxy,
                                point.FalsePositivesWithProxyAndAttackerIp,
                                point.FalseNegativesWithBenignIp, point.FalseNegativesWithProxy,
                                point.FalseNegativesWithBenignProxyIp,
                                point.BlockingThreshold);
                        }
                        writer.Flush();


                    }
                    Console.Out.WriteLine("Finished with Condition {0}", condition.Name);
                }
            }
        }
    }
}
