#!/bin/sh
set -euf

NGINX_CONF="${1:-/etc/nginx/conf.d/default.conf}"
INDEX_HTML="${2:-/usr/share/nginx/html/index.html}"
AWS_WAF_INTEGRATION_URL="${AWS_WAF_INTEGRATION_URL:-}"
AWS_WAF_SOURCE="https://*.awswaf.com"

rewrite() {
    sed -e "$1" "$2" > "$2.waf"
    cat "$2.waf" > "$2"
    rm -f "$2.waf"
}

if [ -z "$AWS_WAF_INTEGRATION_URL" ]; then
    rewrite "s| AWS_WAF_SOURCE_PLACEHOLDER||g" "$NGINX_CONF"
    exit 0
fi

if ! printf '%s' "$AWS_WAF_INTEGRATION_URL" | grep -Eq '^https://[a-z0-9-]+(\.[a-z0-9-]+)*\.awswaf\.com(/[A-Za-z0-9_-]+)*/?$'; then
    echo "AWS_WAF_INTEGRATION_URL is \"$AWS_WAF_INTEGRATION_URL\", which is not an AWS WAF integration URL. Copy it from the web ACL's application integration page, such as https://abc123.edge.sdk.awswaf.com/abc123/def456, or leave it unset. The web container did not start. Correct the setting and start it again." >&2
    exit 1
fi

rewrite "s| AWS_WAF_SOURCE_PLACEHOLDER| $AWS_WAF_SOURCE|g" "$NGINX_CONF"
# A restart runs the script again on the page it already changed.
if ! grep -q 'name="aws-waf-integration-url"' "$INDEX_HTML"; then
    rewrite "s|</head>|<meta name=\"aws-waf-integration-url\" content=\"${AWS_WAF_INTEGRATION_URL%/}\"></head>|" "$INDEX_HTML"
fi
