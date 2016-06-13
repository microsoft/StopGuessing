using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using StopGuessing.DataStructures;
using Xunit;

namespace StopGuessingTests
{
    // This project can output the Class library as a NuGet Package.
    // To enable this option, right-click on the project and select the Properties menu item. In the Build tab select "Produce outputs on build".
    public class SequenceTest
    {

        [Fact]
        public void SequenceTestSerialize()
        {
            Sequence<int> before = new Sequence<int>(3);
            before.AddRange( new[] {1,2});
            before.Add(3);
            before.Add(4);

            Assert.Equal(4, before[0]);
            Assert.Equal(3, before[1]);
            Assert.Equal(2, before[2]);
            Assert.Equal(3, before.Capacity);
            Assert.Equal(4, before.CountOfAllObservedItems);

            //JsonSerializerSettings serializerSettings = new JsonSerializerSettings();
            //serializerSettings.Converters.Add(new SequenceConverter());

            string serialized = JsonConvert.SerializeObject(before);
            Sequence<int> after = JsonConvert.DeserializeObject<Sequence<int>>(serialized);

            Assert.Equal(4, after[0]);
            Assert.Equal(3, after[1]);
            Assert.Equal(2,after[2]);
            Assert.Equal(3, after.Capacity);
            Assert.Equal(4, after.CountOfAllObservedItems);
            Assert.Equal(before, after);
        }

        [Fact]
        public void SequenceTestToList()
        {
            Sequence<int> s = new Sequence<int>(5);
            s.AddRange(new[] { 1, 2, 3 });
            List<int> asList = s.ToList();
            Assert.Equal(1, asList[0]);
            Assert.Equal(2, asList[1]);
            Assert.Equal(3, asList[2]);
        }
    }
}
