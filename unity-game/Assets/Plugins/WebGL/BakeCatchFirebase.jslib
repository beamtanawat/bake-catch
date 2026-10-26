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
      window.BakeCatchLastSessionId = id;
      if (result.hasGripSamples !== true) { delete result.leftAvgGrip; delete result.rightAvgGrip; }
      // Persist synchronously BEFORE loading Firebase: offline/module errors must not lose the result.
      localStorage.setItem('bakecatch.session.' + id, JSON.stringify({ id: id, result: result,
        profile: result.profile || null, ownerUid: localStorage.getItem('bakecatch.firebaseOwner'), endedAtClient: Date.now() }));
      window.dispatchEvent(new Event('bakecatch-session-queued'));
      _BakeCatchFirebaseInit();
    } catch (error) { console.error('Bake Catch could not queue the session result:', error.message); }
  },
  BakeCatchFirebaseProfile__deps: ['BakeCatchFirebaseInit'],
  BakeCatchFirebaseProfile: function (profilePtr) {
    try {
      var profile = JSON.parse(UTF8ToString(profilePtr));
      localStorage.setItem('bakecatch.selectedProfile', JSON.stringify(profile));
      localStorage.setItem('bakecatch.profile.' + profile.code, JSON.stringify({ profile: profile,
        ownerUid: localStorage.getItem('bakecatch.firebaseOwner') }));
      window.dispatchEvent(new Event('bakecatch-session-queued'));
      _BakeCatchFirebaseInit();
    } catch (error) { console.error('Bake Catch could not queue the player profile:', error.message); }
  },
  BakeCatchFirebaseFindPlayer__deps: ['BakeCatchFirebaseInit'],
  BakeCatchFirebaseFindPlayer: function (profilePtr, targetPtr) {
    var candidate = JSON.parse(UTF8ToString(profilePtr)), target = UTF8ToString(targetPtr);
    _BakeCatchFirebaseInit();
    var timer;
    var timeout = new Promise(function (_, reject) {
      timer = setTimeout(function () { reject(new Error('Connection timed out')); }, 15000);
    });
    Promise.race([timeout, window.BakeCatchFirebaseLoading.then(function () {
      if (!window.BakeCatchFirebase) throw new Error('Firebase unavailable');
      return window.BakeCatchFirebase.findPlayerByName(candidate);
    })]).then(function (profile) {
      SendMessage(target, 'ReceivePlayerProfile', JSON.stringify(profile));
    }).catch(function () {
      SendMessage(target, 'ReceivePlayerError', 'Connect to the internet and try again. Your history is unchanged.');
    }).finally(function () { clearTimeout(timer); });
  },
  BakeCatchFirebaseSaveState: function () {
    var id = window.BakeCatchLastSessionId;
    if (!id) return 0;
    if (window.BakeCatchFirebase && window.BakeCatchFirebase.status.lastSessionId === id) return 2;
    try { if (localStorage.getItem('bakecatch.session.' + id)) return 1; } catch (_) {}
    return 0;
  },
  BakeCatchFirebaseHistory__deps: ['BakeCatchFirebaseInit'],
  BakeCatchFirebaseHistory: function (requestPtr, targetPtr) {
    var request = JSON.parse(UTF8ToString(requestPtr)), target = UTF8ToString(targetPtr), timer;
    _BakeCatchFirebaseInit();
    var timeout = new Promise(function (_, reject) { timer = setTimeout(function () { reject(new Error('Timeout')); }, 15000); });
    Promise.race([timeout, window.BakeCatchFirebaseLoading.then(function () {
      if (!window.BakeCatchFirebase) throw new Error('Firebase unavailable');
      return window.BakeCatchFirebase.getPlayerHistory(request.profile);
    })]).then(function (history) {
      history.requestId = request.requestId;
      SendMessage(target, 'ReceivePlayerHistory', JSON.stringify(history));
    }).catch(function () {
      SendMessage(target, 'ReceivePlayerHistory', JSON.stringify({ requestId: request.requestId, code: request.profile.code,
        error: 'History could not load. Connect to the internet and press Refresh.' }));
    }).finally(function () { clearTimeout(timer); });
  }
});
