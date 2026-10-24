// Pure tree-patch core: no DOM, so Node tests can load this file and apply the same patches
// the browser does (MaldaLang.Tests/Fuzz/UiPatchFuzzTests.cs).
const MaldaUiCore = (function () {
  function applyPatches(root, patches) {
    for (const patch of patches || []) {
      if (!patch || !patch.op) {
        continue;
      }

      if (patch.op === "ReplaceNode") {
        root = applyReplaceNode(root, patch);
      } else if (patch.op === "SetProp") {
        applySetProp(root, patch);
      } else if (patch.op === "RemoveProp") {
        applyRemoveProp(root, patch);
      } else if (patch.op === "InsertChild") {
        applyInsertChild(root, patch);
      } else if (patch.op === "RemoveChild") {
        applyRemoveChild(root, patch);
      } else if (patch.op === "MoveChild") {
        applyMoveChild(root, patch);
      }
    }
    return root;
  }

  function applyReplaceNode(root, patch) {
    const path = patch.path || "/";
    if (path === "/") {
      return patch.value || null;
    }

    const parentNode = getNodeAtPath(root, parentOf(path));
    if (!parentNode || !Array.isArray(parentNode.children)) {
      return root;
    }

    parentNode.children[childIndexOf(path)] = patch.value || null;
    return root;
  }

  function applySetProp(root, patch) {
    const node = getNodeAtPath(root, patch.path || "/");
    if (!node) return;
    node.props = node.props || {};
    node.props[patch.prop] = patch.value;
  }

  function applyRemoveProp(root, patch) {
    const node = getNodeAtPath(root, patch.path || "/");
    if (!node || !node.props) return;
    delete node.props[patch.prop];
  }

  function applyInsertChild(root, patch) {
    const node = getNodeAtPath(root, patch.path || "/");
    if (!node) return;
    node.children = node.children || [];
    const index = Number.isInteger(patch.index) ? patch.index : node.children.length;
    node.children.splice(index, 0, patch.value || null);
  }

  function applyRemoveChild(root, patch) {
    const node = getNodeAtPath(root, patch.path || "/");
    if (!node || !Array.isArray(node.children)) return;
    if (!Number.isInteger(patch.index)) return;
    node.children.splice(patch.index, 1);
  }

  // Takes the child at `from` out, then inserts it at `index` of the shortened list.
  function applyMoveChild(root, patch) {
    const node = getNodeAtPath(root, patch.path || "/");
    if (!node || !Array.isArray(node.children)) return;
    if (!Number.isInteger(patch.from) || !Number.isInteger(patch.index)) return;
    if (patch.from < 0 || patch.from >= node.children.length) return;
    const [moved] = node.children.splice(patch.from, 1);
    node.children.splice(patch.index, 0, moved);
  }

  function getNodeAtPath(root, path) {
    if (!path || path === "/") {
      return root;
    }

    let current = root;
    for (const idx of parsePath(path)) {
      if (!current || !Array.isArray(current.children) || idx < 0 || idx >= current.children.length) {
        return null;
      }
      current = current.children[idx];
    }
    return current;
  }

  function parsePath(path) {
    return path
      .split("/")
      .filter(Boolean)
      .map((segment) => Number.parseInt(segment, 10))
      .filter((n) => Number.isInteger(n));
  }

  function parentOf(path) {
    const segments = parsePath(path);
    segments.pop();
    return segments.length === 0 ? "/" : `/${segments.join("/")}/`;
  }

  function childIndexOf(path) {
    const segments = parsePath(path);
    return segments.length === 0 ? 0 : segments[segments.length - 1];
  }

  // The envelopes' `treeHash` (MaldaLang.UIHost/UiTreeHash.cs): 32-bit FNV-1a over the UTF-8
  // bytes of the tree's JSON with object keys sorted, as 8 lowercase hex digits.
  function treeHash(tree) {
    const bytes = new TextEncoder().encode(canonicalJson(tree));
    let hash = 0x811c9dc5;
    for (const b of bytes) {
      hash = Math.imul(hash ^ b, 0x01000193) >>> 0;
    }
    return hash.toString(16).padStart(8, "0");
  }

  function canonicalJson(value) {
    if (value === null || typeof value !== "object") {
      const text = JSON.stringify(value);
      return text === undefined ? "null" : text;
    }
    if (Array.isArray(value)) {
      return "[" + value.map(canonicalJson).join(",") + "]";
    }
    const names = Object.keys(value)
      .filter((name) => value[name] !== undefined && typeof value[name] !== "function")
      .sort();
    return "{" + names.map((name) => JSON.stringify(name) + ":" + canonicalJson(value[name])).join(",") + "}";
  }

  // UiDiffEngine.Diff (MaldaLang/Runtime/UI/UiDiffEngine.cs) for local mode, where the program
  // runs in the browser: the same patches in the same order for the same two trees
  // (UiPatchFuzzTests compares them). Nodes are { type, key?, props, children }.
  function diff(previous, current) {
    const patches = [];
    diffNode(previous || null, current || null, "/", patches);
    return patches;
  }

  function keyOf(node) {
    return typeof node.key === "string" ? node.key : null;
  }

  function diffNode(previous, current, path, patches) {
    if (!previous && !current) {
      return;
    }
    if (!previous || !current || previous.type !== current.type || keyOf(previous) !== keyOf(current)) {
      patches.push({ op: "ReplaceNode", path, value: current || null });
      return;
    }
    const before = previous.props || {};
    const after = current.props || {};
    for (const name of Object.keys(before)) {
      if (!Object.prototype.hasOwnProperty.call(after, name)) {
        patches.push({ op: "RemoveProp", path, prop: name });
      }
    }
    for (const name of Object.keys(after)) {
      if (!Object.prototype.hasOwnProperty.call(before, name) || !propEquals(before[name], after[name])) {
        patches.push({ op: "SetProp", path, prop: name, value: after[name] });
      }
    }
    diffChildren(previous.children || [], current.children || [], path, patches);
  }

  // RuntimeValueEquals: numbers within 1e-9, other scalars strictly, lists and dicts by content
  // (member order does not count).
  function propEquals(left, right) {
    if (typeof left === "number" && typeof right === "number") {
      return left === right || Math.abs(left - right) < 1e-9;
    }
    if (left === null || right === null || typeof left !== "object" || typeof right !== "object") {
      return left === right;
    }
    return Array.isArray(left) === Array.isArray(right) && canonicalJson(left) === canonicalJson(right);
  }

  function diffChildren(previous, current, path, patches) {
    if (previous.length > 0 && current.length > 0 && hasUniqueKeys(previous) && hasUniqueKeys(current)) {
      diffChildrenByKey(previous, current, path, patches);
      return;
    }
    const shared = Math.min(previous.length, current.length);
    for (let i = 0; i < shared; i++) {
      diffNode(previous[i], current[i], `${path}${i}/`, patches);
    }
    for (let i = previous.length; i < current.length; i++) {
      patches.push({ op: "InsertChild", path, value: current[i], index: i });
    }
    for (let i = previous.length - 1; i >= current.length; i--) {
      patches.push({ op: "RemoveChild", path, index: i });
    }
  }

  function hasUniqueKeys(nodes) {
    const seen = new Set();
    for (const node of nodes) {
      const key = node ? keyOf(node) : null;
      if (key === null || key.trim() === "" || seen.has(key)) {
        return false;
      }
      seen.add(key);
    }
    return true;
  }

  // Removes the children whose key is gone (highest index first), then walks the new order,
  // moving or inserting each child at its final index before diffing it.
  function diffChildrenByKey(previous, current, path, patches) {
    const currentKeys = new Set(current.map(keyOf));
    const working = [];
    for (let i = previous.length - 1; i >= 0; i--) {
      if (currentKeys.has(keyOf(previous[i]))) {
        working.unshift(previous[i]);
      } else {
        patches.push({ op: "RemoveChild", path, index: i });
      }
    }
    for (let i = 0; i < current.length; i++) {
      const key = keyOf(current[i]);
      let from = -1;
      for (let j = i; j < working.length; j++) {
        if (keyOf(working[j]) === key) {
          from = j;
          break;
        }
      }
      if (from < 0) {
        patches.push({ op: "InsertChild", path, value: current[i], index: i });
        working.splice(i, 0, current[i]);
        continue;
      }
      if (from !== i) {
        patches.push({ op: "MoveChild", path, index: i, from });
        const [moved] = working.splice(from, 1);
        working.splice(i, 0, moved);
      }
      diffNode(working[i], current[i], `${path}${i}/`, patches);
    }
  }

  return { applyPatches, getNodeAtPath, parsePath, parentOf, childIndexOf, treeHash, canonicalJson, diff };
})();

// Pure protocol state machine: no DOM or socket, so Node tests can drive it with the messages
// a host sends (MaldaLang.Tests/UiHostProtocolTests.cs). onOpen() and receive() return the
// actions the browser carries out: { apply: payload }, { send: message } and { warn: detail }.
// Protocol 1.1 (MaldaLang.UIHost/UiHostHub.cs): `sequence` numbers the messages of one socket
// in each direction and restarts at 1 on every connection; `revision` / `baseRevision` version
// the tree, and a patch applies only on top of the revision the client holds. After applying,
// verifyTree() compares the tree with the envelope's `treeHash`.
const MaldaUiProtocol = (function () {
  const version = "1.1";

  function createState(sessionId) {
    return { sessionId, revision: 0, expectedServerSequence: 1, nextClientSequence: 1, resyncPending: false };
  }

  // A new socket: both sequences restart, then ask for the whole tree.
  function onOpen(state) {
    state.expectedServerSequence = 1;
    state.nextClientSequence = 1;
    state.resyncPending = false;
    return requestResync(state);
  }

  function receive(state, message) {
    if (!message) {
      return [];
    }

    if (message.version && message.version !== version) {
      // Asking this host for a resync would get the same version back, so only report it.
      return [{ warn: { kind: "version_mismatch", expected: version, got: message.version } }];
    }

    const actions = [];
    if (typeof message.sequence === "number") {
      if (message.sequence !== state.expectedServerSequence) {
        actions.push({ warn: { kind: "sequence_gap", expected: state.expectedServerSequence, got: message.sequence } });
        actions.push(...requestResync(state));
      }
      state.expectedServerSequence = message.sequence + 1;
    }

    switch (message.type) {
      case "ping":
        actions.push({ send: control(state, "pong", {}) });
        break;
      case "error":
      case "nack":
        actions.push({ warn: { kind: message.type, error: message.error || {} } });
        break;
      case "mount":
      case "resync": {
        if (message.type === "resync") {
          state.resyncPending = false;
        }
        const payload = normalizePayload(message.payload);
        if (payload) {
          actions.push({ apply: payload });
          state.revision = revisionOf(payload, state.revision);
        }
        break;
      }
      case "patch": {
        const payload = normalizePayload(message.payload);
        if (!payload) {
          break;
        }
        if (typeof payload.baseRevision === "number" && payload.baseRevision !== state.revision) {
          actions.push(...requestResync(state));
          break;
        }
        actions.push({ apply: payload });
        state.revision = revisionOf(payload, state.revision);
        break;
      }
      default:
        break;
    }

    return actions;
  }

  // Called with the tree after an { apply: payload } action. A tree that does not hash to the
  // payload's `treeHash` has drifted from the server's, so ask for the whole tree; a mismatch
  // right after a resync would recur on every resync, so that one is only reported.
  function verifyTree(state, tree, payload) {
    if (!payload || typeof payload.treeHash !== "string") {
      return [];
    }
    const actual = MaldaUiCore.treeHash(tree);
    if (actual === payload.treeHash) {
      return [];
    }
    const actions = [{ warn: { kind: "tree_hash_mismatch", expected: payload.treeHash, got: actual, revision: state.revision } }];
    if (payload.type !== "resync") {
      actions.push(...requestResync(state));
    }
    return actions;
  }

  // At most one resync in flight: the answer is the whole tree, so asking again adds nothing.
  function requestResync(state) {
    if (state.resyncPending) {
      return [];
    }
    state.resyncPending = true;
    return [{ send: control(state, "resync", { reason: "client_out_of_sync" }) }];
  }

  function revisionOf(payload, fallback) {
    return typeof payload.revision === "number" ? payload.revision : fallback;
  }

  // `target` ({ key, type } of the node at targetPath) lets the session drop the event when the
  // node at that path is no longer the one the user acted on (UiSession.TryDequeueEvent).
  function event(state, eventType, targetPath, payload, target) {
    const message = {
      type: "event",
      eventType,
      targetPath,
      payload,
      sessionId: state.sessionId,
      version,
      envelopeId: randomId()
    };
    if (target) {
      message.targetKey = target.key != null ? String(target.key) : null;
      message.targetType = target.type || null;
    }
    return message;
  }

  function control(state, type, payload) {
    return {
      type,
      payload,
      sessionId: state.sessionId,
      version,
      envelopeId: randomId()
    };
  }

  // Numbers a message as it goes out on the current socket; queued messages get theirs on flush.
  function outbound(state, message) {
    return Object.assign({}, message, { sequence: state.nextClientSequence++ });
  }

  function normalizePayload(payload) {
    if (!payload) return null;
    if (payload.raw) {
      try {
        return JSON.parse(payload.raw);
      } catch {
        return null;
      }
    }
    return payload;
  }

  // The tree after one inbound payload: a patch list, a { tree } snapshot, or a bare tree.
  function applyPayload(tree, payload) {
    if (!payload) {
      return tree;
    }
    if (payload.patches) {
      return MaldaUiCore.applyPatches(tree, payload.patches);
    }
    if (payload.tree) {
      return payload.tree;
    }
    return payload;
  }

  function randomId() {
    const cryptoApi = typeof crypto !== "undefined" ? crypto : null;
    if (cryptoApi && typeof cryptoApi.randomUUID === "function") {
      return cryptoApi.randomUUID().replace(/-/g, "");
    }
    return String(Date.now()) + String(Math.floor(Math.random() * 1e6));
  }

  return { version, createState, onOpen, receive, verifyTree, event, outbound, normalizePayload, applyPayload };
})();

// DOM renderer and patcher, given a document: the browser's, or the fake DOM of
// MaldaLang.Tests/Fuzz/UiPatchFuzzTests.cs, which requires the DOM after a patch to equal a
// fresh render of the patched tree. hooks.sendEvent(type, path, payload, target) sends a UI
// event and returns its id (target is { key, type } of the node, or null); hooks.requestFrame(fn)
// runs fn before the next paint (requestAnimationFrame in the browser); hooks.setTimeout /
// hooks.clearTimeout time the input debounce (the global ones by default).
const MaldaUiDom = (function () {
  function create(doc, hooks) {
    const requestFrame = hooks.requestFrame || ((fn) => fn());
    const setTimer = hooks.setTimeout || ((fn, ms) => setTimeout(fn, ms));
    const clearTimer = hooks.clearTimeout || ((id) => clearTimeout(id));

    // Input events: consecutive `input`s of one element coalesce into one event sent
    // `inputDebounceMs` after the last keystroke (default 150 ms for textField / textArea, 0
    // elsewhere). Any other event sends the pending inputs first, so the server sees the typed
    // value before the click or submit that follows it.
    const defaultInputDebounceMs = { textField: 150, textArea: 150 };
    const pendingInputs = new Set();
    // Ids of the events sent and not yet answered, oldest first. A session envelope's
    // `answersEventId` names the last event the program had pulled when it rendered; that event
    // and every one sent before it are answered.
    const unansweredEvents = [];
    // Element → { value, checked }: the server props held back because they predate an edit.
    const heldBack = new Map();
    const inputKey = "__maldaInput";

    function inputState(el) {
      return el[inputKey] || (el[inputKey] = { timer: null, lastEventId: null });
    }

    function sendEvent(type, path, payload, el) {
      if (type !== "input") {
        flushPendingInputs();
      }
      const node = el ? el[nodeKey] : null;
      // The handler prop's text (onClick: "save" → action "save"), so a program can tell its
      // controls apart without matching paths.
      if (node && payload && typeof payload === "object" && !Array.isArray(payload) && !("action" in payload)) {
        const handler = (node.props || {})["on" + type.charAt(0).toUpperCase() + type.slice(1)];
        if (typeof handler === "string") {
          payload.action = handler;
        }
      }
      const id = hooks.sendEvent(type, path, payload, node ? { key: node.key ?? null, type: node.type || "div" } : null);
      if (id != null) {
        unansweredEvents.push(id);
        if (el) {
          inputState(el).lastEventId = id;
        }
      }
      return id;
    }

    function scheduleInput(el, delay) {
      const state = inputState(el);
      if (state.timer != null) {
        clearTimer(state.timer);
      }
      pendingInputs.add(el);
      const timer = setTimer(() => sendInput(el), delay);
      state.timer = pendingInputs.has(el) ? timer : null; // a synchronous timer already sent it
    }

    // The coalesced event reads path, props and value when it goes out, not when typing began.
    function sendInput(el) {
      const state = inputState(el);
      if (state.timer != null) {
        clearTimer(state.timer);
        state.timer = null;
      }
      pendingInputs.delete(el);
      const { type, props, path } = describe(el);
      sendEvent("input", path, collectPayload(type, el, props), el);
    }

    function flushPendingInputs() {
      for (const el of Array.from(pendingInputs)) {
        sendInput(el);
      }
    }

    function inputDebounceMs(type, props) {
      const configured = Number(props.inputDebounceMs);
      if (props.inputDebounceMs != null && Number.isFinite(configured) && configured >= 0) {
        return configured;
      }
      return defaultInputDebounceMs[type] || 0;
    }

    function markAnswered(eventId) {
      if (eventId == null) {
        return;
      }
      const index = unansweredEvents.indexOf(eventId);
      if (index >= 0) {
        unansweredEvents.splice(0, index + 1);
      }
    }

    // True while the element holds edits the server has not seen: a debounced input not sent
    // yet, or an event sent and not answered. A server value then would roll back the typing.
    function hasUnseenEdits(el) {
      if (pendingInputs.has(el)) {
        return true;
      }
      const state = el[inputKey];
      return !!(state && state.lastEventId != null && unansweredEvents.includes(state.lastEventId));
    }

    // Once the server has seen the edits, a held-back value or checked state lands after all.
    function releaseHeldBack() {
      for (const [el, held] of Array.from(heldBack)) {
        if (hasUnseenEdits(el)) {
          continue;
        }
        heldBack.delete(el);
        const node = el[nodeKey];
        if (node && el.getAttribute("data-ui-path") != null) {
          syncFormState(el, renderElement(node, el.getAttribute("data-ui-path")), held.value, held.checked);
        }
      }
    }
    const richEventProps = new Set([
      "onClick", "onChange", "onInput", "onSubmit", "onClose", "onFocus", "onBlur",
      "onRowClick", "onSelectionChange", "onSort", "onFilter", "onPageChange", "onViewportChange",
      "onColumnResize", "onNodeSelect", "onNodeToggle", "onNodeExpand", "onNodeCollapse", "onNodeActivate", "onLoadChildren",
      "onDragStart", "onDragOver", "onDrop", "onDragEnd",
      "onSelect", "onResize", "onPointerDown", "onPointerMove", "onPointerUp", "onWheel"
    ]);

    const controlStateByPath = new Map();
    // Text and the controls drawn from props (dataGrid, treeView, menuBar, commandPalette,
    // propertyGrid): their DOM children do not mirror their UiNode children, so a patch on or
    // inside one renders that node again.
    const opaqueTypes = new Set(["text", "dataGrid", "treeView", "menuBar", "commandPalette", "propertyGrid"]);
    // Live ui.shortcuts and ui.modal elements, for the document's keydown and pointerdown listeners.
    const shortcutElements = new Set();
    const modalElements = new Set();
    // name → element of each ui.hostSlot: a re-render moves the content the program put there.
    const hostSlots = new Map();
    let documentListenersInstalled = false;
    // The open menu-bar dropdown or context menu: { close }.
    let openPopup = null;
    // Kept across patches: the browser's own marks on an input the user has edited.
    const preservedAttributes = new Set(["data-ui-dirty", "data-ui-touched"]);
    const nodeKey = "__maldaNode";

    // The attribute rules of UiPropSanitizer (C#), for trees from any host: no on* attributes,
    // srcdoc or formaction; URL attributes only where the control takes one, with a safe scheme;
    // no style that loads URLs or runs script.
    const urlAttributesByType = { image: ["src"], icon: ["src"], form: ["action"] };
    const urlAttributes = new Set(["href", "src", "srcset", "action", "poster", "background", "cite", "data", "codebase", "ping", "manifest", "xlink:href"]);
    function isSafeUrl(value) {
      const normalized = String(value).replace(/[\t\n\r]/g, "").replace(/^[\x00-\x20]+|[\x00-\x20]+$/g, "");
      const scheme = /^([A-Za-z][A-Za-z0-9+.-]*):/.exec(normalized);
      return !scheme || ["http", "https", "mailto", "tel"].includes(scheme[1].toLowerCase());
    }
    function isSafeCss(value) {
      const compact = String(value).replace(/\/\*[\s\S]*?\*\//g, "").replace(/\s+/g, "").toLowerCase();
      return !["\\", "url(", "image-set(", "expression(", "javascript:", "vbscript:", "@import", "behavior:", "-moz-binding"].some((t) => compact.includes(t));
    }
    function isSafeAttribute(type, name, value) {
      if (!/^[A-Za-z][A-Za-z0-9_:.-]*$/.test(name) || /^on/i.test(name) || /^(srcdoc|formaction)$/i.test(name)) {
        return false;
      }
      const lower = name.toLowerCase();
      if (urlAttributes.has(lower)) {
        return (urlAttributesByType[type] || []).includes(lower) && (lower === "srcset"
          ? String(value).split(",").every((c) => isSafeUrl(c.trim().split(" ")[0]))
          : isSafeUrl(value));
      }
      return true;
    }

    function renderInto(container, tree) {
      while (container.firstChild) {
        container.removeChild(container.firstChild);
      }
      container.appendChild(tree ? renderNode(tree, "/") : doc.createTextNode("No UI mounted yet."));
    }

    // Applies one inbound payload to `tree` and to the DOM under `container`, and returns the new
    // tree. Each patch touches only the nodes it names, so untouched elements keep focus, caret,
    // selection and scroll. A snapshot, or a DOM that no longer matches the tree, renders it all.
    function applyPayload(container, tree, payload) {
      if (!payload) {
        return tree;
      }
      markAnswered(payload.answersEventId);
      if (!payload.patches || !tree || !container.firstChild) {
        const next = MaldaUiProtocol.applyPayload(tree, payload);
        renderInto(container, next);
        return next;
      }

      let inSync = true;
      for (const patch of payload.patches) {
        tree = MaldaUiCore.applyPatches(tree, [patch]);
        if (inSync) {
          inSync = patchDom(container, tree, patch);
        }
      }
      if (!inSync) {
        renderInto(container, tree);
      }
      releaseHeldBack();
      return tree;
    }

    // One patch, already applied to `tree`, carried out on the DOM. False when the DOM does not
    // match what the patch expects; the caller then renders everything.
    function patchDom(container, tree, patch) {
      if (!patch || !patch.op) {
        return true;
      }
      const path = patch.path || "/";
      const onNode = patch.op === "SetProp" || patch.op === "RemoveProp";
      if (patch.op === "ReplaceNode" && path === "/") {
        renderInto(container, tree);
        return true;
      }

      // Walk to the patched node (SetProp / RemoveProp) or to the parent whose children change.
      const segments = MaldaUiCore.parsePath(onNode || patch.op !== "ReplaceNode" ? path : MaldaUiCore.parentOf(path));
      let node = tree;
      let el = container.firstChild;
      let nodePath = "/";
      for (let depth = 0; ; depth++) {
        if (!node || !el) {
          return false;
        }
        if (opaqueTypes.has(node.type)) {
          return replaceDom(el, node, nodePath);
        }
        if (depth === segments.length) {
          break;
        }
        const index = segments[depth];
        node = Array.isArray(node.children) ? node.children[index] : null;
        el = el.childNodes[index];
        nodePath = `${nodePath}${index}/`;
      }

      const children = Array.isArray(node.children) ? node.children : [];
      switch (patch.op) {
        case "SetProp":
        case "RemoveProp":
          syncElement(el, renderElement(node, nodePath), patch.prop);
          if (node.type === "canvas") {
            drawCanvas(el, node.props || {}); // the canvas keeps its identity, so a drag keeps its pointer capture
          }
          return true;
        case "ReplaceNode": {
          const index = MaldaUiCore.childIndexOf(path);
          const old = el.childNodes[index];
          return !!old && replaceDom(old, children[index], `${nodePath}${index}/`);
        }
        case "InsertChild": {
          const index = Number.isInteger(patch.index) ? patch.index : children.length - 1;
          if (index < 0 || index >= children.length || el.childNodes.length !== children.length - 1) {
            return false;
          }
          el.insertBefore(renderNode(children[index], `${nodePath}${index}/`), el.childNodes[index] || null);
          renumberChildren(el, children, nodePath, index + 1);
          return true;
        }
        case "RemoveChild": {
          const index = patch.index;
          if (el.childNodes.length === children.length) {
            return true; // the tree ignored it too
          }
          if (!Number.isInteger(index) || index < 0 || index > children.length || el.childNodes.length !== children.length + 1) {
            return false;
          }
          el.removeChild(el.childNodes[index]);
          renumberChildren(el, children, nodePath, index);
          return true;
        }
        case "MoveChild": {
          const from = patch.from;
          const to = patch.index;
          if (!Number.isInteger(from) || !Number.isInteger(to) || from < 0 || to < 0 ||
              from >= children.length || to >= children.length || el.childNodes.length !== children.length) {
            return false;
          }
          const moved = el.childNodes[from];
          el.removeChild(moved);
          el.insertBefore(moved, el.childNodes[to] || null);
          renumberChildren(el, children, nodePath, Math.min(from, to));
          return true;
        }
        default:
          return true;
      }
    }

    // A control drawn from props renders again on a patch; an editor inside it that had focus
    // (data-ui-focus-key) gets focus and caret back in the new DOM.
    function replaceDom(old, node, path) {
      const active = doc.activeElement;
      const focusKey = active && active !== old && typeof old.contains === "function" && old.contains(active) &&
        typeof active.getAttribute === "function" ? active.getAttribute("data-ui-focus-key") : null;
      const caret = focusKey && typeof active.selectionStart === "number" ? [active.selectionStart, active.selectionEnd] : null;
      const fresh = renderNode(node, path);
      const scrollTop = old.scrollTop;
      old.parentNode.replaceChild(fresh, old);
      if (scrollTop) {
        fresh.scrollTop = scrollTop;
      }
      if (focusKey && typeof fresh.querySelector === "function") {
        const target = fresh.querySelector(`[data-ui-focus-key="${focusKey.replace(/["\\]/g, "\\$&")}"]`);
        if (target && typeof target.focus === "function") {
          target.focus();
          if (caret && typeof target.setSelectionRange === "function") {
            try { target.setSelectionRange(caret[0], caret[1]); } catch { /* not a text input */ }
          }
        }
      }
      return true;
    }

    // Siblings from `start` on moved: their data-ui-path (and their subtrees') follow.
    function renumberChildren(el, children, parentPath, start) {
      for (let index = start; index < children.length; index++) {
        renumber(el.childNodes[index], children[index], `${parentPath}${index}/`);
      }
    }

    function renumber(domNode, node, path) {
      if (!node || node.type === "text" || !domNode || domNode.nodeType !== 1) {
        return;
      }
      if (domNode.getAttribute("data-ui-path") === path) {
        return; // same path, so the subtree is right too
      }
      if (opaqueTypes.has(node.type)) {
        replaceDom(domNode, node, path); // its listeners hold the old path
        return;
      }
      domNode.setAttribute("data-ui-path", path);
      const children = Array.isArray(node.children) ? node.children : [];
      children.forEach((child, index) => renumber(domNode.childNodes[index], child, `${path}${index}/`));
    }

    // Copies what a fresh render of the node would have onto the live element, which keeps its
    // identity, so focus, caret and listeners survive a prop change. The live value and checked
    // state are the user's: only a patch to the prop that sets them overwrites them, and only
    // once the server has seen the user's edits to that element (else it is held back).
    function syncElement(el, fresh, prop) {
      const freshNames = new Set(Array.from(fresh.attributes, (attr) => attr.name));
      for (const name of Array.from(el.attributes, (attr) => attr.name)) {
        if (!freshNames.has(name) && !preservedAttributes.has(name)) {
          el.removeAttribute(name);
        }
      }
      for (const name of freshNames) {
        const value = fresh.getAttribute(name);
        if (el.getAttribute(name) !== value) {
          el.setAttribute(name, value);
        }
      }
      if (el.style.cssText !== fresh.style.cssText) {
        el.style.cssText = fresh.style.cssText;
      }
      if (el.tagName === "INPUT" && el.type !== fresh.type) {
        el.type = fresh.type;
      }
      el[nodeKey] = fresh[nodeKey];
      const syncValue = prop === "value" || prop === "defaultValue";
      const syncChecked = prop === "checked" || prop === "defaultChecked";
      if ((syncValue || syncChecked) && hasUnseenEdits(el)) {
        const held = heldBack.get(el) || { value: false, checked: false };
        heldBack.set(el, { value: held.value || syncValue, checked: held.checked || syncChecked });
      } else {
        syncFormState(el, fresh, syncValue, syncChecked);
      }
    }

    function syncFormState(el, fresh, syncValue, syncChecked) {
      if (syncValue && (el.tagName === "INPUT" || el.tagName === "TEXTAREA" || el.tagName === "SELECT") && el.value !== fresh.value) {
        el.value = fresh.value;
      }
      if (syncChecked && el.tagName === "INPUT" && el.checked !== fresh.checked) {
        el.checked = fresh.checked;
      }
    }

    function renderNode(node, path) {
      if (!node) {
        return doc.createTextNode("");
      }
      const type = node.type || "div";
      const props = node.props || {};

      if (type === "text") {
        return doc.createTextNode(props.value != null ? String(props.value) : "");
      }
      if (type === "dataGrid") {
        return renderDataGrid(path, props, node);
      }
      if (type === "treeView") {
        return renderTreeView(path, props);
      }
      if (type === "menuBar") {
        return renderMenuBar(path, props, node);
      }
      if (type === "commandPalette") {
        return renderCommandPalette(path, props, node);
      }
      if (type === "propertyGrid") {
        return renderPropertyGrid(path, props, node);
      }

      const el = renderElement(node, path);
      if (type === "canvas") {
        attachCanvas(el);
        drawCanvas(el, props);
        return el;
      }
      attachListeners(el);
      attachControl(type, el);
      (node.children || []).forEach((child, index) => {
        el.appendChild(renderNode(child, `${path}${index}/`));
      });
      return el;
    }

    // The element for one node, without children or listeners.
    function renderElement(node, path) {
      const type = node.type || "div";
      const props = node.props || {};
      const tag = mapTag(type);
      const el = doc.createElement(tag);
      el.setAttribute("data-ui-path", path);
      el.setAttribute("data-ui", type);
      applyInputTypeDefaults(type, el, props);

      for (const [key, value] of Object.entries(props)) {
        if (richEventProps.has(key) || key === "inputDebounceMs") {
          continue;
        }
        if (key === "className") {
          el.className = String(value);
        } else if (key === "style") {
          applyStyleProp(el, value);
        } else if (key === "value" && (tag === "input" || tag === "textarea")) {
          el.value = String(value ?? "");
        } else if (key === "checked" && tag === "input") {
          el.checked = !!value;
        } else if (key === "defaultValue" && (tag === "input" || tag === "textarea") && props.value == null) {
          el.value = String(value ?? "");
        } else if (key === "defaultChecked" && tag === "input" && props.checked == null) {
          el.checked = !!value;
        } else if (key === "ariaLabel") {
          el.setAttribute("aria-label", String(value));
        } else if (value != null && (typeof value === "string" || typeof value === "number" || typeof value === "boolean") && isSafeAttribute(type, key, value)) {
          el.setAttribute(key, String(value));
        }
      }

      applyA11yDefaults(type, el, props);
      if (type === "splitPane") {
        applySplitLayout(el, node, path);
      } else if (type === "modal") {
        el.setAttribute("aria-modal", "true");
        if (props.title != null) {
          el.removeAttribute("title");
          el.setAttribute("data-ui-title", String(props.title));
          if (!el.hasAttribute("aria-label")) el.setAttribute("aria-label", String(props.title));
        }
      } else if (type === "hostSlot") {
        el.setAttribute("data-ui-host-slot", props.name != null ? String(props.name) : "");
      }
      el[nodeKey] = node;
      return el;
    }

    // Per-type listeners for the controls whose DOM mirrors their children.
    function attachControl(type, el) {
      if (type === "splitPane") {
        attachSplitPane(el);
      } else if (type === "contextMenu") {
        attachContextMenu(el);
      } else if (type === "shortcuts") {
        shortcutElements.add(el);
        installDocumentListeners();
      } else if (type === "modal") {
        modalElements.add(el);
        installDocumentListeners();
      } else if (type === "hostSlot") {
        const name = el.getAttribute("data-ui-host-slot");
        const previous = hostSlots.get(name);
        if (previous && previous !== el) {
          while (previous.firstChild) {
            el.appendChild(previous.firstChild);
          }
        }
        hostSlots.set(name, el);
      }
    }

    // Every element gets the same listeners; each reads the element's node and path when the
    // event fires, so patches can change props or move the element without re-binding.
    function attachListeners(el) {
      el.addEventListener("click", () => {
        const { type, props, path } = describe(el);
        if (type === "button" || props.onClick) {
          sendEvent("click", path, collectPayload(type, el, props), el);
        }
      });
      el.addEventListener("change", () => {
        const { type, props, path } = describe(el);
        if (changeTypes.has(type) || props.onChange) {
          sendEvent("change", path, collectPayload(type, el, props), el);
        }
      });
      el.addEventListener("input", () => {
        const { type, props } = describe(el);
        if (inputTypes.has(type) || props.onInput) {
          const delay = inputDebounceMs(type, props);
          if (delay > 0) {
            scheduleInput(el, delay);
          } else {
            sendInput(el);
          }
        }
      });
      el.addEventListener("submit", (evt) => {
        const { type, props, path } = describe(el);
        if (type === "form" || props.onSubmit) {
          evt.preventDefault();
          sendEvent("submit", path, collectPayload(type, el, props), el);
        }
      });
      el.addEventListener("focus", () => {
        const { type, props, path } = describe(el);
        if (props.onFocus) {
          sendEvent("focus", path, collectPayload(type, el, props), el);
        }
      });
      el.addEventListener("blur", () => {
        const { type, props, path } = describe(el);
        if (props.onBlur) {
          sendEvent("blur", path, collectPayload(type, el, props), el);
        }
      });
    }

    function describe(el) {
      const node = el[nodeKey] || {};
      return { type: node.type || "div", props: node.props || {}, path: el.getAttribute("data-ui-path") };
    }

    const changeTypes = new Set([
      "textField", "slider", "checkbox", "select", "textArea", "radioGroup", "switch", "datePicker", "paginator"
    ]);
    const inputTypes = new Set(["textField", "textArea", "slider", "datePicker"]);


    function renderDataGrid(path, props, node) {
      const root = doc.createElement("div");
      root.setAttribute("data-ui-path", path);
      root.setAttribute("data-ui", "dataGrid");
      root[nodeKey] = node;
      const emit = (type, payload) => sendEvent(type, path, payload, root);
      applyCommonProps(root, props, new Set(["columns", "rows", "selectedKeys", "sort", "filter", "rowKey"]));
      root.setAttribute("role", root.getAttribute("role") || "grid");
      root.classList.add("malda-data-grid");

      const columns = Array.isArray(props.columns) ? props.columns : [];
      const rawRows = Array.isArray(props.rows) ? props.rows : [];
      const rowHeight = Math.max(20, Number(props.rowHeight || 36));
      const overscan = Math.max(0, Number(props.overscan || 5));
      const virtualize = !!props.virtualize;
      const state = getControlState(path);
      const selectedKeys = normalizeKeySet(props.selectedKeys);
      if (Array.isArray(props.selectedKeys)) {
        state.localSelectedKeys = new Set(selectedKeys);
      } else if (!(state.localSelectedKeys instanceof Set)) {
        state.localSelectedKeys = new Set(selectedKeys);
      }
      const effectiveSelectedKeys = state.localSelectedKeys;
      const selectionMode = String(props.selectionMode || "single");
      const rowKeyField = typeof props.rowKey === "string" ? props.rowKey : "id";
      const dragEnabled = !!(props.onDragStart || props.onDragOver || props.onDrop || props.onDragEnd);
      const resizableColumns = !!(props.resizableColumns || props.resizable);
      const minColumnWidth = Math.max(80, Number(props.minColumnWidth || 100));
      if (!state.columnWidths) {
        state.columnWidths = {};
      }
      if (props.onSort) {
        state.localSort = null;
      }
      const activeSort = normalizeGridSort(props.sort) || (!props.onSort ? normalizeGridSort(state.localSort) : null);
      const rows = getGridRowsWithLocalSort(rawRows, columns, activeSort, !!props.sortable);
      const resolveColumnWidth = (column, idx) => {
        const stateWidth = state.columnWidths[idx];
        if (stateWidth != null && !Number.isNaN(Number(stateWidth))) {
          return `${Math.max(minColumnWidth, Number(stateWidth))}px`;
        }
        if (column && typeof column.width === "number" && Number.isFinite(column.width)) {
          return `${Math.max(minColumnWidth, column.width)}px`;
        }
        if (column && typeof column.width === "string" && column.width.trim() !== "") {
          return column.width.trim();
        }
        return "minmax(100px, 1fr)";
      };
      const buildTemplateColumns = () => columns.map((column, idx) => resolveColumnWidth(column, idx)).join(" ");
      const applyTemplateColumns = (templateColumns, viewportEl, headerEl) => {
        if (!templateColumns || !headerEl) {
          return;
        }
        headerEl.style.gridTemplateColumns = templateColumns;
        if (!viewportEl) {
          return;
        }
        viewportEl.querySelectorAll(".malda-data-grid-row").forEach((rowEl) => {
          rowEl.style.gridTemplateColumns = templateColumns;
        });
      };

      const header = doc.createElement("div");
      header.className = "malda-data-grid-header";
      header.setAttribute("role", "row");
      columns.forEach((column, idx) => {
        const cell = doc.createElement("div");
        cell.className = "malda-data-grid-header-cell";
        cell.setAttribute("role", "columnheader");
        cell.tabIndex = 0;
        const title = column && column.title != null ? String(column.title) : `Column ${idx + 1}`;
        const sortKey = column && column.key != null ? String(column.key) : title;
        cell.setAttribute("data-column-key", sortKey);
        cell.setAttribute("data-column-title", title);
        const titleEl = doc.createElement("span");
        titleEl.className = "malda-data-grid-header-title";
        titleEl.textContent = title;
        cell.appendChild(titleEl);
        const isSortable = !!(column && (column.sortable || props.sortable));
        if (isSortable) {
          const direction = activeSort && activeSort.key === sortKey ? activeSort.direction : null;
          cell.setAttribute("aria-sort", direction === "asc" ? "ascending" : direction === "desc" ? "descending" : "none");
        }
        if (isSortable) {
          cell.addEventListener("click", () => {
            const nextSort = getNextGridSort(sortKey, activeSort);
            if (props.onSort) {
              emit("sort", nextSort);
              return;
            }
            state.localSort = nextSort;
            if (root.parentNode) {
              root.parentNode.replaceChild(renderDataGrid(path, props, node), root);
            }
          });
        }
        if (resizableColumns) {
          const handle = doc.createElement("span");
          handle.className = "malda-data-grid-resize-handle";
          handle.addEventListener("mousedown", (evt) => {
            evt.preventDefault();
            evt.stopPropagation();
            const startX = evt.clientX;
            const startWidth = Math.max(minColumnWidth, Math.round(cell.getBoundingClientRect().width || minColumnWidth));
            const onMouseMove = (moveEvt) => {
              const nextWidth = Math.max(minColumnWidth, Math.round(startWidth + (moveEvt.clientX - startX)));
              state.columnWidths[idx] = nextWidth;
              const nextTemplate = buildTemplateColumns();
              applyTemplateColumns(nextTemplate, viewport, header);
            };
            const onMouseUp = () => {
              doc.removeEventListener("mousemove", onMouseMove);
              doc.removeEventListener("mouseup", onMouseUp);
              if (props.onColumnResize) {
                const resizedColumn = column && column.key != null ? String(column.key) : String(idx);
                emit("columnResize", {
                  columnKey: resizedColumn,
                  width: state.columnWidths[idx],
                  widths: columns.map((_, colIdx) => state.columnWidths[colIdx] || null)
                });
              }
            };
            doc.addEventListener("mousemove", onMouseMove);
            doc.addEventListener("mouseup", onMouseUp);
          });
          handle.addEventListener("click", (evt) => {
            evt.preventDefault();
            evt.stopPropagation();
          });
          cell.appendChild(handle);
        }
        header.appendChild(cell);
      });
      root.appendChild(header);

      if (props.onFilter) {
        const filterBox = doc.createElement("input");
        filterBox.type = "text";
        filterBox.className = "malda-data-grid-filter";
        filterBox.placeholder = "Filter";
        filterBox.value = props.filter != null ? String(props.filter) : "";
        filterBox.addEventListener("input", () => {
          emit("filter", { filter: filterBox.value });
        });
        root.appendChild(filterBox);
      }

      const viewport = doc.createElement("div");
      viewport.className = "malda-data-grid-viewport";
      viewport.style.overflow = "auto";
      viewport.style.maxHeight = props.height != null ? String(props.height) : "320px";
      viewport.tabIndex = 0;
      root.appendChild(viewport);

      const totalRows = rows.length;
      const visibleCount = Math.max(1, Math.ceil(parseCssPixelValue(viewport.style.maxHeight, 320) / rowHeight));
      let start = 0;
      let end = totalRows;
      if (virtualize) {
        start = Math.max(0, Math.floor((state.scrollTop || 0) / rowHeight) - overscan);
        end = Math.min(totalRows, start + visibleCount + overscan * 2);
      }

      const topSpacer = doc.createElement("div");
      topSpacer.style.height = virtualize ? `${start * rowHeight}px` : "0px";
      viewport.appendChild(topSpacer);

      for (let rowIndex = start; rowIndex < end; rowIndex++) {
        const row = rows[rowIndex];
        const rowKey = resolveRowKey(row, rowIndex, rowKeyField);
        const rowEl = doc.createElement("div");
        rowEl.className = "malda-data-grid-row";
        rowEl.setAttribute("role", "row");
        rowEl.setAttribute("data-row-index", String(rowIndex));
        rowEl.setAttribute("data-row-key", rowKey);
        rowEl.tabIndex = 0;
        if (dragEnabled) {
          rowEl.draggable = true;
        }
        if (effectiveSelectedKeys.has(rowKey)) {
          rowEl.setAttribute("aria-selected", "true");
        }

        columns.forEach((column, cellIndex) => {
          const cell = doc.createElement("div");
          cell.className = "malda-data-grid-cell";
          cell.setAttribute("role", "gridcell");
          const columnTitle = column && column.title != null ? String(column.title) : `Column ${cellIndex + 1}`;
          const columnKey = column && column.key != null ? String(column.key) : columnTitle;
          cell.setAttribute("data-column-key", columnKey);
          cell.setAttribute("data-column-title", columnTitle);
          const value = readCellValue(row, column, cellIndex);
          cell.textContent = value == null ? "" : String(value);
          rowEl.appendChild(cell);
        });

        rowEl.addEventListener("click", () => {
          const nextSelected = computeNextSelection(selectionMode, effectiveSelectedKeys, rowKey);
          state.localSelectedKeys = nextSelected;
          applyGridSelectionState(viewport, nextSelected);
          emit("rowClick", { rowIndex, rowKey, row });
          emit("selectionChange", { rowIndex, rowKey, selectedKeys: Array.from(nextSelected) });
        });
        if (dragEnabled) {
          rowEl.addEventListener("dragstart", (evt) => {
            state.draggedRow = { rowIndex, rowKey, row };
            if (evt.dataTransfer) {
              evt.dataTransfer.effectAllowed = "move";
              evt.dataTransfer.setData("text/plain", rowKey);
            }
            emit("dragStart", { rowIndex, rowKey, row });
          });
          rowEl.addEventListener("dragover", (evt) => {
            evt.preventDefault();
            emit("dragOver", {
              rowIndex,
              rowKey,
              sourceRow: state.draggedRow || null
            });
          });
          rowEl.addEventListener("drop", (evt) => {
            evt.preventDefault();
            const sourceRow = state.draggedRow || null;
            emit("drop", { rowIndex, rowKey, row, sourceRow });
          });
          rowEl.addEventListener("dragend", () => {
            emit("dragEnd", { rowIndex, rowKey, row });
            state.draggedRow = null;
          });
        }
        rowEl.addEventListener("keydown", (evt) => handleGridRowKeydown(evt, viewport, path, rowIndex, rowKey, totalRows));
        viewport.appendChild(rowEl);
      }
      const templateColumns = buildTemplateColumns();
      applyTemplateColumns(templateColumns, viewport, header);

      const bottomSpacer = doc.createElement("div");
      bottomSpacer.style.height = virtualize ? `${Math.max(0, totalRows - end) * rowHeight}px` : "0px";
      viewport.appendChild(bottomSpacer);

      viewport.addEventListener("scroll", () => {
        state.scrollTop = viewport.scrollTop;
        if (virtualize && props.onViewportChange) {
          const nextStart = Math.max(0, Math.floor(viewport.scrollTop / rowHeight) - overscan);
          const nextEnd = Math.min(totalRows, nextStart + visibleCount + overscan * 2);
          const signature = `${nextStart}:${nextEnd}:${totalRows}`;
          if (state.viewportSignature !== signature) {
            state.viewportSignature = signature;
            emit("viewportChange", { start: nextStart, end: nextEnd, totalRows });
          }
        }
      });
      viewport.addEventListener("keydown", (evt) => handleGridViewportKeydown(evt, viewport));
      if (dragEnabled) {
        viewport.addEventListener("dragover", (evt) => {
          evt.preventDefault();
        });
        viewport.addEventListener("drop", (evt) => {
          evt.preventDefault();
          const sourceRow = state.draggedRow || null;
          emit("drop", { rowIndex: null, rowKey: null, sourceRow });
        });
      }

      requestFrame(() => {
        if (state.scrollTop && viewport.scrollTop !== state.scrollTop) {
          viewport.scrollTop = state.scrollTop;
        }
      });

      if (props.onPageChange && props.page != null && props.pageSize != null) {
        const pager = doc.createElement("div");
        pager.className = "malda-data-grid-pager";
        const page = Number(props.page);
        const pageSize = Math.max(1, Number(props.pageSize));
        const totalItems = props.totalItems != null ? Number(props.totalItems) : rows.length;
        const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

        const prev = doc.createElement("button");
        prev.type = "button";
        prev.textContent = "Prev";
        prev.disabled = page <= 1;
        prev.addEventListener("click", () => emit("pageChange", { page: Math.max(1, page - 1), pageSize, totalItems }));

        const next = doc.createElement("button");
        next.type = "button";
        next.textContent = "Next";
        next.disabled = page >= totalPages;
        next.addEventListener("click", () => emit("pageChange", { page: Math.min(totalPages, page + 1), pageSize, totalItems }));

        const label = doc.createElement("span");
        label.textContent = `Page ${page} / ${totalPages}`;
        pager.appendChild(prev);
        pager.appendChild(label);
        pager.appendChild(next);
        root.appendChild(pager);
      }

      return root;
    }

    function renderTreeView(path, props) {
      const root = doc.createElement("div");
      root.setAttribute("data-ui-path", path);
      root.setAttribute("data-ui", "treeView");
      applyCommonProps(root, props, new Set(["nodes", "expandedKeys", "selectedKeys", "nodeKey", "selectionMode"]));
      root.setAttribute("role", root.getAttribute("role") || "tree");
      root.classList.add("malda-tree-view");
      root.tabIndex = 0;

      const nodes = Array.isArray(props.nodes) ? props.nodes : [];
      const expandedKeys = normalizeKeySet(props.expandedKeys);
      const selectedKeys = normalizeKeySet(props.selectedKeys);
      const selectionMode = String(props.selectionMode || "single");
      const nodeKeyField = typeof props.nodeKey === "string" ? props.nodeKey : "id";
      const state = getControlState(path);
      state.loadedNodeKeys = state.loadedNodeKeys || new Set();
      state.loadingNodeKeys = state.loadingNodeKeys || new Set();

      const list = doc.createElement("ul");
      list.style.listStyle = props.showLines ? "disc" : "none";
      list.style.paddingLeft = "0.75rem";
      root.appendChild(list);
      renderTreeNodes(list, nodes, path, 1, expandedKeys, selectedKeys, selectionMode, nodeKeyField, props, state);

      root.addEventListener("keydown", (evt) => {
        const focused = doc.activeElement;
        if (!focused || !root.contains(focused)) return;
        const items = Array.from(root.querySelectorAll("[role='treeitem']"));
        const index = items.indexOf(focused);
        if (index < 0) return;
        let next = index;
        if (evt.key === "ArrowDown") next = Math.min(items.length - 1, index + 1);
        if (evt.key === "ArrowUp") next = Math.max(0, index - 1);
        if (evt.key === "Home") next = 0;
        if (evt.key === "End") next = items.length - 1;
        if (next !== index) {
          items[next].focus();
          evt.preventDefault();
        }
        if (evt.key === "Enter" || evt.key === " ") {
          focused.click();
          evt.preventDefault();
        }
      });

      return root;
    }

    function renderTreeNodes(container, nodes, path, level, expandedKeys, selectedKeys, selectionMode, nodeKeyField, props, treeState) {
      (nodes || []).forEach((node, index) => {
        const li = doc.createElement("li");
        li.setAttribute("role", "none");
        const item = doc.createElement("div");
        const nodePath = `${path}${index}/`;
        const nodeKey = resolveTreeKey(node, nodePath, nodeKeyField);
        const children = Array.isArray(node && node.children) ? node.children : [];
        const nodeHasChildrenFlag = !!(node && typeof node === "object" && (node.hasChildren === true || Number(node.childCount || 0) > 0 || node.childrenLoaded === false));
        const hasChildren = children.length > 0 || nodeHasChildrenFlag;
        const expanded = expandedKeys.has(nodeKey);
        const lazy = !!props.lazy;
        if (children.length > 0) {
          treeState.loadedNodeKeys.add(nodeKey);
          treeState.loadingNodeKeys.delete(nodeKey);
        }
        if (lazy && hasChildren && expanded && children.length === 0 && !treeState.loadedNodeKeys.has(nodeKey) && !treeState.loadingNodeKeys.has(nodeKey)) {
          treeState.loadingNodeKeys.add(nodeKey);
          sendEvent("nodeExpand", nodePath, { nodeKey, expanded: true, lazy: true, loadChildren: true, node });
          if (props.onLoadChildren) {
            sendEvent("loadChildren", nodePath, { nodeKey, node });
          }
        }
        item.setAttribute("role", "treeitem");
        item.setAttribute("aria-level", String(level));
        item.setAttribute("data-ui-path", nodePath);
        item.setAttribute("data-node-key", nodeKey);
        item.tabIndex = 0;
        item.textContent = String(node && (node.label ?? node.title ?? node.name ?? nodeKey));
        if (hasChildren) {
          item.setAttribute("aria-expanded", expanded ? "true" : "false");
        }
        if (selectedKeys.has(nodeKey)) {
          item.setAttribute("aria-selected", "true");
        }
        if (props.onDragStart || props.onDragOver || props.onDrop || props.onDragEnd) {
          item.draggable = true;
          item.addEventListener("dragstart", (evt) => {
            treeState.draggedNode = { nodeKey, nodePath, level, node };
            if (evt.dataTransfer) {
              evt.dataTransfer.effectAllowed = "move";
              evt.dataTransfer.setData("text/plain", nodeKey);
            }
            sendEvent("dragStart", nodePath, { nodeKey, nodePath, level, node });
          });
          item.addEventListener("dragover", (evt) => {
            evt.preventDefault();
            sendEvent("dragOver", nodePath, { nodeKey, nodePath, level, node, sourceNode: treeState.draggedNode || null });
          });
          item.addEventListener("drop", (evt) => {
            evt.preventDefault();
            sendEvent("drop", nodePath, { nodeKey, nodePath, level, node, sourceNode: treeState.draggedNode || null });
          });
          item.addEventListener("dragend", () => {
            sendEvent("dragEnd", nodePath, { nodeKey, nodePath, level, node });
            treeState.draggedNode = null;
          });
        }

        item.addEventListener("click", () => {
          const nextSelection = computeNextSelection(selectionMode, selectedKeys, nodeKey);
          sendEvent("nodeSelect", nodePath, { nodeKey, selectedKeys: Array.from(nextSelection), node });
          if (props.onNodeActivate) {
            sendEvent("nodeActivate", nodePath, { nodeKey, node });
          }
        });

        if (hasChildren) {
          item.addEventListener("dblclick", () => {
            const nextExpanded = !expanded;
            sendEvent("nodeToggle", nodePath, { nodeKey, expanded: nextExpanded });
            sendEvent(nextExpanded ? "nodeExpand" : "nodeCollapse", nodePath, { nodeKey, expanded: nextExpanded });
            if (nextExpanded && props.lazy && children.length === 0 && !treeState.loadedNodeKeys.has(nodeKey) && !treeState.loadingNodeKeys.has(nodeKey)) {
              treeState.loadingNodeKeys.add(nodeKey);
            }
            if (nextExpanded && props.onLoadChildren && props.lazy && children.length === 0) {
              sendEvent("loadChildren", nodePath, { nodeKey, node });
            }
          });
        }

        li.appendChild(item);
        if (hasChildren && expanded && children.length > 0) {
          const nested = doc.createElement("ul");
          nested.style.listStyle = props.showLines ? "circle" : "none";
          nested.style.paddingLeft = "1rem";
          renderTreeNodes(nested, children, `${nodePath}`, level + 1, expandedKeys, selectedKeys, selectionMode, nodeKeyField, props, treeState);
          li.appendChild(nested);
        } else if (hasChildren && expanded && props.lazy && children.length === 0 && treeState.loadingNodeKeys.has(nodeKey)) {
          const loading = doc.createElement("div");
          loading.textContent = "Loading...";
          loading.className = "malda-tree-view-loading";
          li.appendChild(loading);
        }
        container.appendChild(li);
      });
    }

    function emitFrom(el, type, payload) {
      return sendEvent(type, el.getAttribute("data-ui-path"), payload, el);
    }

    // ui.splitPane: a grid whose gaps are the separators. sizes are weights (fr); a drag keeps its
    // sizes for that pane until the program passes other sizes, and sends `resize` with the sizes
    // as percentages when the pane has onResize.
    function applySplitLayout(el, node, path) {
      const props = node.props || {};
      const count = Array.isArray(node.children) ? node.children.length : 0;
      const vertical = props.direction === "column" || props.direction === "vertical";
      const state = getControlState(path);
      const given = JSON.stringify(props.sizes ?? null);
      let sizes = state.sizes && state.sizes.length === count && state.givenSizes === given ? state.sizes
        : Array.isArray(props.sizes) && props.sizes.length === count ? props.sizes.map(Number) : [];
      if (sizes.length !== count || sizes.some((size) => !Number.isFinite(size) || size <= 0)) {
        sizes = new Array(count).fill(1);
      }
      const template = sizes.map((size) => `minmax(0, ${size}fr)`).join(" ");
      el.setAttribute("data-ui-direction", vertical ? "column" : "row");
      el.style.gridTemplateColumns = vertical ? "" : template;
      el.style.gridTemplateRows = vertical ? template : "";
    }

    function attachSplitPane(el) {
      el.addEventListener("pointerdown", (evt) => {
        if (evt.target !== el || evt.button !== 0) {
          return;
        }
        const vertical = el.getAttribute("data-ui-direction") === "column";
        const panes = Array.from(el.childNodes);
        if (panes.some((pane) => pane.nodeType !== 1)) {
          return;
        }
        const rects = panes.map((pane) => pane.getBoundingClientRect());
        const at = vertical ? evt.clientY : evt.clientX;
        const gap = rects.findIndex((rect, i) => i + 1 < rects.length &&
          at >= (vertical ? rect.bottom : rect.right) - 2 && at <= (vertical ? rects[i + 1].top : rects[i + 1].left) + 2);
        if (gap < 0) {
          return;
        }
        evt.preventDefault();
        const { props, path } = describe(el);
        const state = getControlState(path);
        const sizes = rects.map((rect) => Math.max(1, vertical ? rect.height : rect.width));
        const first = sizes[gap];
        const pair = first + sizes[gap + 1];
        const minSize = Math.min(pair / 2, Number(props.minSize) > 0 ? Number(props.minSize) : 40);
        if (typeof el.setPointerCapture === "function") el.setPointerCapture(evt.pointerId);
        const move = (moveEvt) => {
          const next = Math.max(minSize, Math.min(pair - minSize, first + (vertical ? moveEvt.clientY : moveEvt.clientX) - at));
          sizes[gap] = next;
          sizes[gap + 1] = pair - next;
          state.sizes = sizes.slice();
          state.givenSizes = JSON.stringify(describe(el).props.sizes ?? null);
          applySplitLayout(el, el[nodeKey], path);
        };
        const up = () => {
          el.removeEventListener("pointermove", move);
          el.removeEventListener("pointerup", up);
          el.removeEventListener("pointercancel", up);
          if (typeof el.releasePointerCapture === "function") el.releasePointerCapture(evt.pointerId);
          const total = sizes.reduce((sum, size) => sum + size, 0);
          if (describe(el).props.onResize) {
            emitFrom(el, "resize", { sizes: sizes.map((size) => Math.round(size / total * 1000) / 10) });
          }
        };
        el.addEventListener("pointermove", move);
        el.addEventListener("pointerup", up);
        el.addEventListener("pointercancel", up);
      });
    }

    // A menu of { id, label, shortcut?, disabled? } and { separator: true } items. pick(item) runs
    // on a click or Enter; Escape closes it.
    function renderMenuList(items, pick, close) {
      const list = doc.createElement("div");
      list.className = "malda-menu";
      list.setAttribute("role", "menu");
      for (const item of Array.isArray(items) ? items : []) {
        if (!item || typeof item !== "object") continue;
        if (item.separator) {
          const separator = doc.createElement("div");
          separator.className = "malda-menu-separator";
          separator.setAttribute("role", "separator");
          list.appendChild(separator);
          continue;
        }
        const button = doc.createElement("button");
        button.type = "button";
        button.className = "malda-menu-item";
        button.setAttribute("role", "menuitem");
        button.disabled = !!item.disabled;
        const label = doc.createElement("span");
        label.textContent = String(item.label ?? item.id ?? "");
        button.appendChild(label);
        if (item.shortcut) {
          const shortcut = doc.createElement("span");
          shortcut.className = "malda-menu-shortcut";
          shortcut.textContent = String(item.shortcut);
          button.appendChild(shortcut);
        }
        button.addEventListener("click", (evt) => {
          evt.stopPropagation();
          if (!item.disabled) pick(item);
        });
        list.appendChild(button);
      }
      list.addEventListener("keydown", (evt) => {
        const buttons = Array.from(list.querySelectorAll(".malda-menu-item:not([disabled])"));
        const index = buttons.indexOf(doc.activeElement);
        let next = -1;
        if (evt.key === "ArrowDown") next = (index + 1) % buttons.length;
        else if (evt.key === "ArrowUp") next = (index - 1 + buttons.length) % buttons.length;
        else if (evt.key === "Home") next = 0;
        else if (evt.key === "End") next = buttons.length - 1;
        else if (evt.key === "Escape") {
          evt.preventDefault();
          close();
          return;
        }
        if (next >= 0 && buttons[next]) {
          evt.preventDefault();
          buttons[next].focus();
        }
      });
      return list;
    }

    // Opens a popup (menu-bar dropdown or context menu); a press outside it, or Escape, closes it.
    function showPopup(list, parent, onClose) {
      closePopup();
      parent.appendChild(list);
      const outside = (evt) => {
        if (!list.contains(evt.target)) closePopup();
      };
      doc.addEventListener("pointerdown", outside, true);
      openPopup = {
        list,
        close() {
          doc.removeEventListener("pointerdown", outside, true);
          if (list.parentNode) list.parentNode.removeChild(list);
          if (onClose) onClose();
        }
      };
      const first = list.querySelector(".malda-menu-item:not([disabled])");
      if (first) first.focus();
    }

    function closePopup() {
      const popup = openPopup;
      openPopup = null;
      if (popup) popup.close();
    }

    // ui.menuBar({ menus: [{ label, items }] , onSelect }): sends `select` with { id, label, menu }.
    function renderMenuBar(path, props, node) {
      const root = doc.createElement("div");
      root.setAttribute("data-ui-path", path);
      root.setAttribute("data-ui", "menuBar");
      root[nodeKey] = node;
      applyCommonProps(root, props, new Set(["menus"]));
      root.classList.add("malda-menu-bar");
      root.setAttribute("role", "menubar");
      const menus = Array.isArray(props.menus) ? props.menus : [];
      let openIndex = -1;
      const buttons = menus.map((menu, index) => {
        const button = doc.createElement("button");
        button.type = "button";
        button.className = "malda-menu-bar-item";
        button.setAttribute("role", "menuitem");
        button.setAttribute("aria-haspopup", "true");
        button.setAttribute("aria-expanded", "false");
        button.textContent = String(menu && menu.label != null ? menu.label : `Menu ${index + 1}`);
        const open = () => {
          const list = renderMenuList(menu && menu.items, (item) => {
            closePopup();
            emitFrom(root, "select", { id: item.id ?? null, label: item.label ?? null, menu: menu.label ?? null });
          }, closePopup);
          list.style.left = `${button.offsetLeft || 0}px`;
          showPopup(list, root, () => {
            openIndex = -1;
            button.setAttribute("aria-expanded", "false");
          });
          openIndex = index;
          button.setAttribute("aria-expanded", "true");
        };
        button.addEventListener("click", () => {
          if (openIndex === index) closePopup();
          else open();
        });
        button.addEventListener("mouseenter", () => {
          if (openIndex >= 0 && openIndex !== index) open();
        });
        button.addEventListener("keydown", (evt) => {
          if (evt.key === "ArrowRight" || evt.key === "ArrowLeft") {
            evt.preventDefault();
            const next = buttons[(index + (evt.key === "ArrowRight" ? 1 : buttons.length - 1)) % buttons.length];
            if (next) next.focus();
          } else if (evt.key === "ArrowDown") {
            evt.preventDefault();
            open();
          }
        });
        root.appendChild(button);
        return button;
      });
      return root;
    }

    // ui.contextMenu({ items, onSelect }, [child]): a right click inside opens the items; `select`
    // carries { id, label, targetPath } with the path of the element clicked.
    function attachContextMenu(el) {
      el.addEventListener("contextmenu", (evt) => {
        const { props } = describe(el);
        if (!Array.isArray(props.items) || props.items.length === 0) {
          return;
        }
        evt.preventDefault();
        let target = evt.target;
        while (target && target !== el && !(target.getAttribute && target.getAttribute("data-ui-path"))) {
          target = target.parentNode;
        }
        const targetPath = target && target.getAttribute ? target.getAttribute("data-ui-path") : el.getAttribute("data-ui-path");
        const list = renderMenuList(props.items, (item) => {
          closePopup();
          emitFrom(el, "select", { id: item.id ?? null, label: item.label ?? null, targetPath });
        }, closePopup);
        list.style.position = "fixed";
        list.style.left = `${evt.clientX}px`;
        list.style.top = `${evt.clientY}px`;
        showPopup(list, doc.body || el, null);
      });
    }

    // ui.commandPalette({ open, commands: [{ id, label, group?, shortcut? }], onSelect, onClose }):
    // filters as the user types (every word must match); `select` carries { id, label }, and
    // Escape or a press outside sends `close`. The typed query survives re-renders while open.
    function renderCommandPalette(path, props, node) {
      const root = doc.createElement("div");
      root.setAttribute("data-ui-path", path);
      root.setAttribute("data-ui", "commandPalette");
      root[nodeKey] = node;
      applyCommonProps(root, props, new Set(["commands", "open", "placeholder"]));
      root.classList.add("malda-command-palette");
      const state = getControlState(path);
      if (!props.open) {
        root.hidden = true;
        state.query = "";
        state.active = 0;
        return root;
      }
      const box = doc.createElement("div");
      box.className = "malda-command-palette-box";
      box.setAttribute("role", "dialog");
      const input = doc.createElement("input");
      input.className = "malda-command-palette-input";
      input.type = "text";
      input.placeholder = props.placeholder != null ? String(props.placeholder) : "Type a command";
      input.setAttribute("data-ui-focus-key", "palette");
      input.setAttribute("aria-label", input.placeholder);
      input.value = state.query || "";
      const list = doc.createElement("div");
      list.className = "malda-command-palette-list";
      list.setAttribute("role", "listbox");
      box.appendChild(input);
      box.appendChild(list);
      root.appendChild(box);

      const commands = (Array.isArray(props.commands) ? props.commands : []).filter((c) => c && c.id != null);
      let visible = [];
      const choose = (command) => emitFrom(root, "select", { id: command.id, label: command.label ?? null });
      const draw = () => {
        const words = String(state.query || "").toLowerCase().split(/\s+/).filter(Boolean);
        visible = commands.filter((c) => {
          const text = `${c.label ?? c.id} ${c.group ?? ""}`.toLowerCase();
          return words.every((word) => text.includes(word));
        });
        state.active = Math.max(0, Math.min(state.active || 0, visible.length - 1));
        while (list.firstChild) list.removeChild(list.firstChild);
        if (visible.length === 0) {
          const empty = doc.createElement("div");
          empty.className = "malda-command-palette-empty";
          empty.textContent = "No matching commands";
          list.appendChild(empty);
        }
        visible.forEach((command, index) => {
          const option = doc.createElement("div");
          option.className = "malda-command-palette-option";
          option.setAttribute("role", "option");
          option.setAttribute("aria-selected", index === state.active ? "true" : "false");
          const label = doc.createElement("span");
          label.textContent = String(command.label ?? command.id);
          option.appendChild(label);
          const hint = command.shortcut ?? command.group;
          if (hint != null) {
            const side = doc.createElement("span");
            side.className = "malda-command-palette-group";
            side.textContent = String(hint);
            option.appendChild(side);
          }
          option.addEventListener("pointerdown", (evt) => evt.preventDefault());
          option.addEventListener("click", () => choose(command));
          list.appendChild(option);
        });
      };
      input.addEventListener("input", () => {
        state.query = input.value;
        state.active = 0;
        draw();
      });
      input.addEventListener("keydown", (evt) => {
        if (evt.key === "ArrowDown" || evt.key === "ArrowUp") {
          evt.preventDefault();
          const step = evt.key === "ArrowDown" ? 1 : -1;
          state.active = visible.length ? (state.active + step + visible.length) % visible.length : 0;
          draw();
        } else if (evt.key === "Enter") {
          evt.preventDefault();
          if (visible[state.active]) choose(visible[state.active]);
        } else if (evt.key === "Escape") {
          evt.preventDefault();
          emitFrom(root, "close", { reason: "escape" });
        }
      });
      root.addEventListener("pointerdown", (evt) => {
        if (evt.target === root) emitFrom(root, "close", { reason: "outside" });
      });
      draw();
      requestFrame(() => {
        if (typeof input.focus === "function") input.focus();
      });
      return root;
    }

    // ui.propertyGrid({ properties: [{ key, label?, type?, value, options?, group?, readOnly?,
    // min?, max?, step? }], onChange }): one editor per property (text, number, bool, select,
    // color); `change` carries { key, value } once an edit is committed (Enter, blur or a pick).
    function renderPropertyGrid(path, props, node) {
      const root = doc.createElement("div");
      root.setAttribute("data-ui-path", path);
      root.setAttribute("data-ui", "propertyGrid");
      root[nodeKey] = node;
      applyCommonProps(root, props, new Set(["properties"]));
      root.classList.add("malda-property-grid");
      let group = null;
      for (const property of Array.isArray(props.properties) ? props.properties : []) {
        if (!property || typeof property !== "object" || property.key == null) continue;
        const key = String(property.key);
        if (property.group != null && String(property.group) !== group) {
          group = String(property.group);
          const header = doc.createElement("div");
          header.className = "malda-property-grid-group";
          header.textContent = group;
          root.appendChild(header);
        }
        const label = doc.createElement("label");
        label.className = "malda-property-grid-label";
        label.textContent = String(property.label ?? key);
        label.title = label.textContent;
        root.appendChild(label);
        const editor = createPropertyEditor(property, (value) => emitFrom(root, "change", { key: property.key, value }));
        editor.setAttribute("data-ui-focus-key", "property:" + key);
        editor.setAttribute("aria-label", label.textContent);
        root.appendChild(editor);
      }
      return root;
    }

    function createPropertyEditor(property, commit) {
      const type = String(property.type || (typeof property.value === "boolean" ? "bool" : typeof property.value === "number" ? "number" : "text"));
      const value = property.value;
      if (property.readOnly) {
        const span = doc.createElement("span");
        span.textContent = value == null ? "" : typeof value === "object" ? JSON.stringify(value) : String(value);
        span.tabIndex = 0;
        return span;
      }
      if (type === "bool" || type === "boolean") {
        const box = doc.createElement("input");
        box.type = "checkbox";
        box.checked = !!value;
        box.addEventListener("change", () => commit(!!box.checked));
        return box;
      }
      if (type === "select") {
        const select = doc.createElement("select");
        const options = Array.isArray(property.options) ? property.options : [];
        options.forEach((option, index) => {
          const optionValue = option && typeof option === "object" ? option.value : option;
          const el = doc.createElement("option");
          el.value = String(index);
          el.textContent = String(option && typeof option === "object" ? option.label ?? option.value : option);
          if (optionValue === value) el.selected = true;
          select.appendChild(el);
        });
        select.addEventListener("change", () => {
          const option = options[Number(select.value)];
          commit(option && typeof option === "object" ? option.value : option);
        });
        return select;
      }
      const input = doc.createElement("input");
      input.type = type === "number" ? "number" : type === "color" ? "color" : "text";
      for (const name of ["min", "max", "step", "placeholder"]) {
        if (property[name] != null) input.setAttribute(name, String(property[name]));
      }
      input.value = value == null ? "" : String(value);
      input.addEventListener("change", () => {
        if (type !== "number") {
          commit(input.value);
        } else if (input.value.trim() === "" || !Number.isFinite(Number(input.value))) {
          commit(null);
        } else {
          commit(Number(input.value));
        }
      });
      input.addEventListener("keydown", (evt) => {
        if (evt.key === "Escape") {
          input.value = value == null ? "" : String(value);
          input.blur();
        }
      });
      return input;
    }

    // ui.canvas({ width, height, background?, shapes, onClick?, onPointerDown?, onPointerMove?,
    // onPointerUp?, onWheel? }): a 2D drawing from a list of shapes, redrawn on every patch.
    // Pointer events carry canvas coordinates and the modifier keys; moves go out once per frame.
    function drawCanvas(el, props) {
      const ctx = typeof el.getContext === "function" ? el.getContext("2d") : null;
      if (!ctx) {
        return;
      }
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.globalAlpha = 1;
      ctx.clearRect(0, 0, el.width, el.height);
      if (props.background && isSafeCss(props.background)) {
        ctx.fillStyle = String(props.background);
        ctx.fillRect(0, 0, el.width, el.height);
      }
      for (const shape of Array.isArray(props.shapes) ? props.shapes : []) {
        if (shape && typeof shape === "object") drawShape(ctx, shape);
      }
    }

    function drawShape(ctx, shape) {
      const num = (value, fallback = 0) => Number.isFinite(Number(value)) ? Number(value) : fallback;
      const color = (value) => value != null && isSafeCss(value) ? String(value) : null;
      ctx.save();
      ctx.globalAlpha = Math.max(0, Math.min(1, num(shape.alpha, 1)));
      ctx.lineWidth = num(shape.lineWidth, 1);
      if (Array.isArray(shape.dash)) ctx.setLineDash(shape.dash.map((d) => num(d)));
      const fill = color(shape.fill);
      const stroke = color(shape.stroke);
      const kind = String(shape.kind || shape.type || "");
      const points = (list) => {
        const flat = (Array.isArray(list) ? list : []).flatMap((p) => Array.isArray(p) ? p : [p]).map((v) => num(v));
        ctx.beginPath();
        for (let i = 0; i + 1 < flat.length; i += 2) {
          if (i === 0) ctx.moveTo(flat[i], flat[i + 1]);
          else ctx.lineTo(flat[i], flat[i + 1]);
        }
      };
      if (kind === "rect") {
        const [x, y, w, h] = [num(shape.x), num(shape.y), num(shape.w ?? shape.width), num(shape.h ?? shape.height)];
        if (fill) { ctx.fillStyle = fill; ctx.fillRect(x, y, w, h); }
        if (stroke) { ctx.strokeStyle = stroke; ctx.strokeRect(x, y, w, h); }
      } else if (kind === "circle") {
        ctx.beginPath();
        ctx.arc(num(shape.x), num(shape.y), Math.max(0, num(shape.r ?? shape.radius)), 0, Math.PI * 2);
        if (fill) { ctx.fillStyle = fill; ctx.fill(); }
        if (stroke) { ctx.strokeStyle = stroke; ctx.stroke(); }
      } else if (kind === "line") {
        ctx.beginPath();
        ctx.moveTo(num(shape.x1), num(shape.y1));
        ctx.lineTo(num(shape.x2), num(shape.y2));
        ctx.strokeStyle = stroke || fill || "#e8edff";
        ctx.stroke();
      } else if (kind === "polyline" || kind === "polygon") {
        points(shape.points);
        if (kind === "polygon") ctx.closePath();
        if (fill && kind === "polygon") { ctx.fillStyle = fill; ctx.fill(); }
        if (stroke || kind === "polyline") { ctx.strokeStyle = stroke || fill || "#e8edff"; ctx.stroke(); }
      } else if (kind === "text") {
        if (shape.font && isSafeCss(shape.font)) ctx.font = String(shape.font);
        ctx.textAlign = ["left", "right", "center", "start", "end"].includes(shape.align) ? shape.align : "start";
        ctx.textBaseline = ["top", "middle", "bottom", "alphabetic", "hanging"].includes(shape.baseline) ? shape.baseline : "alphabetic";
        ctx.fillStyle = fill || "#e8edff";
        ctx.fillText(String(shape.text ?? ""), num(shape.x), num(shape.y));
      }
      ctx.restore();
    }

    function attachCanvas(el) {
      const point = (evt) => {
        const rect = el.getBoundingClientRect ? el.getBoundingClientRect() : { left: 0, top: 0, width: 0, height: 0 };
        const scaleX = rect.width ? el.width / rect.width : 1;
        const scaleY = rect.height ? el.height / rect.height : 1;
        const round = (value) => Math.round(value * 100) / 100;
        return {
          x: round((evt.clientX - rect.left) * scaleX),
          y: round((evt.clientY - rect.top) * scaleY),
          button: evt.button ?? 0,
          buttons: evt.buttons ?? 0,
          shift: !!evt.shiftKey,
          ctrl: !!evt.ctrlKey,
          alt: !!evt.altKey,
          meta: !!evt.metaKey
        };
      };
      const handles = (prop) => !!describe(el).props[prop];
      el.addEventListener("click", (evt) => {
        if (handles("onClick")) emitFrom(el, "click", point(evt));
      });
      el.addEventListener("pointerdown", (evt) => {
        if (!handles("onPointerDown") && !handles("onPointerMove")) return;
        if (typeof el.setPointerCapture === "function") el.setPointerCapture(evt.pointerId);
        if (handles("onPointerDown")) emitFrom(el, "pointerDown", point(evt));
      });
      let pendingMove = null;
      el.addEventListener("pointermove", (evt) => {
        if (!handles("onPointerMove")) return;
        const waiting = pendingMove !== null;
        pendingMove = point(evt);
        if (!waiting) {
          requestFrame(() => {
            const move = pendingMove;
            pendingMove = null;
            if (move) emitFrom(el, "pointerMove", move);
          });
        }
      });
      el.addEventListener("pointerup", (evt) => {
        if (typeof el.releasePointerCapture === "function" && el.hasPointerCapture && el.hasPointerCapture(evt.pointerId)) {
          el.releasePointerCapture(evt.pointerId);
        }
        if (handles("onPointerUp")) emitFrom(el, "pointerUp", point(evt));
      });
      el.addEventListener("wheel", (evt) => {
        if (!handles("onWheel")) return;
        evt.preventDefault();
        emitFrom(el, "wheel", Object.assign(point(evt), { deltaX: evt.deltaX || 0, deltaY: evt.deltaY || 0 }));
      }, { passive: false });
    }

    // One keydown and one pointerdown listener per document: ui.shortcuts bindings, and Escape
    // or a press outside an open ui.modal (sent as `close` when it has onClose).
    function installDocumentListeners() {
      if (documentListenersInstalled || typeof doc.addEventListener !== "function") {
        return;
      }
      documentListenersInstalled = true;
      doc.addEventListener("keydown", onDocumentKeydown);
      doc.addEventListener("pointerdown", onDocumentPointerdown);
    }

    function liveElements(set) {
      for (const el of Array.from(set)) {
        if (el.isConnected === false) set.delete(el);
      }
      return Array.from(set);
    }

    function topModal() {
      const open = liveElements(modalElements).filter((el) => el.getAttribute("open") !== "false");
      return open.length ? open[open.length - 1] : null;
    }

    function onDocumentKeydown(evt) {
      if (evt.defaultPrevented) {
        return;
      }
      const modal = topModal();
      if (evt.key === "Escape" && modal && describe(modal).props.onClose) {
        evt.preventDefault();
        emitFrom(modal, "close", { reason: "escape" });
        return;
      }
      const combo = comboOfEvent(evt);
      if (!combo) {
        return;
      }
      const target = evt.target;
      const typing = !!(target && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName || "")));
      for (const el of liveElements(shortcutElements)) {
        const bindings = describe(el).props.bindings;
        if (!bindings || typeof bindings !== "object") continue;
        for (const [keys, id] of Object.entries(bindings)) {
          if (normalizeCombo(keys) !== combo) continue;
          if (typing && !/(^|\+)(ctrl|alt|meta)\+/.test(combo) && !/^(escape|f\d+)$/.test(combo)) continue;
          evt.preventDefault();
          emitFrom(el, "shortcut", { id, keys });
          return;
        }
      }
    }

    function onDocumentPointerdown(evt) {
      const modal = topModal();
      if (!modal || modal.contains(evt.target) || (openPopup && openPopup.list.contains(evt.target))) {
        return;
      }
      const props = describe(modal).props;
      if (props.onClose && props.closeOnOutside !== false) {
        emitFrom(modal, "close", { reason: "outside" });
      }
    }

    const isMac = typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform || "");
    const keyAliases = { esc: "escape", del: "delete", ins: "insert", up: "arrowup", down: "arrowdown", left: "arrowleft", right: "arrowright", return: "enter", " ": "space", spacebar: "space", plus: "+" };

    // "Ctrl+Shift+K" → "ctrl+shift+K": modifiers in a fixed order, letters upper case, named keys
    // lower case. Cmd / Command / Win are meta, Option is alt, Mod is meta on macOS, ctrl elsewhere.
    function normalizeCombo(text) {
      const parts = String(text).split("+").map((part) => part.trim());
      let key = parts.pop();
      if (key === "" && parts.length && parts[parts.length - 1] === "") {
        parts.pop();
        key = "+";
      }
      const mods = new Set(parts.filter(Boolean).map((part) => {
        const lower = part.toLowerCase();
        if (lower === "control") return "ctrl";
        if (lower === "cmd" || lower === "command" || lower === "win") return "meta";
        if (lower === "option") return "alt";
        if (lower === "mod") return isMac ? "meta" : "ctrl";
        return lower;
      }));
      return comboText(mods, key || "");
    }

    function comboOfEvent(evt) {
      if (!evt || typeof evt.key !== "string" || ["Control", "Shift", "Alt", "Meta"].includes(evt.key)) {
        return null;
      }
      const mods = new Set();
      if (evt.ctrlKey) mods.add("ctrl");
      if (evt.altKey) mods.add("alt");
      if (evt.shiftKey) mods.add("shift");
      if (evt.metaKey) mods.add("meta");
      // Shift+1 reports "!": bindings name the key on the keycap, so use the code for letters and digits.
      const code = typeof evt.code === "string" ? /^(?:Key([A-Z])|Digit([0-9]))$/.exec(evt.code) : null;
      return comboText(mods, code ? code[1] || code[2] : evt.key);
    }

    function comboText(mods, key) {
      const lower = key.toLowerCase();
      const name = keyAliases[lower] || (key.length === 1 ? key.toUpperCase() : lower);
      return ["ctrl", "alt", "shift", "meta"].filter((mod) => mods.has(mod)).concat([name]).join("+");
    }

    function applyCommonProps(el, props, skippedKeys) {
      for (const [key, value] of Object.entries(props || {})) {
        if (skippedKeys && skippedKeys.has(key)) continue;
        if (richEventProps.has(key)) continue;
        if (key === "className") {
          el.className = String(value);
          continue;
        }
        if (key === "style") {
          applyStyleProp(el, value);
          continue;
        }
        if (key === "ariaLabel") {
          el.setAttribute("aria-label", String(value));
          continue;
        }
        if (value != null && (typeof value === "string" || typeof value === "number" || typeof value === "boolean") && isSafeAttribute("", key, value)) {
          el.setAttribute(key, String(value));
        }
      }
    }

    function applyStyleProp(el, value) {
      // Treat each update as a full replacement so patch rerenders never keep stale declarations.
      if (value == null) {
        el.style.cssText = "";
        return;
      }

      if (typeof value === "string" || typeof value === "number" || typeof value === "boolean") {
        el.style.cssText = isSafeCss(value) ? String(value) : "";
        return;
      }

      if (typeof value !== "object" || Array.isArray(value)) {
        el.style.cssText = "";
        return;
      }

      el.style.cssText = "";
      for (const [propName, propValue] of Object.entries(value)) {
        if (propValue == null || !isSafeCss(propName) || !isSafeCss(propValue)) {
          continue;
        }

        if (propName.includes("-")) {
          el.style.setProperty(propName, String(propValue));
        } else {
          el.style[propName] = String(propValue);
        }
      }
    }

    function applyInputTypeDefaults(type, el, props) {
      if (el.tagName !== "INPUT") return;
      if (props.type != null) return;
      if (type === "checkbox" || type === "switch") el.type = "checkbox";
      else if (type === "slider") el.type = "range";
      else if (type === "radioGroup") el.type = "radio";
      else if (type === "datePicker") el.type = props.includeTime ? "datetime-local" : "date";
      else el.type = "text";
    }

    function normalizeKeySet(value) {
      const set = new Set();
      if (!Array.isArray(value)) return set;
      value.forEach((item) => set.add(String(item)));
      return set;
    }

    function getControlState(path) {
      let state = controlStateByPath.get(path);
      if (!state) {
        state = {};
        controlStateByPath.set(path, state);
      }
      return state;
    }

    function resolveRowKey(row, rowIndex, rowKeyField) {
      if (row && typeof row === "object" && row[rowKeyField] != null) {
        return String(row[rowKeyField]);
      }
      return String(rowIndex);
    }

    function resolveTreeKey(node, fallback, nodeKeyField) {
      if (node && typeof node === "object" && node[nodeKeyField] != null) {
        return String(node[nodeKeyField]);
      }
      return String(fallback);
    }

    function readCellValue(row, column, cellIndex) {
      if (row == null) return "";
      if (Array.isArray(row)) {
        return row[cellIndex];
      }
      if (column && column.key != null && typeof row === "object") {
        const valueByKey = row[column.key];
        if (valueByKey !== undefined) return valueByKey;
      }
      if (typeof row === "object" && column && column.title) {
        return row[column.title];
      }
      return "";
    }

    function normalizeGridSort(sort) {
      if (!sort || typeof sort !== "object") return null;
      if (sort.key == null) return null;
      const direction = String(sort.direction || "asc").toLowerCase() === "desc" ? "desc" : "asc";
      return { key: String(sort.key), direction };
    }

    function getNextGridSort(sortKey, currentSort) {
      const normalizedCurrent = normalizeGridSort(currentSort);
      if (!normalizedCurrent || normalizedCurrent.key !== sortKey) {
        return { key: sortKey, direction: "asc" };
      }
      return { key: sortKey, direction: normalizedCurrent.direction === "asc" ? "desc" : "asc" };
    }

    function getGridRowsWithLocalSort(rows, columns, sort, defaultSortable) {
      const normalizedSort = normalizeGridSort(sort);
      if (!normalizedSort || !Array.isArray(rows) || rows.length <= 1) {
        return rows;
      }

      const columnIndex = columns.findIndex((column) => {
        if (!column) return false;
        if (column.key != null && String(column.key) === normalizedSort.key) return true;
        return column.title != null && String(column.title) === normalizedSort.key;
      });

      if (columnIndex < 0) {
        return rows;
      }

      const column = columns[columnIndex];
      const isSortable = !!(column && (column.sortable || defaultSortable));
      if (!isSortable) {
        return rows;
      }
      const directionFactor = normalizedSort.direction === "desc" ? -1 : 1;

      return rows
        .map((row, index) => ({ row, index }))
        .sort((left, right) => {
          const leftValue = readCellValue(left.row, column, columnIndex);
          const rightValue = readCellValue(right.row, column, columnIndex);
          const baseCompare = compareGridValues(leftValue, rightValue);
          if (baseCompare !== 0) {
            return baseCompare * directionFactor;
          }
          return left.index - right.index;
        })
        .map((entry) => entry.row);
    }

    function compareGridValues(left, right) {
      if (left == null && right == null) return 0;
      if (left == null) return 1;
      if (right == null) return -1;

      if (typeof left === "number" && typeof right === "number") {
        return left - right;
      }
      if (typeof left === "boolean" && typeof right === "boolean") {
        if (left === right) return 0;
        return left ? 1 : -1;
      }

      const leftString = String(left);
      const rightString = String(right);
      return leftString.localeCompare(rightString, undefined, { numeric: true, sensitivity: "base" });
    }

    function computeNextSelection(mode, selectedSet, key) {
      const next = new Set(selectedSet);
      if (mode === "multiple") {
        if (next.has(key)) next.delete(key);
        else next.add(key);
        return next;
      }
      next.clear();
      next.add(key);
      return next;
    }

    function applyGridSelectionState(viewport, selectedSet) {
      if (!viewport) {
        return;
      }
      const rows = viewport.querySelectorAll(".malda-data-grid-row[data-row-key]");
      rows.forEach((rowEl) => {
        const key = rowEl.getAttribute("data-row-key");
        if (key != null && selectedSet.has(key)) {
          rowEl.setAttribute("aria-selected", "true");
        } else {
          rowEl.removeAttribute("aria-selected");
        }
      });
    }

    function handleGridRowKeydown(evt, viewport, path, rowIndex, rowKey, totalRows) {
      if (evt.key === "Enter" || evt.key === " ") {
        sendEvent("rowClick", path, { rowIndex, rowKey, keyboard: true });
        evt.preventDefault();
        return;
      }
      if (evt.key === "ArrowDown" || evt.key === "ArrowUp") {
        const rows = Array.from(viewport.querySelectorAll("[data-row-index]"));
        const current = evt.currentTarget;
        const idx = rows.indexOf(current);
        const nextIdx = evt.key === "ArrowDown" ? Math.min(rows.length - 1, idx + 1) : Math.max(0, idx - 1);
        if (rows[nextIdx]) {
          rows[nextIdx].focus();
        }
        evt.preventDefault();
        return;
      }
      if (evt.key === "Home" || evt.key === "End") {
        sendEvent("viewportChange", path, { jump: evt.key === "Home" ? "start" : "end", totalRows });
        evt.preventDefault();
      }
    }

    function handleGridViewportKeydown(evt, viewport) {
      if (evt.key === "PageDown") {
        viewport.scrollTop = viewport.scrollTop + viewport.clientHeight;
        evt.preventDefault();
      }
      if (evt.key === "PageUp") {
        viewport.scrollTop = Math.max(0, viewport.scrollTop - viewport.clientHeight);
        evt.preventDefault();
      }
    }

    function parseCssPixelValue(value, fallback) {
      const parsed = Number.parseFloat(String(value || ""));
      return Number.isFinite(parsed) ? parsed : fallback;
    }

    function mapTag(type) {
      switch (type) {
        case "row":
        case "column":
        case "stack":
        case "panel":
        case "modal":
        case "list":
        case "table":
        case "alert":
        case "progress":
        case "field":
        case "tabs":
        case "accordion":
        case "breadcrumbs":
        case "drawer":
        case "dataGrid":
        case "emptyState":
        case "badge":
        case "toast":
        case "skeleton":
        case "spinner":
        case "errorBoundary":
        case "splitPane":
        case "contextMenu":
        case "shortcuts":
        case "hostSlot":
        case "menuBar":
        case "commandPalette":
        case "propertyGrid":
          return "div";
        case "canvas":
          return "canvas";
        case "heading":
          return "h2";
        case "button":
          return "button";
        case "textField":
        case "switch":
        case "datePicker":
          return "input";
        case "textArea":
          return "textarea";
        case "checkbox":
        case "radioGroup":
          return "input";
        case "form":
          return "form";
        case "select":
          return "select";
        case "paginator":
          return "nav";
        case "slider":
          return "input";
        case "image":
        case "icon":
          return "img";
        default:
          console.warn("[ui] Unknown control type, rendering as div:", type);
          return "div";
      }
    }

    function collectPayload(type, el, props) {
      const payload = {
        name: props.name || el.getAttribute("name") || null,
        value: el.type === "checkbox" ? !!el.checked : (el.value ?? null),
        checked: typeof el.checked === "boolean" ? !!el.checked : null,
        dirty: el.dataset.uiDirty === "true",
        touched: el.dataset.uiTouched === "true",
        validity: el.validity ? {
          valid: el.validity.valid,
          valueMissing: el.validity.valueMissing,
          typeMismatch: el.validity.typeMismatch,
          tooLong: el.validity.tooLong,
          tooShort: el.validity.tooShort,
          rangeOverflow: el.validity.rangeOverflow,
          rangeUnderflow: el.validity.rangeUnderflow
        } : null,
        controlType: type
      };
      el.dataset.uiDirty = "true";
      if (!el.dataset.uiTouched) {
        el.dataset.uiTouched = "true";
      }
      return payload;
    }

    function applyA11yDefaults(type, el, props) {
      if (!el.hasAttribute("role")) {
        if (type === "tabs") el.setAttribute("role", "tablist");
        if (type === "accordion") el.setAttribute("role", "group");
        if (type === "modal") el.setAttribute("role", "dialog");
        if (type === "alert") el.setAttribute("role", "alert");
        if (type === "progress") el.setAttribute("role", "progressbar");
        if (type === "dataGrid") el.setAttribute("role", "grid");
        if (type === "badge") el.setAttribute("role", "status");
      }

      // A button's label and a heading's value show through CSS (their DOM children mirror the
      // node's children); the accessible name carries the same text.
      if ((type === "button" && props.label != null) || (type === "heading" && props.value != null)) {
        if (!el.hasAttribute("aria-label")) el.setAttribute("aria-label", String(type === "button" ? props.label : props.value));
      }

      if ((type === "textField" || type === "textArea" || type === "datePicker") && !el.hasAttribute("aria-label") && props.placeholder) {
        el.setAttribute("aria-label", String(props.placeholder));
      }
    }

    return { renderInto, applyPayload, renderNode };
  }

  // Layout and the desktop controls. Every element carries data-ui="<type>"; colors come from
  // --malda-ui-* custom properties so a page can theme them.
  const layoutStyles = `
      [data-ui="row"] { display: flex; flex-direction: row; gap: 8px; align-items: center; flex-wrap: wrap; }
      [data-ui="column"] { display: flex; flex-direction: column; gap: 8px; }
      [data-ui="stack"] { display: grid; }
      [data-ui="stack"] > * { grid-area: 1 / 1; }
      [data-ui="spacer"] { flex: 1 1 auto; }
      [data-ui="panel"] { border: 1px solid var(--malda-ui-line, #2d3a63); border-radius: 8px; padding: 10px; background: var(--malda-ui-surface, transparent); min-width: 0; min-height: 0; overflow: auto; }
      [data-ui="hostSlot"] { position: relative; min-width: 0; min-height: 0; }
      [data-ui="shortcuts"] { display: none; }
      [data-ui="splitPane"] { display: grid; gap: 6px; min-height: 0; min-width: 0; background: var(--malda-ui-line, #2d3a63); }
      [data-ui="splitPane"][data-ui-direction="row"] { cursor: col-resize; }
      [data-ui="splitPane"][data-ui-direction="column"] { cursor: row-resize; }
      [data-ui="splitPane"] > * { cursor: auto; min-width: 0; min-height: 0; overflow: auto; background: var(--malda-ui-bg, #0b1020); }
      [data-ui="modal"] { position: fixed; z-index: 1000; top: 50%; left: 50%; transform: translate(-50%, -50%); display: flex; flex-direction: column; gap: 10px; min-width: min(420px, 90vw); max-width: 90vw; max-height: 85vh; overflow: auto; padding: 16px; border-radius: 10px; background: var(--malda-ui-surface2, #16224a); color: var(--malda-ui-text, #e8edff); box-shadow: 0 0 0 100vmax rgba(0, 0, 0, 0.5), 0 12px 40px rgba(0, 0, 0, 0.5); }
      [data-ui="modal"][open="false"] { display: none; }
      [data-ui="modal"][data-ui-title]::before { content: attr(data-ui-title); font-weight: 700; font-size: 16px; }
      .malda-menu-bar { display: flex; gap: 2px; padding: 2px 4px; position: relative; background: var(--malda-ui-surface2, #16224a); border-bottom: 1px solid var(--malda-ui-line, #2d3a63); }
      .malda-menu-bar-item { border: 0; background: transparent; color: inherit; padding: 4px 10px; border-radius: 4px; cursor: pointer; font: inherit; }
      .malda-menu-bar-item:hover, .malda-menu-bar-item[aria-expanded="true"] { background: var(--malda-ui-hover, #24315f); }
      .malda-menu { position: absolute; top: 100%; z-index: 1100; display: flex; flex-direction: column; min-width: 180px; padding: 4px; border-radius: 6px; background: var(--malda-ui-surface2, #16224a); color: var(--malda-ui-text, #e8edff); border: 1px solid var(--malda-ui-line, #2d3a63); box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45); }
      .malda-menu-item { display: flex; justify-content: space-between; gap: 24px; border: 0; background: transparent; color: inherit; text-align: left; padding: 5px 10px; border-radius: 4px; cursor: pointer; font: inherit; }
      .malda-menu-item:hover:not([disabled]), .malda-menu-item:focus { background: var(--malda-ui-hover, #24315f); outline: none; }
      .malda-menu-item[disabled] { opacity: 0.45; cursor: default; }
      .malda-menu-shortcut { opacity: 0.6; font-size: 12px; }
      .malda-menu-separator { height: 1px; margin: 4px 2px; background: var(--malda-ui-line, #2d3a63); }
      .malda-command-palette { position: fixed; inset: 0; z-index: 1200; display: flex; justify-content: center; align-items: flex-start; padding-top: 12vh; background: rgba(0, 0, 0, 0.45); }
      .malda-command-palette[hidden] { display: none; }
      .malda-command-palette-box { width: min(560px, 92vw); border-radius: 10px; overflow: hidden; background: var(--malda-ui-surface2, #16224a); color: var(--malda-ui-text, #e8edff); border: 1px solid var(--malda-ui-line, #2d3a63); box-shadow: 0 16px 48px rgba(0, 0, 0, 0.5); }
      .malda-command-palette-input { width: 100%; box-sizing: border-box; padding: 12px 14px; border: 0; border-bottom: 1px solid var(--malda-ui-line, #2d3a63); background: transparent; color: inherit; font: inherit; font-size: 15px; outline: none; }
      .malda-command-palette-list { max-height: 50vh; overflow: auto; padding: 4px; }
      .malda-command-palette-option { display: flex; justify-content: space-between; gap: 16px; padding: 7px 10px; border-radius: 6px; cursor: pointer; }
      .malda-command-palette-option[aria-selected="true"] { background: var(--malda-ui-hover, #24315f); }
      .malda-command-palette-group { opacity: 0.6; font-size: 12px; }
      .malda-command-palette-empty { padding: 10px; opacity: 0.6; }
      .malda-property-grid { display: grid; grid-template-columns: minmax(90px, 40%) 1fr; gap: 4px 8px; align-items: center; font-size: 13px; }
      .malda-property-grid-group { grid-column: 1 / -1; margin-top: 6px; font-weight: 700; font-size: 11px; letter-spacing: 0.4px; text-transform: uppercase; opacity: 0.7; }
      .malda-property-grid-label { opacity: 0.85; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .malda-property-grid input:not([type="checkbox"]), .malda-property-grid select { width: 100%; box-sizing: border-box; min-width: 0; }
      [data-ui="canvas"] { display: block; max-width: 100%; touch-action: none; }
      [data-ui="button"][label]:empty::before { content: attr(label); }
      [data-ui="heading"][value]:empty::before, [data-ui="badge"][value]:empty::before { content: attr(value); }
      [data-ui="badge"] { display: inline-block; padding: 1px 8px; border-radius: 999px; font-size: 12px; background: var(--malda-ui-hover, #24315f); }
    `;

  // The default stylesheet, once per document: the host page and local mode (the program
  // running in the browser) both install it. Pages can override any rule.
  function installStyles(doc) {
    if (!doc || typeof doc.getElementById !== "function" || doc.getElementById("malda-ui-framework-styles")) {
      return;
    }
    const style = doc.createElement("style");
    style.id = "malda-ui-framework-styles";
    style.textContent = `
      [data-ui-connection="reconnecting"]::after,
      [data-ui-connection="closed"]::after {
        position: fixed;
        top: 8px;
        right: 8px;
        z-index: 2147483647;
        padding: 4px 10px;
        border-radius: 999px;
        font: 12px/1.5 Inter, Segoe UI, Arial, sans-serif;
        color: #fff;
        background: #b45309;
        pointer-events: none;
      }
      [data-ui-connection="reconnecting"]::after {
        content: "Reconnecting…";
      }
      [data-ui-connection="closed"]::after {
        content: "Disconnected";
        background: #b91c1c;
      }
      .malda-data-grid {
        border: 1px solid var(--line, #2d3a63);
        border-radius: 8px;
        overflow: hidden;
        background: var(--surface, #0f1735);
        color: var(--txt, #e8edff);
        font-family: Inter, Segoe UI, Arial, sans-serif;
      }
      .malda-data-grid-header,
      .malda-data-grid-row {
        display: grid;
        grid-auto-flow: column;
        grid-auto-columns: minmax(100px, 1fr);
      }
      .malda-data-grid-header {
        background: var(--surface2, #16224a);
        border-bottom: 1px solid var(--line, #2d3a63);
      }
      .malda-data-grid-header-cell {
        padding: 10px 12px;
        font-size: 12px;
        font-weight: 700;
        letter-spacing: 0.4px;
        text-transform: uppercase;
        border-right: 1px solid var(--line, #2d3a63);
        user-select: none;
        position: relative;
      }
      .malda-data-grid-header-cell:last-child {
        border-right: 0;
      }
      .malda-data-grid-header-cell[aria-sort]:hover {
        background: var(--btn-bg, #1d2c5a);
        cursor: pointer;
      }
      .malda-data-grid-header-title {
        display: block;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .malda-data-grid-resize-handle {
        position: absolute;
        top: 0;
        right: -4px;
        width: 8px;
        height: 100%;
        cursor: col-resize;
        z-index: 2;
      }
      .malda-data-grid-resize-handle::after {
        content: "";
        position: absolute;
        top: 25%;
        bottom: 25%;
        left: 50%;
        transform: translateX(-50%);
        width: 2px;
        border-radius: 2px;
        background: var(--muted, rgba(196, 211, 255, 0.4));
      }
      .malda-data-grid-viewport {
        background: var(--surface, #0f1735);
      }
      .malda-data-grid-row {
        border-bottom: 1px solid var(--line, rgba(78, 104, 179, 0.35));
      }
      .malda-data-grid-row:hover {
        background: var(--surface2, rgba(94, 124, 228, 0.12));
      }
      .malda-data-grid-row[aria-selected="true"] {
        background: rgba(94, 124, 228, 0.28);
      }
      .malda-data-grid-row[aria-selected="true"]:hover {
        background: rgba(94, 124, 228, 0.34);
      }
      .malda-data-grid-cell {
        padding: 10px 12px;
        border-right: 1px solid var(--line, rgba(78, 104, 179, 0.35));
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .malda-data-grid-cell:last-child {
        border-right: 0;
      }
      .malda-data-grid-filter {
        width: calc(100% - 16px);
        margin: 8px;
        padding: 8px 10px;
        border: 1px solid var(--input-border, #304274);
        border-radius: 6px;
        background: var(--input-bg, #0d1636);
        color: var(--txt, #e8edff);
      }
      .malda-data-grid-pager {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 8px;
        border-top: 1px solid var(--line, #2d3a63);
        background: var(--surface2, #16224a);
      }
      .malda-data-grid-pager button {
        border: 1px solid var(--btn-border, #3b5495);
        border-radius: 6px;
        padding: 6px 10px;
        background: var(--btn-bg, #13204a);
        color: var(--btn-text, #e7eeff);
      }
      .malda-data-grid-pager button:disabled {
        opacity: 0.55;
      }
      .malda-tree-view {
        border: 1px solid #2d3a63;
        border-radius: 8px;
        padding: 8px;
        background: #0f1735;
        color: #e8edff;
      }
      .malda-tree-view button {
        border: 0;
        background: transparent;
        color: inherit;
        cursor: pointer;
      }
      .malda-tree-view-loading {
        font-size: 12px;
        opacity: 0.8;
      }
    ` + layoutStyles;
    (doc.head || doc.documentElement).appendChild(style);
  }

  return { create, installStyles };
})();

if (typeof module !== "undefined" && module.exports) {
  module.exports = Object.assign({}, MaldaUiCore, { MaldaUiCore, MaldaUiProtocol, MaldaUiDom });
}
// For malda-js-runtime.js: top-level consts of a classic script are not properties of globalThis.
if (typeof globalThis !== "undefined") {
  globalThis.MaldaUiCore = MaldaUiCore;
  globalThis.MaldaUiProtocol = MaldaUiProtocol;
  globalThis.MaldaUiDom = MaldaUiDom;
}

(function () {
  if (typeof window === "undefined" || typeof document === "undefined") {
    return;
  }
  // Local mode (<script src="malda-ui-client.js" data-ui-mode="local">): a program compiled to
  // JavaScript drives the UI in the page through the runtime's ui.*, so there is no host to call.
  const script = document.currentScript;
  if (script && script.getAttribute("data-ui-mode") === "local") {
    return;
  }

  const params = new URLSearchParams(window.location.search);
  const sessionId = params.get("session") || "default";
  const appRoot = document.getElementById("app");
  const authToken = readAuthToken();
  // ?debug=1 logs every protocol message in and out.
  const debug = params.get("debug") === "1";

  let tree = null;
  const protocol = MaldaUiProtocol.createState(sessionId);
  const dom = MaldaUiDom.create(document, {
    sendEvent,
    requestFrame: (fn) => requestAnimationFrame(fn)
  });

  const wsProtocol = window.location.protocol === "https:" ? "wss" : "ws";
  const wsUrl = `${wsProtocol}://${window.location.host}/ui/ws/${encodeURIComponent(sessionId)}`;
  const reconnectBaseDelayMs = 500;
  const reconnectMaxDelayMs = 10000;
  const reconnectJitterMs = 250;
  const queuedOutboundLimit = 200;
  MaldaUiDom.installStyles(document);
  let socket = null;
  let reconnectAttempts = 0;
  let reconnectTimerId = null;
  let manualDisconnect = false;
  const queuedOutboundMessages = [];
  setConnectionState("connecting");
  connectWebSocket();
  window.addEventListener("beforeunload", () => {
    manualDisconnect = true;
    setConnectionState("closed");
    clearReconnectTimer();
    if (socket && socket.readyState === WebSocket.OPEN) {
      socket.close(1000, "page_unload");
    }
  });

  // data-ui-connection on #app: connecting (first connect), open, reconnecting (connection lost,
  // retrying) or closed (page unloading, or the host refused the token). The default style
  // shows a badge while reconnecting or closed; pages can restyle it with the attribute.
  function setConnectionState(state) {
    if (appRoot && appRoot.getAttribute("data-ui-connection") !== state) {
      appRoot.setAttribute("data-ui-connection", state);
      if (debug) console.debug("malda-ui connection", state);
    }
  }


  function applyInboundPayload(payload) {
    if (!payload) return;
    const focusPath = document.activeElement ? document.activeElement.getAttribute("data-ui-path") : null;
    tree = dom.applyPayload(appRoot, tree, payload);
    // Patches keep the focused element; a full render replaces it, so focus its successor.
    if (focusPath && !appRoot.contains(document.activeElement)) {
      const focusEl = appRoot.querySelector(`[data-ui-path="${cssEscape(focusPath)}"]`);
      if (focusEl && typeof focusEl.focus === "function") {
        focusEl.focus();
      }
    }
  }

  // Allows host pages to bootstrap rendering directly from a fetched envelope.
  window.maldaUiApplyEnvelope = function (envelopeOrPayload) {
    if (!envelopeOrPayload) return;
    const payload = envelopeOrPayload.payload != null ? envelopeOrPayload.payload : envelopeOrPayload;
    applyInboundPayload(MaldaUiProtocol.normalizePayload(payload));
  };


  function sendEvent(type, targetPath, payload, target) {
    const message = MaldaUiProtocol.event(protocol, type, targetPath, payload, target);
    enqueueOrSend(message);
    return message.envelopeId;
  }

  function runProtocolActions(actions) {
    for (const action of actions) {
      if (action.apply !== undefined) {
        applyInboundPayload(action.apply);
        runProtocolActions(MaldaUiProtocol.verifyTree(protocol, tree, action.apply));
      } else if (action.send) {
        enqueueOrSend(action.send);
      } else if (action.warn) {
        console.warn("malda-ui protocol", action.warn);
      }
    }
  }

  // The host's token (MALDA_UI_AUTH_TOKEN) arrives in the fragment, #token=…, which the
  // browser never sends to a server. It moves to sessionStorage so reloads and reconnects keep it.
  function readAuthToken() {
    const storageKey = "malda-ui-token";
    const fragment = new URLSearchParams(window.location.hash.replace(/^#/, ""));
    const fromFragment = fragment.get("token");
    if (fromFragment) {
      fragment.delete("token");
      const rest = fragment.toString();
      history.replaceState(null, "", window.location.pathname + window.location.search + (rest ? "#" + rest : ""));
      try { sessionStorage.setItem(storageKey, fromFragment); } catch (_) { /* storage blocked */ }
      return fromFragment;
    }
    try { return sessionStorage.getItem(storageKey); } catch (_) { return null; }
  }

  // With a token, each connection uses a fresh single-use ticket from POST /ui/ticket.
  async function socketUrl() {
    if (!authToken) {
      return wsUrl;
    }
    const response = await fetch("/ui/ticket", { method: "POST", headers: { "X-Malda-UI-Auth": authToken } });
    if (response.status === 401 || response.status === 403) {
      throw Object.assign(new Error(`ticket refused (${response.status})`), { fatal: true });
    }
    if (!response.ok) {
      throw new Error(`ticket request failed (${response.status})`);
    }
    const { ticket } = await response.json();
    return `${wsUrl}?ticket=${encodeURIComponent(ticket)}`;
  }

  async function connectWebSocket() {
    clearReconnectTimer();
    if (manualDisconnect) {
      return;
    }

    let nextSocket = null;
    try {
      const url = await socketUrl();
      if (manualDisconnect) {
        return;
      }
      nextSocket = new WebSocket(url);
    } catch (err) {
      console.error("malda-ui connect failed", err);
      if (err.fatal) {
        setConnectionState("closed");
      } else {
        scheduleReconnect();
      }
      return;
    }

    socket = nextSocket;

    nextSocket.addEventListener("open", () => {
      reconnectAttempts = 0;
      clearReconnectTimer();
      setConnectionState("open");
      console.log("malda-ui connected", { sessionId });
      // onOpen restarts the socket's sequence numbers, so it runs before queued messages go out.
      const openActions = MaldaUiProtocol.onOpen(protocol);
      flushQueuedOutboundMessages();
      runProtocolActions(openActions);
    });

    nextSocket.addEventListener("message", (evt) => {
      try {
        const message = JSON.parse(evt.data);
        if (debug) console.debug("malda-ui ←", message);
        runProtocolActions(MaldaUiProtocol.receive(protocol, message));
      } catch (err) {
        console.warn("malda-ui message parse error", err);
      }
    });

    nextSocket.addEventListener("error", (evt) => {
      console.warn("malda-ui websocket error", evt);
    });

    nextSocket.addEventListener("close", (evt) => {
      if (socket === nextSocket) {
        socket = null;
      }
      if (manualDisconnect) {
        return;
      }
      console.warn("malda-ui websocket closed", {
        code: evt.code,
        reason: evt.reason || "no reason",
        wasClean: evt.wasClean
      });
      scheduleReconnect();
    });
  }

  function scheduleReconnect() {
    if (manualDisconnect || reconnectTimerId != null) {
      return;
    }

    setConnectionState("reconnecting");
    reconnectAttempts += 1;
    const baseDelay = Math.min(reconnectMaxDelayMs, reconnectBaseDelayMs * (2 ** Math.max(0, reconnectAttempts - 1)));
    const jitter = Math.floor(Math.random() * reconnectJitterMs);
    const delay = baseDelay + jitter;
    reconnectTimerId = window.setTimeout(() => {
      reconnectTimerId = null;
      connectWebSocket();
    }, delay);
    console.info("malda-ui reconnect scheduled", { attempt: reconnectAttempts, delayMs: delay, sessionId });
  }

  function clearReconnectTimer() {
    if (reconnectTimerId == null) {
      return;
    }
    clearTimeout(reconnectTimerId);
    reconnectTimerId = null;
  }

  function canSendNow() {
    return !!(socket && socket.readyState === WebSocket.OPEN);
  }

  function enqueueOrSend(message) {
    if (!message) {
      return;
    }
    if (canSendNow()) {
      trySendJson(message, false);
      return;
    }
    if (queuedOutboundMessages.length >= queuedOutboundLimit) {
      queuedOutboundMessages.shift();
    }
    queuedOutboundMessages.push(message);
  }

  function flushQueuedOutboundMessages() {
    if (!canSendNow() || queuedOutboundMessages.length === 0) {
      return;
    }
    const queued = queuedOutboundMessages.splice(0, queuedOutboundMessages.length);
    for (let i = 0; i < queued.length; i++) {
      const message = queued[i];
      if (!trySendJson(message, true)) {
        // Put current and remaining messages back in order if connection drops mid-flush.
        queuedOutboundMessages.unshift(message, ...queued.slice(i + 1));
        return;
      }
    }
  }

  function trySendJson(message, isFlushing) {
    if (!canSendNow()) {
      return false;
    }
    try {
      const outbound = MaldaUiProtocol.outbound(protocol, message);
      if (debug) console.debug("malda-ui →", outbound);
      socket.send(JSON.stringify(outbound));
      return true;
    } catch (err) {
      console.warn("malda-ui send failed", {
        error: err,
        type: message && message.type,
        flushing: !!isFlushing
      });
      if (socket) {
        try {
          socket.close();
        } catch {
          // Best effort shutdown; reconnect path is handled by close event.
        }
      }
      return false;
    }
  }

  function cssEscape(value) {
    if (window.CSS && typeof window.CSS.escape === "function") {
      return window.CSS.escape(value);
    }
    return String(value).replace(/"/g, '\\"');
  }
})();
