# syntax=docker/dockerfile:1

# Builds the app into an nginx image with this folder as the only context. The bundle reaches the
# API at its own origin, where nginx proxies /api/ to PROXY_BACKEND_URL. VERSION is the
# version line at the foot of the layout; without it the line is absent.

# The bundle is the same for every CPU, so the build runs on the build machine's own.
FROM --platform=$BUILDPLATFORM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
ARG VERSION=""
ARG TERMS_URL=""
ARG PRIVACY_URL=""
RUN VITE_VERSION="$VERSION" VITE_TERMS_URL="$TERMS_URL" VITE_PRIVACY_URL="$PRIVACY_URL" npm run build

FROM nginx:alpine@sha256:060849a174666c53439dc7cecbf8f67e225c7cbfb1a44ca97915d9a172a81e6d
COPY --from=build /app/dist /usr/share/nginx/html

# Checksums the packaged default.conf with `apk manifest nginx` before it will edit it, which hangs
# with no route out. The copy below replaces that file, so the script can only ever no-op.
RUN rm -f /docker-entrypoint.d/10-listen-on-ipv6-by-default.sh

# The digest pin freezes the Alpine packages on the day that image was built. The upgrade brings in
# the fixes Alpine published since. The image pins nginx to one version in /etc/apk/world, so only a
# new digest moves nginx.
RUN apk upgrade --no-cache && nginx -v

COPY --chmod=755 docker/40-proxy-backend.sh /docker-entrypoint.d/40-proxy-backend.sh
COPY --chmod=755 docker/41-content-security-policy.sh /docker-entrypoint.d/41-content-security-policy.sh
COPY --chmod=755 docker/42-aws-waf-integration.sh /docker-entrypoint.d/42-aws-waf-integration.sh
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf

HEALTHCHECK --interval=30s --timeout=30s --start-period=5s --retries=3 \
  CMD wget --quiet --tries=1 --spider http://127.0.0.1:80/health || exit 1

CMD ["nginx", "-g", "daemon off;"]
