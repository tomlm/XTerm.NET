"""Runs vttest against XTerm.NET and against tmux, and reports where the two screens differ.

    python vtsweep.py <menu> <returns>        walk a menu that is a sequence of screens
    python vtsweep.py --keys <key> [<key>...] drive an explicit script, for sub-menus

A key is what to send before capturing: "6\\r" sends 6 and RETURN, "-" sends nothing and captures
again. Sub-menus (3, 8, 9, 11 and everything under 11) list numbered tests, and RETURN there only
redraws the list, so those need --keys.

Screens are matched by CONTENT, not by step number. The two terminals take different numbers of
screens through a menu -- tmux drops out of tests it cannot render -- so screen 3 on one side is
screen 4 on the other and every row after that reads as a difference. Matching on the first line
carrying words survives that: an extra screen becomes one reported omission instead.

Both sides advance on screen STABILITY rather than a fixed delay, because vttest paints some screens
in stages and a fixed wait captures one terminal mid-paint.

WHAT THIS TOOL IS AND IS NOT

It nominates; it does not judge. vttest's own verdict text ("-- OK", "-- Ignores origin mode",
"expected EAED") is what decides which side is wrong, and several differences found this way were
tmux's bugs rather than XTerm.NET's.

tmux is a reference, not an oracle, and it is blind in ways worth knowing before believing a diff:

  * no VT52 support at all -- menu 7 is unusable, tmux prints the raw payloads
  * DECCOLM ignored -- it never resizes, so 132-column content differences are expected
  * capture-pane reports the UNTRANSLATED characters for line-drawing and national sets, so menu 3
    always differs even when both terminals are right
  * no DECRQM, no DECRQCRA

And it cannot see attributes at all -- colour, BCE fills, protection, line attributes. Those need a
direct probe against the emulator, which is what the tests in XTerm.NET.Tests/VtTestBehaviourTests
were written from.

REQUIREMENTS

vttest and tmux inside WSL (`apt install vttest tmux`), and `dotnet` on PATH. Build VtDrive first:

    dotnet build VtDrive/VtDrive.csproj
"""
import os
import re
import subprocess
import sys
import time

VTDRIVE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "VtDrive")
WORDS = re.compile(r"[A-Za-z]{3,}")


def xterm_net(keys):
    """Screens as XTerm.NET painted them, one list of rows per step."""
    out = subprocess.run(
        ["dotnet", "run", "-c", "Debug", "--no-build", "--", *keys],
        cwd=VTDRIVE, capture_output=True, text=True, timeout=900).stdout

    screens, current = [], None
    for line in out.splitlines():
        if line.startswith("##########"):
            current = []
            screens.append(current)
        elif current is not None and re.match(r"^\s*\d+\|", line):
            current.append(line.split("|", 1)[1].rstrip())
    return screens


def wsl(command):
    return subprocess.run(["wsl", "-e", "bash", "-lc", command],
                          capture_output=True, text=True, timeout=120).stdout


def tmux_stable():
    """Capture once tmux has stopped repainting, matching the C# side's rule."""
    previous, stable_for, waited = None, 0.0, 0.0
    while waited < 8 and stable_for < 0.6:
        time.sleep(0.2)
        waited += 0.2
        now = [l.rstrip() for l in wsl("tmux capture-pane -p -t vt").splitlines()]
        stable_for = stable_for + 0.2 if now == previous else 0.0
        previous = now
    return previous or []


def tmux(keys):
    wsl("tmux kill-session -t vt 2>/dev/null; tmux new-session -d -s vt -x 80 -y 24 vttest")
    screens = []
    for key in keys:
        if key != "-":
            literal = key.replace("\\r", "")
            if literal:
                wsl(f"tmux send-keys -t vt '{literal}'")
            if "\\r" in key:
                wsl("tmux send-keys -t vt Enter")
        screens.append(tmux_stable())
    wsl("tmux kill-session -t vt 2>/dev/null")
    return screens


def label(screen):
    """A screen's name: the first line carrying real words, which is what vttest titles with."""
    for line in screen:
        if WORDS.search(line):
            return line.strip()[:60]
    return "(no text)"


def labelled(screens):
    seen, out = {}, {}
    for screen in screens:
        key = label(screen)
        seen[key] = seen.get(key, 0) + 1
        out[f"{key} #{seen[key]}"] = screen
    return out


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2

    if sys.argv[1] == "--keys":
        keys = sys.argv[2:]
    else:
        menu, returns = sys.argv[1], int(sys.argv[2])
        keys = ["-", f"{menu}\\r"] + ["\\r"] * returns

    mine = labelled(xterm_net(keys))
    theirs = labelled(tmux(keys))

    print("===== " + " ".join(k.replace("\\r", "<CR>") for k in keys) + " =====")
    print(f"XTerm.NET painted {len(mine)} distinct screen(s), tmux {len(theirs)}")
    print()

    for key in mine:
        if key not in theirs:
            print(f"  ONLY IN XTerm.NET: {key}")
    for key in theirs:
        if key not in mine:
            print(f"  ONLY IN tmux     : {key}")
    print()

    for key, a in mine.items():
        b = theirs.get(key)
        if b is None:
            continue

        rows = max(len(a), len(b))
        diffs = [(r, a[r] if r < len(a) else "", b[r] if r < len(b) else "")
                 for r in range(rows)
                 if (a[r] if r < len(a) else "").rstrip() != (b[r] if r < len(b) else "").rstrip()]

        if not diffs:
            print(f"  [same] {key}")
            continue

        print(f"  [DIFF] {key}: {len(diffs)} row(s)")
        for r, mine_row, their_row in diffs[:8]:
            print(f"      row {r:2}  XTerm.NET |{mine_row[:76]}")
            print(f"              tmux      |{their_row[:76]}")
        if len(diffs) > 8:
            print(f"      ... {len(diffs) - 8} more")

    return 0


if __name__ == "__main__":
    sys.exit(main())
