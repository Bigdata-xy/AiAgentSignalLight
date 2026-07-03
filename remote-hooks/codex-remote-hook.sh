#!/usr/bin/env bash
set +e

env_file="$HOME/.signal-light/env"
if [ -f "$env_file" ]; then
  # shellcheck disable=SC1090
  . "$env_file"
fi

event_name=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    --event|-EventName)
      event_name="$2"
      shift 2
      ;;
    *)
      shift
      ;;
  esac
done

payload="$(cat)"
bridge_url="${SIGNAL_LIGHT_BRIDGE_URL:-http://127.0.0.1:37631/api/events}"
bridge_token="${SIGNAL_LIGHT_BRIDGE_TOKEN:-}"
remote_host="${SIGNAL_LIGHT_REMOTE_HOST:-$(hostname 2>/dev/null)}"
remote_user="${SIGNAL_LIGHT_REMOTE_USER:-${USER:-}}"
workspace="${PWD:-}"
session_id="${CODEX_SESSION_ID:-${CODEX_CONVERSATION_ID:-${TERM_SESSION_ID:-$$}}}"

if [ -z "$bridge_token" ]; then
  mkdir -p "$HOME/.signal-light/diagnostics" 2>/dev/null
  printf '%s\n' "SIGNAL_LIGHT_BRIDGE_TOKEN is not set." > "$HOME/.signal-light/diagnostics/latest-remote-hook-error.txt" 2>/dev/null
  exit 0
fi

if command -v python3 >/dev/null 2>&1; then
  body="$(
    SIGNAL_LIGHT_EVENT_NAME="$event_name" \
    SIGNAL_LIGHT_RAW_PAYLOAD="$payload" \
    SIGNAL_LIGHT_REMOTE_HOST_VALUE="$remote_host" \
    SIGNAL_LIGHT_REMOTE_USER_VALUE="$remote_user" \
    SIGNAL_LIGHT_WORKSPACE_VALUE="$workspace" \
    SIGNAL_LIGHT_SESSION_VALUE="$session_id" \
    python3 - <<'PY'
import json
import os

raw = os.environ.get("SIGNAL_LIGHT_RAW_PAYLOAD", "")
title = ""
session_id = os.environ.get("SIGNAL_LIGHT_SESSION_VALUE", "")
try:
    parsed = json.loads(raw) if raw.strip() else {}
    def find_text(value, names):
        if isinstance(value, dict):
            for name in names:
                item = value.get(name)
                if isinstance(item, str) and item.strip() and item.lower() not in ("true", "false"):
                    return item
            for item in value.values():
                text = find_text(item, names)
                if text:
                    return text
        if isinstance(value, list):
            for item in value:
                text = find_text(item, names)
                if text:
                    return text
        return ""
    title = find_text(parsed, ["prompt", "user_prompt", "userPrompt", "message", "text", "input"])
    parsed_session = find_text(parsed, [
        "session_id", "sessionId", "session",
        "conversation_id", "conversationId", "conversation",
        "codex_session_id", "codexSessionId",
        "thread_id", "threadId",
        "terminal_id", "terminalId", "terminal",
        "process_id", "processId", "pid"
    ])
    if parsed_session:
        session_id = parsed_session
except Exception:
    pass

print(json.dumps({
    "schemaVersion": 1,
    "codexEvent": os.environ.get("SIGNAL_LIGHT_EVENT_NAME", ""),
    "source": "remote-ssh",
    "adapter": "codex-remote-hooks",
    "remoteHost": os.environ.get("SIGNAL_LIGHT_REMOTE_HOST_VALUE", ""),
    "remoteUser": os.environ.get("SIGNAL_LIGHT_REMOTE_USER_VALUE", ""),
    "workspace": os.environ.get("SIGNAL_LIGHT_WORKSPACE_VALUE", ""),
    "sessionId": session_id,
    "title": title,
    "payload": {
        "codexEvent": os.environ.get("SIGNAL_LIGHT_EVENT_NAME", ""),
        "rawPayload": raw
    }
}, ensure_ascii=False))
PY
  )"
else
  escaped_payload="$(printf '%s' "$payload" | sed 's/\\/\\\\/g; s/"/\\"/g')"
  body="{\"schemaVersion\":1,\"codexEvent\":\"$event_name\",\"source\":\"remote-ssh\",\"adapter\":\"codex-remote-hooks\",\"remoteHost\":\"$remote_host\",\"remoteUser\":\"$remote_user\",\"workspace\":\"$workspace\",\"sessionId\":\"$session_id\",\"payload\":{\"codexEvent\":\"$event_name\",\"rawPayload\":\"$escaped_payload\"}}"
fi

if command -v curl >/dev/null 2>&1; then
  curl -fsS -X POST "$bridge_url" \
    -H "Authorization: Bearer $bridge_token" \
    -H "Content-Type: application/json" \
    --data "$body" >/dev/null 2>&1
  code=$?
elif command -v python3 >/dev/null 2>&1; then
  SIGNAL_LIGHT_BRIDGE_URL_VALUE="$bridge_url" \
  SIGNAL_LIGHT_BRIDGE_TOKEN_VALUE="$bridge_token" \
  SIGNAL_LIGHT_REQUEST_BODY="$body" \
  python3 - <<'PY' >/dev/null 2>&1
import os
import sys
import urllib.error
import urllib.request

url = os.environ.get("SIGNAL_LIGHT_BRIDGE_URL_VALUE", "")
token = os.environ.get("SIGNAL_LIGHT_BRIDGE_TOKEN_VALUE", "")
body = os.environ.get("SIGNAL_LIGHT_REQUEST_BODY", "").encode("utf-8")

request = urllib.request.Request(
    url,
    data=body,
    headers={
        "Authorization": f"Bearer {token}",
        "Content-Type": "application/json",
    },
    method="POST",
)

try:
    with urllib.request.urlopen(request, timeout=5) as response:
        sys.exit(0 if 200 <= response.status < 300 else 1)
except Exception:
    sys.exit(1)
PY
  code=$?
else
  code=127
fi

if [ "$code" -ne 0 ]; then
  mkdir -p "$HOME/.signal-light/diagnostics" 2>/dev/null
  {
    printf 'time=%s\n' "$(date -Iseconds 2>/dev/null)"
    printf 'event=%s\n' "$event_name"
    printf 'bridge_url=%s\n' "$bridge_url"
    printf 'exit_code=%s\n' "$code"
  } > "$HOME/.signal-light/diagnostics/latest-remote-hook-error.txt" 2>/dev/null
fi

exit 0
