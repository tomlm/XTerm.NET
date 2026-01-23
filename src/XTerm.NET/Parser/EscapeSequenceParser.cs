using System.Diagnostics;
using System.Text;
using XTerm.Common;
using XTerm.Events.Parser;

namespace XTerm.Parser;

/// <summary>
/// VT100/ANSI escape sequence parser implementing a state machine.
/// Based on Paul Williams' ANSI parser state machine.
/// </summary>
public class EscapeSequenceParser
{
    private ParserState _state;
    private readonly Params _params;
    private readonly StringBuilder _collect;
    private readonly StringBuilder _osc;
    private readonly StringBuilder _dcs;
    
    // Parser events - Standard C# event pattern
    /// <summary>
    /// Fired when printable characters are parsed.
    /// </summary>
    public event EventHandler<PrintEventArgs>? Print;

    /// <summary>
    /// Fired when control characters are executed.
    /// </summary>
    public event EventHandler<ExecuteEventArgs>? Execute;

    /// <summary>
    /// Fired when CSI sequences are parsed.
    /// </summary>
    public event EventHandler<CsiEventArgs>? Csi;

    /// <summary>
    /// Fired when ESC sequences are parsed.
    /// </summary>
    public event EventHandler<EscEventArgs>? Esc;

    /// <summary>
    /// Fired when OSC sequences are parsed.
    /// </summary>
    public event EventHandler<OscEventArgs>? Osc;

    /// <summary>
    /// Fired when DCS sequences are parsed.
    /// </summary>
    public event EventHandler<DcsEventArgs>? Dcs;

    public EscapeSequenceParser()
    {
        _state = ParserState.Ground;
        _params = new Params();
        _collect = new StringBuilder();
        _osc = new StringBuilder();
        _dcs = new StringBuilder();
    }

    /// <summary>
    /// Parses input data byte by byte.
    /// </summary>
    public void Parse(string data)
    {
        foreach (var rune in data.EnumerateRunes())
        {
            ParseChar(rune.Value);
        }
    }

    /// <summary>
    /// Parses a single character/code point.
    /// </summary>
    private void ParseChar(int code)
    {
        var currentState = _state;

        // C0/C1 control characters
        if (code < 0x20 || (code >= 0x80 && code < 0xA0))
        {
            switch (currentState)
            {
                case ParserState.Ground:
                case ParserState.Escape:
                case ParserState.CsiEntry:
                case ParserState.CsiParam:
                case ParserState.CsiIntermediate:
                case ParserState.CsiIgnore:
                    OnExecute(code);
                    if (code == 0x1B) // ESC
                    {
                        Transition(ParserState.Escape);
                    }
                    return;

                case ParserState.OscString:
                    if (code == 0x1B || code == 0x07) // ESC or BEL
                    {
                        DispatchOsc();
                        Transition(code == 0x1B ? ParserState.Escape : ParserState.Ground);
                    }
                    else if (code >= 0x20)
                    {
                        OscPut(code);
                    }
                    return;
            }
        }

        // Normal state machine processing
        switch (_state)
        {
            case ParserState.Ground:
                if (code >= 0x20)
                {
                    OnPrint(code);
                }
                break;

            case ParserState.Escape:
                switch (code)
                {
                    case 0x5B: // [
                        Transition(ParserState.CsiEntry);
                        break;
                    case 0x5D: // ]
                        Transition(ParserState.OscString);
                        break;
                    case 0x50: // P
                        Transition(ParserState.DcsEntry);
                        break;
                    case 0x5E: // ^
                    case 0x5F: // _
                    case 0x58: // X
                        Transition(ParserState.SosPmApcString);
                        break;
                    case >= 0x20 and < 0x30:
                        Collect(code);
                        Transition(ParserState.EscapeIntermediate);
                        break;
                    case >= 0x30 and < 0x7F:
                        DispatchEsc(code);
                        Transition(ParserState.Ground);
                        break;
                    default:
                        Transition(ParserState.Ground);
                        break;
                }
                break;

            case ParserState.EscapeIntermediate:
                if (code >= 0x20 && code < 0x30)
                {
                    Collect(code);
                }
                else if (code >= 0x30 && code < 0x7F)
                {
                    DispatchEsc(code);
                    Transition(ParserState.Ground);
                }
                break;

            case ParserState.CsiEntry:
                if (code >= 0x3C && code <= 0x3F) // Private parameter markers (<, =, >, ?)
                {
                    Collect(code);
                }
                else if (code >= 0x30 && code < 0x3C) // 0-9, :, ;
                {
                    Param(code);
                    Transition(ParserState.CsiParam);
                }
                else if (code >= 0x40 && code < 0x7F)
                {
                    DispatchCsi(code);
                    Transition(ParserState.Ground);
                }
                else if (code >= 0x20 && code < 0x30)
                {
                    Collect(code);
                    Transition(ParserState.CsiIntermediate);
                }
                break;

            case ParserState.CsiParam:
                if (code >= 0x30 && code < 0x40)
                {
                    Param(code);
                }
                else if (code >= 0x40 && code < 0x7F)
                {
                    DispatchCsi(code);
                    Transition(ParserState.Ground);
                }
                else if (code >= 0x20 && code < 0x30)
                {
                    Collect(code);
                    Transition(ParserState.CsiIntermediate);
                }
                else if (code == 0x3A) // :
                {
                    // Sub-parameter separator
                    Transition(ParserState.CsiIgnore);
                }
                break;

            case ParserState.CsiIntermediate:
                if (code >= 0x20 && code < 0x30)
                {
                    Collect(code);
                }
                else if (code >= 0x40 && code < 0x7F)
                {
                    DispatchCsi(code);
                    Transition(ParserState.Ground);
                }
                break;

            case ParserState.CsiIgnore:
                if (code >= 0x40 && code < 0x7F)
                {
                    Transition(ParserState.Ground);
                }
                break;

            case ParserState.OscString:
                OscPut(code);
                break;

            case ParserState.DcsEntry:
            case ParserState.DcsParam:
            case ParserState.DcsIgnore:
            case ParserState.DcsPassthrough:
                // DCS handling (simplified)
                if (code == 0x9C || code == 0x1B) // ST or ESC
                {
                    Transition(ParserState.Ground);
                }
                break;
        }
    }

    private void Transition(ParserState newState)
    {
        // Exit actions
        switch (_state)
        {
            case ParserState.CsiEntry:
            case ParserState.CsiParam:
            case ParserState.CsiIntermediate:
            case ParserState.CsiIgnore:
                if (newState != ParserState.CsiParam && newState != ParserState.CsiIntermediate && newState != ParserState.CsiIgnore)
                {
                    _params.Reset();
                    _collect.Clear();
                }
                break;
        }

        _state = newState;

        // Entry actions
        switch (newState)
        {
            case ParserState.CsiEntry:
            case ParserState.DcsEntry:
                _params.Reset();
                _collect.Clear();
                _params.AddParam(0);
                break;

            case ParserState.OscString:
                _osc.Clear();
                break;
        }
    }

    /// <summary>
    /// Raises the Print event.
    /// </summary>
    protected virtual void OnPrint(int code)
    {
        Print?.Invoke(this, new PrintEventArgs(char.ConvertFromUtf32(code)));
    }

    /// <summary>
    /// Raises the Execute event.
    /// </summary>
    protected virtual void OnExecute(int code)
    {
        Execute?.Invoke(this, new ExecuteEventArgs(code));
    }

    private void Collect(int code)
    {
        _collect.Append((char)code);
    }

    private void Param(int code)
    {
        if (code == 0x3B) // ;
        {
            _params.AddParam(0);
        }
        else if (code >= 0x30 && code <= 0x39) // 0-9
        {
            var digit = code - 0x30;
            
            // Get current value of last parameter and update it
            var currentValue = _params.GetParam(_params.Length - 1, 0);
            var newValue = currentValue * 10 + digit;
            _params.UpdateLastParam(newValue);
        }
    }

    private void DispatchCsi(int code)
    {
        var finalChar = ((char)code).ToString();
        // Clone params so handlers get their own copy
        var paramsClone = _params.Clone();
        // Collected characters come BEFORE the final character (e.g., "?" before "h" gives "?h")
        var identifier = _collect.ToString() + finalChar;
        OnCsi(identifier, paramsClone);
    }

    /// <summary>
    /// Raises the Csi event.
    /// </summary>
    protected virtual void OnCsi(string identifier, Params parameters)
    {
        Csi?.Invoke(this, new CsiEventArgs(identifier, parameters));
    }

    private void DispatchEsc(int code)
    {
        var finalChar = ((char)code).ToString();
        OnEsc(finalChar, _collect.ToString());
    }

    /// <summary>
    /// Raises the Esc event.
    /// </summary>
    protected virtual void OnEsc(string finalChar, string collected)
    {
        Esc?.Invoke(this, new EscEventArgs(finalChar, collected));
    }

    private void OscPut(int code)
    {
        _osc.Append(char.ConvertFromUtf32(code));
    }

    private void DispatchOsc()
    {
        OnOsc(_osc.ToString());
    }

    /// <summary>
    /// Raises the Osc event.
    /// </summary>
    protected virtual void OnOsc(string data)
    {
        Osc?.Invoke(this, new OscEventArgs(data));
    }

    /// <summary>
    /// Resets the parser to initial state.
    /// </summary>
    public void Reset()
    {
        _state = ParserState.Ground;
        _params.Reset();
        _collect.Clear();
        _osc.Clear();
        _dcs.Clear();
    }
}
