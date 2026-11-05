#!/usr/bin/env bash
# Checks the rules the one-command stack keeps, on the configuration Docker Compose resolves:
#
#   docker/check-compose.sh <compose file>
#
# In the one repository the compose file is compose.yaml at its root; in the platform master it is
# distribution/compose.yaml.
# Each failure prints one line, and the script exits 1 when any rule fails. It needs Docker
# Compose v2 and jq, and builds nothing.
set -euo pipefail

compose_file="$(realpath "${1:?Pass the compose file to check}")"

config="$(
    ANTHROPIC_API_KEY=check-compose-anthropic-key \
    INIA_ADMIN_PASSWORD=CheckCompose1 \
    docker compose -f "$compose_file" config --format json
)"

env_template="$(dirname "$compose_file")/.env.example"
template_text="$(cat "$env_template" 2>/dev/null || true)"

failures="$(jq -r --arg template "$template_text" --arg template_path "$env_template" '
  def service_env: (.environment // {});
  def dotnet: ((.build.dockerfile // "") == "docker/Dockerfile" and .entrypoint == null)
    or ((service_env | keys) | any(startswith("ASPNETCORE_") or startswith("PlatformSettings__")));
  def secret_name: test("api_?key|password|secret|token|connectionstrings__"; "i");
  def models: map(select(. != "")) | sort | join(",");

  (.services | to_entries[] | .key as $name | .value as $service | ($service | service_env) as $env
    | (
        (if $service | dotnet
         then {ASPNETCORE_ENVIRONMENT: "Production", FORCE_AWS_MODE: "false"} | to_entries[]
           | select(($env[.key] // "") != .value)
           | "\($name): \(.key) is \($env[.key] // "unset"), not \(.value)"
         else empty end),
        (if ($service.image // "") | test("azurecr\\.io")
         then "\($name): pulls \($service.image) from a private registry" else empty end),
        ($env | keys[] | select(test("OAUTH_CLIENT_(ID|SECRET)"))
         | "\($name): hands the web container \(.)"),
        ($env | to_entries[] | select((.key | secret_name) and (.key | endswith("_FILE") | not) and ((.value // "") != ""))
         | "\($name): passes \(.key) as an environment variable, which docker inspect shows"),
        (($service.build.args // {}) | to_entries[] | select((.key | secret_name) and ((.value // "") != ""))
         | "\($name): passes \(.key) as a build argument, which the image history shows"),
        (($service.configs // [])[] | select((.mode // "" | tostring) as $mode | $mode != "0400" and $mode != "256")
         | "\($name): mounts the config \(.source) without mode 0400, so every user in the container reads it"),
        (if ($service.network_mode // "") == "host"
         then "\($name): shares the host network, so its ports skip the 127.0.0.1 binding" else empty end),
        (if ($name == "postgres" or $name == "agent-runtime") and (($service.ports // []) | length) > 0
         then "\($name): publishes a host port" else empty end),
        (($service.ports // [])[] | select((.host_ip // "") != "127.0.0.1")
         | "\($name): publishes port \(.published) on \(.host_ip // "every address"), not 127.0.0.1")
      )),
  [.services[] | (.depends_on // {}) | to_entries[] | select(.value.condition == "service_completed_successfully") | .key] as $awaited
  | (.services | to_entries[] | select((.value.restart // "no") == "no") | .key | select(IN($awaited[]) | not)
     | "\(.): has no restart policy, so it runs once, and no service waits for it to complete, so docker compose up --wait fails when it exits"),
  (.configs // {} | to_entries[] | select(((.value.content // "") == "") and .value.file == null and .value.environment == null)
   | "config \(.key) has no content, so Compose mounts the project folder in its place"),
  ([.services.api.environment // {} | to_entries[] | select(.key | startswith("PlatformSettings__AgentRuntime__AllowedModels__")) | .value] | models) as $platform
  | ((.services["agent-runtime"].environment.ANTHROPIC_ALLOWED_MODELS // "") | split(",") | models) as $runtime
  | (if $platform != $runtime
     then "api and agent-runtime allow different models: [\($platform)] and [\($runtime)]" else empty end),
  ($template | [match("(?:^|\n)[ \t]*(ASPNETCORE_ENVIRONMENT|FORCE_AWS_MODE|FORCE_AZURE_MODE)="; "g").captures[0].string][]
   | "\($template_path) sets \(.)")
' <<< "$config")"

if [ -n "$failures" ]; then
    echo "$failures"
    exit 1
fi
echo "compose check passed: $compose_file"
