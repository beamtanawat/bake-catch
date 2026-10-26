mergeInto(LibraryManager.library, {
  BakeCatchGripStart: function (targetPtr) {
    var target = UTF8ToString(targetPtr);
    var engine = Module;
    var deliver = function (frame) {
      engine.SendMessage(target, 'ReceiveGrip', JSON.stringify(frame ?
        { available: true, left: frame.left, right: frame.right,
          shake: frame.shake || 0, hasShake: typeof frame.shake === 'number' } : { available: false }));
    };
    if (window.BakeCatchGrip) { window.BakeCatchGrip.start(deliver); return; }
    var script = document.createElement('script');
    script.src = (window.BakeCatchGripBaseUrl || 'http://127.0.0.1:8080/') + 'grip-client.js';
    script.onload = function () { window.BakeCatchGrip.start(deliver); };
    script.onerror = function () { deliver(null); console.warn('Start the local grip broker, then reload the game.'); };
    document.head.appendChild(script);
  },
  BakeCatchGripStop: function () { if (window.BakeCatchGrip) window.BakeCatchGrip.stop(); }
});
