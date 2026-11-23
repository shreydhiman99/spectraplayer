# Milestone 5 plan: ask questions about a section, offline

> **Status: not part of this version.** On 2026-11-23 the decision was to stop at milestone 4 for this release and spend
> the time on the look and feel. Only a design was done: no model, server or benchmark was run. A first sketch of the
> prompt-side code (transcript blocks, citation checking, keyword ranking, a context planner) was started and then
> removed because it was untested and unused; this document is what remains. The exact llama.cpp build (b12650) and model
> files recorded below were looked up that day and should be re-checked before use.

**Goal.** With a section marked and transcribed (milestone 4), the student can **summarise it, ask questions about it,
turn it into notes, and be quizzed on it**, using a language model that runs on their own computer. Answers must be
grounded in the transcript and point back to the video with clickable timestamps.

## What is known and what is not

| Fact | Status |
|---|---|
| llama.cpp ships prebuilt Windows x64 zips (CPU, Vulkan, CUDA, others), and its CPU build picks the best instruction set at start-up, so there is no separate AVX2 zip. | Searched; the exact current release tag and asset names are **unverified** (sources disagree on the version) |
| `llama-server` exposes an OpenAI-compatible `/v1/chat/completions`, a public `/health`, an optional API key (sent as a Bearer token), parallel "slots", and reuses the KV cache between requests (`cache_prompt`, on by default). | Searched. Exact flag names differ between builds (for example `--webui` against `--no-webui`): **check against `--help` of the pinned build** |
| Candidate models, 3–4B class: Qwen3-4B-Instruct-2507 (Q4_K_M ≈ 2.5 GB), Phi-4-mini 3.8B, SmolLM3 3B (Q4 ≈ 1.9 GB), Qwen3 1.7B (smaller and faster), Gemma 3 4B (strength is images, not needed here), Llama 3.2 3B (Meta community licence). Roundups now also mention Qwen3.5 4B. | Searched. **Licences are unconfirmed or conflicting** (Phi-4-mini is reported as both MIT and Apache-2.0): read each model card before shipping |
| Speed on CPU: one test reports about 24 tokens/s for a 3B model on a recent laptop chip and about 9 tokens/s on a low-end Intel N150. | One source. This dev machine (i5-7200U, 2 cores / 4 threads, AVX2) is likely **slower than both** |
| Memory: this machine has 16 GB; the target is 8 GB. Whisper is already released after each run, so the model can have most of the free RAM. | Known |

**Sources:** [llama.cpp Windows builds (May 2026 note)](https://knightli.com/en/2026/05/18/llama-cpp-windows-cuda-vulkan-gguf/),
[llama-server documentation](https://www.mintlify.com/ggml-org/llama.cpp/inference/server),
[small-model roundup](https://tinyweights.dev/posts/best-small-language-models-2026/),
[CPU-only model guide](https://www.popularai.org/p/best-cpu-only-local-llm-2026),
[Qwen3-4B GGUF sizes](https://huggingface.co/bartowski/Qwen_Qwen3-4B-GGUF),
[Qwen3-4B-Instruct-2507 GGUF](https://huggingface.co/lmstudio-community/Qwen3-4B-Instruct-2507-GGUF).

## Spike 5a: what has been looked up (nothing downloaded yet)

Checked on 2026-11-23 from GitHub's and Hugging Face's own metadata:

- **llama.cpp.** The release marked "latest" (`v1.0.0`) is only a placeholder with no binaries. Real builds are tagged `bNNNNN`;
  the newest is **b12650** (published 2026-11-23, 38 assets). Windows x64 assets of that build, with the SHA-256 GitHub publishes:

  | Asset | Size | SHA-256 |
  |---|---|---|
  | `llama-b12650-bin-win-cpu-x64.zip` | 23.9 MB | `98ddbb60ab72c2b5c32fb66c17f4cb1fbc4b7f077aa8f1dee9fe9b941537f74d` |
  | `llama-b12650-bin-win-vulkan-x64.zip` | 37.1 MB | `6dea3c0aa58319337bfbe32046d3b36573e8ac742ebefbccc6dedd9b283b6876` |

  (CUDA, ROCm, SYCL and OpenVINO builds also exist, 100–400 MB each; not relevant here.) The CPU zip is small (24 MB), so
  shipping the llama.cpp program is cheap. Whether b12650 is a good build to pin is **not yet tested**.
- **Models** (all Q4_K_M unless noted; licence field as published on the repo):

  | Model | File | Size | Licence | Native context |
  |---|---|---|---|---|
  | Qwen3-4B-Instruct-2507 (`unsloth/…-GGUF`) | `Qwen3-4B-Instruct-2507-Q4_K_M.gguf` | 2,382 MB | Apache-2.0 | 262,144 |
  | Qwen3 1.7B (`unsloth/Qwen3-1.7B-GGUF`) | `Qwen3-1.7B-Q4_K_M.gguf` | 1,056 MB | Apache-2.0 | 40,960 |
  | SmolLM3 3B (`ggml-org/SmolLM3-3B-GGUF`) | `SmolLM3-Q4_K_M.gguf` | 1,827 MB | Apache-2.0 | 65,536 |
  | Phi-4-mini 3.8B (`unsloth/Phi-4-mini-instruct-GGUF`) | `Phi-4-mini-instruct-Q4_K_M.gguf` | 2,376 MB | MIT | 131,072 |
  | Qwen3.5 4B (`bartowski/Qwen_Qwen3.5-4B-GGUF`) | `Qwen_Qwen3.5-4B-Q4_K_M.gguf` | 2,873 MB | Apache-2.0 | 262,144 |

  The licence question from the table above is therefore settled for the listed repos (Phi-4-mini is MIT there, not
  conflicting), though a model card should still be read before shipping. Per-file SHA-256 values are available from the same
  metadata for pinning.
- **Proposed spike downloads:** *core set* = CPU zip + Qwen3-4B + Qwen3 1.7B (about 3.4 GB); *everything* = core + SmolLM3 +
  Phi-4-mini + the Vulkan zip (about 7.6 GB). **Declined for now; the spike has not been run.**

## The central risk: speed on a weak laptop

A 10-minute section is about 1,500 words, roughly 2,000 tokens. The model must *read* all of that before it writes
the first word. If a 4B model reads at 15–40 tokens/s on this CPU (a guess, not a measurement), the first answer takes
**one to several minutes**, then writes at maybe 2–4 tokens/s. The design therefore treats waiting as normal and attacks it:

1. **Prefill while the student types.** As soon as a section is chosen on the Ask tab, send its transcript to the server
   as a prefix and let it be processed in the background. The question typed afterwards only costs its own tokens.
2. **Keep the transcript as a stable prefix** (system message first, question last), so every follow-up question reuses
   the server's cache instead of re-reading.
3. **Fewer tokens in.** Group transcript lines into 20–30 second blocks with one timestamp each, instead of one timestamp per
   line (saves roughly a tenth of the prompt).
4. **A smaller model on demand.** Offer a "Faster, simpler answers" model (about 1.7B) next to the default one.
5. **Honest progress.** "Reading the transcript… 40%", then streamed words, with **Stop** at all times.

## Design

### Pieces
- **Sidecar, not in-process.** `llama-server` runs as a child process on `127.0.0.1` and a random port, started on first
  use. A crash or a model-load failure cannot take the player down, and killing it frees all its memory.
- **Lifecycle.** Free-port pick, health polling, a random API key, a Windows Job Object so the server dies with the app even
  if the app crashes, output captured to a log file, **stopped after about 10 idle minutes** and **while a transcription
  is running** (two heavy models plus playback on 2 cores is too much).
- **Client.** Streaming over server-sent events with cancel (closing the connection stops generation), friendly errors, and a
  fake server for tests.
- **Downloads, with consent, as for Whisper models.** Two assets: the `llama-server` build (an *executable*, so the dialog
  says so; pinned release tag, SHA-256 verified, unpacked only into Spectraplayer's own folder, never auto-updated) and a model
  file (GGUF, size and SHA-256 pinned from Hugging Face's metadata). The existing `ModelManager` generalises to both.
- **Prompting.** Plain instructions first (answer only from the transcript, say "not covered in this section" otherwise,
  cite every claim as `[mm:ss]`), then the transcript blocks, then the question. Reasoning-mode models must have their
  thinking switched off or stripped.
- **Citations that work.** The app parses `[mm:ss]` in the answer, **checks the time exists in the section** (drops or flags
  invented ones) and renders it as a chip that seeks the video.
- **Long sections.** If the transcript does not fit the context (about 8k tokens planned): summaries use map-reduce
  (summarise windows, then combine); questions use **keyword retrieval with SQLite FTS5/BM25** to pick the best few
  windows. No embedding model in the first version: nothing extra to download or keep in RAM.
- **Notes from the model.** "Key points" asks for structured output (a JSON schema, if the pinned build supports it) so
  small models stay parseable; the student reviews a checklist and saves the chosen ones as notes with `source = "ai"` and
  the right timestamps.
- **Persistence.** Questions and answers saved per video and section (schema v4), so a study session can be revisited.

### The Ask tab
The side panel already has four tabs at 340 px; a fifth does not fit as text. Recommendation: **icon tabs, with a text
label only on the selected one** (tooltips on all), panel a little wider.
- Context chip: which section (or "whole transcript") the answers are based on, how many lines, which transcript source.
  If the section has no transcript yet: "Transcribe it first" button, or do it automatically with one confirmation.
- Quick actions: **Summarise**, **Key points**, **Explain simply**, **Quiz me**, plus a free question box.
- Answer streams in; citation chips are clickable; **Stop**; **Copy**; **Save as note**.
- Always visible: a one-line reminder that small models make mistakes and the cited lines are the evidence.

## Steps

| # | Step | Done when |
|---|---|---|
| 5a | **Spike and benchmark** | Pinned llama.cpp build and 2–3 candidate models measured on this machine (load time, reading speed, writing speed, memory, effect of the prompt cache, CPU against Vulkan); quality compared on a realistic lecture transcript with answerable and **unanswerable** questions; the server flags, JSON-schema output, no-thinking switch and streaming format confirmed. Decisions recorded in this file |
| 5b | Core | Server lifecycle, streaming client, download of server and model with consent and checksums, prompt builder with citation checking, FTS5 retrieval, chunking; all with unit tests against a fake server; one real end-to-end run |
| 5c | Ask tab | Summarise, ask, stream, stop, citations, save as note, prefill while typing, auto-transcribe-first, progress, clear errors; checked in the running app |
| 5d | Study extras (optional) | Key points → notes checklist, quiz and flashcards (copy or Anki-style file), questions across the whole video or tab, chat history |

## Testing approach
- **Unit:** SSE parsing, cancel, error paths, prompt and chunk budgets, citation validation, FTS retrieval, download
  verification: all with fakes.
- **Quality set (5a):** a written lecture transcript of about 1,500 words with a fixed list of questions: some answerable
  with a known fact, some **not covered** (the model must say so), some needing a timestamp. Scored per model.
- **Real runs:** the same command-line style harness as the Whisper spike, then the running app with screenshots.

## Risks and open questions
- **Speed**, above. The minimum acceptable experience on an 8 GB, 2-core laptop is a product decision (question 1 below).
- **Wrong answers.** Small models hallucinate. Mitigations are the grounding prompt, citation checking, showing the source
  lines and the reminder; they reduce the problem, they do not remove it.
- **Downloading and running an executable.** Pinned version and checksum, own folder, explicit consent. Unsigned binaries can
  trigger antivirus warnings; to be checked in 5a.
- **llama.cpp moves fast.** One pinned release is tested and shipped; flags are only trusted for that build.
- **Memory.** Server, app and video together on 8 GB; mitigated by idle shutdown and by stopping the server during transcription.
- **Licences.** llama.cpp is MIT. Each model's licence must be read on its card before it goes in the catalog.
- **Loopback server.** Bound to 127.0.0.1 with an API key; to be checked that Windows raises no firewall prompt.
- **English only** for now, like the speech models.
- **Permissions during development:** 5a needs the llama.cpp release zip (size not yet known, tens of MB for the CPU build)
  and two or three model files (roughly 1.1 GB, 1.9 GB and 2.5 GB). I will list exact names, sources and sizes and ask first.

## Decisions needed from you
1. **Speed.** Is "first answer in one to three minutes on a 2-core laptop, much faster on newer machines, with a faster
   smaller model as an option" acceptable for the first release?
2. **First features.** Summarise and ask questions with timestamp citations first (recommended), then notes from key points,
   then quizzes. Agree?
3. **Scope of a question.** The marked section only at first (recommended); whole video or tab via retrieval in 5d.
4. **Tab strip.** Icon tabs with a label on the selected one (recommended).
