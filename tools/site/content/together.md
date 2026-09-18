# With your AI

Not an agent you send away — an assistant looking at the same screen you are.

Most AI tooling is built for delegation. You describe a goal, something goes away and works
somewhere you cannot see, and you review what comes back. That is a good shape for a lot of work,
and the people who build the models are already very good at it.

Nexaflow is built for the other shape. You are reading the log, turning the 3D model, scrolling the
DICOM series — and so is the assistant, through the same view-model, holding the same state.
Neither of you is a proxy for the other.

## It sees the tab you are on

Every page can describe itself. When you ask a question with the page attached, the send waits until
that page says its context is ready, so the answer is about what is actually on screen rather than a
guess at what you might be looking at. You do not spend the first half of every request explaining
where you are.

## It works the same controls you do

A page's tools drive the same view-model your keyboard and mouse drive. An edit the assistant makes
to a document appears in your editor and saves through your save. There is no separate copy to
reconcile afterwards, because there was never a second copy.

Some of the tools look rather than read. The assistant can capture the image you are viewing, render
the SVG, take a picture of a PDF page. On the 3D viewer it can orbit the model and *then* look at it
— steer the camera, take the picture, answer the question.

## It asks before it changes anything

Reading runs on its own. Anything that writes is approval-gated and shows you what it intends first,
and deletes go to the Recycle Bin rather than into the void.

Some of the restraint is deliberate and permanent:

- **Git tooling is read-only.** The assistant reads status, log, diff, blame, history and worktrees.
  It does not commit, push, or switch your branch. You do that.
- **`dotnet run` is withheld**, because a program that might never exit is not a good thing to hand
  something that cannot see the window.
- **File actions stay inside the folder you are in**, apart from one deliberately unconfined mode
  that says so plainly.

This is not caution for its own sake. A tool that can quietly do anything is a tool you have to
supervise instead of use.

## Your choice of model

Claude, Gemini, OpenAI, or a local model through Ollama — or a combination of them. The AI is
optional: Nexaflow is a workspace first, and every page in it works with no provider configured at
all.
