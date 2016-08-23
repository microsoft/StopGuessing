using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;

namespace PostSimulationAnalysisOldRuntime
{

    public class Condition
    {
        public string Name = "";
        public double alpha = 5.53d;
        public double beta_notypo = 1.0d;
        public double beta_typo = 0.061;
        public double repeat = 0; // FIXME Stuart
        public double phi_frequent = 11.97;
        public double phi_infrequent = 1.0;
        public double gamma = 0;
        public double T = 287.62; // FIXME Cormac

        public Condition(string name = "Baseline")
        {
            Name = name;
        }

        public static IEnumerable<Condition> GetConditions()
        {
            return new Condition[]
            {
                new Condition("AllOn"),
                new Condition("NoTypoDetection") {beta_typo = 0},
                new Condition("NoRepeatCorrection") {repeat = 1},
                new Condition("PhiIgnoresFrequency") {phi_frequent = 1},
                new Condition("FixedThreshold") {T = 1},
                new Condition("NoAlpha") {alpha = 1},
                new Condition("Control") {alpha = 1, beta_typo =1, beta_notypo=1, phi_frequent = 1, phi_infrequent = 1, T=1, repeat =1, gamma=0},
                new Condition("ControlNoRepeats") {alpha = 1, beta_typo =1, beta_notypo=1, phi_frequent = 1, phi_infrequent = 1, T=1, repeat =0, gamma=0}
            };
        }

        public float GetScore(IPState s, bool IsFrequentlyGuessedPassword, bool DeviceCookieHadPriorSuccessfulLoginForThisAccount)
        {

            double score =
                alpha *
                ((
                    s.AccountFailuresInfrequentPassword * phi_infrequent +
                    s.AccountFailuresFrequentPassword * phi_frequent
                    ) +
                    repeat *
                   (
                    s.RepeatAccountFailuresInfrequentPassword * phi_infrequent +
                    s.RepeatAccountFailuresFrequentPassword * phi_frequent
                   )
                )
                +
                beta_notypo * phi_infrequent * (
                    s.PasswordFailuresNoTypoInfrequentPassword +
                    s.RepeatPasswordFailuresNoTypoInfrequentPassword * repeat)
                +
                beta_notypo * phi_frequent * (
                    s.PasswordFailuresNoTypoFrequentPassword +
                    s.RepeatPasswordFailuresNoTypoFrequentPassword * repeat)
                +
                beta_typo * phi_infrequent * (
                    s.PasswordFailuresTypoInfrequentPassword +
                    s.RepeatPasswordFailuresTypoInfrequentPassword * repeat)
                +
                beta_typo * phi_frequent * (
                    s.PasswordFailuresTypoFrequentPassword +
                    s.RepeatPasswordFailuresTypoFrequentPassword * repeat)
                ;
            score -= gamma * s.SuccessfulLogins;
            if (!IsFrequentlyGuessedPassword)
                score /= T;
            if (DeviceCookieHadPriorSuccessfulLoginForThisAccount)
                score = 0;

            return (float)score;
        }
    }

}
