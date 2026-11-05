import { rm } from "node:fs/promises";
import type { RemoveTreeFn } from "./setpriv.js";

// Test trees are owned by the test's own uid, so a plain rm is what the pool remover would run.
export const removeOwnTree: RemoveTreeFn = (path) => rm(path, { recursive: true, force: true });
