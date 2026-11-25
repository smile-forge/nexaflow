#!/usr/bin/env bash
# PreToolUse hook: hands require-nfi-reads.ps1 only the calls that could read a checkout's own files, so the rest
# pay no PowerShell start-up. jq, sed and grep come with Git for Windows' bash, the shell Claude Code runs hooks in.
#
# What passes here is what the PowerShell half would let through anyway:
#   - a reader not where a command starts — `grep` inside `nfi ask '…'`, `Type` in a C# edit script;
#   - a reader straight after a pipe, which reads the pipe (`… | Select-String error`);
#   - a reader whose file is a variable (`Get-Content "$s\b1.log"`), which no hook can resolve;
#   - nfi's own `graph cat`;
#   - the Read, Grep and Glob tools given an absolute path that names no Nexaflow checkout.
input=$(cat)
tool=$(printf '%s' "$input" | jq -r '.tool_name // empty')
case "$tool" in
  Read|Grep|Glob)
    path=$(printf '%s' "$input" | jq -r '.tool_input.file_path // .tool_input.path // empty')
    case "${path,,}" in
      ''|*nexaflow*) ;;
      [a-z]:*|/*|\\*) exit 0 ;;   # absolute and nowhere near a checkout; a relative path is the session's folder
    esac ;;
  Bash|PowerShell)
    printf '%s' "$input" | jq -r '.tool_input.command // empty' |
      sed -E \
        -e 's/graph[[:space:]]+cat/graph-show/gI' \
        -e 's/\|[[:space:]]*(&[[:space:]]*)?(select-string|sls|grep|egrep|fgrep|rg|head|tail|sed|awk|less|more)([^a-z0-9-]|$)/| piped\3/gI' \
        -e 's/(get-content|select-string|gc|sls|cat|type)(([[:space:]]+-[a-z]+)*)[[:space:]]+"?\$/variable /gI' |
      grep -qiE '(^|[;&|({=])[[:space:]]*([a-z_][a-z0-9_]*=[^[:space:]]*[[:space:]]+)*(get-content|select-string|gc|sls|cat|type|head|tail|less|more|grep|egrep|fgrep|rg|sed|awk|git([[:space:]]+-C[[:space:]]+[^[:space:]]+)?[[:space:]]+grep)([^a-z0-9-]|$)' ||
      exit 0 ;;
  *) exit 0 ;;
esac
printf '%s' "$input" | pwsh -NoLogo -NoProfile -NonInteractive -File "$(dirname "$0")/require-nfi-reads.ps1"
