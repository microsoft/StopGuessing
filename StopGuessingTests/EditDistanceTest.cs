using StopGuessing.DataStructures;
using Xunit;

namespace StopGuessingTests
{
    public class EditDistanceTests
    {
        [Fact]
        public void EditDistanceTranspose()
        {
            Assert.Equal(0.8f, EditDistance.Calculate("Transposition", "Transopsition", new EditDistance.Costs(transpose: .8f)));
        }

        [Fact]
        public void EditDistanceSubstitute()
        {
            Assert.Equal(0.75f, EditDistance.Calculate("Substitution", "Sabstitution!", new EditDistance.Costs(substitute: .25f, add: .5f)));
        }

        [Fact]
        public void EditDistanceDelete()
        {
            Assert.Equal(0.4f, EditDistance.Calculate("Deletion", "eletion", new EditDistance.Costs(delete: 0.4f)));
        }

        [Fact]
        public void EditDistanceAdd()
        {
            Assert.Equal(0.4f, EditDistance.Calculate("dd", "Add", new EditDistance.Costs(add: 0.4f)));
            Assert.Equal(0.4f, EditDistance.Calculate("Add", "Add!", new EditDistance.Costs(add: 0.4f)));
        }

        [Fact]
        public void EditDistanceChangeCase()
        {
            Assert.Equal(0.8f, EditDistance.Calculate("CaseSensitive", "casesensitive", new EditDistance.Costs(caseChange: 0.4f)));
        }

    }
}
