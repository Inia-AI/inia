#!/bin/bash
# Check if Chrome is installed and can be executed
if ! command -v google-chrome &> /dev/null; then
  echo "Chrome test failed: Google Chrome is not installed"
  exit 1
fi

# Check the Chrome version
CHROME_VERSION=$(google-chrome --version 2>/dev/null || echo "")
if [[ -z "$CHROME_VERSION" ]]; then
  echo "Chrome test failed: Unable to get Chrome version"
  exit 1
fi

# Check if Chromedriver is installed and can be executed
if ! command -v chromedriver &> /dev/null; then
  echo "Chrome test failed: Chromedriver is not installed"
  exit 1
fi

# Check the Chromedriver version
CHROMEDRIVER_VERSION=$(chromedriver --version 2>/dev/null | head -n 1 || echo "")
if [[ -z "$CHROMEDRIVER_VERSION" ]]; then
  echo "Chrome test failed: Unable to get Chromedriver version"
  exit 1
fi

# Run Chrome in headless mode with a simple command
TEST_OUTPUT=$(google-chrome --headless --disable-gpu --no-sandbox --dump-dom "data:text/html,<html><body>Chrome Test</body></html>" 2>&1 || echo "CHROME_ERROR")

# Check if Chrome executed successfully
if [[ "$TEST_OUTPUT" == *"CHROME_ERROR"* ]]; then
  echo "Chrome test failed: Unable to run Chrome in headless mode"
  echo "Output: $TEST_OUTPUT"
  exit 1
fi

echo "Chrome test successful: Chrome $CHROME_VERSION and Chromedriver $CHROMEDRIVER_VERSION are working properly"
exit 0
