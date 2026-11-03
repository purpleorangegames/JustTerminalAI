using System.Linq;
using System.Text;
using JustTerminal;
using RimWorld;
using Verse;
using Command = JustTerminal.Command;

namespace JustTerminalAI;

// what the model is told before the talk: how to work, the commands in one line each, the manual's parts and the colony
public static class Prompt
{
    private const string Rules =
@"You work the terminal of JustTerminal, a mod for RimWorld. The player writes what they want in plain words and you do it with the terminal's commands.

How to work:
- Read first when you aren't sure of names, numbers or what's there. Names with spaces use _ in commands.
- help <command> shows a command's examples. manual <part> shows that part of the manual, read it before writing a cron script or a program.
- Anything that changes the game, files or settings is a change, like schedule, draft, rm, mkdir or cron on.
- Do several reads or several changes at once when you can.
- Don't explain what you're going to do, do it. When it's done, answer in one or two short lines of plain words.
- If the commands can't do it, say so in one line.";

    private const string Lines =
@"

Answer only with lines that start with one of these:
look: <command>   runs a command that only reads, you get back what it printed
run: <command>    runs a command that changes things, the player may be asked first
say: <text>       tells the player something
write: <path>     writes a file in ~, the lines after it are the file, up to a line with only end";

    public static string Build()
    {
        StringBuilder b = new StringBuilder(Rules);
        if (!AiMod.Settings.tools)
        {
            b.Append(Lines);
        }
        b.Append("\n\nCommands that only read:\n");
        foreach (Command c in Commands.All.Where(c => !c.hidden && c.name != "ai"))
        {
            b.Append(Line(c));
        }
        b.Append("\nCommands that change the game:\n");
        foreach (Command c in EditCommands.All.Where(c => !c.hidden))
        {
            b.Append(Line(c));
        }
        b.Append("\nThe manual's parts, for manual <part>:\n");
        b.Append(string.Join(", ", Programs.Sections().Select(s => s.title))).Append('\n');
        b.Append("\nNow:\n");
        b.Append("Colony: ").Append(Faction.OfPlayer?.Name).Append('\n');
        if (Find.CurrentMap != null)
        {
            b.Append("Date: ").Append(GenDate.DateFullStringWithHourAt(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile))).Append('\n');
        }
        b.Append("Colonists: ").Append(string.Join(", ", PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoCryptosleep.Select(p => p.LabelShort.Replace(' ', '_')))).Append('\n');
        return b.ToString();
    }

    private static string Line(Command c) => c.Shown + (c.args.Length > 0 ? " " + c.args : "") + "  - " + c.help + "\n";
}
