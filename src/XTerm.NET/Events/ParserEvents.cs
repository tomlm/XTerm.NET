using XTerm.Parser;

namespace XTerm.Events.Parser;

/// <summary>
/// Event arguments for print operations.
/// </summary>
public class PrintEventArgs : EventArgs
{
    /// <summary>
    /// The character(s) to print.
    /// </summary>
    public string Data { get; }

    public PrintEventArgs(string data)
    {
        Data = data;
    }
}

/// <summary>
/// Event arguments for control character execution.
/// </summary>
public class ExecuteEventArgs : EventArgs
{
    /// <summary>
    /// The control character code.
    /// </summary>
    public int Code { get; }

    public ExecuteEventArgs(int code)
    {
        Code = code;
    }
}

/// <summary>
/// Event arguments for CSI (Control Sequence Introducer) sequences.
/// </summary>
public class CsiEventArgs : EventArgs
{
    /// <summary>
    /// The CSI sequence identifier (final character and any collected intermediates).
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// The parameters for the CSI sequence.
    /// </summary>
    public Params Parameters { get; }

    public CsiEventArgs(string identifier, Params parameters)
    {
        Identifier = identifier;
        Parameters = parameters;
    }
}

/// <summary>
/// Event arguments for ESC sequences.
/// </summary>
public class EscEventArgs : EventArgs
{
    /// <summary>
    /// The final character of the ESC sequence.
    /// </summary>
    public string FinalChar { get; }

    /// <summary>
    /// Any collected intermediate characters.
    /// </summary>
    public string Collected { get; }

    public EscEventArgs(string finalChar, string collected)
    {
        FinalChar = finalChar;
        Collected = collected;
    }
}

/// <summary>
/// Event arguments for OSC (Operating System Command) sequences.
/// </summary>
public class OscEventArgs : EventArgs
{
    /// <summary>
    /// The OSC command data.
    /// </summary>
    public string Data { get; }

    public OscEventArgs(string data)
    {
        Data = data;
    }
}

/// <summary>
/// Event arguments for DCS (Device Control String) sequences.
/// </summary>
public class DcsEventArgs : EventArgs
{
    /// <summary>
    /// The DCS command data.
    /// </summary>
    public string Data { get; }

    /// <summary>
    /// The parameters for the DCS sequence.
    /// </summary>
    public Params Parameters { get; }

    public DcsEventArgs(string data, Params parameters)
    {
        Data = data;
        Parameters = parameters;
    }
}
