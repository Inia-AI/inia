# syntax=docker/dockerfile:1

# Builds the API (`--target api`) and the WebApp (`--target webapp`) for the CPU of the build
# platform, amd64 or arm64, with the repository root as the only context. Both run on the `base`
# stage below, which starts from a public image. A pipeline that keeps a prebuilt base passes it
# as BASE_IMAGE.
ARG BASE_IMAGE=base

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
ARG TARGETARCH

# The tag holds the OS packages of the day Microsoft last rebuilt it. The upgrade brings in every
# security fix the distribution published before this build.
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get upgrade -y \
    && apt-get install -y --no-install-recommends wget gnupg curl \
    && rm -rf /var/lib/apt/lists/*

# Documents that name Arial, Times New Roman, Courier New, Calibri or Cambria render with their
# free metric twins (Liberation, Carlito, Caladea); fontconfig's metric aliases map each name to its
# twin. DejaVu stays the generic sans, serif and mono: fontconfig prefers it there, and without it
# every generic family falls to Liberation Mono.
RUN curl -fsSL https://dl-ssl.google.com/linux/linux_signing_key.pub \
       | gpg --dearmor -o /usr/share/keyrings/google-chrome.gpg \
    && echo "deb [signed-by=/usr/share/keyrings/google-chrome.gpg] http://dl.google.com/linux/chrome/deb/ stable main" \
       > /etc/apt/sources.list.d/google-chrome.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
      apt-utils \
      libgdiplus \
      libc6-dev \
      fontconfig \
      fonts-crosextra-caladea \
      fonts-crosextra-carlito \
      fonts-dejavu-core \
      fonts-dejavu-mono \
      fonts-liberation \
      unzip \
      libasound2t64 \
      fonts-noto-cjk \
      libatk-bridge2.0-0t64 \
      libatk1.0-0t64 \
      libatspi2.0-0t64 \
      libcups2t64 \
      libdbus-1-3 \
      libdrm2 \
      libgbm1 \
      libgtk-3-0t64 \
      libnspr4 \
      libnss3 \
      libxcomposite1 \
      libxdamage1 \
      libxfixes3 \
      libxrandr2 \
      libxshmfence1 \
      xdg-utils \
    && fc-cache -f -v \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Chrome for Testing names its Linux builds linux64 and linux-arm64.
RUN case "$TARGETARCH" in \
      amd64) DRIVER_PLATFORM=linux64 ;; \
      arm64) DRIVER_PLATFORM=linux-arm64 ;; \
      *) echo "No Chrome build for $TARGETARCH" >&2; exit 1 ;; \
    esac \
    && apt-get update \
    && apt-get install -y --no-install-recommends google-chrome-stable \
    && CHROME_VERSION=$(google-chrome --version | awk '{ print $3 }' | cut -d'.' -f1) \
    && CHROME_DRIVER_RELEASE=$(curl -fsS https://googlechromelabs.github.io/chrome-for-testing/LATEST_RELEASE_$CHROME_VERSION) \
    && wget -q -O /tmp/chromedriver.zip https://storage.googleapis.com/chrome-for-testing-public/${CHROME_DRIVER_RELEASE}/${DRIVER_PLATFORM}/chromedriver-${DRIVER_PLATFORM}.zip \
    && unzip /tmp/chromedriver.zip -d /tmp/ \
    && mv /tmp/chromedriver-${DRIVER_PLATFORM}/chromedriver /usr/local/bin/ \
    && chmod +x /usr/local/bin/chromedriver \
    && rm -rf /tmp/chromedriver* \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Fail the build when a named font or a generic family stops resolving where documents expect it.
RUN for pair in 'Arial:Liberation Sans' 'Times New Roman:Liberation Serif' 'Courier New:Liberation Mono' \
      'Calibri:Carlito' 'Cambria:Caladea' \
      'sans-serif:DejaVu Sans' 'serif:DejaVu Serif' 'monospace:DejaVu Sans Mono'; do \
      requested="${pair%%:*}"; expected="${pair#*:}"; \
      resolved="$(fc-match -f '%{family[0]}' "$requested")"; \
      [ "$resolved" = "$expected" ] || { echo "$requested resolves to $resolved, not $expected" >&2; exit 1; }; \
    done

# The trust store and the TLS library .NET uses, so a build log shows which security fixes it carries.
RUN dpkg-query -W -f '${Package} ${Version}\n' ca-certificates libssl3t64

RUN mkdir -p /tmp/chrome /var/chrome /var/crash /app/certs /app/monitoring /app/logs \
    && chmod -R 755 /tmp/chrome /var/chrome /var/crash

COPY --chmod=755 docker/base/healthcheck/chrome-check.sh /opt/healthcheck/
COPY --chmod=755 docker/base/healthcheck/combined-check.sh /opt/healthcheck/

ENV CHROME_BIN=/usr/bin/google-chrome \
    CHROME_PATH=/usr/bin/google-chrome \
    CHROMIUM_FLAGS="--headless --disable-gpu --no-sandbox"

HEALTHCHECK --interval=30s --timeout=10s --retries=3 CMD /opt/healthcheck/combined-check.sh

# The SDK runs on the build machine's own CPU and publishes for the target's, so a cross-platform
# build emulates no compiler.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# The SDK sends usage data to Microsoft on every command unless told not to.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /src

# Restore reads the sources as well as the project files: the Web SDK adds the package that serves
# the Blazor script only to a project whose Razor components it can see.
COPY . .

# The downloads stay in a build cache, so a source change downloads no package again. The packages
# stay in this layer for publish, because BuildKit may drop a cache while it keeps the layers built
# on it.
RUN --mount=type=cache,id=nuget-http,target=/root/.local/share/NuGet/http-cache \
    dotnet restore IniaAi.WebApi/IniaAi.WebApi.csproj -a "$TARGETARCH" \
    && dotnet restore IniaAi.WebApp/IniaAi.WebApp.csproj -a "$TARGETARCH"

# Declared after the restore, so a new version reuses the restored packages.
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ENV PUBLISH_PROPERTIES="-c $BUILD_CONFIGURATION -a $TARGETARCH --self-contained false -p:UseAppHost=false -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$ASSEMBLY_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION"

FROM build AS publish-api
RUN dotnet publish IniaAi.WebApi/IniaAi.WebApi.csproj --no-restore $PUBLISH_PROPERTIES -o /app/publish

FROM build AS publish-webapp
RUN dotnet publish IniaAi.WebApp/IniaAi.WebApp.csproj --no-restore $PUBLISH_PROPERTIES -o /app/publish

FROM ${BASE_IMAGE} AS app
WORKDIR /app
EXPOSE 80
# The driver path sits here, not in the base stage, so it also reaches an image built on a prebuilt
# base from before the setting existed. Every base installs chromedriver at this path.
ENV ASPNETCORE_HTTP_PORTS=80 \
    CHROMEDRIVER_PATH=/usr/local/bin/chromedriver \
    MALLOC_TRIM_THRESHOLD_=131072 \
    MALLOC_MMAP_THRESHOLD_=131072
# glibc malloc gives freed memory back to the OS sooner: it trims the heap top above 128 KB of
# free memory, and serves each allocation above 128 KB from mmap, which a free releases at once.

# The PDF reader starts as root, as the app runs, and switches to pdf-reader, a user that owns no
# file, so a crafted PDF that takes over the reader cannot read the app's secrets or its logs.
RUN useradd --system --no-create-home --shell /usr/sbin/nologin pdf-reader \
    && mkdir -p /app/logs \
    && chmod 700 /app/logs
HEALTHCHECK --interval=30s --timeout=30s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:80/health || exit 1

FROM app AS api
COPY --from=publish-api /app/publish .
ENTRYPOINT ["dotnet", "IniaAi.WebApi.dll"]

FROM app AS webapp
COPY --from=publish-webapp /app/publish .
ENTRYPOINT ["dotnet", "IniaAi.WebApp.dll"]
