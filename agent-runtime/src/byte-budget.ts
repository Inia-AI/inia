import type { BudgetLogContext, Logger } from "./logger.js";

export type ByteReservation = {
  readonly bytes: number;
  readonly live: boolean;
  settleTo(bytes: number): void;
  tryGrowTo(bytes: number): boolean;
  release(): void;
};

export type ByteBudget = {
  tryReserve(bytes: number): ByteReservation | undefined;
  reservedBytes(): number;
};

export type TaggedByteReservation = ByteReservation & {
  tag(fields: BudgetLogContext): void;
};

export type InstrumentedByteBudget = {
  tryReserve(bytes: number, fields?: BudgetLogContext): TaggedByteReservation | undefined;
  reservedBytes(): number;
};

export function instrumentByteBudget(
  budget: ByteBudget,
  name: string,
  logger: Logger,
): InstrumentedByteBudget {
  return {
    tryReserve(bytes, fields = {}) {
      let context: BudgetLogContext = { ...fields };
      const logRefusal = (requestedBytes: number): void => {
        logger.warn("budget_refused", {
          budget: name,
          requestedBytes,
          reservedBytes: budget.reservedBytes(),
          ...context,
        });
      };
      const reservation = budget.tryReserve(bytes);
      if (reservation === undefined) {
        logRefusal(bytes);
        return undefined;
      }
      logger.info("budget_reserved", {
        budget: name,
        bytes,
        reservedBytes: budget.reservedBytes(),
        ...context,
      });
      return {
        get bytes() {
          return reservation.bytes;
        },
        get live() {
          return reservation.live;
        },
        tag: (moreFields) => {
          context = { ...context, ...moreFields };
        },
        settleTo: (nextBytes) => reservation.settleTo(nextBytes),
        tryGrowTo: (nextBytes) => {
          const granted = reservation.tryGrowTo(nextBytes);
          if (!granted) {
            logRefusal(nextBytes);
          }
          return granted;
        },
        release: () => {
          if (!reservation.live) {
            return;
          }
          const releasedBytes = reservation.bytes;
          reservation.release();
          logger.info("budget_released", {
            budget: name,
            bytes: releasedBytes,
            reservedBytes: budget.reservedBytes(),
            ...context,
          });
        },
      };
    },
    reservedBytes: () => budget.reservedBytes(),
  };
}

export function createByteBudget(maxBytes: number): ByteBudget {
  let reserved = 0;
  return {
    tryReserve(bytes) {
      if (reserved + bytes > maxBytes) {
        return undefined;
      }
      reserved += bytes;
      let charged = bytes;
      let live = true;
      return {
        get bytes() {
          return charged;
        },
        get live() {
          return live;
        },
        settleTo(nextBytes) {
          if (!live) {
            return;
          }
          reserved = Math.max(0, reserved - charged + nextBytes);
          charged = nextBytes;
        },
        tryGrowTo(nextBytes) {
          if (!live) {
            return false;
          }
          if (nextBytes <= charged) {
            return true;
          }
          if (reserved - charged + nextBytes > maxBytes) {
            return false;
          }
          reserved = reserved - charged + nextBytes;
          charged = nextBytes;
          return true;
        },
        release() {
          if (!live) {
            return;
          }
          live = false;
          reserved = Math.max(0, reserved - charged);
          charged = 0;
        },
      };
    },
    reservedBytes: () => reserved,
  };
}

export type ChargeBudget = {
  charge(bytes: number): void;
};

export function createChargeBudget(maxBytes: number, toError: (maxBytes: number) => Error): ChargeBudget {
  let remaining = maxBytes;
  return {
    charge(bytes) {
      remaining -= bytes;
      if (remaining < 0) {
        throw toError(maxBytes);
      }
    },
  };
}
