mergeInto(LibraryManager.library, {
  SendMessageToShell: function (messagePtr) {
    function resolveShellOrigin() {
      if (window.__SusaPlayShellOrigin) return window.__SusaPlayShellOrigin;
      var resolved = null;
      try {
        if (document.referrer) {
          resolved = new URL(document.referrer).origin;
        }
      } catch (e) {
        resolved = null;
      }
      if (!resolved) resolved = window.location.origin;
      window.__SusaPlayShellOrigin = resolved;
      return resolved;
    }

    var message = UTF8ToString(messagePtr);
    var targetOrigin = resolveShellOrigin();
    var parsed;
    try {
      parsed = JSON.parse(message);
    } catch (e) {
      console.error("[susaplay] Bridge failed to parse outgoing message:", e);
      return;
    }

    // When embedded, always post to resolved shell origin.
    // If same-window context, this still works with current origin.
    window.parent.postMessage(parsed, targetOrigin || "*");
  },

  InitializeMessageListener: function (callbackPtr) {
    function resolveShellOrigin() {
      if (window.__SusaPlayShellOrigin) return window.__SusaPlayShellOrigin;
      var resolved = null;
      try {
        if (document.referrer) {
          resolved = new URL(document.referrer).origin;
        }
      } catch (e) {
        resolved = null;
      }
      if (!resolved) resolved = window.location.origin;
      window.__SusaPlayShellOrigin = resolved;
      return resolved;
    }

    function isAllowedOrigin(origin) {
      var resolved = resolveShellOrigin();
      if (origin === resolved) return true;
      return (
        origin === "https://susaplay.com" ||
        origin === "https://play.susaplay.com" ||
        origin === "http://localhost:5173" ||
        origin === "http://localhost:5174" ||
        origin === "http://127.0.0.1:5173" ||
        origin === "http://127.0.0.1:5174"
      );
    }

    resolveShellOrigin();
    window.addEventListener("message", function (event) {
      if (!isAllowedOrigin(event.origin)) return;
      var json = JSON.stringify(event.data);
      var len = lengthBytesUTF8(json) + 1;
      var buffer = _malloc(len);
      stringToUTF8(json, buffer, len);
      dynCall_vi(callbackPtr, buffer);
      _free(buffer);
    });
  },
});
