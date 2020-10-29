namespace DotNetEpicsWeb.Data
{
    public sealed class TreeNodeStatus
    {
        public string Release { get; set; }
        public string Status { get; set; }

        public override string ToString()
        {
            return $"{Release} ({Status})";
        }
    }
}
