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
    public int maxSteps = 25;
    public int timeout = 180;
    // tool calls, off for servers that don't have them, then it answers in look: and run: lines
    public bool tools = true;
    // the model thinks before answering, slower and sometimes it never stops
    public bool think = false;
    // sampling, only a low temperature by default; below 0 leaves it to the server
    public float temperature = 0.3f;
    public float topP = -1f;
    public int topK = -1;
    public float minP = -1f;
    public float presencePenalty = -1f;

    // Qwen's numbers for each mode, small Qwen models repeat themselves without the presence penalty
    public void QwenSampling(bool thinking)
    {
        temperature = thinking ? 1.0f : 0.7f;
        topP = thinking ? 0.95f : 0.8f;
        topK = 20;
        minP = 0f;
        presencePenalty = 1.5f;
    }

    public override void ExposeData()
    {
        Scribe_Values.Look(ref url, "url", "http://127.0.0.1:8080/v1");
        Scribe_Values.Look(ref model, "model", "");
        Scribe_Values.Look(ref apiKey, "apiKey", "");
        Scribe_Values.Look(ref askFirst, "askFirst", true);
        Scribe_Values.Look(ref maxSteps, "maxSteps", 25);
        Scribe_Values.Look(ref timeout, "timeout", 180);
        Scribe_Values.Look(ref tools, "tools", true);
        Scribe_Values.Look(ref think, "think", false);
        Scribe_Values.Look(ref temperature, "temperature", 0.3f);
        Scribe_Values.Look(ref topP, "topP", -1f);
        Scribe_Values.Look(ref topK, "topK", -1);
        Scribe_Values.Look(ref minP, "minP", -1f);
        Scribe_Values.Look(ref presencePenalty, "presencePenalty", -1f);
    }
}

public class AiMod : Mod
{
    public static AiSettings Settings;
    public static AiMod Instance;

    private string stepsBuffer;
    private string timeoutBuffer;
    private string temperatureBuffer;
    private string topPBuffer;
    private string topKBuffer;
    private string minPBuffer;
    private string presenceBuffer;
    private System.Threading.Tasks.Task<List<string>> test;

    public AiMod(ModContentPack content) : base(content)
    {
        Instance = this;
        Settings = GetSettings<AiSettings>();
        Commands.All.Add(new Command
        {
            name = "ai",
            args = "<what you want>|session|remember <text>|memory|ask on|off",
            help = "ask a language model to do it with the terminal's commands, ai session to talk to it",
            run = Session.Command,
            examples = new[]
            {
                ("ai who's the hungriest?", "it reads with the read commands and answers"),
                ("ai make the miners work nights", "it shows the edit commands it wants, y runs them"),
                ("ai tell me when food is low", "it can write a cron script in ~"),
                ("ai session", "a red screen where each line goes to it and it remembers the talk, exit leaves"),
                ("ai remember Drill and Kovul are the miners", "it reads this before every answer"),
                ("ai memory", "the memories as a list, d removes one, e edits them"),
                ("ai ask off", "it runs changes without asking, ai ask on asks again")
            },
            complete = done => done.Length == 0 ? new List<string> { "session", "remember", "memory", "ask" } : done.Length == 1 && done[0] == "ask" ? new List<string> { "on", "off" } : new List<string>()
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
        l.Label("JTA_Sampling".Translate(), tooltip: "JTA_SamplingTip".Translate());
        l.TextFieldNumericLabeled("JTA_Temperature".Translate(), ref Settings.temperature, ref temperatureBuffer, -1f, 2f);
        l.TextFieldNumericLabeled("JTA_TopP".Translate(), ref Settings.topP, ref topPBuffer, -1f, 1f);
        l.TextFieldNumericLabeled("JTA_TopK".Translate(), ref Settings.topK, ref topKBuffer, -1, 200);
        l.TextFieldNumericLabeled("JTA_MinP".Translate(), ref Settings.minP, ref minPBuffer, -1f, 1f);
        l.TextFieldNumericLabeled("JTA_Presence".Translate(), ref Settings.presencePenalty, ref presenceBuffer, -1f, 2f);
        if (l.ButtonText("JTA_QwenSampling".Translate()))
        {
            Settings.QwenSampling(Settings.think);
            temperatureBuffer = topPBuffer = topKBuffer = minPBuffer = presenceBuffer = null;
        }
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
