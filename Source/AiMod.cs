using System;
using System.Collections.Generic;
using JustTerminal;
using UnityEngine;
using Verse;
using Command = JustTerminal.Command;

namespace JustTerminalAI;

public class AiSettings : ModSettings
{
    // where the model answers, anything that talks like OpenAI: vLLM, llama.cpp, LM Studio, Ollama
    public string url = "http://127.0.0.1:8080/v1";
    // empty takes the first one the server lists
    public string model = "";
    public string apiKey = "";
    // edit commands wait for y
    public bool askFirst = true;
    // how many times it can go back and forth before it stops
    public int maxSteps = 10;
    public int timeout = 180;
    // tool calls, off for servers that don't have them, then it answers in look: and run: lines
    public bool tools = true;
    // the model thinks before answering, slower and sometimes it never stops
    public bool think = false;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref url, "url", "http://127.0.0.1:8080/v1");
        Scribe_Values.Look(ref model, "model", "");
        Scribe_Values.Look(ref apiKey, "apiKey", "");
        Scribe_Values.Look(ref askFirst, "askFirst", true);
        Scribe_Values.Look(ref maxSteps, "maxSteps", 10);
        Scribe_Values.Look(ref timeout, "timeout", 180);
        Scribe_Values.Look(ref tools, "tools", true);
        Scribe_Values.Look(ref think, "think", false);
    }
}

public class AiMod : Mod
{
    public static AiSettings Settings;

    private string stepsBuffer;
    private string timeoutBuffer;
    private System.Threading.Tasks.Task<List<string>> test;

    public AiMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<AiSettings>();
        Commands.All.Add(new Command
        {
            name = "ai",
            args = "<what you want>|new",
            help = "ask a language model to do it with the terminal's commands",
            run = Session.Command,
            examples = new[]
            {
                ("ai who's the hungriest?", "it looks with the read commands and answers"),
                ("ai make the miners work nights", "it shows the edit commands it wants, y runs them"),
                ("ai tell me when food is low", "it can write a cron script in ~"),
                ("ai new", "forgets the talk so far")
            },
            complete = done => done.Length == 0 ? new List<string> { "new" } : new List<string>()
        });
    }

    public override string SettingsCategory() => "JustTerminalAI";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        Listing_Standard l = new Listing_Standard();
        l.Begin(inRect);
        l.Label("JTA_Url".Translate());
        Settings.url = l.TextEntry(Settings.url);
        l.Label("JTA_Model".Translate());
        Settings.model = l.TextEntry(Settings.model);
        l.Label("JTA_Key".Translate());
        Settings.apiKey = l.TextEntry(Settings.apiKey);
        l.Gap();
        l.CheckboxLabeled("JTA_AskFirst".Translate(), ref Settings.askFirst, "JTA_AskFirstTip".Translate());
        l.CheckboxLabeled("JTA_Tools".Translate(), ref Settings.tools, "JTA_ToolsTip".Translate());
        l.CheckboxLabeled("JTA_Think".Translate(), ref Settings.think, "JTA_ThinkTip".Translate());
        l.TextFieldNumericLabeled("JTA_Steps".Translate(), ref Settings.maxSteps, ref stepsBuffer, 1, 50);
        l.TextFieldNumericLabeled("JTA_Timeout".Translate(), ref Settings.timeout, ref timeoutBuffer, 10, 1800);
        l.Gap();
        if (l.ButtonText("JTA_Test".Translate()))
        {
            test = Client.Models();
        }
        if (test != null)
        {
            l.Label(!test.IsCompleted ? "JTA_Testing".Translate() : test.IsFaulted ? "JTA_TestBad".Translate(Client.Reason(test.Exception)) : "JTA_TestGood".Translate(string.Join(", ", test.Result)));
        }
        l.End();
    }
}
