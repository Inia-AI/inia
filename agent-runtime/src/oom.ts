import { writeFileSync } from "node:fs";
import { RUN_CHILD_OOM_SCORE_ADJ } from "./config.js";

const OOM_SCORE_ADJ_PATH = "/proc/self/oom_score_adj";

export function biasOomScoreAdj(path: string = OOM_SCORE_ADJ_PATH): boolean {
  try {
    writeFileSync(path, String(RUN_CHILD_OOM_SCORE_ADJ));
    return true;
  } catch {
    return false;
  }
}
