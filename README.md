# JustTerminalAI

A RimWorld 1.7 mod that adds an `ai` command to [JustTerminal](https://github.com/purpleorangegames/JustTerminal). Write what you want in plain words and a language model running on your own computer does it with the terminal's commands.

    ai who's the hungriest?
    ai make the miners work nights
    ai send me a letter when someone has a mental break

It reads with the terminal's read commands, help and the manual's parts, then shows the commands that change things and waits for y. Scripts and programs it writes are checked by JustTerminal before you're asked.

Each `ai` line starts fresh. `ai session` turns the screen red with an `ai>` prompt, every line goes to the model and it remembers the talk until you leave with `exit` or ctrl+c.

`ai remember <text>` keeps a line it reads before every answer, and in a session you can just tell it to remember something. `ai memory` lists them like cron_edit: d removes one, e edits the file. They live in `~/ai.memory`, so they belong to the save and go along with export and import.

It needs a server that answers like OpenAI's chat completions: vLLM, llama.cpp server, LM Studio or Ollama. The address is in the mod options, the default is http://127.0.0.1:8080/v1. It uses tool calls, and for servers without them there's an option to answer in plain lines instead. The model can only use the terminal's own commands, inside the game: it reads, and changes things or writes files in the save's ~ after your y. It can't run anything on your computer or touch your real files. Thinking is off by default, it's slower and some models think until they run out of tokens.

Tests/requests.txt has the requests to try after a change, with what should happen.

Build with `dotnet build -c Release` in Source, it needs JustTerminal next to it in the same folder.
