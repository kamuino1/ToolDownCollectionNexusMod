namespace NexusShared;

// One row of the collection CSV: mod title, link, and current processing step
public class ModEntry
{
    public int Index;
    public string Name = "";
    public string Url = "";
    public string Status = "pending";
    public string UpdatedAt = "";
}
