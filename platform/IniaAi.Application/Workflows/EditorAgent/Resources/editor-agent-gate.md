The platform checked `out/workflow.json` the way a save checks a workflow, and these checks failed (attempt {round} of {rounds}):

{errors}

Fix every error in `out/workflow.json`, run the checker again until it passes, and update `out/summary.json`. If `out/` is empty, this message started a fresh session: your previous attempt is staged as `docs/previous-attempt.json`, with its summary in `docs/previous-attempt-summary.json` and your answer in `docs/previous-attempt-answer.md`. Start from that file, fix the errors, and write `out/workflow.json` and `out/summary.json` again. Then answer again in full: your new answer replaces the previous one, so say again everything you changed and why.
