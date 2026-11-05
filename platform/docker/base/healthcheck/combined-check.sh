#!/bin/bash
# Run Chrome healthcheck
/opt/healthcheck/chrome-check.sh
CHROME_STATUS=$?

if [ $CHROME_STATUS -eq 0 ]; then
  echo "All healthchecks passed successfully!"
  exit 0
else
  echo "Chrome healthcheck failed: $CHROME_STATUS"
  exit 1
fi
