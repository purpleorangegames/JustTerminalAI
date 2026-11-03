using System;
using System.Collections.Generic;
using System.Linq;
using JustTerminal;
using UnityEngine;
using Verse;
using Screen = JustTerminal.Screen;

namespace JustTerminalAI;

// things the player told the ai to remember, one a line in ~/ai.memory, so they're in the save and export with it
public static class Memory
{
    public const string File = "~/ai.memory";
    public const int Max = 50;
    public const int MaxLength = 200;

    public static List<string> Lines()
    {
        FileNode f = Files.Root != null ? Files.Find(null, File) : null;
        return f == null || f.folder ? new List<string>() : f.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    }

    private static string Save(List<string> lines) => Files.WriteText(File, string.Join("\n", lines), false);

    // null when it was kept, the reason when it wasn't
    public static string Add(string text)
    {
        text = (text ?? "").Replace('\n', ' ').Trim();
        if (Files.Root == null)
        {
            return "memories need a game running";
        }
        if (text.Length == 0)
        {
            return "nothing to remember";
        }
        if (text.Length > MaxLength)
        {
            return $"a memory is {MaxLength} letters at most";
        }
        List<string> lines = Lines();
        if (lines.Any(l => l.Equals(text, StringComparison.OrdinalIgnoreCase)))
        {
            return "that's remembered already";
        }
        if (lines.Count >= Max)
        {
            return $"there are {Max} memories already, ai memory removes some";
        }
        lines.Add(text);
        return Save(lines);
    }

    public static string Remove(int index)
    {
        List<string> lines = Lines();
        if (index < 0 || index >= lines.Count)
        {
            return "no memory there";
        }
        lines.RemoveAt(index);
        return Save(lines);
    }

    // ai remember <text>
    public static void Remember(Shell sh, string text)
    {
        string bad = Add(text);
        sh.Print(bad ?? "remembered, " + Lines().Count + " of " + Max, bad == null ? Ink.Good : Ink.Bad);
    }
}

// ai memory: the memories as a list, d removes one, e edits the file in nano
public class MemoryApp : App
{
    private int selected;
    private int listTop;
    private List<string> lines = new List<string>();
    private string message;
    private readonly List<(int y, int index)> rowHits = new List<(int, int)>();

    public MemoryApp()
    {
        lines = Memory.Lines();
    }

    public override void Back()
    {
        lines = Memory.Lines();
        selected = Mathf.Clamp(selected, 0, Math.Max(0, lines.Count - 1));
        message = null;
    }

    public override bool HandleKey(Event e)
    {
        switch (e.keyCode)
        {
            case KeyCode.Escape:
            case KeyCode.Q:
                Done = true;
                return true;
            case KeyCode.UpArrow:
                Select(selected - 1);
                return true;
            case KeyCode.DownArrow:
                Select(selected + 1);
                return true;
            case KeyCode.Home:
                Select(0);
                return true;
            case KeyCode.End:
                Select(lines.Count - 1);
                return true;
            case KeyCode.D:
            case KeyCode.Delete:
            case KeyCode.Backspace:
                Delete();
                return true;
            case KeyCode.E:
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                Edit();
                return true;
        }
        return false;
    }

    private void Select(int index) => selected = Mathf.Clamp(index, 0, Math.Max(0, lines.Count - 1));

    private void Delete()
    {
        if (lines.Count == 0)
        {
            return;
        }
        string gone = lines[selected];
        string bad = Memory.Remove(selected);
        lines = Memory.Lines();
        Select(selected);
        message = bad ?? "removed: " + gone;
    }

    private void Edit()
    {
        FileNode f = Files.Root != null ? Files.Find(null, Memory.File) : null;
        if (f == null)
        {
            message = "no memories yet, ai remember adds one";
            return;
        }
        Push = new NanoApp(f.parent, f.name, f);
    }

    public override void Scroll(int rows) => Select(selected - Math.Sign(rows));

    public override void Click(int x, int y)
    {
        foreach ((int ry, int index) in rowHits)
        {
            if (ry == y)
            {
                Select(index);
                return;
            }
        }
    }

    public override void Draw(Screen s)
    {
        if (Files.Root == null)
        {
            s.Write(1, 1, "no game running, q quits", Ink.Bad);
            return;
        }
        rowHits.Clear();
        int w = s.Cols, h = s.Rows;
        for (int i = 0; i < w; i++)
        {
            s.Put(i, 0, ' ', Ink.Screen, Ink.Normal);
        }
        int hx = s.Write(1, 0, "ai memory", Ink.Screen, Ink.Normal);
        s.Write(hx + 2, 0, $"{lines.Count} of {Memory.Max}, in {Memory.File}", Ink.Screen, Ink.Normal);

        int rows = Math.Max(1, h - 3);
        if (lines.Count == 0)
        {
            s.Write(2, 2, "nothing remembered yet", Ink.Bright);
            s.Write(2, 3, "q and then ai remember <something> adds one, the ai reads them before each answer", Ink.Dim);
        }
        if (selected < listTop)
        {
            listTop = selected;
        }
        if (selected >= listTop + rows)
        {
            listTop = selected - rows + 1;
        }
        for (int i = 0; i < rows && listTop + i < lines.Count; i++)
        {
            int ry = 1 + i;
            int index = listTop + i;
            rowHits.Add((ry, index));
            bool sel = index == selected;
            if (sel)
            {
                for (int cx = 0; cx < w; cx++)
                {
                    s.Put(cx, ry, ' ', Ink.Normal, Ink.Dim);
                }
                s.Put(0, ry, '>', Ink.Screen, Ink.Dim);
            }
            int nx = s.Write(2, ry, (index + 1).ToString().PadLeft(2), sel ? Ink.Screen : Ink.Dim, sel ? Ink.Dim : Ink.None);
            s.Write(nx + 2, ry, App.Clip(lines[index], w - nx - 3), sel ? Ink.Screen : Ink.Normal, sel ? Ink.Dim : Ink.None);
        }
        if (message != null)
        {
            s.Write(1, h - 2, App.Clip(message, w - 2), Ink.Warn);
        }
        int kx = 1;
        foreach ((string key, string label) in new[] { ("↑↓", "pick"), ("d", "remove"), ("e", "edit"), ("q", "quit") })
        {
            kx = s.Write(kx, h - 1, key, Ink.Bright);
            kx = s.Write(kx + 1, h - 1, label, Ink.Dim) + 2;
        }
    }
}
