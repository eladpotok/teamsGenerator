using System;

namespace TeamsGenerator.API
{
    public class VersionAttribute : Attribute
    {
        public string MinVersion { get; set; }
    }
}
