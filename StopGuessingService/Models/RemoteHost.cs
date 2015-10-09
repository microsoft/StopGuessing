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

        public bool IsLocalHost { get; set; }
    }
}
