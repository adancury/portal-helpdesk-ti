namespace PortalHelpdeskTI.Models
{
    public class RupturaPrevisaoJobSettings
    {
        public bool Enabled { get; set; } = true;
        public bool RunOnStartup { get; set; } = false;
        public bool RunMissedOnStartup { get; set; } = true;
        public string PythonExePath { get; set; } = "python";
        public string ScriptPath { get; set; } = "";
        public string RunAt { get; set; } = "05:00";
        public int TimeoutMinutes { get; set; } = 240;
    }
}
