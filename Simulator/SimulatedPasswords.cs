using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;

namespace Simulator
{
    public class SimulatedPasswords
    {
        public WeightedSelector<string> passwordSelector;
        public List<string> passwordsAlreadyKnownToBePopular;
        public List<string> OrderedListOfMostCommonPasswords = new List<string>();
        public WeightedSelector<string> PasswordSelector;
        public WeightedSelector<string> CommonPasswordSelector;
        private DebugLogger _logger;

        public SimulatedPasswords(DebugLogger logger, ExperimentalConfiguration experimentalConfiguration)
        {
            _logger = logger;
            _logger.WriteStatus("Loading popular password file");
            LoadPasswordSelector(experimentalConfiguration.PasswordFrequencyFile);
            _logger.WriteStatus("Loading passwords known to be common by the algorithm before the attack");
            LoadKnownPopularPasswords(experimentalConfiguration.PreviouslyKnownPopularPasswordFile);
            _logger.WriteStatus("Creating common password selector");
            CommonPasswordSelector = PasswordSelector.TrimToInitialItems(
                    (int)experimentalConfiguration.NumberOfPopularPasswordsForAttackerToExploit);
            _logger.WriteStatus("Finished creating common password selector");

            _logger.WriteStatus("Creating list of most common passwords");
            OrderedListOfMostCommonPasswords =
                PasswordSelector.GetItems((int)experimentalConfiguration.NumberOfPopularPasswordsForAttackerToExploit);
            _logger.WriteStatus("Finished creating list of most common passwords");


        }


        public string GetPasswordFromWeightedDistribution()
        {
            return passwordSelector.GetItemByWeightedRandom();
        }

        public void LoadKnownPopularPasswords(string pathToPreviouslyKnownPopularPasswordFile)
        {
            passwordsAlreadyKnownToBePopular = new List<string>();
            using (System.IO.StreamReader file =
                new System.IO.StreamReader(pathToPreviouslyKnownPopularPasswordFile))
            {

                string line;
                while ((line = file.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (!string.IsNullOrEmpty(line))
                        passwordsAlreadyKnownToBePopular.Add(line);
                }
            }
        }

        public async Task PrimeWithKnownPasswordsAsync(LoginAttemptController loginAttemptController)
        {
            await TaskParalllel.ForEachWithWorkers(passwordsAlreadyKnownToBePopular, async (password, itemNumer, cancelToken) =>
                await loginAttemptController.PrimeCommonPasswordAsync(password, 100, cancelToken));
        }


        private void LoadPasswordSelector(string pathToWeightedFrequencyFile)
        {
            passwordSelector = new WeightedSelector<string>();
            // Created a weighted-random selector for paasswords based on the RockYou database.
            using (System.IO.StreamReader file =
                new System.IO.StreamReader(pathToWeightedFrequencyFile))
            {
                string lineWithCountFollowedBySpaceFollowedByPassword;
                while ((lineWithCountFollowedBySpaceFollowedByPassword = file.ReadLine()) != null)
                {
                    lineWithCountFollowedBySpaceFollowedByPassword =
                        lineWithCountFollowedBySpaceFollowedByPassword.Trim();
                    int indexOfFirstSpace = lineWithCountFollowedBySpaceFollowedByPassword.IndexOf(' ');
                    if (indexOfFirstSpace < 0 ||
                        indexOfFirstSpace + 1 >= lineWithCountFollowedBySpaceFollowedByPassword.Length)
                        continue; // The line is invalid as it doesn't have a space with a password after it
                    string countAsString = lineWithCountFollowedBySpaceFollowedByPassword.Substring(0, indexOfFirstSpace);
                    ulong count;
                    if (!ulong.TryParse(countAsString, out count))
                        continue; // The count field is invalid as it doesn't parse to an unsigned number
                    string password = lineWithCountFollowedBySpaceFollowedByPassword.Substring(indexOfFirstSpace + 1);
                    passwordSelector.AddItem(password, count);
                }
            }
        }



    }
}
