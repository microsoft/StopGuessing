using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StopGuessing.DataStructures;
using Xunit;

namespace StopGuessingTests
{ 

    public class CacheTests
    {
        private Cache<int, string> CreateCacheContainingFirstThousandCountingNumbers()
        {
            Cache<int, string> c = new Cache<int, string>();
            foreach (KeyValuePair<int, string> entry in Enumerable.Range(1, 1000).Select(i => new KeyValuePair<int, string>(i, i.ToString())))
                c.Add(entry);
            return c;
        }

        [Fact]
        public void CacheTestBasicFetchAndLru()
        {
            Cache<int, string> c = CreateCacheContainingFirstThousandCountingNumbers();
            Assert.False(c.ContainsKey(1001));
            Assert.False(c.ContainsKey(0));
            Assert.False(c.ContainsKey(8675309));
            Assert.Equal("1", c.LeastRecentlyAccessed.Value);
            Assert.Equal(1, c.LeastRecentlyAccessed.Key);
            Assert.Equal("1000", c.MostRecentlyAccessed.Value);
            Assert.Equal(1000, c.MostRecentlyAccessed.Key);
            string y = c[1];
            Assert.Equal("1", y);
            string z = c[3];
            Assert.Equal("3", z);
            Assert.Equal("3", c.MostRecentlyAccessed.Value);
            Assert.Equal("2", c.LeastRecentlyAccessed.Value);
        }

        [Fact]
        public void CacheTestRecoverSpace()
        {
            Cache<int, string> c = CreateCacheContainingFirstThousandCountingNumbers();
            c.RecoverSpace(0.25d);
            Assert.Equal(750, c.Count);
            Assert.Equal("251", c.LeastRecentlyAccessed.Value);
            Assert.Equal("1000", c.MostRecentlyAccessed.Value);

            // Make elements 251--750 touched more recently than 751-1000
            for (int i = 251; i <= 750; i++)
            {
                Assert.Equal(i.ToString(), c[i]);
            }
            // Remove another 250 elements
            c.RecoverSpace(250);
            Assert.Equal(500, c.Count);
            Assert.Equal("750", c.MostRecentlyAccessed.Value);
            Assert.Equal("251", c.LeastRecentlyAccessed.Value);

            List<KeyValuePair<int, string>> lra = c.GetLeastRecentlyAccessed(500);
            for (int i = 251; i <= 750; i++)
            {
                Assert.Equal(i, lra[i - 251].Key);
            }
        }

        [Fact]
        public void CacheSelfLoadingAsync()
        {
            // Not to be confused with a self-loathing cache.

            Cache<int, string> c = new SelfLoadingCache<int, string>(
                // Map key to value
                (key, cancelToken) => { return Task.Run(() => key.ToString(), cancelToken); }
                );
            Assert.Equal(0, c.Count);
            for (int i = 1; i <= 1000; i++)
            {
                Assert.Equal(i.ToString(), c[i]);
            }
            Assert.Equal(1000, c.Count);
            Assert.Equal("1000", c.MostRecentlyAccessed.Value);
            Assert.Equal("1", c.LeastRecentlyAccessed.Value);
        }
    }
}
