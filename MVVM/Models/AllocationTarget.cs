namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Allocation target for requirements (Hardware, Software, or Both)
    /// Used to indicate whether a requirement applies to hardware, software, or both subsystems
    /// </summary>
    public enum AllocationTarget
    {
        Unassigned,
        Hardware,
        Software,
        Both
    }
}
