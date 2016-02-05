using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PostSimulationAnalysisOldRuntime
{

    public class Trial
    {
        static float[][] ScoreTable;
        private static int MasterIndex = -1;
        public int Index;
        public bool IsPasswordCorrect;
        public bool IsFromAttacker;
        public bool IsAGuess;
        public bool IsIpInAttackersPool = false;
        public bool IsIpInBenignPool = false;
        public bool IsClientAProxyIP;
        //public string TypeOfMistake;
        //public float IndustryBlockScore;
        //public float SSHBlockScore;
        public int UserID;
        public uint ClientIP;
        //public string Password;
        //public float[] scoreForEachCondition;

        static Trial()
        {
            ScoreTable = new float[15][];
            for (int i = 0; i < 15; i++)
            {
                if (i > 0 && i < 7)
                    continue;
                ScoreTable[i] = new float[1000*1000*1000];
            }
        }
        

        public Trial(string[] fields)
        {
            // Interlocked increment
            Index = Interlocked.Increment(ref MasterIndex);
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
            if (!int.TryParse(fields[field++].Substring(5), out UserID))
                UserID = -1; // Bad account
            ClientIP = 0;
            string[] ipAsByteStrings = fields[field++].Split('.');
            foreach (string byteStr in ipAsByteStrings)
                ClientIP = (ClientIP << 8) + uint.Parse(byteStr);
            field++; // Password = fields[field++];
            // Just in case password had comma in it.
            if (fields.Length - field > 15)
                field = fields.Length - 15;
            int condition = 0;
            for (; field < fields.Length; field++)
            {
                if (condition == 0 || condition >= 7)
                {
                    float f;
                    float.TryParse(fields[field], out f);
                    ScoreTable[condition][Index] = f;
                }
                condition++;
            }
        }

        public float GetScoreForCondition(int condition)
        {
            return ScoreTable[condition][Index];
        }

        public int CompareTo(Trial other, int condition)
        {
            float myScore = GetScoreForCondition(condition);
            float othersScore = other.GetScoreForCondition(condition);
            if (myScore < othersScore)
                return -1;
            if (myScore > othersScore)
                return 1;
            return 0;
        }

    }
}
