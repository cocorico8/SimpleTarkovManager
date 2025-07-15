namespace SimpleTarkovManager.Models
{
    // This enum defines the different stages of our "Hybrid Staging" update process.
    public enum UpdateStage
    {
        Preparing,
        Copying,
        Patching,
        Finalizing
    }

    // This class holds all the rich information about the current state of the update.
    public class UpdateProgressReport
    {
        public UpdateStage Stage { get; set; }
        public int TotalSteps { get; set; }
        public int CurrentStep { get; set; }
        public string? Message { get; set; }
        public double Percentage => TotalSteps > 0 ? (double)CurrentStep / TotalSteps * 100 : 0;
    }
}