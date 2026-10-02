#!/usr/bin/env bash
# Checks files against VirusTotal and reports how many engines flag each one. A file VirusTotal
# already knows is only looked up; anything new is uploaded and waited for. Used by release.yml
# (fresh build: everything is new) and nexus.yml (the same files again: lookups only).
#
#   virustotal.sh FILE...
#
# env  VT_API_KEY   free public API key (secret VIRUSTOTAL_API_KEY); without it nothing is checked
#      VT_REPORT    markdown report is written here (default virustotal.md)
#      VT_TIMEOUT   seconds to wait for scans before reporting them as pending (default 900)
# out  GITHUB_OUTPUT gets complete=true|false and flagged=<most engines flagging any one file>
#
# The free API allows 4 requests a minute, so every call waits until 16 s after the last one.
# Never fails the job: a VirusTotal outage must not block a release. Callers read the outputs.
set -uo pipefail

API=https://www.virustotal.com/api/v3
GUI=https://www.virustotal.com/gui/file
REPORT=${VT_REPORT:-virustotal.md}
OUT=${GITHUB_OUTPUT:-/dev/null}
deadline=$(( $(date +%s) + ${VT_TIMEOUT:-900} ))

if [ -z "${VT_API_KEY:-}" ]; then
  echo "::warning::VIRUSTOTAL_API_KEY is not set; skipping the VirusTotal check."
  echo "VirusTotal: not checked (no API key)." > "$REPORT"
  { echo "complete=false"; echo "flagged=0"; } >> "$OUT"
  exit 0
fi

# The last call's time lives in a file: vt runs inside $(...), so a variable would not stick.
stamp=$(mktemp); echo 0 > "$stamp"
vt() {  # vt URL-or-PATH [curl args...]
  local url=$1; shift
  [[ $url == http* ]] || url=$API$url
  local wait=$(( $(cat "$stamp") + 16 - $(date +%s) ))
  [ $wait -gt 0 ] && sleep $wait
  date +%s > "$stamp"
  curl -sS --retry 2 -H "x-apikey: $VT_API_KEY" "$@" "$url"
}

paths=(); names=(); shas=(); analyses=(); results=()
for f in "$@"; do
  paths+=("$f")
  names+=("$(basename "$f")")
  shas+=("$(sha256sum "$f" | cut -d' ' -f1)")
  analyses+=("")
  results+=("")
done

# A file counts as done once VirusTotal has a finished analysis for it.
stats_line() { jq -r '"\(.malicious) \(.suspicious) \(.malicious + .suspicious + .undetected + .harmless)"'; }

for i in "${!names[@]}"; do
  r=$(vt "/files/${shas[$i]}")
  if jq -e '.data.attributes.last_analysis_date' <<<"$r" >/dev/null 2>&1; then
    results[$i]=$(jq '.data.attributes.last_analysis_stats' <<<"$r" | stats_line)
    echo "${names[$i]}: already known, ${results[$i]}"
  elif jq -e '.error.code == "NotFoundError"' <<<"$r" >/dev/null 2>&1; then
    # upload_url takes files of any size (up to 650 MB); the plain endpoint stops at 32 MB.
    up=$(vt /files/upload_url | jq -r '.data // empty')
    id=""
    [ -n "$up" ] && id=$(vt "$up" -F "file=@${paths[$i]}" | jq -r '.data.id // empty')
    analyses[$i]=$id
    echo "${names[$i]}: uploaded (analysis ${id:-FAILED})"
  else
    echo "::warning::VirusTotal lookup failed for ${names[$i]}: $(jq -c '.error // .' <<<"$r" 2>/dev/null || echo "$r")"
  fi
done

# Poll the uploads round-robin until they finish or the time runs out.
while [ "$(date +%s)" -lt "$deadline" ]; do
  waiting=0
  for i in "${!names[@]}"; do
    [ -n "${analyses[$i]}" ] && [ -z "${results[$i]}" ] || continue
    r=$(vt "/analyses/${analyses[$i]}")
    if [ "$(jq -r '.data.attributes.status // empty' <<<"$r")" = completed ]; then
      results[$i]=$(jq '.data.attributes.stats' <<<"$r" | stats_line)
      echo "${names[$i]}: scanned, ${results[$i]}"
    else
      waiting=1
    fi
  done
  [ $waiting -eq 1 ] || break
done

complete=true; flagged=0
{
  echo "**VirusTotal** (engines flagging each file; the links show which):"
  echo
  for i in "${!names[@]}"; do
    link="[${names[$i]}]($GUI/${shas[$i]})"
    if [ -n "${results[$i]}" ]; then
      read -r mal sus total <<<"${results[$i]}"
      [ "$mal" -gt "$flagged" ] && flagged=$mal
      line="$mal of $total"
      [ "$sus" -gt 0 ] && line="$line, $sus more call it suspicious"
      echo "- $link: $line"
    elif [ -n "${analyses[$i]}" ]; then
      complete=false
      echo "- $link: scan not finished yet"
    else
      complete=false
      echo "- $link: not checked (VirusTotal error)"
    fi
  done
} > "$REPORT"

cat "$REPORT"
{ echo "complete=$complete"; echo "flagged=$flagged"; } >> "$OUT"
exit 0
