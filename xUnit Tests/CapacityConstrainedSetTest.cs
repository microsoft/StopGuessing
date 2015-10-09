using Xunit;
using StopGuessing.DataStructures;
using Newtonsoft.Json;

namespace xUnit_Tests
{
    // This project can output the Class library as a NuGet Package.
    // To enable this option, right-click on the project and select the Properties menu item. In the Build tab select "Produce outputs on build".
    public class CapacityConstrainedSetTest
    {

        [Fact]
        public void CapacityConstrainedSetTestSerialize()
        {
            CapacityConstrainedSet<int> before = new CapacityConstrainedSet<int>(3);
            before.UnionWith( new[] {1,2} );
            before.Add(3);
            before.Add(4);

            Assert.False(before.Contains(0));
            Assert.False(before.Contains(1));
            Assert.True(before.Contains(2));
            Assert.True(before.Contains(3));
            Assert.True(before.Contains(4));
            Assert.Equal(before.Capacity, 3);

            string serialized = JsonConvert.SerializeObject(before);
            CapacityConstrainedSet<int> after = JsonConvert.DeserializeObject<CapacityConstrainedSet<int>>(serialized);

            Assert.False(after.Contains(0));
            Assert.False(after.Contains(1));
            Assert.True(after.Contains(2));
            Assert.True(after.Contains(3));
            Assert.True(after.Contains(4));

            Assert.Equal(before.InOrderAdded, after.InOrderAdded);

            Assert.Equal(before, after);
        }
    }
}
