namespace XTerm.Common;

/// <summary>
/// What the terminal answers when a program asks it, through XTGETTCAP, what it can do.
/// </summary>
/// <remarks>
/// <para>XTGETTCAP exists because a program's idea of the terminal comes from <c>TERM</c> and the
/// terminfo database on the machine it happens to be running on — which, over ssh or in a container,
/// describes some other terminal entirely. Asking the terminal directly is the way out of that, so
/// what is reported here has to be what THIS emulator actually does, not what the entry named by
/// <see cref="Options.TerminalOptions.TermName"/> claims.</para>
/// <para>Both spellings of a capability are answerable: the two-letter termcap name (<c>ku</c>) and
/// the terminfo one (<c>kcuu1</c>), because a caller uses whichever its own database uses and has no
/// way to know which one we would have preferred. Names are case-sensitive — <c>Co</c> and <c>co</c>
/// are different capabilities in termcap — so the lookup is ordinal.</para>
/// </remarks>
internal static class TermCapabilities
{
    /// <summary>ESC, named so no source line has to carry a control character of its own.</summary>
    private static readonly string E = ((char)0x1B).ToString();

    /// <summary>BEL, which ends the OSC in <c>Ms</c> and is the whole of <c>bel</c>.</summary>
    private static readonly string Bel = ((char)0x07).ToString();

    /// <summary>DEL, which is what the Backspace key sends here.</summary>
    private static readonly string Del = ((char)0x7F).ToString();

    /// <summary>
    /// The capabilities whose value is the same for every terminal built from this code. The ones
    /// that depend on the instance — its name, its size, whether Sixel is switched on — are answered
    /// by <see cref="TryGet"/> instead.
    /// </summary>
    private static readonly Dictionary<string, string> Static = new(StringComparer.Ordinal)
    {
        // ---- Colour ---------------------------------------------------------------------------
        // Direct colour, spelled three ways: RGB and Tc are what a program tests to decide whether
        // it may emit 24-bit SGR at all, and setrgbf/setrgbb are the strings it uses once it may.
        ["RGB"] = "8/8/8",
        ["Tc"] = "",
        ["setrgbf"] = E + "[38:2:%p1%d:%p2%d:%p3%dm",
        ["setrgbb"] = E + "[48:2:%p1%d:%p2%d:%p3%dm",

        // ---- Extensions this emulator implements ------------------------------------------------
        ["Smulx"] = E + "[4:%p1%dm",
        ["Setulc"] = E + "[58:2::%p1%{65536}%/%d:%p1%{256}%/%{255}%&%d:%p1%{255}%&%dm",
        ["Ms"] = E + "]52;%p1%s;%p2%s" + Bel,

        // ---- Cursor movement --------------------------------------------------------------------
        ["cup"] = E + "[%i%p1%d;%p2%dH",
        ["cm"] = E + "[%i%p1%d;%p2%dH",
        ["home"] = E + "[H",
        ["ho"] = E + "[H",
        ["hpa"] = E + "[%i%p1%dG",
        ["ch"] = E + "[%i%p1%dG",
        ["vpa"] = E + "[%i%p1%dd",
        ["cv"] = E + "[%i%p1%dd",
        ["cuu"] = E + "[%p1%dA",
        ["UP"] = E + "[%p1%dA",
        ["cud"] = E + "[%p1%dB",
        ["DO"] = E + "[%p1%dB",
        ["cuf"] = E + "[%p1%dC",
        ["RI"] = E + "[%p1%dC",
        ["cub"] = E + "[%p1%dD",
        ["LE"] = E + "[%p1%dD",
        ["cuu1"] = E + "[A",
        ["up"] = E + "[A",
        ["cuf1"] = E + "[C",
        ["nd"] = E + "[C",
        ["cub1"] = "\b",
        ["le"] = "\b",
        ["cud1"] = "\n",
        ["do"] = "\n",
        ["cr"] = "\r",
        ["ht"] = "\t",
        ["ind"] = "\n",
        ["sf"] = "\n",
        ["ri"] = E + "M",
        ["sr"] = E + "M",
        ["sc"] = E + "7",
        ["rc"] = E + "8",
        ["csr"] = E + "[%i%p1%d;%p2%dr",
        ["cs"] = E + "[%i%p1%d;%p2%dr",

        // ---- Erasing and editing ------------------------------------------------------------------
        ["clear"] = E + "[H" + E + "[2J",
        ["cl"] = E + "[H" + E + "[2J",
        ["ed"] = E + "[J",
        ["cd"] = E + "[J",
        ["el"] = E + "[K",
        ["ce"] = E + "[K",
        ["el1"] = E + "[1K",
        ["cb"] = E + "[1K",
        ["ech"] = E + "[%p1%dX",
        ["ec"] = E + "[%p1%dX",
        ["il1"] = E + "[L",
        ["al"] = E + "[L",
        ["il"] = E + "[%p1%dL",
        ["AL"] = E + "[%p1%dL",
        ["dl1"] = E + "[M",
        // "dl" and "il" are the one place the two vocabularies collide: in terminfo they take a
        // count, in termcap they mean the single-line form that terminfo spells dl1/il1. Terminfo
        // wins, because that is the vocabulary a program querying over XTGETTCAP is speaking; the
        // termcap single-line forms are still reachable under their unambiguous names.
        ["dl"] = E + "[%p1%dM",
        ["DL"] = E + "[%p1%dM",
        ["ich1"] = E + "[@",
        ["ic"] = E + "[@",
        ["ich"] = E + "[%p1%d@",
        ["IC"] = E + "[%p1%d@",
        ["dch1"] = E + "[P",
        ["dc"] = E + "[P",
        ["dch"] = E + "[%p1%dP",
        ["DC"] = E + "[%p1%dP",
        ["rep"] = "%p1%c" + E + "[%p2%{1}%-%db",
        ["rp"] = "%p1%c" + E + "[%p2%{1}%-%db",

        // ---- Attributes ------------------------------------------------------------------------
        // sgr0 puts the character set back to ASCII as well as clearing SGR, because a program that
        // left G0 in line-drawing mode has no other string to undo it with.
        ["sgr0"] = E + "(B" + E + "[m",
        ["me"] = E + "(B" + E + "[m",
        ["bold"] = E + "[1m",
        ["md"] = E + "[1m",
        ["dim"] = E + "[2m",
        ["mh"] = E + "[2m",
        ["sitm"] = E + "[3m",
        ["ZH"] = E + "[3m",
        ["ritm"] = E + "[23m",
        ["ZR"] = E + "[23m",
        ["smul"] = E + "[4m",
        ["us"] = E + "[4m",
        ["rmul"] = E + "[24m",
        ["ue"] = E + "[24m",
        ["blink"] = E + "[5m",
        ["mb"] = E + "[5m",
        ["rev"] = E + "[7m",
        ["mr"] = E + "[7m",
        ["smso"] = E + "[7m",
        ["so"] = E + "[7m",
        ["rmso"] = E + "[27m",
        ["se"] = E + "[27m",
        ["invis"] = E + "[8m",
        ["mk"] = E + "[8m",
        ["smxx"] = E + "[9m",
        ["rmxx"] = E + "[29m",

        // ---- Screens, cursor visibility, keypad ---------------------------------------------------
        ["smcup"] = E + "[?1049h",
        ["ti"] = E + "[?1049h",
        ["rmcup"] = E + "[?1049l",
        ["te"] = E + "[?1049l",
        ["civis"] = E + "[?25l",
        ["vi"] = E + "[?25l",
        ["cnorm"] = E + "[?25h",
        ["ve"] = E + "[?25h",
        ["smkx"] = E + "[?1h" + E + "=",
        ["ks"] = E + "[?1h" + E + "=",
        ["rmkx"] = E + "[?1l" + E + ">",
        ["ke"] = E + "[?1l" + E + ">",
        ["bel"] = Bel,
        ["bl"] = Bel,

        // ---- Keys ----------------------------------------------------------------------------------
        // The cursor keys are reported in their application-mode form, which is the form terminfo asks
        // for: smkx precedes them, and that is the mode this emulator sends ESC O A in. Home and End
        // are reported as CSI, because that is what the keyboard generator emits in BOTH modes, and a
        // terminal describing itself should say what it will actually send.
        ["kcuu1"] = E + "OA",
        ["ku"] = E + "OA",
        ["kcud1"] = E + "OB",
        ["kd"] = E + "OB",
        ["kcuf1"] = E + "OC",
        ["kr"] = E + "OC",
        ["kcub1"] = E + "OD",
        ["kl"] = E + "OD",
        ["khome"] = E + "[H",
        ["kh"] = E + "[H",
        ["kend"] = E + "[F",
        ["@7"] = E + "[F",
        ["kpp"] = E + "[5~",
        ["kP"] = E + "[5~",
        ["knp"] = E + "[6~",
        ["kN"] = E + "[6~",
        ["kich1"] = E + "[2~",
        ["kI"] = E + "[2~",
        ["kdch1"] = E + "[3~",
        ["kD"] = E + "[3~",
        ["kbs"] = Del,
        ["kb"] = Del,
        ["kcbt"] = E + "[Z",
        ["kB"] = E + "[Z",
        ["kf1"] = E + "OP",
        ["k1"] = E + "OP",
        ["kf2"] = E + "OQ",
        ["k2"] = E + "OQ",
        ["kf3"] = E + "OR",
        ["k3"] = E + "OR",
        ["kf4"] = E + "OS",
        ["k4"] = E + "OS",
        ["kf5"] = E + "[15~",
        ["k5"] = E + "[15~",
        ["kf6"] = E + "[17~",
        ["k6"] = E + "[17~",
        ["kf7"] = E + "[18~",
        ["k7"] = E + "[18~",
        ["kf8"] = E + "[19~",
        ["k8"] = E + "[19~",
        ["kf9"] = E + "[20~",
        ["k9"] = E + "[20~",
        ["kf10"] = E + "[21~",
        ["k;"] = E + "[21~",
        ["kf11"] = E + "[23~",
        ["F1"] = E + "[23~",
        ["kf12"] = E + "[24~",
        ["F2"] = E + "[24~"
    };

    /// <summary>
    /// Answers an XTGETTCAP request, as one reply per capability that was asked about.
    /// </summary>
    /// <param name="request">
    /// The payload of a <c>DCS + q</c>: capability names hex-encoded two digits per character and
    /// separated by semicolons.
    /// </param>
    /// <param name="terminal">The terminal being asked.</param>
    /// <returns>
    /// The replies to write back, each a complete DCS. A reply per name rather than one reply
    /// listing them all: xterm's grammar allows either, and every name having its own reply is what
    /// lets a client pair an answer with its question even when only some of them were understood.
    /// </returns>
    public static List<string> Answer(string request, Terminal terminal)
    {
        var replies = new List<string>();

        // A request with no names in it is malformed rather than empty-but-fine, so it gets the
        // failure reply. Staying silent would leave a client that expects an answer waiting for one.
        if (request.Length == 0)
        {
            replies.Add(Reply(valid: false, encodedName: string.Empty, value: null));
            return replies;
        }

        foreach (var encoded in request.Split(';'))
        {
            // The name is echoed back exactly as it arrived, not re-encoded, so a client can match
            // the reply against the bytes it sent without knowing which hex case we would pick.
            if (!TryDecodeHex(encoded, out var name) || !TryGet(name, terminal, out var value))
            {
                replies.Add(Reply(valid: false, encodedName: encoded, value: null));
                continue;
            }

            replies.Add(Reply(valid: true, encodedName: encoded, value: value));
        }

        return replies;
    }

    /// <summary>
    /// Builds one reply: <c>DCS 1 + r name=value ST</c> for a capability we have,
    /// <c>DCS 0 + r name ST</c> for one we do not.
    /// </summary>
    private static string Reply(bool valid, string encodedName, string? value)
    {
        var body = valid ? encodedName + "=" + EncodeHex(value!) : encodedName;
        return E + "P" + (valid ? "1" : "0") + "+r" + body + E + "\\";
    }

    /// <summary>
    /// Decodes a hex-encoded capability name.
    /// </summary>
    /// <remarks>
    /// Rejects an odd number of digits and anything that is not one, because a half-decoded name
    /// would be looked up as some other capability and answered confidently with the wrong string.
    /// </remarks>
    private static bool TryDecodeHex(string encoded, out string decoded)
    {
        decoded = string.Empty;
        if (encoded.Length == 0 || encoded.Length % 2 != 0)
            return false;

        var chars = new char[encoded.Length / 2];
        for (var i = 0; i < chars.Length; i++)
        {
            var high = HexDigit(encoded[i * 2]);
            var low = HexDigit(encoded[i * 2 + 1]);
            if (high < 0 || low < 0)
                return false;

            chars[i] = (char)((high << 4) | low);
        }

        decoded = new string(chars);
        return true;
    }

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };

    /// <summary>
    /// Hex-encodes a capability value, two digits per byte.
    /// </summary>
    /// <remarks>
    /// A value is a terminfo string and so is bytes, not text: it holds ESC, BEL and DEL, and the
    /// point of the encoding is that those survive a transport that would otherwise act on them. The
    /// bytes are the UTF-8 ones, because the reader is decoding two digits at a time and a character
    /// above U+00FF would otherwise arrive as four digits and be read as two characters. Every value
    /// in the table is ASCII, where the two encodings agree; the one that need not be is
    /// <c>TermName</c>, which the host sets and so is not ours to assume anything about.
    /// </remarks>
    private static string EncodeHex(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var builder = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            builder.Append(b.ToString("X2"));

        return builder.ToString();
    }

    /// <summary>
    /// Looks up one capability by its termcap or terminfo name.
    /// </summary>
    /// <param name="name">The decoded capability name, e.g. <c>Co</c> or <c>colors</c>.</param>
    /// <param name="terminal">The terminal being asked, for the capabilities that depend on it.</param>
    /// <param name="value">
    /// The capability's value on success. Empty for a boolean capability, which is a real answer and
    /// not a missing one — the caller tells the two apart by this method's return value.
    /// </param>
    /// <returns>True when the capability is one this terminal has.</returns>
    public static bool TryGet(string name, Terminal terminal, out string value)
    {
        switch (name)
        {
            // The name is what the terminal was configured to call itself. It is reported rather
            // than derived so a host that sets TERM to something specific gets the same answer back.
            case "TN" or "name":
                value = terminal.Options.TermName;
                return true;

            case "Co" or "colors":
                value = "256";
                return true;

            // The size is asked for as a fallback when the caller has no ioctl to ask instead, so
            // the useful answer is the CURRENT size, not the one the terminal started at.
            case "co" or "cols":
                value = terminal.Cols.ToString();
                return true;

            case "li" or "lines":
                value = terminal.Rows.ToString();
                return true;

            // Sixel is optional, and claiming it while it is switched off would send a program down
            // a path that puts nothing on the screen.
            case "Su":
                value = string.Empty;
                return terminal.Options.SixelEnabled;

            default:
                if (Static.TryGetValue(name, out var found))
                {
                    value = found;
                    return true;
                }

                value = string.Empty;
                return false;
        }
    }
}
