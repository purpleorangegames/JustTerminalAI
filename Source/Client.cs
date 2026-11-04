using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JustTerminalAI;

// talks to a server that answers like OpenAI's chat completions, off the game's thread
public static class Client
{
    private static readonly HttpClient http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

    private static string Base => AiMod.Settings.url.Trim().TrimEnd('/');

    private static HttpRequestMessage Request(HttpMethod method, string path)
    {
        HttpRequestMessage r = new HttpRequestMessage(method, Base + path);
        if (!string.IsNullOrWhiteSpace(AiMod.Settings.apiKey))
        {
            r.Headers.Add("Authorization", "Bearer " + AiMod.Settings.apiKey.Trim());
        }
        return r;
    }

    public static async Task<List<string>> Models(CancellationToken stop = default)
    {
        using CancellationTokenSource time = CancellationTokenSource.CreateLinkedTokenSource(stop);
        time.CancelAfter(TimeSpan.FromSeconds(15));
        using HttpResponseMessage res = await http.SendAsync(Request(HttpMethod.Get, "/models"), time.Token);
        string text = await res.Content.ReadAsStringAsync(time.Token);
        if (!res.IsSuccessStatusCode)
        {
            throw new Exception((int)res.StatusCode + " " + Short(text));
        }
        return JsonNode.Parse(text)?["data"]?.AsArray().Select(m => (string)m?["id"]).Where(id => id != null).ToList() ?? new List<string>();
    }

    private static string model;
    private static int sent;

    // what the model can call, the terminal does the rest
    private static JsonArray Tools() => new JsonArray
    {
        Tool("read", "Runs a terminal command that only reads and gives back what it printed. help <command> shows a command's examples, manual sections lists the manual's parts, manual <part> shows one, ls / and cat show the game folders.",
            ("command", "the command line, like pawns or cat /pawns/Tasca/mood")),
        Tool("change", "Runs a command that changes the game or the files, as in edit mode. The player is asked first, for all the changes in one answer together.",
            ("command", "the command line, like schedule Tasca nightshift")),
        Tool("write_file", "Writes a file in ~, like a .cron script or a .sh program, folders on the way are made. Scripts and programs are checked first and a bad one comes back with the reason. The player is asked first.",
            ("path", "where, like ~/lowfood.cron or ~/bin/hungry.sh"), ("text", "the whole file")),
        Tool("remember", "Keeps something for every later answer, only when the player asks you to remember it. The player is asked first.",
            ("text", "one short line, like Drill and Kovul are the miners"))
    };

    private static JsonObject Tool(string name, string about, params (string name, string about)[] args)
    {
        JsonObject props = new JsonObject();
        JsonArray required = new JsonArray();
        foreach ((string arg, string what) in args)
        {
            props[arg] = new JsonObject { ["type"] = "string", ["description"] = what };
            required.Add(arg);
        }
        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = about,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = required }
            }
        };
    }

    // the model's answer: its text, without thinking, and the tools it called
    public static async Task<JsonObject> Chat(List<JsonObject> messages, CancellationToken stop)
    {
        string use = AiMod.Settings.model.Trim();
        if (use.Length == 0)
        {
            model ??= (await Models(stop)).FirstOrDefault() ?? throw new Exception("the server lists no models");
            use = model;
        }
        JsonArray list = new JsonArray();
        foreach (JsonObject m in messages)
        {
            list.Add(m.DeepClone());
        }
        JsonObject body = new JsonObject
        {
            ["model"] = use,
            ["messages"] = list,
            ["max_tokens"] = 8192
        };
        // each one below 0 is left to the server's own default
        AiSettings set = AiMod.Settings;
        if (set.temperature >= 0f)
        {
            body["temperature"] = set.temperature;
        }
        if (set.topP >= 0f)
        {
            body["top_p"] = set.topP;
        }
        if (set.topK >= 0)
        {
            body["top_k"] = set.topK;
        }
        if (set.minP >= 0f)
        {
            body["min_p"] = set.minP;
        }
        if (set.presencePenalty >= 0f)
        {
            body["presence_penalty"] = set.presencePenalty;
        }
        if (AiMod.Settings.tools)
        {
            body["tools"] = Tools();
        }
        if (!AiMod.Settings.think)
        {
            // Qwen and others read this, the rest ignore it
            body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
        }
        using CancellationTokenSource time = CancellationTokenSource.CreateLinkedTokenSource(stop);
        time.CancelAfter(TimeSpan.FromSeconds(AiMod.Settings.timeout));
        string text = null;
        for (int attempt = 0; ; attempt++)
        {
            HttpRequestMessage r = Request(HttpMethod.Post, "/chat/completions");
            r.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            // for testing: JTA_DEBUG_DIR=<folder> keeps every request sent, numbered
            if (Environment.GetEnvironmentVariable("JTA_DEBUG_DIR") is string debug && debug.Length > 0)
            {
                System.IO.Directory.CreateDirectory(debug);
                System.IO.File.WriteAllText(System.IO.Path.Combine(debug, $"request-{Interlocked.Increment(ref sent):000}.json"), body.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            using HttpResponseMessage res = await http.SendAsync(r, time.Token);
            text = await res.Content.ReadAsStringAsync(time.Token);
            if (res.IsSuccessStatusCode)
            {
                break;
            }
            // llama-server gives up when the model writes a tool call that isn't json, the next try usually is
            if (attempt == 0 && text.Contains("tool call", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            throw new Exception((int)res.StatusCode + " " + Short(text));
        }
        JsonNode choice = JsonNode.Parse(text)?["choices"]?[0];
        string content = (string)choice?["message"]?["content"] ?? "";
        // some servers leave the thinking in the answer
        content = Regex.Replace(content, "<think>.*?</think>", "", RegexOptions.Singleline).Trim();
        // some models start thinking without the opening tag and only close it
        int closed = content.LastIndexOf("</think>", StringComparison.Ordinal);
        if (closed >= 0)
        {
            content = content.Substring(closed + "</think>".Length).Trim();
        }
        JsonArray calls = choice?["message"]?["tool_calls"] as JsonArray;
        if (content.Length == 0 && (calls == null || calls.Count == 0))
        {
            throw new Exception((string)choice?["finish_reason"] == "length" ? "the model used all its tokens thinking and gave no answer" : "the model gave an empty answer");
        }
        JsonObject answer = new JsonObject { ["role"] = "assistant", ["content"] = content };
        if (calls != null && calls.Count > 0)
        {
            answer["tool_calls"] = calls.DeepClone();
        }
        return answer;
    }

    // why it failed, in a few words
    public static string Reason(Exception e)
    {
        while (e is AggregateException a && a.InnerException != null)
        {
            e = a.InnerException;
        }
        if (e is TaskCanceledException || e is OperationCanceledException)
        {
            return "no answer in time";
        }
        if (e is HttpRequestException h)
        {
            return "can't reach " + Base + (h.InnerException != null ? ", " + h.InnerException.Message : "");
        }
        if (e is JsonException)
        {
            return "the answer wasn't json";
        }
        return e.Message;
    }

    private static string Short(string text)
    {
        text = text.Replace('\n', ' ').Trim();
        return text.Length > 200 ? text.Substring(0, 200) + "..." : text;
    }
}
