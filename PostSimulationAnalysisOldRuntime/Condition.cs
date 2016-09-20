using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;

namespace PostSimulationAnalysisOldRuntime
{

    public interface ICondition
    {
        string Name { get; }
        float GetScore(Trial t);
    }

    public class PasswordFrequencyOnlyCondition : ICondition
    {
        public string Name => "InvalidAttemptsForPassword";
        public float GetScore(Trial t) => (float) t.InvalidAttemptsPerPassword;
    }

    public class PasswordFrequencyOnlyLadderBinaryCondition : ICondition
    {
        public string Name => "FilterOfInvalidAttemptsForPassword";
        public float GetScore(Trial t) => t.IsFrequentlyGuessedPassword ? 1f : 0f;
    }



    public class AccountLoginFailuresOnlyCondition : ICondition
    {
        public string Name => "ConsecutiveAccountLoginFailures";
        public float GetScore(Trial t) => (float)t.DecayedMaxConsecutiveIncorrectAttemptsPerAccount;
    }



    public class StopGuessingCondition : ICondition
    {
        public string Name { get; set; } = "";
        public double alpha = 5.53d;
        public double beta_notypo = 1.0d;
        public double beta_typo = 0.061;
        public double repeat = 0; // FIXME Stuart
        public double phi_frequent = 11.97;
        public double phi_infrequent = 1.0;
        public double gamma = 0;
        public double T = 287.62; // FIXME Cormac
        public bool cookies_off = false;

        public StopGuessingCondition(string name = "Baseline")
        {
            Name = name;
        }

        //public static IEnumerable<StopGuessingCondition> GetConditions()
        //{
        //    return new StopGuessingCondition[]
        //    {
        //        new StopGuessingCondition("AllOn"),
        //        new StopGuessingCondition("NoTypoDetection") {beta_typo = 0},
        //        new StopGuessingCondition("NoRepeatCorrection") {repeat = 1},
        //        new StopGuessingCondition("PhiIgnoresFrequency") {phi_frequent = 1},
        //        new StopGuessingCondition("FixedThreshold") {T = 1},
        //        new StopGuessingCondition("NoAlpha") {alpha = 1},
        //        new StopGuessingCondition("Control") {alpha = 1, beta_typo =1, beta_notypo=1, phi_frequent = 1, phi_infrequent = 1, T=1, repeat =1, gamma=0},
        //        new StopGuessingCondition("ControlNoRepeats") {alpha = 1, beta_typo =1, beta_notypo=1, phi_frequent = 1, phi_infrequent = 1, T=1, repeat =0, gamma=0}
        //    };
        //}

        public float GetScore(Trial t)
        {

            double score =
                alpha *
                ((
                    t.AccountFailuresInfrequentPassword * phi_infrequent +
                    t.AccountFailuresFrequentPassword * phi_frequent
                    ) +
                    repeat *
                   (
                    t.RepeatAccountFailuresInfrequentPassword * phi_infrequent +
                    t.RepeatAccountFailuresFrequentPassword * phi_frequent
                   )
                )
                +
                beta_notypo * phi_infrequent * (
                    t.PasswordFailuresNoTypoInfrequentPassword +
                    t.RepeatPasswordFailuresNoTypoInfrequentPassword * repeat)
                +
                beta_notypo * phi_frequent * (
                    t.PasswordFailuresNoTypoFrequentPassword +
                    t.RepeatPasswordFailuresNoTypoFrequentPassword * repeat)
                +
                beta_typo * phi_infrequent * (
                    t.PasswordFailuresTypoInfrequentPassword +
                    t.RepeatPasswordFailuresTypoInfrequentPassword * repeat)
                +
                beta_typo * phi_frequent * (
                    t.PasswordFailuresTypoFrequentPassword +
                    t.RepeatPasswordFailuresTypoFrequentPassword * repeat)
                ;
            score -= gamma * t.SuccessfulLogins;
            if (!t.IsFrequentlyGuessedPassword)
                score /= T;
            if (t.DeviceCookieHadPriorSuccessfulLoginForThisAccount && !cookies_off)
                score = 0;

            return (float)score;
        }
    }

}
