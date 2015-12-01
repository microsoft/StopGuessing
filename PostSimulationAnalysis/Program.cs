using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PostSimulationAnalysis
{

    public class Trial
    {
        public bool IsPasswordCorrect;
        public bool IsFromAttacker;
        public bool IsAGuess;
        public bool IsIpInAttackersPool;
        public bool IsClientAProxyIP;
        public string TypeOfMistake;
        public double IndustryBlockScore;
        public double SSHBlockScore;
        public string UserID;
        public string Password;
        public double[] scoreForEachCondition;

        public Trial(string[] fields)
        {
            int field = 0;
            IsPasswordCorrect = fields[field++] == "Correct";
            IsFromAttacker = fields[field++] == "FromAttacker";
            IsAGuess = fields[field++] == "IsGuess";
            IsIpInAttackersPool = fields[field++] == "InAttackersIpPool";
            IsClientAProxyIP = fields[field++] == "ProxyIP";
            TypeOfMistake = fields[field++];
            UserID = fields[field++];
            Password = fields[field++];
            scoreForEachCondition = new double[fields.Length - field];
            int condition = 0;
            for (;field < fields.Length;field++)
                double.TryParse(fields[field], out scoreForEachCondition[condition++]);
        }

        public double GetScoreForCondition(int condition)
        {
            return scoreForEachCondition[condition];
        }

        public int CompareTo(Trial other ,int condition)
        {
            double myScore = GetScoreForCondition(condition);
            double othersScore = other.GetScoreForCondition(condition);
            if (myScore < othersScore)
                return -1;
            if (myScore > othersScore)
                return 1;
            return 0;
        }
        
    }

    public class Program
    {

        private List<Trial> LoadData(string path)
        {
            List<Trial> trials = new List<Trial>();
            using (StreamReader file = new StreamReader(path))
            {
                // Skip header
                file.ReadLine();

                string line;
                while ((line = file.ReadLine()) != null)
                {
                    string[] fields = line.Trim().Split(new char[] {','});
                    if (fields.Length >= 11)
                        trials.Add(new Trial(fields));
                }
            }
            return trials;
        }

        public class ROCPoint
        {
            public int FalsePositives;
            public int TruePositives;
            public int FalseNegatives;
            public int TrueNegatives;
            public double BlockingThreshold;

            public ROCPoint(int falsePositives, int truePositives, int falseNegatives, int trueNegatives,
                double blockingThreshold)
            {
                FalsePositives = falsePositives;
                TruePositives = truePositives;
                FalseNegatives = falseNegatives;
                TrueNegatives = trueNegatives;
                BlockingThreshold = blockingThreshold;
            }

            public static double fraction(int numerator, int denominator)
            {
                return ((double) numerator/(double) denominator);
            }


            public double FalsePositiveRate => fraction(FalsePositives, FalsePositives + TrueNegatives);
            public double TruePositiveRate => fraction(TruePositives, TruePositives + FalseNegatives);
            public double Precision => fraction(TruePositives, TruePositives + FalsePositives);
            public double Recall => fraction(TruePositives, TruePositives + FalseNegatives);

        }

        public void Main(string[] args)
        {
            string path = @"C:\Users\stus\Documents\GitHubVisualStudio\Experiment_11_30_16_57\";
            List<Trial> trials = LoadData(path + "data.txt");

            List<Trial> trialsWithCorrectPassword = trials.Where(t => t.IsPasswordCorrect).ToList();
            List<Trial> trialsUsersCorrectPassword = trialsWithCorrectPassword.Where(t => !t.IsFromAttacker || !t.IsAGuess).ToList();
            List<Trial> trialsGuessesCorrectPassword = trialsWithCorrectPassword.Where(t => t.IsFromAttacker && t.IsAGuess).ToList();
            int numConditions = trialsWithCorrectPassword.First().scoreForEachCondition.Length;
            
            for (int conditionNumber = 0; conditionNumber < numConditions; conditionNumber++)
            {
                using (StreamWriter writer = new StreamWriter(path + "PointsFor_" + conditionNumber.ToString() + ".csv"))
                {
                    writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                        "False +",
                        "False -",
                        "True +",
                        "True -",
                        "FP Rate",
                        "TP Rate",
                        "Precision",
                        "Recall",
                        "Threshold");

                    List<Trial> originalMalicious = new List<Trial>(trialsGuessesCorrectPassword);
                    List<Trial> originalBenign = new List<Trial>(trialsUsersCorrectPassword);
                    originalMalicious.Sort((a, b) => -a.CompareTo(b, conditionNumber));
                    originalBenign.Sort((a, b) => -a.CompareTo(b, conditionNumber));

                    Queue<Trial> malicious = new Queue<Trial>(originalMalicious);
                    Queue<Trial> benign = new Queue<Trial>(originalBenign);

                    double blockThreshold = malicious.Peek().GetScoreForCondition(conditionNumber);
                    List<ROCPoint> rocPoints = new List<ROCPoint>
                    {
                        new ROCPoint(0, 0, originalMalicious.Count, originalBenign.Count, blockThreshold)
                    };
                    while (malicious.Count > 0)
                    {
                        // Remove all malicious requests above this new threshold
                        while (malicious.Count > 0 && malicious.Peek().GetScoreForCondition(conditionNumber) >= blockThreshold)
                        {
                            malicious.Dequeue();
                        }

                        // Remove all benign requests above this new threshold
                        while (benign.Count > 0 && benign.Peek().GetScoreForCondition(conditionNumber) >= blockThreshold)
                        {
                            benign.Dequeue();
                        }

                        rocPoints.Add(new ROCPoint(originalBenign.Count - benign.Count, originalMalicious.Count - malicious.Count,
                            malicious.Count, benign.Count, blockThreshold));

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
                        writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                            point.FalsePositives, point.FalseNegatives, point.TruePositives, point.TrueNegatives,
                            point.FalsePositiveRate, point.TruePositiveRate, point.Precision, point.Recall,
                            point.BlockingThreshold);
                    }
                    writer.Flush();


                }
            }
        }
    }
}
