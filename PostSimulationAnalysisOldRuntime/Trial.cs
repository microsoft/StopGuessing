using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PostSimulationAnalysisOldRuntime
{

    public class Trial
    {
        public bool IsPasswordCorrect;
        public bool IsFromAttacker;
        public bool IsAGuess;
        public bool IsIpInAttackersPool = false;
        public bool IsIpInBenignPool = false;
        public bool IsClientAProxyIP;
        //public string TypeOfMistake;
        public double IndustryBlockScore;
        public double SSHBlockScore;
        //public string UserID;
        //public string Password;
        public double[] scoreForEachCondition;

        public Trial(string[] fields)
        {
            int field = 0;
            IsPasswordCorrect = fields[field++] == "Correct";
            IsFromAttacker = fields[field++] == "FromAttacker";
            IsAGuess = fields[field++] == "IsGuess";
            if (IsFromAttacker)
                IsIpInBenignPool = fields[field++] == "IsInBenignPool";
            else
                IsIpInAttackersPool = fields[field++].Contains("InAttackersIpPool");
            IsClientAProxyIP = fields[field++] == "ProxyIP";
            field++; // TypeOfMistake = fields[field++];
            field++; // UserID = fields[field++];
            field++; // Password = fields[field++];
            scoreForEachCondition = new double[fields.Length - field];
            int condition = 0;
            for (; field < fields.Length; field++)
                double.TryParse(fields[field], out scoreForEachCondition[condition++]);
        }

        public double GetScoreForCondition(int condition)
        {
            return scoreForEachCondition[condition];
        }

        public int CompareTo(Trial other, int condition)
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
}
