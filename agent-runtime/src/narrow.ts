export type Narrower = {
  fail(message: string): never;
  asRecord(value: unknown, path: string): Record<string, unknown>;
  rejectUnknownFields(record: Record<string, unknown>, allowed: readonly string[], parent?: string): void;
  requireString(record: Record<string, unknown>, key: string, parent?: string): string;
  requirePossiblyEmptyString(record: Record<string, unknown>, key: string, parent?: string): string;
  optionalString(record: Record<string, unknown>, key: string, parent?: string): string | undefined;
  requireBoolean(record: Record<string, unknown>, key: string, parent?: string): boolean;
  optionalBoolean(record: Record<string, unknown>, key: string, parent?: string): boolean | undefined;
  requireNumber(record: Record<string, unknown>, key: string, parent?: string): number;
  requireNonNegativeNumber(record: Record<string, unknown>, key: string, parent?: string): number;
  requirePositiveNumber(record: Record<string, unknown>, key: string, parent?: string): number;
  requirePositiveInteger(record: Record<string, unknown>, key: string, parent?: string): number;
  requireArray(value: unknown, path: string): unknown[];
  // Strictly undefined, not null — a boundary that skips withNullsAsAbsent still gets null rejected.
  optionalWith<T>(
    record: Record<string, unknown>,
    key: string,
    parseValue: (value: unknown, parse: Narrower, label: string) => T,
    parent?: string,
  ): T | undefined;
};

// System.Text.Json (the platform client) serializes an absent optional as a JSON null,
// so null here must be treated as absent, not rejected as present-but-wrong-type.
export function withNullsAsAbsent(record: Record<string, unknown>): Record<string, unknown> {
  const present: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(record)) {
    if (value !== null) {
      present[key] = value;
    }
  }
  return present;
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

export function createNarrower(toError: (message: string) => Error): Narrower {
  function fail(message: string): never {
    throw toError(message);
  }

  function pathOf(key: string, parent?: string): string {
    return parent === undefined ? key : `${parent}.${key}`;
  }

  function asRecord(value: unknown, path: string): Record<string, unknown> {
    if (!isRecord(value)) {
      fail(`${path} must be a JSON object`);
    }
    return value;
  }

  function requireString(record: Record<string, unknown>, key: string, parent?: string): string {
    const value = record[key];
    if (typeof value !== "string" || value === "") {
      fail(`${pathOf(key, parent)} must be a non-empty string`);
    }
    return value;
  }

  function requirePossiblyEmptyString(record: Record<string, unknown>, key: string, parent?: string): string {
    const value = record[key];
    if (typeof value !== "string") {
      fail(`${pathOf(key, parent)} must be a string`);
    }
    return value;
  }

  function requireBoolean(record: Record<string, unknown>, key: string, parent?: string): boolean {
    const value = record[key];
    if (typeof value !== "boolean") {
      fail(`${pathOf(key, parent)} must be a boolean`);
    }
    return value;
  }

  function requireNumber(record: Record<string, unknown>, key: string, parent?: string): number {
    const value = record[key];
    if (typeof value !== "number" || !Number.isFinite(value)) {
      fail(`${pathOf(key, parent)} must be a finite number`);
    }
    return value;
  }

  function requirePositiveNumber(record: Record<string, unknown>, key: string, parent?: string): number {
    const value = requireNumber(record, key, parent);
    if (value <= 0) {
      fail(`${pathOf(key, parent)} must be positive`);
    }
    return value;
  }

  const narrower: Narrower = {
    fail,
    asRecord,
    rejectUnknownFields(record, allowed, parent) {
      for (const key of Object.keys(record)) {
        if (!allowed.includes(key)) {
          fail(parent === undefined ? `unknown field "${key}"` : `unknown field "${key}" in ${parent}`);
        }
      }
    },
    requireString,
    requirePossiblyEmptyString,
    optionalString(record, key, parent) {
      return record[key] === undefined ? undefined : requireString(record, key, parent);
    },
    requireBoolean,
    optionalBoolean(record, key, parent) {
      return record[key] === undefined ? undefined : requireBoolean(record, key, parent);
    },
    requireNumber,
    requireNonNegativeNumber(record, key, parent) {
      const value = requireNumber(record, key, parent);
      if (value < 0) {
        fail(`${pathOf(key, parent)} must not be negative`);
      }
      return value;
    },
    requirePositiveNumber,
    requirePositiveInteger(record, key, parent) {
      const value = requirePositiveNumber(record, key, parent);
      if (!Number.isInteger(value)) {
        fail(`${pathOf(key, parent)} must be a whole number`);
      }
      return value;
    },
    requireArray(value, path) {
      if (!Array.isArray(value)) {
        fail(`${path} must be an array`);
      }
      return value;
    },
    optionalWith(record, key, parseValue, parent) {
      const value = record[key];
      return value === undefined ? undefined : parseValue(value, narrower, pathOf(key, parent));
    },
  };
  return narrower;
}
