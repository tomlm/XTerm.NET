namespace XTerm.Common;

/// <summary>
/// Where the shell is in its prompt/command cycle, as derived from its OSC 133 marks.
/// </summary>
/// <remarks>
/// <para>What <see cref="XTerm.Terminal.ShellState"/> reports, and the question a host asks before
/// closing a terminal: <see cref="Running"/> means a command owns the terminal and closing it would
/// kill that command; anything else means the shell itself has it.</para>
/// <para>The transitions, one per mark, with the latest mark always winning:</para>
/// <list type="bullet">
/// <item><c>133;A</c> (prompt start) - <see cref="Prompt"/>.</item>
/// <item><c>133;B</c> (input start) - <see cref="Input"/>.</item>
/// <item><c>133;C</c> (command start) - <see cref="Running"/>.</item>
/// <item><c>133;D</c> (command finished) - <see cref="Prompt"/>. The command is over and the shell has
/// the terminal back; what it does next is draw a prompt. Not <see cref="Unknown"/>, because the shell
/// has just told us exactly where it is.</item>
/// <item>RIS (<c>ESC c</c>) - <see cref="Unknown"/>. A full reset discards everything the terminal knew
/// about the session, and the shell has to say again.</item>
/// </list>
/// <para>Because each mark sets the state outright, a lost <c>133;D</c> - a shell that crashed and was
/// replaced, or a command killed before it could report - cannot leave the state stuck at
/// <see cref="Running"/>: the next prompt's <c>133;A</c> puts it back. The same rule covers nesting:
/// a shell started from another shell sends its own marks, and the terminal follows whichever shell
/// spoke last. Switching to and from the alternate screen changes nothing.</para>
/// </remarks>
public enum ShellState
{
    /// <summary>
    /// No mark since the terminal was created or last reset. Shell integration is configured in the
    /// shell, so this is also the permanent state of a shell without it: "cannot say", never "idle".
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The shell is drawing its prompt (after <c>133;A</c>, or after <c>133;D</c> ended a command).
    /// </summary>
    Prompt = 1,

    /// <summary>
    /// The prompt is drawn and the shell is waiting for the user's command line (after <c>133;B</c>).
    /// </summary>
    Input = 2,

    /// <summary>
    /// A command is running and owns the terminal (after <c>133;C</c>, until the next mark).
    /// </summary>
    Running = 3,
}
