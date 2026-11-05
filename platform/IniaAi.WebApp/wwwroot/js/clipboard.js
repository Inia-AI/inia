window.copyText = async function (text) {
  try {
    // Try modern clipboard API (works on HTTPS/localhost)
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch (e) {
    // fall through to legacy method
  }

  // Fallback for HTTP or blocked clipboard API
  const ta = document.createElement('textarea');
  ta.value = text;
  // avoid zoom/scroll jumps
  ta.style.position = 'fixed';
  ta.style.top = '0';
  ta.style.left = '0';
  ta.style.opacity = '0';
  document.body.appendChild(ta);
  ta.focus();
  ta.select();
  let ok = false;
  try {
    ok = document.execCommand('copy');
  } finally {
    document.body.removeChild(ta);
  }
  return ok;
};
