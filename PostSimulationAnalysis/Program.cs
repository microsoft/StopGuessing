using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PostSimulationAnalysis
{
    public enum SystemMode
    {
        // ReSharper disable once InconsistentNaming
        SSH,
        Basic,
        StopGuessing
    };

    public class Trial
    {
        public bool IsPasswordCorrect;
        public bool IsFromAttacker;
        public bool IsAGuess;
        public bool IsIpInAttackersPool;
        public bool IsClientAProxyIP;
        public string TypeOfMistake;
        public double OurBlockScore;
        public double IndustryBlockScore;
        public double SSHBlockScore;
        public string UserID;
        public string Password;

        public Trial(string[] fields)
        {
            int field = 0;
            IsPasswordCorrect = fields[field++] == "Correct";
            IsFromAttacker = fields[field++] == "FromAttacker";
            IsAGuess = fields[field++] == "IsGuess";
            IsIpInAttackersPool = fields[field++] == "InAttackersIpPool";
            IsClientAProxyIP = fields[field++] == "ProxyIP";
            TypeOfMistake = fields[field++];
            double.TryParse(fields[field++], out OurBlockScore);
            double.TryParse(fields[field++], out IndustryBlockScore);
            double.TryParse(fields[field++], out SSHBlockScore);
            UserID = fields[field++];
            Password = fields[field];
        }

        public double GetScoreForMode(SystemMode mode)
        {
            switch (mode)
            {
                case SystemMode.StopGuessing:
                    return OurBlockScore;
                case SystemMode.Basic:
                    return IndustryBlockScore;
                case SystemMode.SSH:
                    return SSHBlockScore;
                default:
                    return 0;
            }
        }

        public int CompareTo(Trial other ,SystemMode mode )
        {
            double myScore = GetScoreForMode(mode);
            double othersScore = other.GetScoreForMode(mode);
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

            public ROCPoint(int falsePositives, int truePositives, int falseNegatives, int trueNegatives)
            {
                FalsePositives = falsePositives;
                TruePositives = truePositives;
                FalseNegatives = falseNegatives;
                TrueNegatives = trueNegatives;
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
            List<Trial> trials = LoadData("testinput.txt");
            string path = "PointsFor_";

            List<Trial> trialsWithCorrectPassword = trials.Where(t => t.IsPasswordCorrect).ToList();
            List<Trial> trialsUsersCorrectPassword = trialsWithCorrectPassword.Where(t => !t.IsFromAttacker || !t.IsAGuess).ToList();
            List<Trial> trialsGuessesCorrectPassword = trialsWithCorrectPassword.Where(t => t.IsFromAttacker && t.IsAGuess).ToList();
            
            foreach (SystemMode mode in new SystemMode[] { SystemMode.StopGuessing, SystemMode.Basic, SystemMode.SSH })
            {
                using (StreamWriter writer = new StreamWriter(path + mode.ToString() + ".csv"))
                {
                    trialsUsersCorrectPassword.Sort((a, b) => -a.CompareTo(b, mode));
                    trialsGuessesCorrectPassword.Sort((a, b) => -a.CompareTo(b, mode));

                    List<Trial> originalMalicious = trialsGuessesCorrectPassword;
                    List<Trial> originalBenign = trialsUsersCorrectPassword;

                    Queue<Trial> malicious = new Queue<Trial>(originalMalicious);
                    Queue<Trial> benign = new Queue<Trial>(originalBenign);

                    List<ROCPoint> rocPoints = new List<ROCPoint>();
                    rocPoints.Add(new ROCPoint(0,0, originalMalicious.Count, originalBenign.Count));
                    double blockThreshold = malicious.Peek().GetScoreForMode(mode);
                    while (malicious.Count > 0)
                    {
                        // Remove all malicious requests above this new threshold
                        while (malicious.Count > 0 && malicious.Peek().GetScoreForMode(mode) >= blockThreshold)
                        {
                            malicious.Dequeue();
                        }

                        // Remove all benign requests above this new threshold
                        while (benign.Count > 0 && benign.Peek().GetScoreForMode(mode) >= blockThreshold)
                        {
                            benign.Dequeue();
                        }

                        rocPoints.Add(new ROCPoint(originalBenign.Count - benign.Count, originalMalicious.Count - malicious.Count,
                            malicious.Count, benign.Count));

                        // Identify next threshold
                        if (malicious.Count > 0)
                            blockThreshold = malicious.Peek().GetScoreForMode(mode);
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
                        writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},",
                            point.FalsePositives, point.FalseNegatives, point.TruePositives, point.TrueNegatives,
                            point.FalsePositiveRate, point.TruePositiveRate, point.Precision, point.Recall
                            );
                    }


                }
            }
        }
    }
}
