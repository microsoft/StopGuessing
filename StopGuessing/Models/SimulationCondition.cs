using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StopGuessing.Models
{
    public class SimulationCondition
    {
        public BlockingAlgorithmOptions Options;
        public bool IgnoresRepeats;
        public bool RewardsClientCookies;
        public bool CreditsValidLogins;
        public bool UsesAlphaForAccountFailures;
        public bool FixesTypos;
        public bool ProtectsAccountsWithPopularPasswords;
        public bool PunishesPopularGuesses;
        public string Name;
        public int Index;

        public SimulationCondition(BlockingAlgorithmOptions options, int index, string name, bool ignoresRepeats, bool rewardsClientCookies, bool creditsValidLogins,
    bool usesAlphaForAccountFailures, bool fixesTypos, bool protectsAccountsWithPopularPasswords, bool punishesPopularGuesses)
        {
            Options = options;
            Index = index;
            Name = name;
            IgnoresRepeats = ignoresRepeats;
            RewardsClientCookies = rewardsClientCookies;
            CreditsValidLogins = creditsValidLogins;
            UsesAlphaForAccountFailures = usesAlphaForAccountFailures;
            FixesTypos = fixesTypos;
            PunishesPopularGuesses = punishesPopularGuesses;
            ProtectsAccountsWithPopularPasswords = protectsAccountsWithPopularPasswords;
        }
    }
}
