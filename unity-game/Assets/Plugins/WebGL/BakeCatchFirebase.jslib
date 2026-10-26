mergeInto(LibraryManager.library, {
  BakeCatchFirebaseInit: function () {
    if (!window.BakeCatchFirebaseLoading) {
      window.BakeCatchFirebaseLoading = import((window.BakeCatchGripBaseUrl || 'http://127.0.0.1:8080/') + 'firebase-client.js')
        .then(function (client) { window.BakeCatchFirebase = client; })
        .catch(function (error) {
          window.BakeCatchFirebaseLoading = null;
          console.warn('Bake Catch Firebase unavailable; queued results are kept.', error.message);
        });
    }
  },
  BakeCatchFirebaseSave__deps: ['BakeCatchFirebaseInit'],
  BakeCatchFirebaseSave: function (idPtr, resultPtr) {
    try {
      var id = UTF8ToString(idPtr), result = JSON.parse(UTF8ToString(resultPtr));
      delete result.leftAvgGrip; delete result.rightAvgGrip;
      // Persist synchronously BEFORE loading Firebase: offline/module errors must not lose the result.
      localStorage.setItem('bakecatch.session.' + id, JSON.stringify({ id: id, result: result, endedAtClient: Date.now() }));
      window.dispatchEvent(new Event('bakecatch-session-queued'));
      _BakeCatchFirebaseInit();
    } catch (error) { console.error('Bake Catch could not queue the session result:', error.message); }
  }
});
