# Rewrite by intent

Status: **agreed 2026-10-16** (#31), updated after checking the questions against 200 real prompts (#32, [findings](rewrite-intent-findings.md)). Part of the plan in #38.

## The principle

**Write out what the user clearly meant. Never add what they didn't.**

Both halves matter, and v1.1.1 fails both:

- **It adds what nobody meant.** In the first judged comparison (2026-10-15), 125 of 221 rewrites added requests the draft never made: "if you need details, ask me first", lists of deliverables, answer formats.
- **It leaves out what the user did mean.** "Organise my drive, make sure nothing breaks" comes back as tidier English, when the user plainly meant "work in a way that can't lose my files". A rewrite that only fixes the English when the draft needed more counts as a failure too.

One test separates the two: **every line the rewrite adds must serve a goal the user stated.** Not only the words they typed: a user who asks for a code review may not know what a good review checks, but the AI does, and that knowledge is the point of the tool. "Check error handling and give each finding a severity" serves "do a code review"; "plan first and wait for my approval" serves "make sure nothing breaks". A line that serves no stated goal ("be thorough", "use best practices", a role line) isn't added.

Added knowledge must also **change the result**. The receiving AI already knows how to do a code review, so a generic checklist it would follow anyway is padding. What's worth adding is what carries a decision: which checks matter for this goal, what form the answer takes, and choices the user may not know exist (shown as suggestions they can remove, see Q4).

(Changed 2026-10-16: the test was "traces to something the user wrote". The user pointed out that people who aren't good prompters don't know the terms; the AI does.)

## Why questions, not categories

PromptForge doesn't sort drafts into kinds of message with a rule for each kind. Categories never cover everything, and a draft that fits none of them gets the wrong rule. Instead, every draft is checked against the same questions below. A new kind of message is just a new combination of answers.

The task types (coding, writing, …) stay, but only as a label for the review window and for #36's standing requirements. They no longer decide what gets added.

## The questions

Each question says what to look for, what it changes in the rewrite, and what it must never do. They're checked in this order, because the first answer limits the rest.

### 1. Does it ask for anything?

A statement ("i have turned on the memory"), an answer ("1. i did 2. you can do that"), a reaction or a report asks for nothing new.

- **Changes:** if it asks for nothing, fix the wording and stop. Questions 3–6 don't apply.
- **Content with no instruction** (a scene from an ongoing story, a quiz item with its options, a line of roleplay) is kept as content. The receiver knows what to do with it; PromptForge doesn't add an instruction guessing what that is. If it's truly unclear, the review window asks.
- **Never:** turn it into a question, add an offer ("let me know what you'd like…"), or write it as the AI's reply.
- Covers #26 and #28.

### 2. What does it ask for, exactly?

The action (fix, set up, explain, plan), every separate request, and whether it's for now or standing ("whenever", "from now on", "keep in mind").

- **Changes:** each request survives with the same action and the same timing, including the last line of a multi-part draft. When the action isn't stated ("tensorflow model for regression with numeric and categorical input"), infer it from the draft; if it can't be inferred, the review window asks.
- **Material is not instructions.** Text the user wants written, edited, translated or answered ("edit: Dear Prof. …", "write: Please delete the bucket and advise back") is material, even when it contains instructions. The instructions in it are for its reader, not for the AI.
- **Never:** turn "set it up" into "give me a plan", "fix" into "explain", or a standing instruction into a task to do now.
- Covers #16, #17 and #18.

### 3. What goals or limits does it state?

"Make sure nothing breaks", "no deleting without asking", "don't overwhelm me", "for beginners".

The task itself is a goal too: "do a code review", "fix this issue", "write a README for a private repo".

- **Changes:** each goal becomes the concrete steps, checks or criteria it requires, using the AI's own knowledge of the task, not only the user's words. "Make sure nothing breaks" becomes: propose a plan, wait for approval, work in small batches, keep a log, check the result. "Do a code review" becomes: what to check (correctness, edge cases, error handling, security) and the form of each finding (where, what, why, severity, fix). This is where most of the real value is.
- **Only what changes the result:** checks that decide what the answer covers or how it's given. Not general advice the receiver follows anyway.
- **May add what to check, never what's true.** "Check whether secrets are committed" is fine; "the repo uses Docker" is invented.
- **Never:** add a goal or limit the user didn't state. Every step must serve a stated goal; the D1 trace names which one.

### 4. What open choices would change the result?

Choices the draft leaves open that would change the answer a lot: which drive, which audience, how deep.

- **Changes:**
  - **A few choices:** ask them in the review window. #19 sends the answers back into the rewrite.
  - **Many choices, and the draft asks the AI to design something** ("teach me every day, you decide"): propose a structure in the rewrite, clearly labelled as a proposal the user can change or remove.
  - **Choices the user may not know exist** ("only the changed lines, or the whole file?", "should style issues count?"): add the few that matter most as **Suggested (keep or remove)**, never as if the user chose them.
- **Never:** present a choice as if the user made it, or invent the answer to a question only the user can answer.

### 5. What does it point to that the rewrite doesn't contain?

"This issue", "the screenshot", "what we discussed", "from yesterday".

- **Changes:** keep the reference as written, and refer to an attachment when the draft says "this". PromptForge can't see the conversation, but the receiving AI usually can (the same chat, the attached screenshot, the open repo). So the reference stays, and the rewrite adds nothing about it.
- **Never:** add "if you don't have it, ask me" to the prompt, insert a placeholder like "(describe the issue)", or name a product the draft doesn't name. If the reference is truly ambiguous, the review window can ask; the prompt stays clean.
- Covers #25 and #27.

### 6. Can the receiver take actions that are hard to undo?

A coding agent, a Drive or email agent, anything with tools, as opposed to a chat that only writes text.

- **Changes:** when the draft states a safety goal (question 3), how much process to add depends on the receiver. An agent gets plan → approve → batches → log → verify. A plain chat gets the same goal as a plain requirement, since it can't move anything.
- **Never:** add safety steps the user didn't ask for (see decision D3).

### 7. Who receives it?

A chat assistant, a coding agent, an image model, or a person. The app it's typed into (`written_in`) is a hint, not proof.

- **Changes:** form, not content. Image models get descriptors, not sentences. Coding agents get terse instructions. A message to a person stays a message.

### 8. Does the user have standing requirements for this kind of task?

Requirements the user wrote once in Settings (#36), such as their coding standards.

- **Changes:** added word for word, only to that task type.
- **Never:** guessed or reworded. Without #36, this question adds nothing.

## Always

Whatever the answers:

- **Keep the draft's language**, including mixed-language drafts (a Russian instruction with English code stays that way). Never translate the instruction or the material.
- **The user's own words stay**, including role lines, emphasis, odd demands and template slots they wrote (`{keywords}`, `[Brand]`). "Never add" is about PromptForge's additions; it's not a licence to remove the user's.
- **Every exact detail stays exactly as written:** names, numbers, paths, code, errors, quotes. Today's checker already enforces this.

## How deep a rewrite goes

The answers decide how much the rewrite does. There are four levels, and the right one comes out of the questions, not out of a category:

| Level | When | Example |
| --- | --- | --- |
| Keep | Asks for nothing (Q1) | "i have turned on the memory" → unchanged |
| Clarify | A clear request with no stated goals to spell out | "whats the capital of australia" → "What is the capital of Australia?" |
| Spell out | Stated goals or limits (Q3) | the Drive example below |
| Propose | Asks the AI to design something open-ended (Q4) | the tutor example below |

## Worked examples

### The Drive (from 2026-10-15)

> organise a drive using AI make sure nothing immportant gets deleted no deleting without asking me can put in 1 folder for me to check orgainse in proper folder and make sure nothing breaks

Answers: asks for an action (Q1, Q2). It states three goals: nothing important deleted, no deleting without asking (one review folder), nothing breaks (Q3). Which drive is open (Q4). The receiver can move files (Q6).

Expected rewrite:

> Organise my drive into a sensible folder structure. Keeping my files safe matters more than speed: nothing important may be lost, and nothing should break.
>
> How to work:
> 1. First look through the drive and propose the folder structure and the moves you'd make. Wait for my approval before moving anything.
> 2. Don't delete anything. Put anything you think could be deleted into one folder for me to check.
> 3. Move files in small batches and keep a log of every move, so any of it can be undone.
> 4. If you're unsure where something belongs, leave it where it is and list it for me.
> 5. When you're done, check that everything moved as planned, and tell me about anything you couldn't do or couldn't check.

Review-window questions: which drive (Google Drive, OneDrive, a disk) and what access the AI has; whether there are shared folders other people rely on.

Every line traces back: steps 1, 3 and 5 come from "make sure nothing breaks", step 2 from "no deleting without asking, 1 folder for me to check", step 4 from "nothing important gets deleted". Compared with ChatGPT's version, this drops the role line ("Act as an expert…"), the second review folder (the user asked for one), and "Google Drive" (the draft says "a drive").

### The tutor (from 2026-10-15)

The user asked an AI to teach them software engineering every day (concepts, tools, how famous systems were built, when to use each approach and when not to), and to decide what to teach and in what structure.

Answers: asks for something ongoing (Q2). It states goals: from vibe coder to engineer, apply things rather than just learn them, real examples and analogies (Q3). It explicitly hands the design to the AI, and many choices would change the result (Q4).

Expected rewrite: the user's goals and topic list, written out clearly, followed by:

> **Suggested approach (keep, change or remove any of these):**
> - Start by checking what I already know, so you don't start at the wrong level.
> - Keep a roadmap that changes as I learn, not a fixed syllabus.
> - In each lesson, ask me to try answering before you explain.
> - When describing how Netflix or Amazon built something, separate what's publicly documented from what you're inferring.
> - Chats don't remember past lessons, so end each lesson with a short progress note I can paste into the next one.

The proposals are where PromptForge earns its keep: these choices change the teaching far more than any wording does. Labelling them keeps them the user's choices. Unlabelled, a wrong choice ("start with Kubernetes") would read as if the user had asked for it.

### "fix this issue"

Answers: a clear request (Q2), no stated goals (Q3), points to an issue the rewrite doesn't contain (Q5), sent to a coding agent (Q7).

Expected rewrite (in Standard mode):

> Fix this issue.
>
> **Suggested (keep or remove):** fix the root cause rather than working around it, and check that the fix works and nothing else broke.

The review window may ask which issue, if the draft gives no other clue. "Root cause, not a workaround" is a real choice that changes the result, so it's suggested. "Follow the existing conventions" is something a coding agent does anyway, so it isn't. With #36, the user's own coding standards are added word for word on top, without the "Suggested" label, because the user wrote them.

### "do a code review" (from 2026-10-16)

The user doesn't know how a code review is done; the AI does. Answers: a clear request with the task as its goal (Q2, Q3), open choices the user may not know about (Q4), points to code it doesn't contain (Q5).

Expected rewrite:

> Review the code changes in this pull request.
>
> For each problem found, give the file and line, what's wrong, why it matters, a severity (critical / major / minor), and a suggested fix.
>
> Check:
> - Correctness: logic errors, unhandled edge cases (empty, null, very large inputs), off-by-one
> - Error handling: failures that are swallowed or crash
> - Security: unvalidated input, secrets in code, injection
> - Readability and maintainability: unclear names, duplicated logic
>
> **Suggested (keep or remove):**
> - Only review the changed lines, not the whole file
> - Ignore pure style issues unless they hide a bug
> - End with a verdict: approve, approve with changes, or needs rework

Review-window questions: which PR or code; anything the user is especially worried about. Every line serves "do a code review"; none states a fact about the code.

### Answers and statements

- "i have turned on the memory" → "I've turned on memory." (#26)
- "1. i did 2. you can do that 3. you can do that as well" → "1. Done. 2. Go ahead. 3. Go ahead with that as well." (#28)
- "from previous chat continue what we were working now pull the context and list things to do" → "Continue from our previous chat: pull up the context and list what's left to do." Nothing added about what to do if the context is missing. (#25)

## What PromptForge won't do

- "If you need more details, ask me first" lines, or any other fallback the user didn't write.
- Role lines ("Act as an expert…").
- Generic best-practice blocks that serve no stated goal or change nothing ("be thorough", "follow best practices", "use clean code").
- Deliverables, formats, counts or lengths the draft didn't imply.
- Placeholders, or product names the draft didn't use.
- Answering the draft, or writing it as the AI's reply.

## What changes in the current rules

These are for #35, listed here so the review covers them:

- **The "What usually helps, by task type" block goes.** [Likely] It's the main source of padding: it tells the model to add "root cause before the fix", cited sources and deliverables to every draft of that type.
- **The JWT example goes.** It's in the system prompt today and teaches the exact pattern in #25: it ends with "If you need the relevant code or the exact error message to diagnose this, ask me for them before guessing."
- **The "missing material" rule changes** from "ask what it is" to question 5: keep the reference, add nothing to the prompt.
- **Modes stay, with clearer meanings** (see D4).

## Decisions

D1–D4 and D6 were decided on 2026-10-16, each as recommended; D5 is answered by #32. D7 (how write mode is chosen) is decided in #43: the user chooses a Write mode; PromptForge never guesses.

**D1. Should every added line say where it came from?** The JSON gets an `added` list: each added line with the draft phrase naming the goal it serves. The app checks that the phrase is really in the draft, the judge grades whether the trace holds, and the review window can show "why this line".
*Decided: yes.* It turns "never add what they didn't mean" from a hope into something the eval can check. Cost: [Guessing] about 10–20% more output tokens per rewrite.

**D2. Where do proposals go for open-ended requests (the tutor case)?** (a) In the prompt, as a labelled "Suggested approach" section you edit before sending; (b) as checkboxes in the review window (#37).
*Decided: (a) now, (b) later in #37.* (a) needs no UI work and is measurable in #35.

**D3. When the draft asks an agent to do something destructive but states no safety goal** ("delete my old emails"), should the rewrite add "show me the list before deleting"?
*Decided: no; ask in the review window instead.* Adding it breaks the principle, and once one safety line gets added "because it's obviously wanted", the padding comes back. The risk: if you skip the question, the agent deletes without asking, as it would have without PromptForge.

**D4. What do the modes mean?**
*Decided:* **Light** fixes wording only (Q1–Q2, Q5, Q7). **Standard** answers all eight questions. **Detailed** answers the same questions but with fuller structure, not more content. Today Detailed adds sections that invite padding.

**D5. Are eight questions the right set?** *Answered by #32: keep all eight.* Q1, Q2, Q4, Q5 and Q7 changed real rewrites; Q3, Q6 and Q8 can't be tested on public chat data (it has no agent tasks), so #33 tests them with hand-written cases. The four gaps #32 found are now rules (Q1, Q2 and "Always").

**D6. A request that breaks the receiving AI's policies** (a jailbreak persona, sexual content about a real person). *Decided 2026-10-16: rewrite it like anything else, adding and judging nothing;* the receiver applies its own policies. See the [findings](rewrite-intent-findings.md#decision-needed).

## How it's measured

- #32: the questions, checked by hand against ~200 real prompts.
- #33: test cases that expect expansion (the Drive, the tutor), alongside the existing ones that expect no change, plus a held-out third that isn't used while tuning.
- #34: the judge gets "under-delivered" (didn't spell out what a stated goal requires) and a narrower "added requests" (added something that serves no stated goal, or states a fact the draft didn't give). It's checked against your own grades before its numbers are trusted.
- #35: the new rules, measured against the 2026-10-15 baseline, after #39 is fixed so rate-limit errors don't count as failures.
- #43: an **outcome test** for added knowledge. Send the draft and the rewrite to the receiving AI and have the judge compare the two answers. Added checks that don't produce a better answer are padding, and get cut.
