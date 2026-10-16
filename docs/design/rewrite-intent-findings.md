# Intent questions vs. real prompts (#32)

Checks the eight questions in [rewrite-intent.md](rewrite-intent.md) against 200 real messages, before anything is built. Result: the questions hold up, with **four gaps** that are now rules in the spec, and **one big blind spot in the data**: public chat logs contain almost none of the agent-style prompts the spec cares most about.

## Method

- **Sample:** 200 English, non-toxic user messages from [WildChat-1M](https://huggingface.co/datasets/allenai/WildChat-1M) (ODC-BY), drawn at random on 2026-10-16: 150 first messages of a conversation and 50 later messages picked from inside conversations. Later messages matter because PromptForge rewrites messages mid-chat too, without seeing the chat. Repeated templates were capped at two per opening (one site had posted hundreds of near-identical "give me a response to…" messages).
- **Plus** the 10 drafts the plan was built on: the seven bad rewrites (#16–#18, #25–#28) and the Drive, tutor and "fix this issue" examples.
- **Labelling:** each message was run through the questions by one rater (Claude), using the 380-character view of long messages. One rater means the counts are a guide, not a measurement; #34 checks the judge against the user's own grades, which is the measurement that counts.
- **Data:** the message ids and labels are in [`data/intent-labels-wildchat.json`](data/intent-labels-wildchat.json), and the sampling script is [`data/sample_wildchat.py`](data/sample_wildchat.py). No message text is committed; examples below are paraphrased.

## What the sample looks like

| | First messages (150) | Later messages (50) | All (200) |
| --- | --- | --- | --- |
| Asks for nothing new (Q1 → keep) | 8 | 7 | 15 (7.5%) |
| Points to something it doesn't contain (Q5) | 7 | **19** | 26 (13%) |
| Standing instruction or set-up for the chat (Q2) | 8 | 0 | 8 |
| Asks the AI to design something open-ended (Q4 → propose) | 2 | 0 | 2 |
| States a goal that needs spelling out (Q3) | 0 | 0 | 0 |
| Receiver can take hard-to-undo actions (Q6) | 0 | 0 | 0 |

The other 183 messages (91%) are clear requests that need clarifying at most. The rows overlap: most of the Q5 messages are among them.

**What this says about the questions**

- **Q5 is the most important rule for mid-chat messages.** 19 of 50 later messages point to the chat ("write it again", "suggest someone else", "continue.", "keep the opening paragraph I provided"). The v1.1.1 rule asks what the missing material is, and the bad-rewrite issues show it turning into "if you don't have it, ask me". On real data that would pad **almost two in five** mid-chat messages. The spec's rule (keep the reference, add nothing) is right.
- **Q1 matters as much.** 15 messages (7.5%) ask for nothing new: thanks, "it's a Facebook page name" (answering the AI's question), "But Korea doesn't have this bug" (a pushback), a game idea answered with "yes, yes, yes", a greeting. A rewrite that turns any of these into a request is the #26/#28 failure.
- **Q3 (spell out) and Q6 (risky actions) never came up**, and propose (Q4) only twice. That's not evidence they're useless: WildChat is 2023 ChatGPT chat with no tools, so nobody asks it to reorganise a drive. In the user's own 10 drafts, they come up constantly: the Drive (Q3, Q6), the Notion set-up in #17 (Q6), the tutor (propose). **Public data can't test the part of the spec that matters most to the user** (see "What this means for #33").

## Gaps: real prompts the questions handled badly

Each one is now a rule in the spec.

1. **Content with no instruction (6 messages).** A scene from an ongoing story pasted on its own, a quiz item with answer options and nothing else, a line of roleplay. Q1 says "keep", but these do want something: continue the story, answer the quiz. The receiver knows what to do; PromptForge would have to guess. **Rule (Q1):** keep it as content. Don't add an instruction guessing what to do with it; if it's truly unclear, ask in the review window.
2. **Material that contains instructions (10 messages).** "write: Please delete the bucket from the application and advise back…", "edit: Dear Prof. …", "polish this: …", "better sentence". The material is a message for a person, and it contains instructions ("delete the bucket"). A rewrite that reads them as instructions to the AI changes the task completely. **Rule (Q2):** text the user wants written, edited, translated or answered is material, even when it contains instructions.
3. **Language (11 messages, 5.5%).** All 200 come from conversations marked English, yet 11 are in Russian, Chinese, Polish or a mix (Russian instruction with English code, Chinese instruction with English text). The spec said nothing about language. **Rule (always):** keep the draft's language, including mixed-language drafts; never translate the instruction or the material.
4. **The user's own filler, role lines and template slots (8 + 3 messages).** "Act like professionals", "You are a prompt engineer…", "write with burstiness", `{keywords}`, `XXXX-1`. The spec says never *add* a role line or placeholder, but not what to do with the user's own. **Rule (always):** the user's own words stay, including role lines, emphasis and template slots they wrote. "Never add" is about PromptForge's additions, not a licence to remove the user's.

Smaller patterns the questions already handle, worth a test case each in #33:

- **Telegraphic requests (14):** a noun phrase with no verb ("tensorflow model for regression with numeric and categorical input", "online store description for HomeSerenity"). Q2 has to infer the action; when it can't, the review window asks.
- **Needs live information or a website (4):** "check this brand's website and summarise", a transfer rumour. The rewrite keeps the request; whether the receiver can browse isn't PromptForge's problem.
- **Requests that break the receiver's usage policies (4):** a jailbreak persona, a request for a software key, sexual content involving real people. See D6 below.

## What this means for #33

- **The test set needs hand-written agent cases.** None of the 200 public messages exercise Q3 or Q6. #33's expansion cases have to come from the user's own drafts (the Drive, the Notion set-up, "fix this issue") and new ones written in the same style: an agent asked to reorganise, delete, migrate or send something, with and without a stated safety goal (D3).
- **Mid-chat messages need their own cases**, without the chat: "continue.", "write it again and refer to my resume", "no letters", "it's a Facebook page name". These are where Q1 and Q5 fail today.
- **One case per gap above**, including at least two non-English drafts and one mixed-language draft.
- **A history export from the user would help most** (still optional). It would show how often Q3 and Q6 come up in real agent use, which no public dataset can.

## Decision needed

**D6. What should PromptForge do with a request that breaks the receiving AI's policies** (a jailbreak persona, sexual content about a real person)? *Recommendation: rewrite it like anything else, adding nothing and judging nothing.* PromptForge is an editor, not a filter, and the receiving AI applies its own policies. The model behind PromptForge may refuse, and the review window already shows that as an error. The risk: PromptForge makes a jailbreak read more clearly. [Guessing] It's small, since jailbreaks work on wording that PromptForge's rules already keep.

## D5 answer

**Keep all eight questions.** Q1, Q2, Q4, Q5 and Q7 changed real rewrites in the sample. Q3, Q6 and Q8 couldn't be tested on public chat data, but they're the reason the plan exists (the Drive, #17, the user's standing standards), so #33 tests them with hand-written cases instead.
