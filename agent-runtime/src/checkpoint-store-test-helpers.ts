import type { CheckpointStore } from "./checkpoint-store.js";

// The two members every in-memory CheckpointStore stub answers the same way. `open` hands back a
// file descriptor, which an in-memory store has none of, and no stub drives the reaper — the
// route and the sweep are both exercised against the real on-disk store instead.
export const NO_SERVED_FILES: Pick<CheckpointStore, "open" | "reapOrphans"> = {
  open: () => Promise.resolve(undefined),
  reapOrphans: () => Promise.resolve(0),
};
