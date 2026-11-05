# The default builds the toolbelt base below from public inputs (ADR 0149).
ARG BASE_IMAGE=toolbelt

FROM oven/bun:1.4.4-debian@sha256:93d5032fe06d163427822dc1598f420363759bb18155c9cc90eb1be05a64a19e AS python-builder

COPY --from=ghcr.io/astral-sh/uv:0.12.0@sha256:606e70c71c852d03f611b1e56a195d08648507018a7057fab82c4974c4eae105 /uv /usr/local/bin/uv
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && rm -rf /var/lib/apt/lists/*
COPY toolbelt/python/pyproject.toml toolbelt/python/uv.lock /opt/python-toolbelt/
RUN cd /opt/python-toolbelt \
    && UV_PROJECT_ENVIRONMENT=/opt/venv UV_PYTHON_PREFERENCE=only-system \
       uv sync --frozen --no-install-project --no-dev \
    && chmod -R go-w /opt/venv

FROM oven/bun:1.4.4-debian@sha256:93d5032fe06d163427822dc1598f420363759bb18155c9cc90eb1be05a64a19e AS toolbelt

# fontconfig validates a cache against its font directory's mtime to the nanosecond, and the
# layer tar keeps whole seconds only: a cache recorded against the live mtime is stale in the
# pulled image and every soffice start rescans the whole font tree. The font directories are
# truncated to whole seconds before fc-cache records them, so the recorded and the shipped
# mtimes agree (ADR 0134; asserted by src/toolbelt.test.ts).
# The bun image is pinned by digest and its Debian packages age from the day it was built, so
# every build upgrades them to the Debian fixes published since (ADR 0153).
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get upgrade -y --no-install-recommends --with-new-pkgs \
    && apt-get install -y --no-install-recommends \
      bubblewrap \
      ca-certificates \
      curl \
      fontconfig \
      fonts-crosextra-caladea \
      fonts-crosextra-carlito \
      fonts-dejavu \
      fonts-liberation \
      fonts-noto-cjk \
      git \
      jq \
      libreoffice-calc \
      libreoffice-writer \
      pandoc \
      poppler-utils \
      python3 \
      qpdf \
      socat \
    && find /usr/share/fonts /usr/local/share/fonts -type d \
         -exec sh -c 'for d; do touch -h -d "@$(stat -c %Y "$d")" "$d"; done' sh {} + \
    && fc-cache -f \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

COPY --from=python-builder /opt/venv /opt/venv
ENV PATH="/opt/venv/bin:${PATH}"

# Every soffice launcher (soffice, libreoffice, localc, lowriter, the absolute program path)
# resolves to program/soffice, so the wrapper replaces that one file and the diversion keeps
# a package upgrade from writing over it (ADR 0139; asserted by src/toolbelt.test.ts).
COPY toolbelt/libreoffice/soffice /usr/local/share/soffice-wrapper
RUN dpkg-divert --local --rename --divert /usr/lib/libreoffice/program/soffice.real \
      --add /usr/lib/libreoffice/program/soffice \
    && install -o root -g root -m 0755 /usr/local/share/soffice-wrapper /usr/lib/libreoffice/program/soffice \
    && rm /usr/local/share/soffice-wrapper

COPY toolbelt/node/package.json toolbelt/node/bun.lock /opt/node/
RUN cd /opt/node && bun install --frozen-lockfile --production \
    && chmod -R go-w /opt/node
ENV NODE_PATH=/opt/node/node_modules

# The bun image's OCI labels describe bun, not this image; the release and base-image workflows
# set the real source, revision and creation time.
LABEL org.opencontainers.image.title="agent-runtime-base" \
      org.opencontainers.image.description="Toolbelt base image for the agent runtime: LibreOffice, pandoc, poppler, qpdf, Liberation + Carlito/Caladea + DejaVu + Noto CJK fonts, locked Python and Node document toolchains" \
      org.opencontainers.image.vendor="Inia AI" \
      org.opencontainers.image.source="" \
      org.opencontainers.image.url="" \
      org.opencontainers.image.version="" \
      org.opencontainers.image.revision="" \
      org.opencontainers.image.created=""

FROM ${BASE_IMAGE} AS base
RUN find / -xdev -type f -perm /6000 -exec chmod a-s {} +
# The deployed container always has /workspaces (a tmpfs); creating it in the image makes the
# SDK's bwrap translation deterministic, because it skips a denied root that does not exist.
# Without this the scripted contract run's argv fixture matches only in containers that happen
# to mount one, so `bun run check` passes under a bare `docker run` and fails under compose.
RUN mkdir -p /workspaces
WORKDIR /app
COPY package.json bun.lock ./

ARG APP_VERSION=""
ARG APP_REVISION=""
ENV AGENT_RUNTIME_VERSION=${APP_VERSION} \
    AGENT_RUNTIME_REVISION=${APP_REVISION}
RUN test -z "$APP_VERSION" || test "$APP_VERSION" = "$(jq -r .version package.json)" \
    || { echo "APP_VERSION ${APP_VERSION} does not match package.json $(jq -r .version package.json)" >&2; exit 1; }

FROM base AS dev
RUN bun install --frozen-lockfile \
    && rm -rf /app/node_modules/@anthropic-ai/*-musl \
    && chmod -R go-w /app/node_modules
COPY tsconfig.json biome.json .editorconfig bunfig.toml ./
# The environment section's tool list is checked against these manifests (src/environment-section.test.ts).
COPY toolbelt/python/pyproject.toml ./toolbelt/python/
COPY toolbelt/node/package.json ./toolbelt/node/
COPY src ./src
COPY evals ./evals
ENV CLAUDE_CONFIG_DIR=/home/bun/.claude
ENTRYPOINT ["setpriv", "--reuid=1000", "--regid=1000", "--clear-groups", \
            "--inh-caps=+setuid,+setgid", "--ambient-caps=+setuid,+setgid", "--"]
CMD ["bun", "--watch", "src/main.ts"]

FROM base AS prod
RUN bun install --frozen-lockfile --production \
    && rm -rf /app/node_modules/@anthropic-ai/*-musl \
    && chmod -R go-w /app/node_modules
COPY src ./src
ENV CLAUDE_CONFIG_DIR=/home/bun/.claude
ENTRYPOINT ["setpriv", "--reuid=1000", "--regid=1000", "--clear-groups", \
            "--inh-caps=+setuid,+setgid", "--ambient-caps=+setuid,+setgid", "--"]
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD ["bun", "-e", "const r = await fetch(`http://127.0.0.1:${process.env.PORT ?? 8130}/healthz`); if (!r.ok) { process.exit(1); }"]
CMD ["bun", "src/main.ts"]

LABEL org.opencontainers.image.title="agent-runtime" \
      org.opencontainers.image.description="Claude Agent SDK sidecar that runs report-generation agent runs for the Inia platform" \
      org.opencontainers.image.vendor="Inia AI"
