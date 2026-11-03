# JustTerminalAI

A RimWorld 1.7 mod that adds an `ai` command to [JustTerminal](https://github.com/purpleorangegames/JustTerminal). Write what you want in plain words and a language model running on your own computer does it with the terminal's commands.

    ai who's the hungriest?
    ai make the miners work nights
    ai send me a letter when someone has a mental break

It reads with the terminal's read commands, help and the manual's parts, then shows the commands that change things and waits for y. Scripts and programs it writes are checked by JustTerminal before you're asked. ai new forgets the talk so far.

It needs a server that answers like OpenAI's chat completions: vLLM, llama.cpp server, LM Studio or Ollama. The address is in the mod options, the default is http://127.0.0.1:8080/v1. It uses tool calls, and for servers without them there's an option to answer in plain lines instead. Thinking is off by default, it's slower and some models think until they run out of tokens.

Build with `dotnet build -c Release` in Source, it needs JustTerminal next to it in the same folder.
