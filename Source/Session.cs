using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using JustTerminal;
using RimWorld;
using UnityEngine;
using Verse;
using Screen = JustTerminal.Screen;

namespace JustTerminalAI;

// one ai line: the model reads with read commands, asks to run edit commands, says what it found
public class Session
{
    // the talk so far: one ai line has its own, a session keeps it until it's left
    private readonly List<JsonObject> talk = new List<JsonObject>();
    // where read commands run, nobody sees it
    private static Shell looker;
    private const int MaxTalk = 60;
    private const int MaxOutput = 6000;

    // these have their own screen, open windows or would loop
    private static readonly HashSet<string> blocked = new HashSet<string>(Scripts.Blocked.Concat(new[] { "ai", "watch", "snake", "sl", "less", "top", "rimtop", "nano", "cron_edit" }), StringComparer.OrdinalIgnoreCase);
    // these change files or settings, so they go through change and the player sees them
    private static readonly HashSet<string> changing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rm", "mv", "mkdir", "cp", "alias", "unalias", "color", "font", "example" };

    private readonly Shell sh;
    private int steps;
    // a at the question: yes to everything for the rest of this line or session
    private bool yesToAll;
    // a session ends when another game is loaded
    private Game game;

    private Session(Shell sh)
    {
        this.sh = sh;
        game = Current.Game;
    }

    private static readonly string[] header =
    {
        " ███   ███ ",
        "█   █   █  ",
        "█████   █  ",
        "█   █   █  ",
        "█   █  ███ "
    };

    public static void Command(Shell sh, string[] args)
    {
        string what = string.Join(" ", args).Trim();
        if (what.Length == 0)
        {
            sh.Print("usage: ai <what you want>, or ai session to talk to it", Ink.Dim);
            return;
        }
        if (Current.ProgramState != ProgramState.Playing)
        {
            sh.Print("ai needs a game running", Ink.Bad);
            return;
        }
        string first = args[0].ToLowerInvariant();
        if (first == "remember")
        {
            Memory.Remember(sh, string.Join(" ", args.Skip(1)));
            return;
        }
        if (first == "ask" && args.Length == 2 && args[1].ToLowerInvariant() is "on" or "off")
        {
            AiMod.Settings.askFirst = args[1].ToLowerInvariant() == "on";
            AiMod.Instance.WriteSettings();
            sh.Print(AiMod.Settings.askFirst ? "ai asks before changing things" : "ai changes things without asking now, ai ask on asks again", AiMod.Settings.askFirst ? Ink.Good : Ink.Warn);
            return;
        }
        if (first == "memory" && args.Length == 1)
        {
            sh.App = new MemoryApp();
            return;
        }
        if (what.Equals("session", StringComparison.OrdinalIgnoreCase))
        {
            Session session = new Session(sh);
            sh.Clear();
            sh.Print();
            foreach (string line in header)
            {
                sh.Print(" " + line, Ink.Bad);
            }
            sh.Print();
            sh.Print("talk to it, it remembers until you leave. exit or ctrl+c leaves", Ink.Dim);
            if (!AiMod.Settings.askFirst)
            {
                sh.Print("it runs changes without asking, ai ask on turns that off", Ink.Warn);
            }
            sh.Mode = new ShellMode
            {
                prompt = "ai> ",
                red = true,
                line = session.Line,
                leave = () => sh.Print("left the ai session", Ink.Dim)
            };
            return;
        }
        // one line, a talk of its own
        new Session(sh).Ask(what);
    }

    private void Line(string text)
    {
        if (Current.Game != game)
        {
            sh.Print("another game was loaded, the session ended", Ink.Warn);
            sh.LeaveMode();
            return;
        }
        Ask(text);
    }

    private void Ask(string what)
    {
        steps = 0;
        Add(Message("user", what));
        Send();
    }

    private static JsonObject Message(string role, string content) => new JsonObject { ["role"] = role, ["content"] = content };

    private void Add(JsonObject m)
    {
        talk.Add(m);
        if (talk.Count > MaxTalk)
        {
            // starts on a player line so tool answers aren't left without their call
            int cut = talk.Count - MaxTalk;
            while (cut < talk.Count && !((string)talk[cut]["role"] == "user" && talk[cut]["tool_call_id"] == null))
            {
                cut++;
            }
            talk.RemoveRange(0, cut);
        }
    }

    private void Send()
    {
        if (++steps > AiMod.Settings.maxSteps)
        {
            sh.Print($"ai stopped after {AiMod.Settings.maxSteps} steps, ask again in fewer words or raise the steps in the mod options", Ink.Warn);
            return;
        }
        List<JsonObject> messages = new List<JsonObject> { Message("system", Prompt.Build()) };
        messages.AddRange(talk);
        CancellationTokenSource stop = new CancellationTokenSource();
        sh.App = new Thinking(sh, this, Client.Chat(messages, stop.Token), stop);
    }

    // one thing the model wants: read, change or write, from a tool call or a text line
    private class Item
    {
        public string kind;
        public string text;
        public string body;
        // the tool call it answers, null for text lines
        public string id;
        public string result;
    }

    private static string Arg(JsonNode args, string name)
    {
        try
        {
            return ((string)args?[name] ?? "").Trim();
        }
        catch (InvalidOperationException)
        {
            return args?[name]?.ToJsonString() ?? "";
        }
    }

    private static List<Item> FromCalls(JsonArray calls)
    {
        List<Item> items = new List<Item>();
        foreach (JsonNode call in calls)
        {
            string id = (string)call?["id"] ?? "";
            string name = (string)call?["function"]?["name"] ?? "";
            JsonNode args;
            try
            {
                args = JsonNode.Parse((string)call?["function"]?["arguments"] ?? "{}");
            }
            catch (Exception)
            {
                args = null;
            }
            Item item = name switch
            {
                "read" => new Item { kind = "read", text = Arg(args, "command") },
                "change" => new Item { kind = "change", text = Arg(args, "command") },
                "write_file" => new Item { kind = "write", text = Arg(args, "path"), body = Arg(args, "text") },
                "remember" => new Item { kind = "remember", text = Arg(args, "text") },
                _ => new Item { kind = "bad", text = name, result = "there's no tool called " + name + ", the tools are read, change, write_file and remember" }
            };
            item.id = id;
            if (item.kind != "bad" && args == null)
            {
                item.kind = "bad";
                item.result = "the arguments weren't json";
            }
            items.Add(item);
        }
        return items;
    }

    // for servers without tools: look:, run:, write: lines, the rest is said to the player
    private static List<Item> FromText(string reply, List<string> said)
    {
        List<Item> items = new List<Item>();
        string[] lines = reply.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("```") || line.Length == 0)
            {
                continue;
            }
            line = line.TrimStart('-', '*', ' ').Trim('`').Trim();
            int colon = line.IndexOf(':');
            string kind = colon > 0 ? line.Substring(0, colon).Trim().ToLowerInvariant() : "";
            string rest = colon > 0 ? line.Substring(colon + 1).Trim().Trim('`').Trim() : line;
            // some models put the write after run:
            if (kind == "run" && rest.StartsWith("write ", StringComparison.OrdinalIgnoreCase))
            {
                kind = "write";
                rest = rest.Substring(6).Trim();
            }
            if (kind == "write")
            {
                // the file is the lines after it, until end
                StringBuilder body = new StringBuilder();
                for (i++; i < lines.Length && lines[i].Trim() != "end"; i++)
                {
                    if (!lines[i].TrimStart().StartsWith("```"))
                    {
                        body.Append(lines[i]).Append('\n');
                    }
                }
                items.Add(new Item { kind = "write", text = rest, body = body.ToString().TrimEnd('\n') });
            }
            else if (kind is "look" or "run" or "remember")
            {
                if (rest.Length > 0)
                {
                    items.Add(new Item { kind = kind == "look" ? "read" : kind == "run" ? "change" : "remember", text = rest });
                }
            }
            // a lone end is left over from a write block
            else if (line != "end")
            {
                said.Add(kind == "say" ? rest : line);
            }
        }
        return items;
    }

    // the answer came, on the game's thread
    private void Answer(JsonObject reply)
    {
        Add(reply);
        string content = (string)reply["content"] ?? "";
        List<Item> items;
        List<string> said = new List<string>();
        if (reply["tool_calls"] is JsonArray calls)
        {
            items = FromCalls(calls);
            said.AddRange(content.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0));
        }
        else
        {
            items = AiMod.Settings.tools ? new List<Item>() : FromText(content, said);
            if (AiMod.Settings.tools)
            {
                said.AddRange(content.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0));
            }
        }
        foreach (string line in said)
        {
            sh.Print(line);
        }
        if (items.Count == 0)
        {
            return;
        }
        foreach (Item read in items.Where(x => x.kind == "read"))
        {
            read.result = Read(read.text);
        }
        // scripts and programs are checked before the player is asked
        foreach (Item write in items.Where(x => x.kind == "write"))
        {
            write.result = CheckWrite(write.text, write.body);
        }
        foreach (Item change in items.Where(x => x.kind == "change"))
        {
            change.result = Blocked(change.text);
        }
        List<Item> waiting = items.Where(x => x.kind is "change" or "write" or "remember" && x.result == null).ToList();
        if (waiting.Count == 0)
        {
            Reply(items);
            return;
        }
        if (!AiMod.Settings.askFirst || yesToAll)
        {
            Do(items, waiting);
            return;
        }
        sh.Print("ai wants to:", Ink.Warn);
        foreach (Item c in waiting)
        {
            sh.Print("  " + (c.kind == "write" ? $"write {c.text} ({c.body.Split('\n').Length} lines)" : c.kind == "remember" ? "remember: " + c.text : c.text), Ink.Bright);
        }
        sh.AskLine("run them? y/n, a for yes to all", answer =>
        {
            answer = answer.Trim().ToLowerInvariant();
            if (answer is "a" or "all")
            {
                yesToAll = true;
            }
            if (answer is "y" or "yes" or "a" or "all")
            {
                Do(items, waiting);
                return;
            }
            sh.Print("left as it was", Ink.Dim);
            string no = "The player said no, nothing was done." + (answer.Length > 1 && answer != "no" ? " They said: " + answer : "");
            foreach (Item c in waiting)
            {
                c.result = no;
            }
            // every call gets its answer, the talk goes on at the next ai line
            Reply(items, false);
        });
    }

    private void Do(List<Item> items, List<Item> waiting)
    {
        foreach (Item c in waiting)
        {
            c.result = c.kind == "write" ? Write(c.text, c.body) : c.kind == "remember" ? Remember(c.text) : Run(c.text);
        }
        Reply(items);
    }

    // what each thing gave back, as tool answers or one text message
    private void Reply(List<Item> items, bool send = true)
    {
        if (items.Any(x => x.id != null))
        {
            foreach (Item i in items)
            {
                Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = i.id, ["content"] = i.result ?? "" });
            }
        }
        else
        {
            StringBuilder b = new StringBuilder();
            foreach (Item i in items)
            {
                b.Append(i.kind == "read" ? "look: " : i.kind == "write" ? "write: " : i.kind == "remember" ? "remember: " : "run: ").Append(i.text).Append('\n').Append(i.result).Append("\n\n");
            }
            Add(Message("user", b.ToString().TrimEnd()));
        }
        if (send)
        {
            Send();
        }
    }

    private static string Blocked(string line)
    {
        string first = line.Split(' ')[0].Trim();
        if (first.Length == 0)
        {
            return "the command was empty";
        }
        return blocked.Contains(first) ? first + " has its own screen or opens a window, ai can't use it" : null;
    }

    // a read command in the hidden shell, the player sees only the line
    private string Read(string line)
    {
        sh.Out("ai", Ink.Accent);
        sh.Out("$ ", Ink.Dim);
        sh.Print(line, Ink.Dim);
        if (Blocked(line) is string bad)
        {
            return bad;
        }
        string[] words = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (changing.Contains(words[0]) || words[0].Equals("cron", StringComparison.OrdinalIgnoreCase) && words.Length > 2 && words[2] != "test" || words[0].Equals("place", StringComparison.OrdinalIgnoreCase) && words.Length > 1)
        {
            return words[0] + " changes things, use change for it";
        }
        // the player's history, not the hidden shell's
        if (words[0].Equals("history", StringComparison.OrdinalIgnoreCase))
        {
            return sh.History.Count == 0 ? "(nothing typed yet)" : string.Join("\n", sh.History);
        }
        if (looker == null)
        {
            looker = new Shell { CanAsk = false };
        }
        looker.Clear();
        looker.Cols = 100;
        looker.App = null;
        long mark = looker.Printed;
        looker.Execute(line);
        looker.App = null;
        string said = string.Join("\n", looker.TextSince(mark));
        return Cut(said.Length == 0 ? "(nothing printed)" : said);
    }

    // an edit command in the player's shell, like typed in edit mode
    private string Run(string line)
    {
        sh.Out("ai", Ink.Accent);
        sh.Out("# ", Ink.Dim);
        sh.Print(line, Ink.Bright);
        bool edit = sh.EditMode;
        bool ask = sh.CanAsk;
        sh.EditMode = true;
        sh.CanAsk = false;
        long mark = sh.Printed;
        try
        {
            sh.Execute(line);
        }
        finally
        {
            sh.EditMode = edit;
            sh.CanAsk = ask;
        }
        string said = string.Join("\n", sh.TextSince(mark));
        return Cut(said.Length == 0 ? "(nothing printed)" : said);
    }

    // null when it can be written, the reason when it can't
    private static string CheckWrite(string path, string text)
    {
        if (!path.StartsWith("~"))
        {
            return "files go in ~, like ~/lowfood.cron";
        }
        if (path.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) && Programs.Check(text) is string bad)
        {
            return "not written, the program has a problem: " + bad;
        }
        if (path.EndsWith(".cron", StringComparison.OrdinalIgnoreCase))
        {
            Script script = Scripts.Parse(new FileNode { name = path.Substring(path.LastIndexOf('/') + 1), text = text });
            if (script.bad != null)
            {
                return "not written, the script has a problem: " + script.bad;
            }
        }
        return null;
    }

    private string Write(string path, string text)
    {
        sh.Out("ai", Ink.Accent);
        sh.Out("# ", Ink.Dim);
        sh.Print("write " + path, Ink.Bright);
        // folders on the way are made too, like ~/bin for the first program
        string[] parts = path.Split('/');
        for (int i = 1; i < parts.Length - 1; i++)
        {
            string dir = string.Join("/", parts, 0, i + 1);
            if (Files.Find(null, dir) == null)
            {
                Shell quiet = new Shell { CanAsk = false };
                quiet.Execute("mkdir " + dir);
            }
        }
        string bad = Files.WriteText(path, text, false);
        sh.Print(bad ?? path, bad == null ? Ink.Good : Ink.Bad);
        return bad ?? "written" + (path.EndsWith(".cron", StringComparison.OrdinalIgnoreCase) ? ", it's off until cron <name> on" : "");
    }

    private string Remember(string text)
    {
        sh.Out("ai", Ink.Accent);
        sh.Out("# ", Ink.Dim);
        sh.Print("remember " + text, Ink.Bright);
        string bad = Memory.Add(text);
        if (bad != null)
        {
            sh.Print(bad, Ink.Bad);
        }
        return bad ?? "remembered";
    }

    private static string Cut(string text) => text.Length > MaxOutput ? text.Substring(0, MaxOutput) + "\n...(cut)" : text;

    // shows the terminal with a line saying it's waiting, esc stops
    private class Thinking : App
    {
        private readonly Shell sh;
        private readonly Session session;
        private readonly Task<JsonObject> task;
        private readonly CancellationTokenSource stop;
        private readonly float start = Time.realtimeSinceStartup;

        public Thinking(Shell sh, Session session, Task<JsonObject> task, CancellationTokenSource stop)
        {
            this.sh = sh;
            this.session = session;
            this.task = task;
            this.stop = stop;
        }

        public override float RedrawEvery => 0.2f;

        public override bool HandleKey(Event e)
        {
            if (e.keyCode == KeyCode.Escape || e.control && e.keyCode == KeyCode.C)
            {
                stop.Cancel();
                Done = true;
                sh.Print("ai stopped", Ink.Dim);
            }
            // the rest wait, typing would go nowhere
            return true;
        }

        public override void Draw(Screen s)
        {
            if (!Done && task.IsCompleted)
            {
                Done = true;
                if (task.IsFaulted || task.IsCanceled)
                {
                    sh.Print("ai: " + (task.IsFaulted ? Client.Reason(task.Exception) : "stopped"), Ink.Bad);
                }
                else
                {
                    try
                    {
                        session.Answer(task.Result);
                    }
                    catch (Exception e)
                    {
                        sh.Print("ai: " + e.Message, Ink.Bad);
                        Log.Warning("[JustTerminalAI] " + e);
                    }
                }
            }
            // the terminal as it is, with the wait on the last line
            App me = sh.App;
            sh.App = null;
            sh.Render(s, false);
            sh.App = me;
            if (Done)
            {
                return;
            }
            int y = s.Rows - 1;
            for (int x = 0; x < s.Cols; x++)
            {
                s.Put(x, y, ' ', Ink.Normal);
            }
            string dots = new string('.', 1 + (int)(Time.realtimeSinceStartup * 2f) % 3).PadRight(3);
            s.Write(0, y, $"ai thinking{dots} {(int)(Time.realtimeSinceStartup - start)}s", Ink.Accent);
            string keys = "esc stops";
            s.Write(s.Cols - keys.Length - 1, y, keys, Ink.Dim);
        }
    }
}
