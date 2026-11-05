#!/usr/bin/env bash
# Documents name Arial, Times New Roman, Courier New, Calibri and Cambria. fontconfig's metric
# aliases render each with its free twin (Liberation, Carlito, Caladea), so text measures as it
# would in the original font.
set -euo pipefail

sudo apt-get update
sudo apt-get install -y --no-install-recommends fontconfig fonts-liberation fonts-crosextra-carlito fonts-crosextra-caladea
for pair in 'Arial:Liberation Sans' 'Times New Roman:Liberation Serif' 'Courier New:Liberation Mono' 'Calibri:Carlito' 'Cambria:Caladea'; do
  requested=${pair%%:*}
  twin=${pair#*:}
  resolved=$(fc-match -f '%{family[0]}' "$requested")
  if [ "$resolved" != "$twin" ]; then
    echo "::error::$requested resolves to $resolved, not its metric twin $twin, so the tests that measure or embed text cannot pass."
    exit 1
  fi
done
