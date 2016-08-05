using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PostSimulationAnalysisOldRuntime
{
    public class IPState
    {
        public double SuccessfulLogins;
        public double AccountFailuresInfrequentPassword;
        public double AccountFailuresFrequentPassword;
        public double RepeatAccountFailuresInfrequentPassword;
        public double RepeatAccountFailuresFrequentPassword;
        public double PasswordFailuresNoTypoInfrequentPassword;
        public double PasswordFailuresNoTypoFrequentPassword;
        public double PasswordFailuresTypoInfrequentPassword;
        public double PasswordFailuresTypoFrequentPassword;
        public double RepeatPasswordFailuresNoTypoInfrequentPassword;
        public double RepeatPasswordFailuresNoTypoFrequentPassword;
        public double RepeatPasswordFailuresTypoInfrequentPassword;
        public double RepeatPasswordFailuresTypoFrequentPassword;
    }

    public class Condition
    {
        public string Name = "Cormacs";
        public double alpha = 5.39d;
        public double beta_notypo = 1.0d;
        public double beta_typo = 0.0518;
        public double repeat = 0; // FIXME Stuart
        public double phi_frequent = 12.2;
        public double phi_infrequent = 1.0;
        public double gamma = 0;
        public double T = 1; // FIXME Cormac
    }

    public class Trial : IPState
    {
        //private static int MasterIndex = -1;
        //public int Index;
        public bool IsPasswordCorrect;
        public bool IsFromAttacker;
        public bool IsAGuess;
        public bool IsIpInAttackersPool = false;
        public bool IsIpInBenignPool = false;

        public uint LineNumber;
        public bool DeviceCookieHadPriorSuccessfulLoginForThisAccount ;
        public bool IsFrequentlyGuessedPassword;
        public bool IsClientAProxyIP;


        //public string TypeOfMistake;
        //public float IndustryBlockScore;
        //public float SSHBlockScore;
        public int UserID;
        public uint ClientIP;
        //public string Password;
        //public float[] scoreForEachCondition;


        //static Trial()
        //{
        //    ScoreTable = new float[15][];
        //    for (int i = 0; i < 15; i++)
        //    {
        //        if (i > 0 && i < 7)
        //            continue;
        //        ScoreTable[i] = new float[1000*1000*1000];
        //    }
        //}
        

        public Trial(uint lineNumber, string[] fields)
        {
            // Interlocked increment
            this.LineNumber = lineNumber;
            //Index = Interlocked.Increment(ref MasterIndex);
            int field = 0;

            field++; // Password

            if (!int.TryParse(fields[field++].Substring(5), out UserID))// UsernameOrAccountID
                UserID = -1; // Bad account

            ClientIP = 0;
            string[] ipAsByteStrings = fields[field++].Split('.');
            foreach (string byteStr in ipAsByteStrings)
                ClientIP = (ClientIP << 8) + uint.Parse(byteStr);

            DeviceCookieHadPriorSuccessfulLoginForThisAccount = fields[field++] == "HadCookie";// simAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount

            IsFrequentlyGuessedPassword = fields[field++] == "Frequent"; // simAttempt.IsFrequentlyGuessedPassword

            IsPasswordCorrect = fields[field++] == "Correct"; // IsPasswordValid == "Correct"

            IsFromAttacker = fields[field++] == "FromAttacker";

            IsAGuess = fields[field++] == "IsGuess"; // simAttempt.IsGuess ? "IsGuess"

            string ipPool = fields[field++]; // simAttempt.IsFromAttacker ? (ipInfo.UsedByBenignUsers ? "IsInBenignPool" : "NotUsedByBenign") : 
            IsIpInBenignPool = !IsFromAttacker || ipPool == "IsInBenignPool";
            IsIpInAttackersPool = IsFromAttacker ||  ipPool.Contains("InAttackersIpPool");

            IsClientAProxyIP = fields[field++] == "ProxyIP"; // ipInfo.IsPartOfProxy ? "ProxyIP" : "NotAProxy",

            field++; // simAttempt.MistakeType

            SuccessfulLogins = double.Parse(fields[field++]);
            AccountFailuresInfrequentPassword = double.Parse(fields[field++]);
            AccountFailuresFrequentPassword = double.Parse(fields[field++]);
            RepeatAccountFailuresInfrequentPassword = double.Parse(fields[field++]);
            RepeatAccountFailuresFrequentPassword = double.Parse(fields[field++]);
            PasswordFailuresNoTypoInfrequentPassword = double.Parse(fields[field++]);
            PasswordFailuresNoTypoFrequentPassword = double.Parse(fields[field++]);
            PasswordFailuresTypoInfrequentPassword = double.Parse(fields[field++]);
            PasswordFailuresTypoFrequentPassword = double.Parse(fields[field++]);
            RepeatPasswordFailuresNoTypoInfrequentPassword = double.Parse(fields[field++]);
            RepeatPasswordFailuresNoTypoFrequentPassword = double.Parse(fields[field++]);
            RepeatPasswordFailuresTypoInfrequentPassword = double.Parse(fields[field++]);
            RepeatPasswordFailuresTypoFrequentPassword = double.Parse(fields[field++]);


            //IsPasswordCorrect = fields[field++] == "Correct";
            //    IsFromAttacker = fields[field++] == "FromAttacker";
            //    IsAGuess = fields[field++] == "IsGuess";
            //    if (IsFromAttacker)
            //        IsIpInBenignPool = fields[field++] == "IsInBenignPool";
            //    else
            //        IsIpInAttackersPool = fields[field++].Contains("InAttackersIpPool");
            //    IsClientAProxyIP = fields[field++] == "ProxyIP";
            //    field++; // TypeOfMistake = fields[field++];
            //    if (!int.TryParse(fields[field++].Substring(5), out UserID))
            //        UserID = -1; // Bad account
            //    ClientIP = 0;
            //    string[] ipAsByteStrings = fields[field++].Split('.');
            //    foreach (string byteStr in ipAsByteStrings)
            //        ClientIP = (ClientIP << 8) + uint.Parse(byteStr);
            //    field++; // Password = fields[field++];
            //    // Just in case password had comma in it.
            //    if (fields.Length - field > 15)
            //        field = fields.Length - 15;
            //    int condition = 0;
            //    for (; field < fields.Length; field++)
            //    {
            //        if (condition == 0 || condition >= 7)
            //        {
            //            float f;
            //            float.TryParse(fields[field], out f);
            //            ScoreTable[condition][Index] = f;
            //        }
            //        condition++;
            //    }
        }

        public float GetScoreForCondition(Condition c)
        {

            double score =
                c.alpha*
                ( ( 
                    AccountFailuresInfrequentPassword*c.phi_infrequent +
                    AccountFailuresFrequentPassword*c.phi_frequent 
                    ) +
                    c.repeat * 
                   (
                    RepeatAccountFailuresInfrequentPassword*c.phi_infrequent + 
                    RepeatAccountFailuresFrequentPassword*c.phi_frequent
                   )
                )
                +
                c.beta_notypo * c.phi_infrequent * (
                    PasswordFailuresNoTypoInfrequentPassword +
                    RepeatPasswordFailuresNoTypoInfrequentPassword * c.repeat)
                +
                c.beta_notypo * c.phi_frequent * (
                    PasswordFailuresNoTypoFrequentPassword +
                    RepeatPasswordFailuresNoTypoFrequentPassword * c.repeat)
                +
                c.beta_typo * c.phi_infrequent * (
                    PasswordFailuresTypoInfrequentPassword +
                    RepeatPasswordFailuresTypoInfrequentPassword * c.repeat)
                +
                c.beta_typo * c.phi_frequent * (
                    PasswordFailuresTypoFrequentPassword +
                    RepeatPasswordFailuresTypoFrequentPassword * c.repeat)
                ;
          score -= c.gamma*SuccessfulLogins;
            if (!IsFrequentlyGuessedPassword)
                score /= c.T;
            if (DeviceCookieHadPriorSuccessfulLoginForThisAccount)
                score = 0;
            
            return (float) score;
        }

        public int CompareTo(Trial other, Condition condition)
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
