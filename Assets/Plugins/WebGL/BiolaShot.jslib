// Screenshot handshake for CI (Tools/capture/capture.mjs increments window.biolaShotAck after
// each screenshot is saved). Read by ScreenshotTour.cs.
mergeInto(LibraryManager.library, {
  BiolaShotAck: function () {
    return (typeof window !== "undefined" && window.biolaShotAck) ? (window.biolaShotAck | 0) : 0;
  }
});
