namespace XTerm.Common;

/// <summary>
/// Control Sequence Introducer (CSI) command identifiers.
/// CSI sequences control cursor movement, text formatting, and terminal behavior.
/// Format: ESC [ parameters final_character
/// </summary>
public enum CsiCommand
{
    /// <summary>
    /// Insert blank characters (CSI @).
    /// </summary>
    InsertChars,
    
    /// <summary>
    /// Cursor Up (CSI A).
    /// </summary>
    CursorUp,
    
    /// <summary>
    /// Cursor Down (CSI B).
    /// </summary>
    CursorDown,
    
    /// <summary>
    /// Cursor Forward (CSI C).
    /// </summary>
    CursorForward,
    
    /// <summary>
    /// Cursor Backward (CSI D).
    /// </summary>
    CursorBackward,
    
    /// <summary>
    /// Cursor Next Line (CSI E).
    /// </summary>
    CursorNextLine,
    
    /// <summary>
    /// Cursor Previous Line (CSI F).
    /// </summary>
    CursorPreviousLine,
    
    /// <summary>
    /// Cursor Horizontal Absolute (CSI G).
    /// </summary>
    CursorCharAbsolute,
    
    /// <summary>
    /// Cursor Position (CSI H or CSI f).
    /// </summary>
    CursorPosition,
    
    /// <summary>
    /// Cursor Forward Tabulation (CSI I).
    /// </summary>
    CursorForwardTab,
    
    /// <summary>
    /// Erase in Display (CSI J).
    /// </summary>
    EraseInDisplay,
    
    /// <summary>
    /// Erase in Line (CSI K).
    /// </summary>
    EraseInLine,
    
    /// <summary>
    /// Insert Lines (CSI L).
    /// </summary>
    InsertLines,
    
    /// <summary>
    /// Delete Lines (CSI M).
    /// </summary>
    DeleteLines,
    
    /// <summary>
    /// Delete Characters (CSI P).
    /// </summary>
    DeleteChars,
    
    /// <summary>
    /// Scroll Up (CSI S).
    /// </summary>
    ScrollUp,
    
    /// <summary>
    /// Scroll Down (CSI T).
    /// </summary>
    ScrollDown,
    
    /// <summary>
    /// Erase Characters (CSI X).
    /// </summary>
    EraseChars,
    
    /// <summary>
    /// Cursor Backward Tabulation (CSI Z).
    /// </summary>
    CursorBackwardTab,
    
    /// <summary>
    /// Tab Clear (CSI g).
    /// Ps = 0: Clear current column tab stop.
    /// Ps = 3: Clear all tab stops.
    /// </summary>
    TabClear,
    
    /// <summary>
    /// Device Attributes (CSI c).
    /// </summary>
    DeviceAttributes,
    
    /// <summary>
    /// Line Position Absolute (CSI d).
    /// </summary>
    LinePositionAbsolute,
    
    /// <summary>
    /// Select Graphic Rendition - Set text attributes (CSI m).
    /// </summary>
    SelectGraphicRendition,
    
    /// <summary>
    /// Device Status Report (CSI n).
    /// </summary>
    DeviceStatusReport,
    
    /// <summary>
    /// Set Top and Bottom Margins (CSI r).
    /// </summary>
    SetScrollRegion,
    
    /// <summary>
    /// Save Cursor Position - ANSI (CSI s).
    /// </summary>
    SaveCursorAnsi,
    
    /// <summary>
    /// Window Manipulation (CSI t).
    /// </summary>
    WindowManipulation,
    
    /// <summary>
    /// Restore Cursor Position - ANSI (CSI u).
    /// </summary>
    RestoreCursorAnsi,
    
    /// <summary>
    /// Set Mode (CSI h).
    /// </summary>
    SetMode,
    
    /// <summary>
    /// Reset Mode (CSI l).
    /// </summary>
    ResetMode,
    
    /// <summary>
    /// Select Cursor Style (DECSCUSR, CSI Ps SP q).
    /// </summary>
    SelectCursorStyle,
    
    /// <summary>
    /// Unknown or unsupported command.
    /// </summary>
    Unknown
}
