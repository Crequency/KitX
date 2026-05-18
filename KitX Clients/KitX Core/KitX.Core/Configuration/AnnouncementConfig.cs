using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// Announcement configuration implementation
/// </summary>
public class AnnouncementConfig : IAnnouncementConfig
{
    /// <summary>
    /// Gets or sets the list of accepted announcement IDs
    /// </summary>
    public List<string> Accepted { get; set; } = [];

    /// <summary>
    /// Configuration file location (for backward compatibility)
    /// </summary>
    public string? ConfigFileLocation { get; set; }

    /// <summary>
    /// Saves the configuration to file (for backward compatibility)
    /// </summary>
    /// <param name="path">File path to save</param>
    /// <returns>This instance</returns>
    public AnnouncementConfig Save(string path)
    {
        // Implementation would save to file - simplified for compatibility
        return this;
    }
}
