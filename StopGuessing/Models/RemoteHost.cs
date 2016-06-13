using System;

namespace StopGuessing.Models
{
    /// <summary>
    /// This class identifies remote hosts to be used as part of a distributed system for
    /// balancing the load of login requests (and storing data associated with past requests)
    /// across systems.
    /// </summary>
    public class RemoteHost
    {
        public Uri Uri { get; set; }

        // FIXME -- remove this and find another way to test
        // public bool IsLocalHost { get; set; }

        public override string ToString()
        {
            return Uri.ToString();
        }
    }

    public class TestRemoveHost : RemoteHost
    {
        public string KeyPrefix { get; set; }

        public new string ToString()
        {
            return KeyPrefix + base.ToString();
        }
    }
}
