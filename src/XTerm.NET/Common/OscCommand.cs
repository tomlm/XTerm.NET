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
    /// ConEmu-style extensions (OSC 9). Multiplexed: the FIRST parameter selects the operation
    /// rather than the code doing so, which is why this one name covers three unrelated features.
    /// Format: OSC 9 ; 9 ; path ST        - current working directory
    ///         OSC 9 ; 4 ; state ; pct ST - progress reporting
    ///         OSC 9 ; text ST            - desktop notification
    /// </summary>
    ConEmu = 9,
    
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
    /// Mouse pointer shape, Kitty's protocol (OSC 22).
    /// Format: OSC 22 ; [=] shape ST      - set the current shape, or reset it when bare
    ///         OSC 22 ; &gt; shape,... ST    - push shapes, the last one becoming current
    ///         OSC 22 ; &lt; ST             - pop the current shape
    ///         OSC 22 ; ? name,... ST     - query support, answered with an OSC 22
    /// </summary>
    PointerShape = 22,

    /// <summary>
    /// Clipboard operations (OSC 52).
    /// Format: OSC 52 ; c ; data ST
    /// </summary>
    Clipboard = 52,

    /// <summary>
    /// Kitty desktop notifications.
    /// Format: OSC 99 ; metadata ; base64-payload ST
    /// </summary>
    KittyNotification = 99,
    /// <summary>
    /// Kitty clipboard operations.
    /// Format: OSC 5522 ; type=read|write:mime=type ; data ST
    /// </summary>
    KittyClipboard = 5522,
    
    /// <summary>
    /// Kitty text sizing (OSC 66).
    /// Format: OSC 66 ; key=value : ... ; text ST
    /// </summary>
    TextSizing = 66,
    
    /// <summary>
    /// Shell integration marks, FinalTerm/FTCS (OSC 133).
    /// Format: OSC 133 ; A ST            - start of prompt
    ///         OSC 133 ; B ST            - start of command line, i.e. end of prompt
    ///         OSC 133 ; C ST            - start of command output
    ///         OSC 133 ; D [; exit] ST   - end of command, with optional exit code
    /// </summary>
    ShellIntegration = 133,

    /// <summary>
    /// iTerm2 proprietary extensions (OSC 1337).
    /// </summary>
    ITerm2 = 1337,
    
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
