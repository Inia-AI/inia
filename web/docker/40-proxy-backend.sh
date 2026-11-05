#!/bin/sh
set -eu

PROXY_BACKEND_URL="${PROXY_BACKEND_URL:-http://api:80}"
# With a trailing slash, nginx would send every proxied request to the root path of the API.
PROXY_BACKEND_URL="${PROXY_BACKEND_URL%/}"
RESOLVER="$(awk '/^nameserver/ { print $2; exit }' /etc/resolv.conf)"
case "$RESOLVER" in
    *:*) RESOLVER="[$RESOLVER]" ;;
esac

sed -i \
    -e "s|PROXY_BACKEND_URL_PLACEHOLDER|$PROXY_BACKEND_URL|g" \
    -e "s|RESOLVER_PLACEHOLDER|$RESOLVER|g" \
    /etc/nginx/conf.d/default.conf
