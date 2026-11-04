using System.Collections.Generic;
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
- Anything that changes the game, files or settings is a change, like schedule, draft, research <project>, hunt, rm, mkdir or cron on. Changes already run as in edit mode, never type edit.
- Do several reads or several changes at once when you can, like all the manual parts you need in one go.
- Positions are x,z on the map, x goes right and z goes up. Saved places are in /places, place save keeps one and goto sends colonists there, goto all gate or goto @group gate sends many in one line. pick waits for the player to click the map, only the player can use it, tell them to when you need a spot.
- /examples has working cron scripts and programs that come with the mod, ls /examples lists them and cat shows one. /bin has the programs the commands are made of. Look at a close example before writing your own.
- Don't explain what you're going to do, do it. When it's done, answer in one or two short lines of plain words.
- If the commands can't do it, say so in one line.
- When the player asks you to remember something, keep it with remember, short.";

    private const string Lines =
@"

You have no tools, you answer with lines that start with one of these instead:
look: <command>   reads, you get back what it printed
run: <command>    a change, the player may be asked first
say: <text>       tells the player something
write: <path>     writes a file in ~, the lines after it are the whole file, then a line with only end
remember: <text>  keeps a line for every later answer, only when the player asks you to remember something

For example:
look: manual cron scripts
run: schedule Tasca nightshift
write: ~/lowfood.cron
every 2h
fire daily
if /stock/food < 3
action letter only {value} days of food left
end
run: cron lowfood on
say: Tasca works nights now, and you get a letter when food is low.

";

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
        List<string> memories = Memory.Lines();
        if (memories.Count > 0)
        {
            b.Append("\nWhat the player told you to remember:\n");
            foreach (string m in memories)
            {
                b.Append("- ").Append(m).Append('\n');
            }
        }
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
