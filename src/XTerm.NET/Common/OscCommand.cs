namespace XTerm.Common;

/// <summary>
/// Operating System Command (OSC) sequence identifiers.
/// OSC sequences are used for terminal configuration and queries.
/// Format: ESC ] command ; parameters BEL or ST
/// </summary>
public enum OscCommand
{
    /// <summary>
    /// Set icon name and window title (OSC 0).
    /// </summary>
    SetIconAndTitle = 0,
    
    /// <summary>
    /// Set icon name (OSC 1).
    /// </summary>
    SetIconName = 1,
    
    /// <summary>
    /// Set window title (OSC 2).
    /// </summary>
    SetWindowTitle = 2,
    
    /// <summary>
    /// Set X property on top-level window (OSC 3).
    /// </summary>
    SetXProperty = 3,
    
    /// <summary>
    /// Change color palette (OSC 4).
    /// Format: OSC 4 ; index ; colorspec ST
    /// </summary>
    ChangeColor = 4,
    
    /// <summary>
    /// Set current working directory (OSC 7).
    /// Format: OSC 7 ; file://hostname/path ST
    /// </summary>
    CurrentDirectory = 7,
    
    /// <summary>
    /// Hyperlink (OSC 8).
    /// Format: OSC 8 ; params ; URI ST
    /// </summary>
    Hyperlink = 8,
    
    /// <summary>
    /// Set foreground color (OSC 10).
    /// </summary>
    ForegroundColor = 10,
    
    /// <summary>
    /// Set background color (OSC 11).
    /// </summary>
    BackgroundColor = 11,
    
    /// <summary>
    /// Set cursor color (OSC 12).
    /// </summary>
    CursorColor = 12,
    
    /// <summary>
    /// Clipboard operations (OSC 52).
    /// Format: OSC 52 ; c ; data ST
    /// </summary>
    Clipboard = 52,
    
    /// <summary>
    /// Reset color palette (OSC 104).
    /// </summary>
    ResetColor = 104,
    
    /// <summary>
    /// Reset foreground color (OSC 110).
    /// </summary>
    ResetForeground = 110,
    
    /// <summary>
    /// Reset background color (OSC 111).
    /// </summary>
    ResetBackground = 111,
    
    /// <summary>
    /// Reset cursor color (OSC 112).
    /// </summary>
    ResetCursor = 112
}
