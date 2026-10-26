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
  }
});
