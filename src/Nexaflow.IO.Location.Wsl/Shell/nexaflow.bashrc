# Nexaflow's shell integration for bash, started as `bash --rcfile <this> -i`.
# It reads the startup files a login shell would, then marks each prompt, command and its end (OSC 133), so Nexaflow's
# terminal can hand over one command's output, and reports the working folder (OSC 7) for any terminal that shows it.
# Nexaflow takes the folder from Linux itself, never from this report, which any output can imitate.
# At each prompt it also writes the shell's variables (`env -0`) to the file Nexaflow names, for its Environment panel.

__nexaflow_environment="$NEXAFLOW_ENVIRONMENT"
unset NEXAFLOW_ENVIRONMENT

if [ -f /etc/profile ]; then . /etc/profile; fi
if [ -f "$HOME/.bash_profile" ]; then . "$HOME/.bash_profile"
elif [ -f "$HOME/.bash_login" ]; then . "$HOME/.bash_login"
elif [ -f "$HOME/.profile" ]; then . "$HOME/.profile"
elif [ -f "$HOME/.bashrc" ]; then . "$HOME/.bashrc"
fi

__nexaflow_urlpath() {
    local LC_ALL=C s="$1" out="" c i
    for (( i = 0; i < ${#s}; i++ )); do
        c="${s:i:1}"
        case "$c" in
            [a-zA-Z0-9/._~-]) out+="$c" ;;
            *) printf -v c '%%%02X' "'$c"; out+="$c" ;;
        esac
    done
    printf '%s' "$out"
}

__nexaflow_prompt() {
    local code=$?
    [ -n "$__nexaflow_environment" ] && env -0 >| "$__nexaflow_environment" 2>/dev/null
    printf '\e]133;D;%s\e\\' "$code"
    printf '\e]7;file://%s%s\e\\' "${HOSTNAME:-localhost}" "$(__nexaflow_urlpath "$PWD")"
    printf '\e]133;A\e\\'
    return $code
}

PROMPT_COMMAND="__nexaflow_prompt${PROMPT_COMMAND:+;$PROMPT_COMMAND}"
PS1="${PS1}\[\e]133;B\e\\\\\]"
PS0="${PS0}\[\e]133;C\e\\\\\]"
