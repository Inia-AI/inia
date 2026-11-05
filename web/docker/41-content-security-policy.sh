#!/bin/sh
set -euf

NGINX_CONF="${1:-/etc/nginx/conf.d/default.conf}"
DEFAULT_CONNECT_SRC="'self' https:"
CSP_CONNECT_SRC="${CSP_CONNECT_SRC:-$DEFAULT_CONNECT_SRC}"
CSP_MODE="${CSP_MODE:-enforce}"
EXAMPLE_CONNECT_SRC="'self' https://accounts.google.com"

refuse() {
    echo "$1 The web container did not start. Correct the setting and start it again." >&2
    exit 1
}

refuse_connect_src() {
    refuse "CSP_CONNECT_SRC is \"$CSP_CONNECT_SRC\", $1. Give sources separated by spaces, such as $EXAMPLE_CONNECT_SRC, or leave it unset to allow $DEFAULT_CONNECT_SRC."
}

# A quote, semicolon, dollar sign, backslash or line break would end the policy or the nginx directive early.
CSP_SOURCE_PATTERN="('[a-z0-9-]+'|[A-Za-z0-9*][A-Za-z0-9.:/*_%+-]*)"
if [ "$(printf '%s' "$CSP_CONNECT_SRC" | wc -l)" -ne 0 ] \
    || ! printf '%s' "$CSP_CONNECT_SRC" | grep -Eq "^$CSP_SOURCE_PATTERN( +$CSP_SOURCE_PATTERN)*\$"; then
    refuse_connect_src "which is not a list of sources"
fi

for source in $CSP_CONNECT_SRC; do
    case "$(printf '%s' "$source" | tr '[:upper:]' '[:lower:]')" in
        '*' | http: | ws: | wss: | data: | blob: | http://* | "'unsafe-"*)
            refuse_connect_src "and $source would allow more connections than $DEFAULT_CONNECT_SRC" ;;
        *_placeholder*)
            refuse_connect_src "and $source is a placeholder of the container's own configuration" ;;
    esac
done

case "$CSP_MODE" in
    enforce)
        sed -e "s|CSP_CONNECT_SRC_PLACEHOLDER|$CSP_CONNECT_SRC|" "$NGINX_CONF" > "$NGINX_CONF.csp" ;;
    report-only)
        sed -e "/CSP_CONNECT_SRC_PLACEHOLDER/{
h
s|CSP_CONNECT_SRC_PLACEHOLDER|$DEFAULT_CONNECT_SRC|
p
g
s|add_header Content-Security-Policy |add_header Content-Security-Policy-Report-Only |
s|CSP_CONNECT_SRC_PLACEHOLDER|$CSP_CONNECT_SRC|
}" "$NGINX_CONF" > "$NGINX_CONF.csp" ;;
    *)
        refuse "CSP_MODE is \"$CSP_MODE\". Set it to enforce or report-only, or leave it unset to enforce the policy." ;;
esac
cat "$NGINX_CONF.csp" > "$NGINX_CONF"
rm -f "$NGINX_CONF.csp"
