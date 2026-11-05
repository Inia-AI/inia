import { z } from "zod";

export const submitResultSchema = z.strictObject({
  resultId: z.string().min(1),
  data: z.unknown(),
});

export type SubmitResult = z.infer<typeof submitResultSchema>;

export class ContractValidationError extends Error {}

export function parseSubmitResult(body: unknown): SubmitResult {
  const result = submitResultSchema.safeParse(body);
  if (!result.success) {
    throw new ContractValidationError(describeIssues(result.error));
  }
  return result.data;
}

function describeIssues(error: z.ZodError): string {
  return error.issues
    .map((issue) =>
      issue.path.length === 0 ? issue.message : `${issue.path.map(String).join(".")}: ${issue.message}`,
    )
    .join("; ");
}
