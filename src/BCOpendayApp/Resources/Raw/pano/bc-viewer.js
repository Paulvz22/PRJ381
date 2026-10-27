// Belgium Campus Open Day - 360° viewer glue around Pannellum (loaded by Views/PanoramaPage).
//  - Arrows ("bcTarget" hotspots) open the next view through the app (bcpano://go?id=...).
//  - Look around by moving the phone: the app calls bcOrient(heading, pitch) ~30 times a second.
//  - VR mode for phone-in-headset viewers (Cardboard / "VR Box" style, no buttons): the view is drawn twice,
//    side by side, and the user looks at an arrow for 1.5 s to go there (a ring fills up while they look).
(function () {
  'use strict';

  var DWELL_MS = 1500;      // how long to look at an arrow before moving
  var GAZE_DEGREES = 7;     // how close to the centre of view an arrow must be to count as "looked at"
  var VR_HFOV = 90;         // field of view of each eye in the headset
  var RING = 2 * Math.PI * 18;

  var state = {
    config: null, sceneId: null, left: null, right: null,
    motion: false, vr: false, reference: null, touching: false,
    gazeSpot: null, gazeStart: 0, normalHfov: null, leaving: false
  };

  function wrap(angle) { return ((angle % 360) + 540) % 360 - 180; }

  function go(id, yaw) {
    if (state.leaving) { return; }
    state.leaving = true;
    var url = 'bcpano://go?id=' + encodeURIComponent(id);
    if (typeof yaw === 'number') { url += '&yaw=' + encodeURIComponent(yaw); }
    window.location.href = url;
    setTimeout(function () { state.leaving = false; }, 1500); // the app ignored it (e.g. same view)
  }

  function arrows() {
    var scene = state.config.scenes[state.sceneId];
    return ((scene && scene.hotSpots) || []).filter(function (spot) { return spot.bcTarget; });
  }

  // Great-circle angle between two view directions (degrees).
  function angleBetween(p1, y1, p2, y2) {
    var r = Math.PI / 180;
    var c = Math.sin(p1 * r) * Math.sin(p2 * r) + Math.cos(p1 * r) * Math.cos(p2 * r) * Math.cos((y1 - y2) * r);
    return Math.acos(Math.max(-1, Math.min(1, c))) / r;
  }

  // The arrow nearest the centre of view, if it is close enough.
  function arrowInCentre() {
    var viewer = state.left;
    if (!viewer || !viewer.isLoaded()) { return null; }
    var pitch = viewer.getPitch(), yaw = viewer.getYaw();
    var best = null, bestAngle = GAZE_DEGREES;
    arrows().forEach(function (spot) {
      var angle = angleBetween(pitch, yaw, spot.pitch, spot.yaw);
      if (angle < bestAngle) { best = spot; bestAngle = angle; }
    });
    return best;
  }

  // ---------------------------------------------------------------- VR overlay (one copy per eye)

  function addEyeOverlay(container) {
    var overlay = document.createElement('div');
    overlay.className = 'bc-eye-overlay';
    overlay.innerHTML =
      '<div class="bc-reticle"><svg viewBox="0 0 44 44">' +
      '<circle class="bc-ring-back" cx="22" cy="22" r="18"></circle>' +
      '<circle class="bc-ring" cx="22" cy="22" r="18" stroke-dasharray="' + RING + '" stroke-dashoffset="' + RING + '"></circle>' +
      '<circle class="bc-dot" cx="22" cy="22" r="3"></circle></svg></div>' +
      '<div class="bc-gaze-label"></div>' +
      '<div class="bc-vr-hint">Look at an arrow for 1.5 seconds to walk there.<br>Press Back to leave VR.</div>';
    container.appendChild(overlay);
  }

  function updateOverlays(spot, progress) {
    var rings = document.querySelectorAll('.bc-ring');
    var labels = document.querySelectorAll('.bc-gaze-label');
    for (var i = 0; i < rings.length; i++) {
      rings[i].setAttribute('stroke-dashoffset', String(RING * (1 - progress)));
    }

    for (var j = 0; j < labels.length; j++) {
      labels[j].textContent = spot ? (spot.text || '') : '';
      labels[j].style.visibility = spot ? 'visible' : 'hidden';
    }
  }

  function showHint() {
    document.body.classList.add('bc-hint');
    setTimeout(function () { document.body.classList.remove('bc-hint'); }, 6000);
  }

  function tick(now) {
    window.requestAnimationFrame(tick);
    if (!state.vr) { return; }

    var spot = arrowInCentre();
    if (spot !== state.gazeSpot) {
      state.gazeSpot = spot;
      state.gazeStart = now;
    }

    var progress = spot ? Math.min(1, (now - state.gazeStart) / DWELL_MS) : 0;
    updateOverlays(spot, progress);
    if (spot && progress >= 1) {
      state.gazeSpot = null;
      go(spot.bcTarget, spot.bcTargetYaw);
    }
  }

  // A copy of the configuration for the right eye: same image (strings are shared, not copied), no controls.
  function rightEyeConfig() {
    var source = state.config;
    var scene = Object.assign({}, source.scenes[state.sceneId]);
    scene.hotSpots = (scene.hotSpots || []).map(function (spot) { return Object.assign({}, spot); });
    scene.yaw = state.left.getYaw();
    scene.pitch = state.left.getPitch();
    scene.hfov = VR_HFOV;
    var scenes = {};
    scenes[state.sceneId] = scene;
    return {
      default: Object.assign({}, source.default, {
        firstScene: state.sceneId, showControls: false, compass: false, draggable: false,
        mouseZoom: false, keyboardZoom: false, disableKeyboardCtrl: true, sceneFadeDuration: 0
      }),
      scenes: scenes
    };
  }

  function setVr(on, withHint) {
    on = !!on;
    if (on === state.vr) {
      if (on && withHint) { showHint(); }
      return;
    }

    state.vr = on;
    state.reference = null;
    state.gazeSpot = null;
    document.body.classList.toggle('bc-vr', on);
    if (on) {
      state.normalHfov = state.left.getHfov();
      state.left.setHfov(VR_HFOV, false);
      state.right = pannellum.viewer('panoramaR', rightEyeConfig());
      addEyeOverlay(document.getElementById('panoramaR'));
      if (withHint) { showHint(); }
    } else {
      if (state.right) { state.right.destroy(); state.right = null; }
      var right = document.getElementById('panoramaR');
      while (right.firstChild) { right.removeChild(right.firstChild); }
      if (state.normalHfov) { state.left.setHfov(state.normalHfov, false); }
      updateOverlays(null, 0);
    }

    setTimeout(function () { state.left.resize(); if (state.right) { state.right.resize(); } }, 50);
  }

  // ---------------------------------------------------------------- called by the app

  window.bcStart = function (config, options) {
    state.config = config;
    state.sceneId = config.default.firstScene;
    state.motion = !!options.motion;

    Object.keys(config.scenes).forEach(function (id) {
      (config.scenes[id].hotSpots || []).forEach(function (spot) {
        if (!spot.bcTarget) { return; }
        spot.clickHandlerArgs = { id: spot.bcTarget, yaw: spot.bcTargetYaw };
        spot.clickHandlerFunc = function (event, args) { go(args.id, args.yaw); };
      });
    });

    state.left = pannellum.viewer('panorama', config);
    addEyeOverlay(document.getElementById('panorama'));

    function release() { state.touching = false; state.reference = null; }
    state.left.on('mousedown', function () { state.touching = true; });
    state.left.on('touchstart', function () { state.touching = true; });
    state.left.on('mouseup', release);
    state.left.on('touchend', release);
    state.left.on('load', function () { state.reference = null; });

    // In the headset nobody can aim a tap, but some viewers have a button that taps the screen:
    // a tap selects the arrow in the centre straight away.
    document.getElementById('bc-vr-tap').addEventListener('click', function () {
      var spot = arrowInCentre();
      if (spot) { go(spot.bcTarget, spot.bcTargetYaw); }
    });

    if (options.vr) {
      if (state.left.isLoaded()) { setVr(true, !!options.hint); }
      else { state.left.on('load', function once() { state.left.off('load', once); setVr(true, !!options.hint); }); }
    }

    window.requestAnimationFrame(tick);
  };

  window.bcSetMotion = function (on) { state.motion = !!on; state.reference = null; };

  window.bcSetVr = function (on, withHint) {
    if (!state.left) { return; }
    if (state.left.isLoaded()) { setVr(on, withHint); }
    else { state.left.on('load', function once() { state.left.off('load', once); setVr(on, withHint); }); }
  };

  // Relative look-around: the view under the phone's direction is kept when motion starts, after a swipe and
  // after a new view loads. Both eyes always look the same way.
  window.bcOrient = function (heading, pitch) {
    var viewer = state.left;
    if (!(state.motion || state.vr) || (state.touching && !state.vr) || !viewer || !viewer.isLoaded()) { return; }
    if (state.reference === null) {
      state.reference = { yaw: viewer.getYaw(), heading: heading };
      viewer.stopMovement();
    }

    var yaw = wrap(state.reference.yaw + wrap(heading - state.reference.heading));
    viewer.setYaw(yaw, false);
    viewer.setPitch(pitch, false);
    if (state.right && state.right.isLoaded()) {
      state.right.setYaw(yaw, false);
      state.right.setPitch(viewer.getPitch(), false);
    }
  };
})();
