(function (global) {
  "use strict";

  function coerceToInt(value) {
    if (value === null || value === undefined) return 0;
    if (typeof value === "number") return Number.isFinite(value) ? Math.trunc(value) : 0;
    if (typeof value === "boolean") return value ? 1 : 0;
    if (typeof value === "string") {
      const parsed = Number(value.trim());
      return Number.isFinite(parsed) ? Math.trunc(parsed) : 0;
    }
    return 0;
  }

  function coerceToFloat(value) {
    if (value === null || value === undefined) return 0;
    if (typeof value === "number") return Number.isFinite(value) ? value : 0;
    if (typeof value === "boolean") return value ? 1 : 0;
    if (typeof value === "string") {
      const parsed = Number(value.trim());
      return Number.isFinite(parsed) ? parsed : 0;
    }
    return 0;
  }

  function coerceToString(value) {
    if (value === null || value === undefined) return "";
    return String(value);
  }

  function resolveAssetUrl(url) {
    const source = coerceToString(url);
    if (!source) {
      return source;
    }
    if (
      source.indexOf("data:") === 0 ||
      source.indexOf("blob:") === 0 ||
      source.indexOf("http://") === 0 ||
      source.indexOf("https://") === 0 ||
      source.indexOf("file:") === 0 ||
      source.charAt(0) === "/"
    ) {
      return source;
    }
    const base = coerceToString(global.__maldaAssetBase);
    if (!base) {
      return source;
    }
    return base.charAt(base.length - 1) === "/" ? base + source : base + "/" + source;
  }

  function isTruthy(value) {
    if (value === null || value === undefined) return false;
    if (typeof value === "boolean") return value;
    if (typeof value === "number") return value !== 0 && !Number.isNaN(value);
    if (typeof value === "string") return value.length > 0;
    return true;
  }

  function equals(left, right) {
    if (left === right) return true;
    if ((left === null || left === undefined) && (right === null || right === undefined)) return true;
    return false;
  }

  let randomState = 123456789;

  function nextRandomUnit() {
    randomState = (Math.imul(randomState, 1664525) + 1013904223) >>> 0;
    return randomState / 4294967296;
  }

  function randomBuiltin() {
    return nextRandomUnit();
  }

  function randomIntBuiltin(minValue, maxValue) {
    const min = coerceToInt(minValue);
    const max = coerceToInt(maxValue);
    if (min > max) {
      throw new Error("randomInt() min must be <= max");
    }
    const range = (max - min) + 1;
    return min + Math.floor(nextRandomUnit() * range);
  }

  function randomFloatBuiltin(minValue, maxValue) {
    const min = coerceToFloat(minValue);
    const max = coerceToFloat(maxValue);
    if (min > max) {
      throw new Error("randomFloat() min must be <= max");
    }
    return min + ((max - min) * nextRandomUnit());
  }

  function lengthBuiltin(value) {
    if (value === null || value === undefined) return 0;
    if (typeof value === "string" || Array.isArray(value)) return value.length;
    if (typeof value === "object") return Object.keys(value).length;
    return coerceToString(value).length;
  }

  function substringBuiltin(value, start, count) {
    const text = coerceToString(value);
    const from = Math.max(0, coerceToInt(start));
    if (count === null || count === undefined) {
      return text.substring(from);
    }

    return text.substring(from, from + Math.max(0, coerceToInt(count)));
  }

  function indexOfBuiltin(value, search) {
    return coerceToString(value).indexOf(coerceToString(search));
  }

  function replaceBuiltin(value, search, replacement) {
    return coerceToString(value).split(coerceToString(search)).join(coerceToString(replacement));
  }

  function lowerBuiltin(value) {
    return coerceToString(value).toLowerCase();
  }

  function roundBuiltin(value, digits) {
    const precision = digits === null || digits === undefined ? 0 : coerceToInt(digits);
    const factor = Math.pow(10, precision);
    return Math.round(coerceToFloat(value) * factor) / factor;
  }

  function variant(tag, payload) {
    return {
      __maldaVariant: true,
      tag: coerceToString(tag),
      payload: Array.isArray(payload) ? payload.slice() : []
    };
  }

  function isVariant(value) {
    return !!(value && typeof value === "object" && value.__maldaVariant === true);
  }

  function variantTag(value) {
    if (!isVariant(value)) return null;
    return coerceToString(value.tag);
  }

  function variantPayload(value) {
    if (!isVariant(value)) return [];
    return Array.isArray(value.payload) ? value.payload.slice() : [];
  }

  const legacyTypeTags = {
    integer: "int",
    boolean: "bool",
    dictionary: "dict"
  };

  function normalizeTypeTag(tag) {
    const trimmed = coerceToString(tag).trim();
    if (!trimmed) return "";
    return legacyTypeTags[trimmed] || trimmed;
  }

  function isMaldaDict(value) {
    return !!(value && typeof value === "object" && value.__maldaDict === true);
  }

  function markDict(value) {
    if (value && typeof value === "object" && !Array.isArray(value) && !isVariant(value)) {
      Object.defineProperty(value, "__maldaDict", {
        value: true,
        enumerable: false,
        configurable: true
      });
    }
    return value;
  }

  function typeOfBuiltin(value) {
    if (value === null || value === undefined) return "null";
    if (typeof value === "boolean") return "bool";
    if (typeof value === "string") return "string";
    if (typeof value === "number") {
      return Number.isInteger(value) ? "int" : "float";
    }
    if (isVariant(value)) return "variant";
    if (value && typeof value.then === "function") return "task";
    if (Array.isArray(value)) return "array";
    if (typeof value === "object") {
      return isMaldaDict(value) ? "dict" : "object";
    }
    if (typeof value === "function") return "function";
    return "unknown";
  }

  function isTagBuiltin(value, tag) {
    const actual = typeOfBuiltin(value);
    const expected = normalizeTypeTag(tag);
    return actual === expected;
  }

  function isNumberBuiltin(value) {
    return typeof value === "number" && Number.isFinite(value);
  }

  async function allBuiltin(...args) {
    let tasks;
    if (args.length === 1 && Array.isArray(args[0])) {
      tasks = args[0];
    } else {
      tasks = args;
    }

    const awaited = tasks.map((task) => {
      if (task && typeof task.then === "function") {
        return task;
      }
      return Promise.resolve(task);
    });

    return Promise.all(awaited);
  }

  const deferStacks = [];

  function pushDeferFrame() {
    deferStacks.push([]);
  }

  function registerDefer(action) {
    if (deferStacks.length === 0) {
      throw new Error("'defer' is only valid inside a block, function, or 'using' body.");
    }
    deferStacks[deferStacks.length - 1].push(action);
  }

  async function runAndPopDeferFrame() {
    if (deferStacks.length === 0) {
      return;
    }
    const actions = deferStacks.pop();
    for (let i = actions.length - 1; i >= 0; i--) {
      try {
        await actions[i]();
      } catch (error) {
        // Defer cleanup errors should not mask primary control flow.
      }
    }
  }

  async function disposeResource(value) {
    if (value === null || value === undefined) {
      return;
    }
    const target = value;
    for (const methodName of ["dispose", "close", "disconnect"]) {
      const method = target[methodName];
      if (typeof method === "function") {
        await method.call(target);
        return;
      }
    }
  }

  function getArray(value) {
    if (Array.isArray(value)) {
      return value.slice();
    }
    return [];
  }

  function rangeBuiltin(...args) {
    let start = 0;
    let end = 0;
    let step = 1;
    if (args.length === 1) {
      end = coerceToInt(args[0]);
    } else if (args.length === 2) {
      start = coerceToInt(args[0]);
      end = coerceToInt(args[1]);
    } else if (args.length === 3) {
      start = coerceToInt(args[0]);
      end = coerceToInt(args[1]);
      step = coerceToInt(args[2]);
      if (step === 0) {
        throw new Error("range() step cannot be zero");
      }
    } else {
      throw new Error("range() expects 1-3 arguments");
    }

    const result = [];
    if (step > 0) {
      for (let i = start; i < end; i += step) {
        result.push(i);
      }
    } else {
      for (let i = start; i > end; i += step) {
        result.push(i);
      }
    }
    return result;
  }

  function joinBuiltin(arrayValue, separator) {
    const array = getArray(arrayValue);
    const sep = separator === null || separator === undefined ? "," : coerceToString(separator);
    return array.map((item) => coerceToString(item)).join(sep);
  }

  function compareSortValues(a, b) {
    const aNum = coerceToFloat(a);
    const bNum = coerceToFloat(b);
    if (Number.isFinite(aNum) && Number.isFinite(bNum)) {
      return aNum - bNum;
    }
    return coerceToString(a).localeCompare(coerceToString(b));
  }

  function sortBuiltin(arrayValue, compareFn) {
    const array = getArray(arrayValue);
    if (typeof compareFn === "function") {
      return array.slice().sort((a, b) => {
        const result = compareFn(a, b);
        if (typeof result === "boolean") {
          return result ? 1 : -1;
        }
        return coerceToInt(result);
      });
    }
    return array.slice().sort(compareSortValues);
  }

  function callArrayMethod(arrayValue, methodName, args) {
    const array = getArray(arrayValue);
    const callArgs = Array.isArray(args) ? args : [];
    switch (methodName) {
      case "sort":
        return sortBuiltin(array, callArgs[0]);
      case "join":
        return joinBuiltin(array, callArgs[0]);
      default:
        throw new Error("Array method not supported in JS runtime: " + methodName);
    }
  }

  function mapVariant(value, mapper, successTag, failureTag) {
    if (!isVariant(value)) {
      throw new Error("Expected a variant value (Ok/Err/Some/None)");
    }
    if (value.tag === failureTag) {
      return value;
    }
    if (value.tag !== successTag) {
      throw new Error("map() expected variant tag '" + successTag + "' or '" + failureTag + "', got '" + value.tag + "'");
    }
    const payload = variantPayload(value);
    const mapped = mapper(payload.length > 0 ? payload[0] : null);
    return variant(successTag, [mapped]);
  }

  function describeAndThenGot(value) {
    if (isVariant(value)) {
      return "'" + coerceToString(value.tag) + "'";
    }
    return typeOfBuiltin(value);
  }

  function andThenVariant(value, mapper, successTag, failureTag, moduleName) {
    if (!isVariant(value)) {
      throw new Error("Expected a variant value (Ok/Err/Some/None)");
    }
    if (value.tag === failureTag) {
      return value;
    }
    if (value.tag !== successTag) {
      throw new Error("andThen() expected variant tag '" + successTag + "' or '" + failureTag + "', got '" + value.tag + "'");
    }
    const payload = variantPayload(value);
    const bound = mapper(payload.length > 0 ? payload[0] : null);
    if (!isVariant(bound) || (bound.tag !== successTag && bound.tag !== failureTag)) {
      throw new Error(
        "andThen() expected fn to return " + successTag + "/" + failureTag +
        "; got " + describeAndThenGot(bound) + ". Use " + moduleName + ".map to transform a payload."
      );
    }
    return bound;
  }

  function unwrapOrVariant(value, defaultValue, successTag) {
    if (!isVariant(value)) {
      throw new Error("Expected a variant value (Ok/Err/Some/None)");
    }
    const payload = variantPayload(value);
    if (value.tag === successTag && payload.length > 0) {
      return payload[0];
    }
    return defaultValue;
  }

  function isVariantTag(value, tag) {
    return isVariant(value) && coerceToString(value.tag) === tag;
  }

  const resultStdLib = {
    ok(value) {
      return variant("Ok", [value]);
    },
    err(value) {
      return variant("Err", [value]);
    },
    map(value, mapper) {
      return mapVariant(value, mapper, "Ok", "Err");
    },
    andThen(value, mapper) {
      return andThenVariant(value, mapper, "Ok", "Err", "result");
    },
    unwrapOr(value, defaultValue) {
      return unwrapOrVariant(value, defaultValue, "Ok");
    },
    isOk(value) {
      return isVariantTag(value, "Ok");
    },
    isErr(value) {
      return isVariantTag(value, "Err");
    }
  };

  const optionStdLib = {
    some(value) {
      return variant("Some", [value]);
    },
    none() {
      return variant("None", []);
    },
    map(value, mapper) {
      return mapVariant(value, mapper, "Some", "None");
    },
    andThen(value, mapper) {
      return andThenVariant(value, mapper, "Some", "None", "option");
    },
    unwrapOr(value, defaultValue) {
      return unwrapOrVariant(value, defaultValue, "Some");
    },
    isSome(value) {
      return isVariantTag(value, "Some");
    },
    isNone(value) {
      return isVariantTag(value, "None");
    }
  };

  const groundedStdLib = {
    wrap(value, citations) {
      const list = [];
      if (citations == null || citations === undefined) {
        // unsourced wrapper
      } else if (Array.isArray(citations)) {
        for (const item of citations) {
          const citation = normalizeGroundedCitation(item);
          if (citation) list.push(citation);
        }
      } else {
        const citation = normalizeGroundedCitation(citations);
        if (citation) list.push(citation);
      }
      return markDict({
        value,
        citations: list,
        sourced: list.length > 0
      });
    }
  };

  const capStamp = Symbol("malda.capability");

  function mintCap(kind, path, callee) {
    if (typeof path !== "string") {
      throw new Error((callee || kind) + "() path must be a string");
    }
    const token = markDict({ kind, path });
    Object.defineProperty(token, capStamp, {
      value: true,
      enumerable: false,
      configurable: false
    });
    return Object.freeze(token);
  }

  function isCapToken(value, kind) {
    if (!value || typeof value !== "object" || value[capStamp] !== true) return false;
    if (kind == null || kind === undefined) return true;
    return value.kind === kind;
  }

  function requireCapToken(value, kind, callee) {
    if (!isCapToken(value)) {
      throw new Error(callee + "() expects an unforgeable capability token, not a string or object literal");
    }
    if (kind && value.kind !== kind) {
      throw new Error(callee + "() capability kind is '" + value.kind + "', expected '" + kind + "'");
    }
    return value;
  }

  function normalizeCapPath(path) {
    const raw = String(path == null ? "" : path).replace(/\\/g, "/");
    const isAbs = raw.startsWith("/");
    const parts = raw.split("/");
    const out = [];
    for (const part of parts) {
      if (part === "" || part === ".") continue;
      if (part === "..") {
        if (out.length > 0) out.pop();
        continue;
      }
      out.push(part);
    }
    const joined = out.join("/");
    return isAbs ? "/" + joined : joined;
  }

  function isPathUnderCap(root, path) {
    const r = normalizeCapPath(root);
    const p = normalizeCapPath(path);
    if (p === r) return true;
    if (r === "" || r === ".") return !p.startsWith("../") && p.indexOf("/../") < 0;
    return p.startsWith(r.endsWith("/") ? r : r + "/");
  }

  function capHostIoUnavailable(callee) {
    throw new Error(callee + "() file I/O is not available on the JavaScript backend");
  }

  const capStdLib = {
    fileRead(path) {
      return mintCap("fileRead", path, "fileRead");
    },
    fileWrite(path) {
      return mintCap("fileWrite", path, "fileWrite");
    },
    dirList(path) {
      return mintCap("dirList", path, "dirList");
    },
    is(value, kind) {
      return isCapToken(value, kind);
    },
    confine(token, relativePath) {
      const parent = requireCapToken(token, null, "confine");
      if (typeof relativePath !== "string") {
        throw new Error("confine() path must be a string");
      }
      const rooted = relativePath.startsWith("/") || /^[A-Za-z]:[\\/]/.test(relativePath);
      const combined = rooted
        ? relativePath
        : (parent.path === "" || parent.path === "."
          ? relativePath
          : String(parent.path).replace(/[/\\]+$/, "") + "/" + relativePath.replace(/^[/\\]+/, ""));
      if (!isPathUnderCap(parent.path, combined)) {
        throw new Error("confine() path '" + relativePath + "' is not under capability path '" + parent.path + "'");
      }
      return mintCap(parent.kind, combined, "confine");
    },
    read() { capHostIoUnavailable("read"); },
    write() { capHostIoUnavailable("write"); },
    list() { capHostIoUnavailable("list"); }
  };

  function normalizeGroundedCitation(item) {
    if (item == null || item === undefined) return null;
    if (typeof item === "string") {
      const source = item.trim();
      return source.length === 0 ? null : markDict({ source });
    }
    if (typeof item !== "object" || Array.isArray(item)) return null;
    const sourceRaw = item.source || item.filePath || "";
    const source = typeof sourceRaw === "string" && sourceRaw.trim().length > 0
      ? sourceRaw.trim()
      : "graph-memory";
    const citation = { source };
    const idRaw = item.id || item.nodeId;
    if (typeof idRaw === "string" && idRaw.trim().length > 0) citation.id = idRaw.trim();
    if (item.span != null && item.span !== undefined) citation.span = item.span;
    return markDict(citation);
  }

  function createSeededRandom(seed) {
    let state = (coerceToInt(seed) >>> 0) || 1;
    return {
      nextInt(minValue, maxValue) {
        state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
        const unit = state / 4294967296;
        const min = coerceToInt(minValue);
        const max = coerceToInt(maxValue);
        const range = (max - min) + 1;
        return min + Math.floor(unit * range);
      }
    };
  }

  async function runPropertyBuiltin(registry, propertyName, iterationsValue, seedValue) {
    const name = coerceToString(propertyName);
    const iterations = coerceToInt(iterationsValue);
    const seed = coerceToInt(seedValue);
    if (iterations <= 0) {
      throw new Error("runProperty iterations must be > 0");
    }

    const entry = registry[name];
    if (!entry || typeof entry.fn !== "function") {
      throw new Error("Property '" + name + "' was not found.");
    }

    const random = createSeededRandom(seed);
    for (let trial = 1; trial <= iterations; trial++) {
      const args = (entry.parameters || []).map(() => random.nextInt(-100, 100));
      let passed = true;
      let error = null;
      try {
        let result = entry.fn(...args);
        if (result && typeof result.then === "function") {
          result = await result;
        }
        if (result === false) {
          passed = false;
          error = "Property returned false.";
        }
      } catch (err) {
        passed = false;
        error = err && err.message ? err.message : String(err);
      }

      if (!passed) {
        return markDict({
          propertyName: name,
          passed: false,
          iterations,
          seed,
          failedTrial: trial,
          error,
          counterexample: null,
          shrunkCounterexample: null
        });
      }
    }

    return markDict({
      propertyName: name,
      passed: true,
      iterations,
      seed,
      failedTrial: null,
      error: null,
      counterexample: null,
      shrunkCounterexample: null
    });
  }

  function throwMalda(value) {
    const error = new Error("MALDA");
    error.__maldaValue = value;
    throw error;
  }

  function unwrapMaldaException(error) {
    if (error && Object.prototype.hasOwnProperty.call(error, "__maldaValue")) {
      return error.__maldaValue;
    }
    if (error && typeof error.message === "string") {
      return error.message;
    }
    return error;
  }

  function arrayAppend(array, value) {
    if (!Array.isArray(array)) {
      throw new Error("append() expects an array");
    }
    array.push(value);
    return array;
  }

  function nullCoalesce(left, rightFactory) {
    if (left === null || left === undefined) {
      return rightFactory();
    }
    return left;
  }

  function getMemberNullSafe(object, member) {
    if (object === null || object === undefined) {
      return null;
    }
    return object[member];
  }

  function getIndexNullSafe(object, index) {
    if (object === null || object === undefined) {
      return null;
    }
    return object[index];
  }

  function matchLiteral(patternValue, runtimeValue) {
    if (patternValue === null) {
      return runtimeValue === null || runtimeValue === undefined;
    }
    if (typeof patternValue === "boolean") {
      return typeof runtimeValue === "boolean" && runtimeValue === patternValue;
    }
    if (typeof patternValue === "string") {
      return typeof runtimeValue === "string" && runtimeValue === patternValue;
    }
    if (typeof patternValue === "number") {
      if (typeof runtimeValue !== "number" || Number.isNaN(runtimeValue)) return false;
      if (Number.isInteger(patternValue)) {
        return Number.isInteger(runtimeValue) && runtimeValue === patternValue;
      }
      return Math.abs(runtimeValue - patternValue) < 0.0001;
    }
    return false;
  }

  function mergeBindings(target, source) {
    const keys = Object.keys(source);
    for (let i = 0; i < keys.length; i++) {
      target[keys[i]] = source[keys[i]];
    }
  }

  function matchPatternInternal(pattern, value, bindings) {
    if (!pattern || typeof pattern !== "object") {
      return false;
    }

    switch (pattern.type) {
      case "Literal":
        return matchLiteral(pattern.value, value);
      case "Identifier":
        bindings[coerceToString(pattern.name)] = value;
        return true;
      case "Wildcard":
        return true;
      case "Variant": {
        if (!isVariant(value)) return false;
        const payloadPatterns = Array.isArray(pattern.payloadPatterns) ? pattern.payloadPatterns : [];
        const payloadValues = variantPayload(value);
        if (variantTag(value) !== coerceToString(pattern.tag)) return false;
        if (payloadValues.length !== payloadPatterns.length) return false;
        for (let i = 0; i < payloadPatterns.length; i++) {
          const localBindings = {};
          if (!matchPatternInternal(payloadPatterns[i], payloadValues[i], localBindings)) {
            return false;
          }
          mergeBindings(bindings, localBindings);
        }
        return true;
      }
      case "Array": {
        if (!Array.isArray(value)) return false;
        const elements = Array.isArray(pattern.elements) ? pattern.elements : [];
        const rest = pattern.rest && typeof pattern.rest === "object" ? pattern.rest : null;
        if (!rest && value.length !== elements.length) return false;
        if (rest && value.length < elements.length) return false;

        for (let i = 0; i < elements.length; i++) {
          const localBindings = {};
          if (!matchPatternInternal(elements[i], value[i], localBindings)) {
            return false;
          }
          mergeBindings(bindings, localBindings);
        }

        if (rest && typeof rest.name === "string" && rest.name.length > 0) {
          bindings[rest.name] = value.slice(elements.length);
        }
        return true;
      }
      case "Object": {
        if (value === null || value === undefined || typeof value !== "object") return false;
        if (Array.isArray(value) || isVariant(value)) return false;

        const properties = Array.isArray(pattern.properties) ? pattern.properties : [];
        for (let i = 0; i < properties.length; i++) {
          const prop = properties[i];
          const key = coerceToString(prop.key);
          if (!Object.prototype.hasOwnProperty.call(value, key)) {
            return false;
          }

          const propValue = value[key];
          if (prop.pattern) {
            const localBindings = {};
            if (!matchPatternInternal(prop.pattern, propValue, localBindings)) {
              return false;
            }
            mergeBindings(bindings, localBindings);
          } else if (typeof prop.bindingName === "string" && prop.bindingName.length > 0) {
            bindings[prop.bindingName] = propValue;
          }
        }

        return true;
      }
      case "Rest":
        // Rest patterns are only valid inside array patterns.
        return false;
      default:
        throw new Error("Unknown pattern type: " + coerceToString(pattern.type));
    }
  }

  function matchPattern(pattern, value) {
    const bindings = {};
    const matched = matchPatternInternal(pattern, value, bindings);
    return { matched, bindings };
  }

  function resolveElement(target) {
    if (typeof document === "undefined") {
      throw new Error("mlRuntime.dom.* requires a browser document.");
    }

    if (typeof target === "string") {
      return document.querySelector(target);
    }

    return target || null;
  }

  function requireBrowserApi(apiName) {
    if (typeof document === "undefined" || typeof window === "undefined") {
      throw new Error(apiName + " requires a browser environment.");
    }
  }

  function toFiniteNumber(value, fallback) {
    const numberValue = Number(value);
    return Number.isFinite(numberValue) ? numberValue : fallback;
  }

  const actorsRuntime = (() => {
    const cells = new Map();
    const callbacks = new Map();
    let nextActorId = 1;
    let nextCorrelationId = 1;
    let currentContext = null;

    function isActorRef(value) {
      return !!(value && typeof value === "object" && value.__maldaActorRef === true && typeof value.id === "number");
    }

    function getCellOrThrow(actorRef) {
      if (!isActorRef(actorRef)) {
        throw new Error("Expected ActorRef.");
      }

      const cell = cells.get(actorRef.id);
      if (!cell) {
        throw new Error("Unknown actor reference.");
      }

      return cell;
    }

    function enqueueReceiveValue(cell, value) {
      if (cell.receiveResolvers.length > 0) {
        const resolve = cell.receiveResolvers.shift();
        resolve(value);
        return;
      }

      cell.receiveQueue.push(value);
    }

    function normalizeArgs(handler, args) {
      const expected = typeof handler.length === "number" ? handler.length : args.length;
      if (expected <= 0) return [];

      const result = [];
      for (let i = 0; i < expected; i++) {
        result.push(i < args.length ? args[i] : null);
      }
      return result;
    }

    function schedule(cell) {
      if (cell.processing || cell.stopped) {
        return;
      }

      cell.processing = true;
      Promise.resolve().then(() => processCell(cell));
    }

    async function processCell(cell) {
      while (cell.queue.length > 0 && !cell.stopped) {
        const invocation = cell.queue.shift();
        const previousContext = currentContext;
        currentContext = {
          self: cell.ref,
          sender: invocation.sender,
          correlationId: invocation.correlationId,
          cell
        };

        try {
          if (typeof invocation.action === "function") {
            await invocation.action();
            continue;
          }

          for (let i = 0; i < invocation.args.length; i++) {
            enqueueReceiveValue(cell, invocation.args[i]);
          }

          const handlerName = invocation.handlerName === null || invocation.handlerName === undefined || invocation.handlerName === ""
            ? "handle"
            : coerceToString(invocation.handlerName);
          const handler = cell.actor[handlerName];
          if (typeof handler !== "function") {
            throw new Error("Actor handler not found: " + handlerName);
          }

          const normalizedArgs = normalizeArgs(handler, invocation.args);
          const maybePromise = handler.apply(cell.actor, normalizedArgs);
          if (maybePromise && typeof maybePromise.then === "function") {
            await maybePromise;
          }
        } finally {
          currentContext = previousContext;
        }
      }

      cell.processing = false;
      if (cell.queue.length > 0 && !cell.stopped) {
        schedule(cell);
      }
    }

    function spawn(actorFactoryOrInstance, ...args) {
      let actor = actorFactoryOrInstance;
      if (typeof actorFactoryOrInstance === "function") {
        actor = actorFactoryOrInstance(...args);
      }

      if (actor === null || actor === undefined || typeof actor !== "object") {
        throw new Error("spawn() expects an actor object instance or a factory function.");
      }

      const id = nextActorId++;
      const ref = { __maldaActorRef: true, id };
      const cell = {
        id,
        ref,
        actor,
        queue: [],
        receiveQueue: [],
        receiveResolvers: [],
        processing: false,
        stopped: false
      };
      cells.set(id, cell);
      return ref;
    }

    function send(targetRef, handlerNameOrNull, ...args) {
      const cell = getCellOrThrow(targetRef);
      if (cell.stopped) {
        return null;
      }

      cell.queue.push({
        handlerName: handlerNameOrNull,
        args: args || [],
        sender: null,
        correlationId: null
      });
      schedule(cell);
      return null;
    }

    function sendWithCallback(senderRef, targetRef, handlerNameOrNull, callbackFn, timeoutMsOrNull, timeoutErrFnOrNull, ...args) {
      if (typeof callbackFn !== "function") {
        throw new Error("sendWithCallback requires callback to be a function.");
      }

      const senderCell = getCellOrThrow(senderRef);
      const targetCell = getCellOrThrow(targetRef);

      if (senderCell.stopped || targetCell.stopped) {
        return null;
      }

      const correlationId = "corr_" + nextCorrelationId++;
      const callbackRecord = {
        senderCell,
        callbackFn,
        timeoutErrFn: typeof timeoutErrFnOrNull === "function" ? timeoutErrFnOrNull : null,
        timeoutHandle: null
      };
      callbacks.set(correlationId, callbackRecord);

      if (timeoutMsOrNull !== null && timeoutMsOrNull !== undefined) {
        const timeoutMs = Math.max(0, coerceToInt(timeoutMsOrNull));
        callbackRecord.timeoutHandle = setTimeout(() => {
          const active = callbacks.get(correlationId);
          if (!active) {
            return;
          }

          callbacks.delete(correlationId);
          if (active.senderCell.stopped) {
            return;
          }

          active.senderCell.queue.push({
            handlerName: null,
            args: [],
            sender: null,
            correlationId: null,
            action: async () => {
              if (active.timeoutErrFn) {
                await active.timeoutErrFn("Request timed out.");
              }
            }
          });
          schedule(active.senderCell);
        }, timeoutMs);
      }

      targetCell.queue.push({
        handlerName: handlerNameOrNull,
        args: args || [],
        sender: senderRef,
        correlationId
      });
      schedule(targetCell);
      return null;
    }

    function getSelf() {
      if (!currentContext || !currentContext.self) {
        throw new Error("getSelf() can only be used inside an actor handler.");
      }
      return currentContext.self;
    }

    function reply(value) {
      if (!currentContext || !currentContext.correlationId) {
        throw new Error("reply() must be called while handling a callback-enabled send.");
      }

      const record = callbacks.get(currentContext.correlationId);
      if (!record) {
        return null;
      }

      callbacks.delete(currentContext.correlationId);
      if (record.timeoutHandle !== null) {
        clearTimeout(record.timeoutHandle);
      }

      if (!record.senderCell.stopped) {
        record.senderCell.queue.push({
          handlerName: null,
          args: [],
          sender: null,
          correlationId: null,
          action: async () => {
            await record.callbackFn(value);
          }
        });
        schedule(record.senderCell);
      }

      return null;
    }

    function receiveAsync() {
      if (!currentContext || !currentContext.cell) {
        throw new Error("receive() can only be used inside an actor handler.");
      }

      const cell = currentContext.cell;
      if (cell.receiveQueue.length > 0) {
        return Promise.resolve(cell.receiveQueue.shift());
      }

      if (cell.stopped) {
        return Promise.resolve(null);
      }

      return new Promise((resolve) => {
        cell.receiveResolvers.push(resolve);
      });
    }

    function stop(actorRef) {
      const cell = getCellOrThrow(actorRef);
      cell.stopped = true;
      cell.queue.length = 0;
      while (cell.receiveResolvers.length > 0) {
        const resolve = cell.receiveResolvers.shift();
        resolve(null);
      }
      return null;
    }

    async function shutdownAsync() {
      const values = Array.from(cells.values());
      for (let i = 0; i < values.length; i++) {
        values[i].stopped = true;
        values[i].queue.length = 0;
        while (values[i].receiveResolvers.length > 0) {
          const resolve = values[i].receiveResolvers.shift();
          resolve(null);
        }
      }

      callbacks.clear();
      await Promise.resolve();
      return null;
    }

    function callActorOrVoidStop(target) {
      if (isActorRef(target)) {
        return stop(target);
      }

      if (target && typeof target.stop === "function") {
        return target.stop();
      }

      return null;
    }

    return {
      spawn,
      send,
      sendWithCallback,
      reply,
      receiveAsync,
      getSelf,
      stop,
      shutdownAsync,
      callActorOrVoidStop
    };
  })();

  function isObject(value) {
    return value !== null && typeof value === "object" && !Array.isArray(value) && !isVariant(value);
  }

  function objectHasKey(object, key) {
    return isObject(object) && Object.prototype.hasOwnProperty.call(object, coerceToString(key));
  }

  function seedBuiltin(value) {
    const seed = coerceToInt(value);
    randomState = (seed >>> 0) || 1;
    return null;
  }

  function numericArray(name, value) {
    return getArray(value).map((item) => coerceToFloat(item));
  }

  function mathAbs(value) {
    if (typeof value === "number" && Number.isInteger(value)) {
      return Math.abs(value);
    }
    return Math.abs(coerceToFloat(value));
  }

  function mathSum(value) {
    return numericArray("sum", value).reduce((acc, item) => acc + item, 0);
  }

  function mathAverage(value) {
    const numbers = numericArray("average", value);
    if (numbers.length === 0) return 0;
    return mathSum(numbers) / numbers.length;
  }

  function mathMax(value) {
    const numbers = numericArray("max", value);
    if (numbers.length === 0) throw new Error("max() expects a non-empty array");
    return Math.max.apply(null, numbers);
  }

  function mathMin(value) {
    const numbers = numericArray("min", value);
    if (numbers.length === 0) throw new Error("min() expects a non-empty array");
    return Math.min.apply(null, numbers);
  }

  function mathClamp(value, minValue, maxValue) {
    const n = coerceToFloat(value);
    const lo = coerceToFloat(minValue);
    const hi = coerceToFloat(maxValue);
    return Math.min(hi, Math.max(lo, n));
  }

  const mathStdLib = {
    abs: mathAbs,
    sum: mathSum,
    average: mathAverage,
    max: mathMax,
    min: mathMin,
    pow: (a, b) => Math.pow(coerceToFloat(a), coerceToFloat(b)),
    sqrt: (value) => Math.sqrt(coerceToFloat(value)),
    floor: (value) => Math.floor(coerceToFloat(value)),
    ceil: (value) => Math.ceil(coerceToFloat(value)),
    round: roundBuiltin,
    trunc: (value) => Math.trunc(coerceToFloat(value)),
    sign: (value) => Math.sign(coerceToFloat(value)),
    exp: (value) => Math.exp(coerceToFloat(value)),
    log: (value) => Math.log(coerceToFloat(value)),
    log10: (value) => Math.log10(coerceToFloat(value)),
    log2: (value) => Math.log2(coerceToFloat(value)),
    sin: (value) => Math.sin(coerceToFloat(value)),
    cos: (value) => Math.cos(coerceToFloat(value)),
    tan: (value) => Math.tan(coerceToFloat(value)),
    asin: (value) => Math.asin(coerceToFloat(value)),
    acos: (value) => Math.acos(coerceToFloat(value)),
    atan: (value) => Math.atan(coerceToFloat(value)),
    atan2: (y, x) => Math.atan2(coerceToFloat(y), coerceToFloat(x)),
    hypot: (a, b) => Math.hypot(coerceToFloat(a), coerceToFloat(b)),
    clamp: mathClamp,
    degToRad: (value) => coerceToFloat(value) * Math.PI / 180,
    radToDeg: (value) => coerceToFloat(value) * 180 / Math.PI,
    random: randomBuiltin,
    randomInt: randomIntBuiltin,
    randomFloat: randomFloatBuiltin,
    seed: seedBuiltin
  };

  function strUpper(value) { return coerceToString(value).toUpperCase(); }
  function strTrim(value) { return coerceToString(value).trim(); }
  function strSplit(value, separator) {
    const sep = separator === null || separator === undefined ? "" : coerceToString(separator);
    return coerceToString(value).split(sep);
  }
  function strStartsWith(value, prefix) {
    return coerceToString(value).startsWith(coerceToString(prefix));
  }
  function strEndsWith(value, suffix) {
    return coerceToString(value).endsWith(coerceToString(suffix));
  }
  function strPadStart(value, length, pad) {
    return coerceToString(value).padStart(Math.max(0, coerceToInt(length)), coerceToString(pad === undefined ? " " : pad));
  }
  function strPadEnd(value, length, pad) {
    return coerceToString(value).padEnd(Math.max(0, coerceToInt(length)), coerceToString(pad === undefined ? " " : pad));
  }
  function strIncludes(value, search) {
    return coerceToString(value).includes(coerceToString(search));
  }
  function strRepeat(value, count) {
    return coerceToString(value).repeat(Math.max(0, coerceToInt(count)));
  }
  function compileRegex(pattern) {
    try {
      return new RegExp(coerceToString(pattern));
    } catch (error) {
      throw new Error("Invalid regex pattern: " + (error && error.message ? error.message : String(error)));
    }
  }
  function strRegexMatch(value, pattern) {
    return compileRegex(pattern).test(coerceToString(value));
  }
  function strRegexReplace(value, pattern, replacement) {
    return coerceToString(value).replace(compileRegex(pattern), coerceToString(replacement));
  }
  function strRegexFind(value, pattern) {
    const match = coerceToString(value).match(compileRegex(pattern));
    return match ? match[0] : null;
  }

  const strStdLib = {
    length: lengthBuiltin,
    upper: strUpper,
    lower: lowerBuiltin,
    trim: strTrim,
    substring: substringBuiltin,
    indexOf: indexOfBuiltin,
    replace: replaceBuiltin,
    split: strSplit,
    join: joinBuiltin,
    startsWith: strStartsWith,
    endsWith: strEndsWith,
    padStart: strPadStart,
    padEnd: strPadEnd,
    includes: strIncludes,
    repeat: strRepeat,
    regexMatch: strRegexMatch,
    regexReplace: strRegexReplace,
    regexFind: strRegexFind
  };

  function ioPrint(value) {
    if (typeof console !== "undefined") {
      console.log(coerceToString(value));
    }
    return null;
  }

  function ioInput(promptText) {
    if (typeof process !== "undefined" && process.env && typeof process.env.MALDA_INPUT === "string") {
      return process.env.MALDA_INPUT;
    }
    if (typeof window !== "undefined" && typeof window.prompt === "function") {
      const result = window.prompt(promptText === undefined || promptText === null ? "" : coerceToString(promptText));
      return result === null ? "" : result;
    }
    return "";
  }

  const ioStdLib = {
    print: ioPrint,
    input: ioInput
  };

  function reviveJson(value) {
    if (value === null) return null;
    if (Array.isArray(value)) return value.map(reviveJson);
    if (typeof value === "object") return markDict(value);
    return value;
  }

  function parseJSON(text) {
    if (typeof text !== "string") {
      throw new Error("parseJSON() expects a string argument");
    }
    try {
      return reviveJson(JSON.parse(text));
    } catch (error) {
      throw new Error("Invalid JSON string");
    }
  }

  function toJSON(value) {
    return JSON.stringify(value, function (_key, item) {
      if (isVariant(item)) {
        return { tag: item.tag, payload: item.payload };
      }
      if (item && typeof item === "object" && item.__maldaDict) {
        const copy = {};
        Object.keys(item).forEach((key) => {
          if (key !== "__maldaDict") copy[key] = item[key];
        });
        return copy;
      }
      return item;
    });
  }

  const schemaRegistry = Object.create(null);
  const sumTypeRegistry = Object.create(null);

  function normalizePrimitive(typeName) {
    const trimmed = coerceToString(typeName).trim().toLowerCase();
    if (trimmed === "string") return "string";
    if (trimmed === "int" || trimmed === "integer") return "integer";
    if (trimmed === "float" || trimmed === "double" || trimmed === "number") return "number";
    if (trimmed === "bool" || trimmed === "boolean") return "boolean";
    if (trimmed === "array" || trimmed === "list") return "array";
    if (trimmed === "object" || trimmed === "json") return "object";
    if (trimmed === "null") return "null";
    return "";
  }

  function jsonTypeOf(value) {
    if (value === null || value === undefined) return "null";
    if (typeof value === "boolean") return "boolean";
    if (typeof value === "number") return Number.isInteger(value) ? "integer" : "number";
    if (typeof value === "string") return "string";
    if (Array.isArray(value)) return "array";
    if (isVariant(value)) return "variant";
    if (typeof value === "object") return "object";
    return typeof value;
  }

  function maldaTypeName(value) {
    if (value === null || value === undefined) return "Null";
    if (typeof value === "boolean") return "Boolean";
    if (typeof value === "number") return Number.isInteger(value) ? "Integer" : "Float";
    if (typeof value === "string") return "String";
    if (Array.isArray(value)) return "Array";
    if (isVariant(value)) return "Variant";
    if (typeof value === "function") return "Function";
    if (typeof value === "object") return "Object";
    return "String";
  }

  function typeMismatch(path, expected, value) {
    return path + " must be " + expected + ", got " + maldaTypeName(value) + ".";
  }

  function validateAgainstType(typeName, value, path) {
    path = path || "$";
    const trimmed = coerceToString(typeName).trim();
    if (trimmed.endsWith("[]")) {
      if (!Array.isArray(value)) {
        return typeMismatch(path, "array", value);
      }
      const elementType = trimmed.slice(0, -2).trim();
      for (let i = 0; i < value.length; i++) {
        const inner = validateAgainstType(elementType, value[i], path + "[" + i + "]");
        if (inner) return inner;
      }
      return "";
    }

    const primitive = normalizePrimitive(trimmed);
    if (primitive) {
      const actual = jsonTypeOf(value);
      if (primitive === "number") {
        return actual === "number" || actual === "integer" ? "" : typeMismatch(path, "number", value);
      }
      if (primitive === "integer") {
        return actual === "integer" ? "" : typeMismatch(path, "integer", value);
      }
      if (primitive === actual) return "";
      return typeMismatch(path, primitive, value);
    }

    if (Object.prototype.hasOwnProperty.call(schemaRegistry, trimmed)) {
      return validateObjectSchema(schemaRegistry[trimmed], value, path);
    }

    if (Object.prototype.hasOwnProperty.call(sumTypeRegistry, trimmed)) {
      if (!isVariant(value) && !isObject(value)) {
        return path + " must be a JSON object with a sum-type tag.";
      }
      const tags = sumTypeRegistry[trimmed];
      const tag = isVariant(value) ? value.tag : (value && value.tag);
      if (typeof tag !== "string") {
        return path + ".tag is required and must be a string constructor name.";
      }
      if (tags.indexOf(tag) < 0) {
        return path + ".tag '" + tag + "' is not a known constructor. Expected one of: " + tags.join(", ") + ".";
      }
      return "";
    }

    return "Unknown schema field type '" + trimmed + "'. Use a Tier-0 JSON type (string, int, float, bool, array, object), a declared schema name, or a declared sum type.";
  }

  function validateObjectSchema(fields, value, path) {
    path = path || "$";
    if (!isObject(value)) {
      return typeMismatch(path, "object", value);
    }
    for (let i = 0; i < fields.length; i++) {
      const field = fields[i];
      const fieldPath = path + "." + field.name;
      const hasKey = objectHasKey(value, field.name);
      const fieldValue = hasKey ? value[field.name] : null;
      if (fieldValue === null || fieldValue === undefined) {
        if (field.required) return fieldPath + " is required.";
        continue;
      }
      const inner = validateAgainstType(field.type, fieldValue, fieldPath);
      if (inner) return inner;
    }
    return "";
  }

  function resolveSchemaArgument(schemaArg) {
    if (typeof schemaArg === "string") {
      if (Object.prototype.hasOwnProperty.call(schemaRegistry, schemaArg)) {
        return { kind: "object", fields: schemaRegistry[schemaArg], name: schemaArg };
      }
      if (Object.prototype.hasOwnProperty.call(sumTypeRegistry, schemaArg)) {
        return { kind: "sum", name: schemaArg };
      }
      throw new Error("Unknown schema '" + schemaArg + "'.");
    }
    throw new Error("validate() expects a schema object or a registered schema or sum-type name.");
  }

  const schemaStdLib = {
    register(name, fields) {
      schemaRegistry[coerceToString(name)] = Array.isArray(fields) ? fields.slice() : [];
      return null;
    },
    registerSumType(name, tags) {
      sumTypeRegistry[coerceToString(name)] = Array.isArray(tags) ? tags.slice() : [];
      return null;
    },
    validate(schemaArg, value) {
      const resolved = resolveSchemaArgument(schemaArg);
      let error = "";
      if (resolved.kind === "object") {
        error = validateObjectSchema(resolved.fields, value);
      } else {
        error = validateAgainstType(resolved.name, value);
      }
      if (!error) {
        return markDict({ ok: true, data: value, error: null });
      }
      return markDict({ ok: false, data: null, error: error });
    }
  };

  function parseJson(value, schemaRef) {
    if (arguments.length < 2) {
      throw new Error("parseJson() expects 2 arguments (value, schemaRef) and optional options object. For a plain JSON reader use parseJSON(text).");
    }
    const parsed = parseJSON(coerceToString(value));
    const result = schemaStdLib.validate(schemaRef, parsed);
    if (isTruthy(result.ok)) {
      return result.data;
    }
    throw new Error("parseJson() validation failed after 1 attempt(s) for schema '" + coerceToString(schemaRef) + "'. Last error: " + coerceToString(result.error));
  }

  function nowBuiltin() {
    return Date.now();
  }

  function pad2(value) {
    return String(value).padStart(2, "0");
  }

  function formatDateBuiltin(timestamp, format) {
    const ms = coerceToFloat(timestamp);
    const date = new Date(ms);
    if (Number.isNaN(date.getTime())) {
      throw new Error("formatDate() timestamp must be a number");
    }
    const pattern = format === null || format === undefined ? "yyyy-MM-dd HH:mm:ss" : coerceToString(format);
    const utc = {
      yyyy: String(date.getUTCFullYear()),
      MM: pad2(date.getUTCMonth() + 1),
      dd: pad2(date.getUTCDate()),
      HH: pad2(date.getUTCHours()),
      mm: pad2(date.getUTCMinutes()),
      ss: pad2(date.getUTCSeconds())
    };
    return pattern
      .replace(/yyyy/g, utc.yyyy)
      .replace(/MM/g, utc.MM)
      .replace(/dd/g, utc.dd)
      .replace(/HH/g, utc.HH)
      .replace(/mm/g, utc.mm)
      .replace(/ss/g, utc.ss);
  }

  function parseDateBuiltin(text) {
    if (typeof text !== "string") {
      throw new Error("parseDate() expects a string argument");
    }
    const parsed = Date.parse(text);
    if (Number.isNaN(parsed)) {
      throw new Error("parseDate() could not parse date string: " + text);
    }
    return parsed;
  }

  function addDaysBuiltin(timestamp, days) {
    return coerceToFloat(timestamp) + (coerceToFloat(days) * 86400000);
  }

  function addHoursBuiltin(timestamp, hours) {
    return coerceToFloat(timestamp) + (coerceToFloat(hours) * 3600000);
  }

  function readEnv(name) {
    if (typeof process === "undefined" || !process.env) {
      return null;
    }
    const key = coerceToString(name);
    if (!Object.prototype.hasOwnProperty.call(process.env, key)) {
      return null;
    }
    const value = process.env[key];
    return value === undefined ? null : String(value);
  }

  function getEnvBuiltin(name) {
    return readEnv(name);
  }

  function getEnvOrBuiltin(name, fallback) {
    const value = readEnv(name);
    if (value === null) {
      return fallback === undefined ? "" : fallback;
    }
    return value;
  }

  function hasEnvBuiltin(name) {
    return readEnv(name) !== null;
  }

  const withinStack = [];

  function withinEnter(ms, name) {
    const deadline = Date.now() + Math.max(0, coerceToInt(ms));
    withinStack.push({ deadline: deadline, name: coerceToString(name || "Function") });
    return withinStack.length;
  }

  function withinLeave() {
    if (withinStack.length > 0) withinStack.pop();
    return null;
  }

  function withinCheck(name) {
    if (withinStack.length === 0) return;
    const top = withinStack[withinStack.length - 1];
    if (Date.now() <= top.deadline) return;
    const label = name ? "Function '" + name + "'" : (top.name ? "Function '" + top.name + "'" : "Function");
    throw new Error(label + " exceeded @within bound.");
  }

  function withinRun(ms, name, fn) {
    withinEnter(ms, name);
    let finished = false;
    try {
      const result = fn();
      if (result && typeof result.then === "function") {
        const timeoutMs = Math.max(0, coerceToInt(ms));
        const timeout = new Promise((_, reject) => {
          setTimeout(() => {
            try {
              withinCheck(name);
              reject(new Error("Function '" + coerceToString(name) + "' exceeded @within bound."));
            } catch (error) {
              reject(error);
            }
          }, timeoutMs);
        });
        return Promise.race([
          Promise.resolve(result).then((value) => {
            withinCheck(name);
            return value;
          }),
          timeout
        ]).finally(() => {
          if (!finished) {
            finished = true;
            withinLeave();
          }
        });
      }
      withinCheck(name);
      return result;
    } finally {
      if (!finished) {
        finished = true;
        withinLeave();
      }
    }
  }

  const withinStdLib = {
    enter: withinEnter,
    leave: withinLeave,
    check: withinCheck,
    run: withinRun
  };

  function headersToObject(headers) {
    const result = {};
    if (!headers || typeof headers.forEach !== "function") return markDict(result);
    headers.forEach((value, key) => {
      result[key] = value;
    });
    return markDict(result);
  }

  function appendQuery(url, queryParams) {
    if (!isObject(queryParams)) return coerceToString(url);
    const parts = [];
    Object.keys(queryParams).forEach((key) => {
      if (key === "__maldaDict") return;
      parts.push(encodeURIComponent(key) + "=" + encodeURIComponent(coerceToString(queryParams[key])));
    });
    if (parts.length === 0) return coerceToString(url);
    const base = coerceToString(url);
    return base + (base.indexOf("?") >= 0 ? "&" : "?") + parts.join("&");
  }

  async function httpRequest(method, url, body, headers, queryParams) {
    withinCheck();
    if (typeof fetch !== "function") {
      throw new Error(method + " requires fetch()");
    }
    const init = { method: method, headers: {} };
    if (isObject(headers)) {
      Object.keys(headers).forEach((key) => {
        if (key === "__maldaDict") return;
        init.headers[key] = coerceToString(headers[key]);
      });
    }
    if (body !== undefined && body !== null && method !== "GET" && method !== "DELETE") {
      if (typeof body === "string") {
        init.body = body;
      } else {
        init.body = toJSON(body);
        if (!init.headers["Content-Type"] && !init.headers["content-type"]) {
          init.headers["Content-Type"] = "application/json";
        }
      }
    }
    try {
      const response = await fetch(appendQuery(url, queryParams), init);
      const text = await response.text();
      let parsedBody = text;
      const contentType = response.headers && response.headers.get ? (response.headers.get("content-type") || "") : "";
      if (contentType.indexOf("json") >= 0 && text) {
        try {
          parsedBody = parseJSON(text);
        } catch (_error) {
          parsedBody = text;
        }
      }
      return markDict({
        status: response.status,
        statusText: response.statusText || "",
        ok: response.ok,
        headers: headersToObject(response.headers),
        body: parsedBody
      });
    } catch (error) {
      return markDict({
        error: error && error.message ? error.message : String(error),
        ok: false,
        status: 0
      });
    }
  }

  const httpStdLib = {
    get(url, headers, queryParams) {
      return httpRequest("GET", url, null, headers, queryParams);
    },
    post(url, body, headers, queryParams) {
      return httpRequest("POST", url, body, headers, queryParams);
    },
    put(url, body, headers, queryParams) {
      return httpRequest("PUT", url, body, headers, queryParams);
    },
    delete(url, headers, queryParams) {
      return httpRequest("DELETE", url, null, headers, queryParams);
    },
    patch(url, body, headers, queryParams) {
      return httpRequest("PATCH", url, body, headers, queryParams);
    }
  };

  const runtime = {
    coerceToInt,
    coerceToFloat,
    coerceToString,
    isTruthy,
    equals,
    variant,
    isVariant,
    variantTag,
    variantPayload,
    markDict,
    typeOf: typeOfBuiltin,
    isTag: isTagBuiltin,
    isNumber: isNumberBuiltin,
    all: allBuiltin,
    throwMalda,
    unwrapMaldaException,
    arrayAppend,
    getMemberNullSafe,
    getIndexNullSafe,
    nullCoalesce,
    matchPattern,
    pushDeferFrame,
    registerDefer,
    runAndPopDeferFrame,
    disposeResource,
    getArray,
    isObject,
    objectHasKey,
    rangeBuiltin,
    joinBuiltin,
    sortBuiltin,
    callArrayMethod,
    parseJSON,
    parseJson,
    toJSON,
    now: nowBuiltin,
    formatDate: formatDateBuiltin,
    parseDate: parseDateBuiltin,
    addDays: addDaysBuiltin,
    addHours: addHoursBuiltin,
    getEnv: getEnvBuiltin,
    getEnvOr: getEnvOrBuiltin,
    hasEnv: hasEnvBuiltin,
    math: mathStdLib,
    str: strStdLib,
    io: ioStdLib,
    schema: schemaStdLib,
    within: withinStdLib,
    http: httpStdLib,
    result: resultStdLib,
    option: optionStdLib,
    grounded: groundedStdLib,
    cap: capStdLib,
    runProperty: runPropertyBuiltin,
    actors: actorsRuntime,
    builtins: {
      print(value) {
        if (typeof console !== "undefined") {
          console.log(coerceToString(value));
        }
        return null;
      },
      println(value) {
        if (typeof console !== "undefined") {
          console.log(value);
        }
        return null;
      },
      sleep(milliseconds) {
        const ms = Math.max(0, coerceToInt(milliseconds));
        withinCheck();
        return new Promise((resolve, reject) => {
          setTimeout(() => {
            try {
              withinCheck();
              resolve(null);
            } catch (error) {
              reject(error);
            }
          }, ms);
        });
      },
      random() {
        return randomBuiltin();
      },
      randomInt(minValue, maxValue) {
        return randomIntBuiltin(minValue, maxValue);
      },
      randomFloat(minValue, maxValue) {
        return randomFloatBuiltin(minValue, maxValue);
      }
    },
    dom: {
      query(selector, root) {
        const scopedRoot = root ? resolveElement(root) : document;
        if (!scopedRoot || typeof scopedRoot.querySelector !== "function") {
          return null;
        }
        return scopedRoot.querySelector(coerceToString(selector));
      },
      create(tagName) {
        if (typeof document === "undefined") {
          throw new Error("mlRuntime.dom.create requires a browser document.");
        }
        return document.createElement(coerceToString(tagName));
      },
      append(parent, child) {
        const parentNode = resolveElement(parent);
        if (!parentNode || !child) return child || null;
        parentNode.appendChild(child);
        return child;
      },
      clear(target) {
        const node = resolveElement(target);
        if (!node) return null;
        if ("replaceChildren" in node) {
          node.replaceChildren();
        } else {
          node.innerHTML = "";
        }
        return null;
      },
      setText(target, text) {
        const node = resolveElement(target);
        if (!node) return null;
        node.textContent = coerceToString(text);
        return null;
      },
      html(target, markup) {
        const node = resolveElement(target);
        if (!node) return null;
        node.innerHTML = coerceToString(markup);
        return null;
      },
      on(target, eventName, handler, options) {
        const node = resolveElement(target);
        if (!node || typeof node.addEventListener !== "function" || typeof handler !== "function") {
          return null;
        }
        node.addEventListener(coerceToString(eventName), handler, options || undefined);
        return null;
      }
    },
    game: (() => {
      const state = {
        canvas: null,
        context: null,
        running: false,
        rafId: null,
        lastTimestamp: null,
        fixedAccumulator: 0,
        fixedTickMs: 1000 / 60,
        backgroundColor: "#000000",
        keysDown: new Set(),
        pendingKeyPressed: new Set(),
        pendingKeyReleased: new Set(),
        keysPressed: new Set(),
        keysReleased: new Set(),
        mouseButtonsDown: new Set(),
        mouseX: 0,
        mouseY: 0,
        touches: new Map(),
        gamepadConnected: new Set(),
        gamepadButtonsDown: new Set(),
        gamepadButtonsPrev: new Set(),
        gamepadButtonsPressed: new Set(),
        gamepadAxes: {},
        inputFrameActive: false,
        listenersAttached: false,
        listeners: null,
        audioContext: null,
        audioMasterGain: null,
        audioNoiseBuffer: null,
        audioPatternTimer: null,
        audioPatternState: null,
        audioActiveSources: [],
        maxConcurrentAudioSources: 32,
        audioSampleCache: new Map(),
        musicTrackAudio: null,
        musicTrackError: null,
        musicTrackSource: null,
        musicTrackReady: false,
        musicTrackPlaying: false,
        musicTrackVolume: 0.6,
        musicTrackLoop: true,
        pixelBuffer: null,
        cameraX: 0,
        cameraY: 0,
        alpha: 1,
        imageCache: new Map()
      };

      function normalizeKey(key) {
        return coerceToString(key).toLowerCase();
      }

      function clampFiniteNumber(value, minValue, maxValue, fallback) {
        const numeric = toFiniteNumber(value, fallback);
        return Math.min(maxValue, Math.max(minValue, numeric));
      }

      function getAudioContextCtor() {
        if (typeof window === "undefined") return null;
        return window.AudioContext || window.webkitAudioContext || null;
      }

      function ensureAudioContext() {
        requireBrowserApi("mlRuntime.game.audioInit");
        const AudioContextCtor = getAudioContextCtor();
        if (!AudioContextCtor) {
          return null;
        }

        if (!state.audioContext || state.audioContext.state === "closed") {
          state.audioContext = new AudioContextCtor();
          state.audioMasterGain = state.audioContext.createGain();
          state.audioMasterGain.gain.value = 0.8;
          state.audioMasterGain.connect(state.audioContext.destination);
          state.audioNoiseBuffer = null;
          state.audioSampleCache = new Map();
        }

        return state.audioContext;
      }

      function registerAudioSource(sourceNode, cleanupNodeList, extra) {
        if (!sourceNode || typeof sourceNode.stop !== "function") return;

        const sourceRecord = {
          source: sourceNode,
          cleanupNodeList: Array.isArray(cleanupNodeList) ? cleanupNodeList : [],
          kind: extra && extra.kind ? extra.kind : "voice",
          url: extra && extra.url ? extra.url : null
        };
        state.audioActiveSources.push(sourceRecord);

        const cleanup = () => {
          const index = state.audioActiveSources.indexOf(sourceRecord);
          if (index >= 0) {
            state.audioActiveSources.splice(index, 1);
          }

          for (let i = 0; i < sourceRecord.cleanupNodeList.length; i++) {
            const node = sourceRecord.cleanupNodeList[i];
            if (node && typeof node.disconnect === "function") {
              try {
                node.disconnect();
              } catch (error) {
                // Ignore disconnect race conditions during cleanup.
              }
            }
          }
        };

        sourceNode.onended = cleanup;

        while (state.audioActiveSources.length > state.maxConcurrentAudioSources) {
          const oldest = state.audioActiveSources.shift();
          if (oldest && oldest.source && typeof oldest.source.stop === "function") {
            try {
              oldest.source.stop();
            } catch (error) {
              // Ignore stop errors from already-finished nodes.
            }
          }
        }
      }

      function getFetchFn() {
        if (typeof fetch === "function") return fetch;
        if (typeof window !== "undefined" && typeof window.fetch === "function") {
          return window.fetch.bind(window);
        }
        return null;
      }

      function decodeAudioBuffer(context, bytes) {
        return new Promise((resolve, reject) => {
          let settled = false;
          const ok = (buffer) => {
            if (settled) return;
            settled = true;
            resolve(buffer);
          };
          const fail = (error) => {
            if (settled) return;
            settled = true;
            reject(error || new Error("decodeAudioData failed"));
          };

          try {
            const result = context.decodeAudioData(bytes, ok, fail);
            if (result && typeof result.then === "function") {
              result.then(ok, fail);
            }
          } catch (error) {
            fail(error);
          }
        });
      }

      function startSamplePlayback(context, buffer, url, volume, loop) {
        if (!context || !state.audioMasterGain || !buffer) return;

        const source = context.createBufferSource();
        source.buffer = buffer;
        source.loop = !!loop;
        const gain = context.createGain();
        if (gain.gain && typeof gain.gain.setValueAtTime === "function") {
          gain.gain.setValueAtTime(volume, context.currentTime);
        } else if (gain.gain) {
          gain.gain.value = volume;
        }
        source.connect(gain);
        gain.connect(state.audioMasterGain);
        registerAudioSource(source, [gain, source], { kind: "sample", url });
        source.start(context.currentTime);
      }

      function resolveSamplePlayArgs(volume, options) {
        if (volume && typeof volume === "object" && (options === undefined || options === null)) {
          const safeOptions = volume;
          return {
            volume: clampFiniteNumber(safeOptions.volume, 0, 1, 1),
            loop: !!safeOptions.loop
          };
        }

        const safeOptions = options && typeof options === "object" ? options : {};
        return {
          volume: clampFiniteNumber(volume, 0, 1, 1),
          loop: !!safeOptions.loop
        };
      }

      function enqueueSamplePlay(url, volume, loop) {
        const entry = state.audioSampleCache.get(url);
        if (!entry || !Array.isArray(entry.pending)) return;
        if (entry.pending.length >= 8) return;
        entry.pending.push({ volume, loop });
      }

      function flushPendingSamplePlays(context, url) {
        const entry = state.audioSampleCache.get(url);
        if (!entry || entry.status !== "ready" || !entry.buffer) return;
        const pending = entry.pending.splice(0, entry.pending.length);
        for (let i = 0; i < pending.length; i++) {
          startSamplePlayback(context, entry.buffer, url, pending[i].volume, pending[i].loop);
        }
      }

      function beginSampleDecode(context, url) {
        const fetchFn = getFetchFn();
        const entry = state.audioSampleCache.get(url);
        if (!fetchFn || !entry) {
          if (entry) {
            entry.status = "error";
            entry.pending.length = 0;
          }
          return;
        }

        Promise.resolve()
          .then(() => fetchFn(resolveAssetUrl(url)))
          .then((response) => {
            if (!response || response.ok === false) {
              throw new Error("Sample fetch failed");
            }
            if (typeof response.arrayBuffer !== "function") {
              throw new Error("Sample response is not binary");
            }
            return response.arrayBuffer();
          })
          .then((bytes) => decodeAudioBuffer(context, bytes))
          .then((buffer) => {
            const cached = state.audioSampleCache.get(url);
            if (!cached) return;
            cached.status = "ready";
            cached.buffer = buffer;
            flushPendingSamplePlays(context, url);
          })
          .catch(() => {
            const cached = state.audioSampleCache.get(url);
            if (!cached) return;
            cached.status = "error";
            cached.buffer = null;
            cached.pending.length = 0;
          });
      }

      function scheduleEnvelope(gainNode, startAt, durationSec, peakVolume) {
        const safeDuration = Math.max(0.001, durationSec);
        const attack = Math.min(0.005, safeDuration / 4);
        const release = Math.min(0.03, safeDuration / 3);
        const sustainStart = Math.min(startAt + attack, startAt + safeDuration);
        const sustainEnd = Math.max(sustainStart, startAt + safeDuration - release);
        const endAt = startAt + safeDuration;

        gainNode.gain.cancelScheduledValues(startAt);
        gainNode.gain.setValueAtTime(0, startAt);
        gainNode.gain.linearRampToValueAtTime(peakVolume, sustainStart);
        gainNode.gain.setValueAtTime(peakVolume, sustainEnd);
        gainNode.gain.linearRampToValueAtTime(0, endAt);
        return endAt + 0.01;
      }

      function scheduleToneAt(startAt, freqHz, durationMs, waveType, volume) {
        const context = ensureAudioContext();
        if (!context || !state.audioMasterGain) return null;

        const safeFreq = clampFiniteNumber(freqHz, 20, 20000, 440);
        const safeDurationMs = clampFiniteNumber(durationMs, 1, 10000, 120);
        const durationSec = safeDurationMs / 1000;
        const safeVolume = clampFiniteNumber(volume, 0, 1, 0.25);
        const requestedWave = coerceToString(waveType || "square");
        const safeWave = requestedWave === "sine" || requestedWave === "square" || requestedWave === "triangle" || requestedWave === "sawtooth"
          ? requestedWave
          : "square";

        const oscillator = context.createOscillator();
        const gain = context.createGain();
        oscillator.type = safeWave;
        oscillator.frequency.setValueAtTime(safeFreq, startAt);
        oscillator.connect(gain);
        gain.connect(state.audioMasterGain);
        const stopAt = scheduleEnvelope(gain, startAt, durationSec, safeVolume);
        registerAudioSource(oscillator, [gain, oscillator]);

        oscillator.start(startAt);
        oscillator.stop(stopAt);
        return null;
      }

      function getNoiseBuffer(context) {
        if (!state.audioNoiseBuffer || state.audioNoiseBuffer.sampleRate !== context.sampleRate) {
          const length = Math.max(1, Math.floor(context.sampleRate * 2));
          const buffer = context.createBuffer(1, length, context.sampleRate);
          const data = buffer.getChannelData(0);
          for (let i = 0; i < data.length; i++) {
            data[i] = Math.random() * 2 - 1;
          }
          state.audioNoiseBuffer = buffer;
        }
        return state.audioNoiseBuffer;
      }

      function ensureCanvasContext(apiName) {
        if (!state.canvas || !state.context) {
          throw new Error("mlRuntime.game." + apiName + " requires game.createCanvas(width, height, mountSelector?) to be called first.");
        }
        return state.context;
      }

      function worldX(x) {
        return toFiniteNumber(x, 0) - state.cameraX;
      }

      function worldY(y) {
        return toFiniteNumber(y, 0) - state.cameraY;
      }

      function applyDrawStyle(context) {
        context.globalAlpha = state.alpha;
      }

      function resolveImageHandle(handle) {
        if (!handle || handle.__maldaGameImage !== true) {
          return null;
        }
        return handle;
      }

      function canvasPointFromClient(clientX, clientY) {
        if (!state.canvas) {
          return { x: 0, y: 0 };
        }
        const rect = state.canvas.getBoundingClientRect();
        const displayX = toFiniteNumber(clientX, 0) - rect.left;
        const displayY = toFiniteNumber(clientY, 0) - rect.top;
        const scaleX = rect.width > 0 ? state.canvas.width / rect.width : 1;
        const scaleY = rect.height > 0 ? state.canvas.height / rect.height : 1;
        return {
          x: displayX * scaleX,
          y: displayY * scaleY
        };
      }

      function updateMousePosition(event) {
        if (!state.canvas || !event) return;
        const point = canvasPointFromClient(event.clientX, event.clientY);
        state.mouseX = point.x;
        state.mouseY = point.y;
      }

      function updateMouseFromTouch(touch) {
        if (!state.canvas || !touch) return;
        const point = canvasPointFromClient(touch.clientX, touch.clientY);
        state.mouseX = point.x;
        state.mouseY = point.y;
      }

      function upsertTouch(touch) {
        if (!touch) return;
        const id = coerceToInt(touch.identifier);
        const point = canvasPointFromClient(touch.clientX, touch.clientY);
        state.touches.set(id, { id: id, x: point.x, y: point.y });
      }

      function syncPrimaryTouchMouse(touchList) {
        const primary = touchList && touchList.length > 0 ? touchList[0] : null;
        if (primary) {
          updateMouseFromTouch(primary);
          state.mouseButtonsDown.add(0);
        } else {
          state.mouseButtonsDown.delete(0);
        }
      }

      function resetKeyboardAndPointerState() {
        state.keysDown.clear();
        state.pendingKeyPressed.clear();
        state.pendingKeyReleased.clear();
        state.keysPressed.clear();
        state.keysReleased.clear();
        state.mouseButtonsDown.clear();
        state.mouseX = 0;
        state.mouseY = 0;
        state.touches.clear();
        state.gamepadButtonsDown.clear();
        state.gamepadButtonsPrev.clear();
        state.gamepadButtonsPressed.clear();
        state.gamepadConnected.clear();
        state.gamepadAxes = {};
        state.inputFrameActive = false;
      }

      function gamepadButtonKey(padIndex, buttonIndex) {
        return String(padIndex) + ":" + String(buttonIndex);
      }

      function gamepadAxisKey(padIndex, axisIndex) {
        return String(padIndex) + ":" + String(axisIndex);
      }

      function pollGamepads() {
        state.gamepadConnected.clear();
        state.gamepadButtonsDown.clear();
        state.gamepadAxes = {};
        let pads = null;
        try {
          if (typeof navigator !== "undefined" && typeof navigator.getGamepads === "function") {
            pads = navigator.getGamepads();
          }
        } catch (_error) {
          pads = null;
        }
        if (!pads) {
          return;
        }

        const count = typeof pads.length === "number" ? pads.length : 0;
        for (let i = 0; i < count; i++) {
          const pad = pads[i];
          if (!pad) {
            continue;
          }
          state.gamepadConnected.add(i);
          const axes = pad.axes || [];
          for (let a = 0; a < axes.length; a++) {
            state.gamepadAxes[gamepadAxisKey(i, a)] = clampFiniteNumber(axes[a], -1, 1, 0);
          }
          const buttons = pad.buttons || [];
          for (let b = 0; b < buttons.length; b++) {
            const button = buttons[b];
            const pressed = button && typeof button === "object" ? !!button.pressed : !!button;
            if (pressed) {
              state.gamepadButtonsDown.add(gamepadButtonKey(i, b));
            }
          }
        }
      }

      function ensureGamepadSnapshot() {
        if (!state.inputFrameActive) {
          pollGamepads();
        }
      }

      function beginInputFrame() {
        state.keysPressed = state.pendingKeyPressed;
        state.keysReleased = state.pendingKeyReleased;
        state.pendingKeyPressed = new Set();
        state.pendingKeyReleased = new Set();

        pollGamepads();
        state.gamepadButtonsPressed.clear();
        for (const key of state.gamepadButtonsDown) {
          if (!state.gamepadButtonsPrev.has(key)) {
            state.gamepadButtonsPressed.add(key);
          }
        }
        state.gamepadButtonsPrev = new Set(state.gamepadButtonsDown);
        state.inputFrameActive = true;
      }

      function endInputFrame() {
        state.keysPressed = new Set();
        state.keysReleased = new Set();
        state.gamepadButtonsPressed.clear();
        state.inputFrameActive = false;
      }

      function attachInputListeners() {
        if (state.listenersAttached || !state.canvas) return;

        const onKeyDown = (event) => {
          const key = normalizeKey(event && event.key);
          if (!state.keysDown.has(key)) {
            state.pendingKeyPressed.add(key);
            state.pendingKeyReleased.delete(key);
          }
          state.keysDown.add(key);
        };
        const onKeyUp = (event) => {
          const key = normalizeKey(event && event.key);
          if (state.keysDown.has(key)) {
            state.pendingKeyReleased.add(key);
          }
          state.keysDown.delete(key);
        };
        const onWindowBlur = () => {
          resetKeyboardAndPointerState();
        };
        const onMouseMove = (event) => {
          updateMousePosition(event);
        };
        const onMouseDown = (event) => {
          updateMousePosition(event);
          state.mouseButtonsDown.add(coerceToInt(event.button));
        };
        const onMouseUp = (event) => {
          state.mouseButtonsDown.delete(coerceToInt(event.button));
        };

        const onTouchStart = (event) => {
          if (event && event.cancelable) event.preventDefault();
          const changed = (event && event.changedTouches) || [];
          for (let i = 0; i < changed.length; i++) {
            upsertTouch(changed[i]);
          }
          const active = (event && event.touches) || changed;
          for (let i = 0; i < active.length; i++) {
            upsertTouch(active[i]);
          }
          syncPrimaryTouchMouse(active);
        };
        const onTouchMove = (event) => {
          if (event && event.cancelable) event.preventDefault();
          const active = (event && event.touches) || [];
          for (let i = 0; i < active.length; i++) {
            upsertTouch(active[i]);
          }
          syncPrimaryTouchMouse(active);
        };
        const onTouchEnd = (event) => {
          const changed = (event && event.changedTouches) || [];
          for (let i = 0; i < changed.length; i++) {
            state.touches.delete(coerceToInt(changed[i] && changed[i].identifier));
          }
          const active = (event && event.touches) || [];
          if (active.length === 0) {
            state.touches.clear();
          }
          syncPrimaryTouchMouse(active);
        };
        const onTouchCancel = (event) => {
          const changed = (event && event.changedTouches) || [];
          for (let i = 0; i < changed.length; i++) {
            state.touches.delete(coerceToInt(changed[i] && changed[i].identifier));
          }
          const active = (event && event.touches) || [];
          if (active.length === 0) {
            state.touches.clear();
          }
          syncPrimaryTouchMouse(active);
        };

        window.addEventListener("keydown", onKeyDown);
        window.addEventListener("keyup", onKeyUp);
        window.addEventListener("blur", onWindowBlur);
        window.addEventListener("mousemove", onMouseMove);
        window.addEventListener("mousedown", onMouseDown);
        window.addEventListener("mouseup", onMouseUp);
        window.addEventListener("touchstart", onTouchStart, { passive: false });
        window.addEventListener("touchmove", onTouchMove, { passive: false });
        window.addEventListener("touchend", onTouchEnd, { passive: true });
        window.addEventListener("touchcancel", onTouchCancel, { passive: true });

        state.listeners = {
          onKeyDown,
          onKeyUp,
          onWindowBlur,
          onMouseMove,
          onMouseDown,
          onMouseUp,
          onTouchStart,
          onTouchMove,
          onTouchEnd,
          onTouchCancel
        };
        state.listenersAttached = true;
      }

      function detachInputListeners() {
        if (!state.listenersAttached || !state.listeners || !state.canvas) return;

        window.removeEventListener("keydown", state.listeners.onKeyDown);
        window.removeEventListener("keyup", state.listeners.onKeyUp);
        window.removeEventListener("blur", state.listeners.onWindowBlur);
        window.removeEventListener("mousemove", state.listeners.onMouseMove);
        window.removeEventListener("mousedown", state.listeners.onMouseDown);
        window.removeEventListener("mouseup", state.listeners.onMouseUp);
        window.removeEventListener("touchstart", state.listeners.onTouchStart);
        window.removeEventListener("touchmove", state.listeners.onTouchMove);
        window.removeEventListener("touchend", state.listeners.onTouchEnd);
        window.removeEventListener("touchcancel", state.listeners.onTouchCancel);

        state.listeners = null;
        state.listenersAttached = false;
      }

      function createCanvas(width, height, mountSelector) {
        requireBrowserApi("mlRuntime.game.createCanvas");
        if (state.running) {
          throw new Error("mlRuntime.game.createCanvas cannot be called while the game loop is running. Call game.stop() first.");
        }

        const canvasWidth = Math.max(1, coerceToInt(width));
        const canvasHeight = Math.max(1, coerceToInt(height));

        let mount = document.body;
        if (mountSelector !== null && mountSelector !== undefined && coerceToString(mountSelector) !== "") {
          mount = document.querySelector(coerceToString(mountSelector));
          if (!mount) {
            throw new Error("mlRuntime.game.createCanvas could not find mount target: " + coerceToString(mountSelector));
          }
        }

        detachInputListeners();
        if (state.canvas && state.canvas.parentNode) {
          state.canvas.parentNode.removeChild(state.canvas);
        }

        const canvas = document.createElement("canvas");
        canvas.width = canvasWidth;
        canvas.height = canvasHeight;
        canvas.style.touchAction = "none";
        canvas.style.display = "block";

        const context = canvas.getContext("2d");
        if (!context) {
          throw new Error("mlRuntime.game.createCanvas failed to create a CanvasRenderingContext2D.");
        }

        mount.appendChild(canvas);
        state.canvas = canvas;
        state.context = context;
        state.pixelBuffer = null;
        state.lastTimestamp = null;
        resetKeyboardAndPointerState();
        state.cameraX = 0;
        state.cameraY = 0;
        state.alpha = 1;
        context.globalAlpha = 1;
        attachInputListeners();
        return null;
      }

      function setBackground(color) {
        ensureCanvasContext("setBackground");
        state.backgroundColor = coerceToString(color || "#000000");
        return null;
      }

      function clear() {
        const context = ensureCanvasContext("clear");
        if (!state.canvas) return null;
        const previousAlpha = context.globalAlpha;
        context.globalAlpha = 1;
        if (state.backgroundColor === null || state.backgroundColor === undefined) {
          context.clearRect(0, 0, state.canvas.width, state.canvas.height);
        } else {
          context.fillStyle = coerceToString(state.backgroundColor);
          context.fillRect(0, 0, state.canvas.width, state.canvas.height);
        }
        context.globalAlpha = previousAlpha;
        return null;
      }

      function fillRect(x, y, width, height, color) {
        const context = ensureCanvasContext("fillRect");
        applyDrawStyle(context);
        context.fillStyle = coerceToString(color || "#ffffff");
        context.fillRect(
          worldX(x),
          worldY(y),
          Math.max(0, toFiniteNumber(width, 0)),
          Math.max(0, toFiniteNumber(height, 0))
        );
        return null;
      }

      function fillCircle(x, y, radius, color) {
        const context = ensureCanvasContext("fillCircle");
        applyDrawStyle(context);
        context.fillStyle = coerceToString(color || "#ffffff");
        context.beginPath();
        context.arc(
          worldX(x),
          worldY(y),
          Math.max(0, toFiniteNumber(radius, 0)),
          0,
          Math.PI * 2
        );
        context.fill();
        return null;
      }

      function drawText(text, x, y, color, font) {
        const context = ensureCanvasContext("drawText");
        applyDrawStyle(context);
        context.fillStyle = coerceToString(color || "#ffffff");
        context.font = coerceToString(font || "16px sans-serif");
        context.fillText(coerceToString(text), worldX(x), worldY(y));
        return null;
      }

      function setCamera(x, y) {
        ensureCanvasContext("setCamera");
        state.cameraX = toFiniteNumber(x, 0);
        state.cameraY = toFiniteNumber(y, 0);
        return null;
      }

      function getCameraX() {
        ensureCanvasContext("getCameraX");
        return state.cameraX;
      }

      function getCameraY() {
        ensureCanvasContext("getCameraY");
        return state.cameraY;
      }

      function setAlpha(alpha) {
        const context = ensureCanvasContext("setAlpha");
        state.alpha = clampFiniteNumber(alpha, 0, 1, 1);
        context.globalAlpha = state.alpha;
        return null;
      }

      function drawLine(x1, y1, x2, y2, color, width) {
        const context = ensureCanvasContext("drawLine");
        applyDrawStyle(context);
        context.strokeStyle = coerceToString(color || "#ffffff");
        context.lineWidth = Math.max(0, toFiniteNumber(width, 1));
        context.beginPath();
        context.moveTo(worldX(x1), worldY(y1));
        context.lineTo(worldX(x2), worldY(y2));
        context.stroke();
        return null;
      }

      function strokeRect(x, y, width, height, color, lineWidth) {
        const context = ensureCanvasContext("strokeRect");
        applyDrawStyle(context);
        context.strokeStyle = coerceToString(color || "#ffffff");
        context.lineWidth = Math.max(0, toFiniteNumber(lineWidth, 1));
        context.strokeRect(
          worldX(x),
          worldY(y),
          Math.max(0, toFiniteNumber(width, 0)),
          Math.max(0, toFiniteNumber(height, 0))
        );
        return null;
      }

      function loadImage(url) {
        requireBrowserApi("mlRuntime.game.loadImage");
        const source = coerceToString(url);
        if (source === "") {
          return {
            __maldaGameImage: true,
            url: "",
            ready: false,
            image: null,
            width: 0,
            height: 0
          };
        }

        const cached = state.imageCache.get(source);
        if (cached) {
          return cached;
        }

        const handle = {
          __maldaGameImage: true,
          url: source,
          ready: false,
          image: null,
          width: 0,
          height: 0
        };
        state.imageCache.set(source, handle);

        const ImageCtor = typeof global.Image === "function" ? global.Image : null;
        if (!ImageCtor) {
          return handle;
        }

        try {
          const img = new ImageCtor();
          img.onload = function () {
            handle.image = img;
            handle.width = img.naturalWidth || img.width || 0;
            handle.height = img.naturalHeight || img.height || 0;
            handle.ready = handle.width > 0 && handle.height > 0;
          };
          img.onerror = function () {
            handle.ready = false;
            handle.image = null;
          };
          img.src = resolveAssetUrl(source);
        } catch (_error) {
          handle.ready = false;
          handle.image = null;
        }

        return handle;
      }

      function imageIsReady(handle) {
        const record = resolveImageHandle(handle);
        return !!(record && record.ready && record.image);
      }

      function drawImage(handle, x, y, width, height) {
        const context = ensureCanvasContext("drawImage");
        const record = resolveImageHandle(handle);
        if (!record || !record.ready || !record.image) {
          return null;
        }

        const destWidth = width === undefined || width === null
          ? record.width
          : Math.max(0, toFiniteNumber(width, 0));
        const destHeight = height === undefined || height === null
          ? record.height
          : Math.max(0, toFiniteNumber(height, 0));
        if (destWidth <= 0 || destHeight <= 0) {
          return null;
        }

        applyDrawStyle(context);
        try {
          context.drawImage(record.image, worldX(x), worldY(y), destWidth, destHeight);
        } catch (_error) {
          // Decode races and detached bitmaps are ignored on the hot path.
        }
        return null;
      }

      function drawImageRect(handle, sx, sy, sw, sh, dx, dy, dw, dh) {
        const context = ensureCanvasContext("drawImageRect");
        const record = resolveImageHandle(handle);
        if (!record || !record.ready || !record.image) {
          return null;
        }

        const sourceWidth = Math.max(0, toFiniteNumber(sw, 0));
        const sourceHeight = Math.max(0, toFiniteNumber(sh, 0));
        if (sourceWidth <= 0 || sourceHeight <= 0) {
          return null;
        }

        const destWidth = dw === undefined || dw === null
          ? sourceWidth
          : Math.max(0, toFiniteNumber(dw, 0));
        const destHeight = dh === undefined || dh === null
          ? sourceHeight
          : Math.max(0, toFiniteNumber(dh, 0));
        if (destWidth <= 0 || destHeight <= 0) {
          return null;
        }

        applyDrawStyle(context);
        try {
          context.drawImage(
            record.image,
            toFiniteNumber(sx, 0),
            toFiniteNumber(sy, 0),
            sourceWidth,
            sourceHeight,
            worldX(dx),
            worldY(dy),
            destWidth,
            destHeight
          );
        } catch (_error) {
          // Decode races and detached bitmaps are ignored on the hot path.
        }
        return null;
      }

      function clampByte(value) {
        const numberValue = toFiniteNumber(value, 0);
        if (numberValue <= 0) return 0;
        if (numberValue >= 255) return 255;
        return (numberValue + 0.5) | 0;
      }

      function packedPixelLength(pixels) {
        if (pixels == null) return 0;
        if (typeof pixels.length === "number") return pixels.length;
        return 0;
      }

      function allocateImageData(width, height) {
        const safeWidth = Math.max(1, coerceToInt(width));
        const safeHeight = Math.max(1, coerceToInt(height));
        if (typeof ImageData === "function") {
          try {
            return new ImageData(safeWidth, safeHeight);
          } catch (_error) {
            // Fall through to createImageData / plain buffer.
          }
        }
        if (state.context && typeof state.context.createImageData === "function") {
          return state.context.createImageData(safeWidth, safeHeight);
        }
        return {
          width: safeWidth,
          height: safeHeight,
          data: new Uint8ClampedArray(safeWidth * safeHeight * 4)
        };
      }

      function fillPixelBufferData(buffer, r, g, b, a) {
        const data = buffer.data;
        const red = clampByte(r);
        const green = clampByte(g);
        const blue = clampByte(b);
        const alpha = clampByte(a);
        for (let i = 0; i < data.length; i += 4) {
          data[i] = red;
          data[i + 1] = green;
          data[i + 2] = blue;
          data[i + 3] = alpha;
        }
      }

      function ensurePixelBuffer(apiName) {
        ensureCanvasContext(apiName);
        if (!state.pixelBuffer) {
          state.pixelBuffer = allocateImageData(state.canvas.width, state.canvas.height);
          fillPixelBufferData(state.pixelBuffer, 0, 0, 0, 255);
        }
        return state.pixelBuffer;
      }

      function copyPackedPixels(buffer, pixels) {
        const width = buffer.width;
        const height = buffer.height;
        const expectedRgb = width * height * 3;
        const expectedRgba = width * height * 4;
        const length = packedPixelLength(pixels);
        if (length !== expectedRgb && length !== expectedRgba) {
          throw new Error(
            "mlRuntime.game.blitPixels expected a packed RGB array of length " +
            expectedRgb + " or RGBA array of length " + expectedRgba +
            " (canvas " + width + "x" + height + "), got length " + length + "."
          );
        }

        const data = buffer.data;
        if (length === expectedRgba) {
          for (let i = 0; i < expectedRgba; i++) {
            data[i] = clampByte(pixels[i]);
          }
          return;
        }

        let source = 0;
        for (let i = 0; i < expectedRgba; i += 4) {
          data[i] = clampByte(pixels[source]);
          data[i + 1] = clampByte(pixels[source + 1]);
          data[i + 2] = clampByte(pixels[source + 2]);
          data[i + 3] = 255;
          source += 3;
        }
      }

      function createPixelBuffer(width, height) {
        ensureCanvasContext("createPixelBuffer");
        const bufferWidth = width === null || width === undefined
          ? state.canvas.width
          : Math.max(1, coerceToInt(width));
        const bufferHeight = height === null || height === undefined
          ? state.canvas.height
          : Math.max(1, coerceToInt(height));
        state.pixelBuffer = allocateImageData(bufferWidth, bufferHeight);
        fillPixelBufferData(state.pixelBuffer, 0, 0, 0, 255);
        return { width: bufferWidth, height: bufferHeight };
      }

      function setPixel(x, y, r, g, b, a) {
        const buffer = ensurePixelBuffer("setPixel");
        const px = coerceToInt(x);
        const py = coerceToInt(y);
        if (px < 0 || py < 0 || px >= buffer.width || py >= buffer.height) {
          return null;
        }
        const offset = (py * buffer.width + px) * 4;
        buffer.data[offset] = clampByte(r);
        buffer.data[offset + 1] = clampByte(g);
        buffer.data[offset + 2] = clampByte(b);
        buffer.data[offset + 3] = a === null || a === undefined ? 255 : clampByte(a);
        return null;
      }

      function blitPixels(pixels, destX, destY) {
        const context = ensureCanvasContext("blitPixels");
        const buffer = ensurePixelBuffer("blitPixels");
        if (pixels !== null && pixels !== undefined) {
          copyPackedPixels(buffer, pixels);
        }
        const x = destX === null || destX === undefined ? 0 : coerceToInt(destX);
        const y = destY === null || destY === undefined ? 0 : coerceToInt(destY);
        context.putImageData(buffer, x, y);
        return null;
      }

      function overlapRect(x1, y1, w1, h1, x2, y2, w2, h2) {
        const ax = toFiniteNumber(x1, 0);
        const ay = toFiniteNumber(y1, 0);
        const aw = toFiniteNumber(w1, 0);
        const ah = toFiniteNumber(h1, 0);
        const bx = toFiniteNumber(x2, 0);
        const by = toFiniteNumber(y2, 0);
        const bw = toFiniteNumber(w2, 0);
        const bh = toFiniteNumber(h2, 0);
        if (aw <= 0 || ah <= 0 || bw <= 0 || bh <= 0) {
          return false;
        }
        return ax <= bx + bw && bx <= ax + aw && ay <= by + bh && by <= ay + ah;
      }

      function overlapCircle(x1, y1, r1, x2, y2, r2) {
        const ax = toFiniteNumber(x1, 0);
        const ay = toFiniteNumber(y1, 0);
        const ar = toFiniteNumber(r1, 0);
        const bx = toFiniteNumber(x2, 0);
        const by = toFiniteNumber(y2, 0);
        const br = toFiniteNumber(r2, 0);
        if (ar <= 0 || br <= 0) {
          return false;
        }
        const dx = bx - ax;
        const dy = by - ay;
        const limit = ar + br;
        return (dx * dx + dy * dy) <= (limit * limit);
      }

      function pointInRect(px, py, x, y, w, h) {
        const pointX = toFiniteNumber(px, 0);
        const pointY = toFiniteNumber(py, 0);
        const rectX = toFiniteNumber(x, 0);
        const rectY = toFiniteNumber(y, 0);
        const rectW = toFiniteNumber(w, 0);
        const rectH = toFiniteNumber(h, 0);
        if (rectW <= 0 || rectH <= 0) {
          return false;
        }
        return pointX >= rectX && pointX <= rectX + rectW && pointY >= rectY && pointY <= rectY + rectH;
      }

      function pointInCircle(px, py, x, y, r) {
        const pointX = toFiniteNumber(px, 0);
        const pointY = toFiniteNumber(py, 0);
        const centerX = toFiniteNumber(x, 0);
        const centerY = toFiniteNumber(y, 0);
        const radius = toFiniteNumber(r, 0);
        if (radius <= 0) {
          return false;
        }
        const dx = pointX - centerX;
        const dy = pointY - centerY;
        return (dx * dx + dy * dy) <= (radius * radius);
      }

      function sweepHit(hit, t, nx, ny, x, y) {
        return { hit: hit, t: t, nx: nx, ny: ny, x: x, y: y };
      }

      function sweepAxisTimes(pos, size, vel, otherPos, otherSize) {
        if (vel === 0) {
          const overlapping = pos < otherPos + otherSize && otherPos < pos + size;
          if (!overlapping) {
            return { enter: Number.POSITIVE_INFINITY, exit: Number.NEGATIVE_INFINITY };
          }
          return { enter: Number.NEGATIVE_INFINITY, exit: Number.POSITIVE_INFINITY };
        }
        let enterDist;
        let exitDist;
        if (vel > 0) {
          enterDist = otherPos - (pos + size);
          exitDist = (otherPos + otherSize) - pos;
        } else {
          enterDist = (otherPos + otherSize) - pos;
          exitDist = otherPos - (pos + size);
        }
        return { enter: enterDist / vel, exit: exitDist / vel };
      }

      function sweepRect(x, y, w, h, dx, dy, ox, oy, ow, oh) {
        const ax = toFiniteNumber(x, 0);
        const ay = toFiniteNumber(y, 0);
        const aw = toFiniteNumber(w, 0);
        const ah = toFiniteNumber(h, 0);
        const adx = toFiniteNumber(dx, 0);
        const ady = toFiniteNumber(dy, 0);
        const bx = toFiniteNumber(ox, 0);
        const by = toFiniteNumber(oy, 0);
        const bw = toFiniteNumber(ow, 0);
        const bh = toFiniteNumber(oh, 0);
        const endX = ax + adx;
        const endY = ay + ady;

        if (aw <= 0 || ah <= 0 || bw <= 0 || bh <= 0) {
          return sweepHit(false, 1, 0, 0, endX, endY);
        }

        const overlapX = Math.min(ax + aw, bx + bw) - Math.max(ax, bx);
        const overlapY = Math.min(ay + ah, by + bh) - Math.max(ay, by);
        if (overlapX > 0 && overlapY > 0) {
          let nx = 0;
          let ny = 0;
          if (overlapX < overlapY) {
            nx = (ax + aw / 2) < (bx + bw / 2) ? -1 : 1;
          } else {
            ny = (ay + ah / 2) < (by + bh / 2) ? -1 : 1;
          }
          return sweepHit(true, 0, nx, ny, ax, ay);
        }

        if (adx === 0 && ady === 0) {
          return sweepHit(false, 1, 0, 0, ax, ay);
        }

        const xTimes = sweepAxisTimes(ax, aw, adx, bx, bw);
        const yTimes = sweepAxisTimes(ay, ah, ady, by, bh);
        const tEnter = Math.max(xTimes.enter, yTimes.enter);
        const tExit = Math.min(xTimes.exit, yTimes.exit);
        if (!(tEnter < tExit) || tEnter > 1 || tEnter < 0) {
          return sweepHit(false, 1, 0, 0, endX, endY);
        }

        let nx = 0;
        let ny = 0;
        if (yTimes.enter > xTimes.enter) {
          ny = ady > 0 ? -1 : 1;
        } else if (xTimes.enter > yTimes.enter) {
          nx = adx > 0 ? -1 : 1;
        } else if (ady !== 0) {
          ny = ady > 0 ? -1 : 1;
        } else {
          nx = adx > 0 ? -1 : 1;
        }

        return sweepHit(true, tEnter, nx, ny, ax + adx * tEnter, ay + ady * tEnter);
      }

      function isKeyDown(key) {
        return state.keysDown.has(normalizeKey(key));
      }

      function wasKeyPressed(key) {
        return state.keysPressed.has(normalizeKey(key));
      }

      function wasKeyReleased(key) {
        return state.keysReleased.has(normalizeKey(key));
      }

      function getMouseX() {
        return state.mouseX;
      }

      function getMouseY() {
        return state.mouseY;
      }

      function isMouseDown(button) {
        const mouseButton = button === null || button === undefined ? 0 : coerceToInt(button);
        return state.mouseButtonsDown.has(mouseButton);
      }

      function getTouches() {
        const result = [];
        state.touches.forEach(function (touch) {
          result.push({ id: touch.id, x: touch.x, y: touch.y });
        });
        return result;
      }

      function isGamepadConnected(index) {
        ensureGamepadSnapshot();
        const padIndex = index === null || index === undefined ? 0 : coerceToInt(index);
        return state.gamepadConnected.has(padIndex);
      }

      function getGamepadAxis(index, axis) {
        ensureGamepadSnapshot();
        const padIndex = index === null || index === undefined ? 0 : coerceToInt(index);
        const axisIndex = coerceToInt(axis);
        const value = state.gamepadAxes[gamepadAxisKey(padIndex, axisIndex)];
        return typeof value === "number" ? value : 0;
      }

      function isGamepadButtonDown(index, button) {
        ensureGamepadSnapshot();
        const padIndex = index === null || index === undefined ? 0 : coerceToInt(index);
        const buttonIndex = coerceToInt(button);
        return state.gamepadButtonsDown.has(gamepadButtonKey(padIndex, buttonIndex));
      }

      function wasGamepadButtonPressed(index, button) {
        const padIndex = index === null || index === undefined ? 0 : coerceToInt(index);
        const buttonIndex = coerceToInt(button);
        return state.gamepadButtonsPressed.has(gamepadButtonKey(padIndex, buttonIndex));
      }

      function audioInit() {
        const context = ensureAudioContext();
        if (!context) return null;
        if (context.state === "suspended" && typeof context.resume === "function") {
          context.resume().catch(() => null);
        }
        return null;
      }

      function audioIsReady() {
        return !!(state.audioContext && state.audioContext.state !== "closed");
      }

      function audioSetMasterVolume(volume) {
        const context = ensureAudioContext();
        if (!context || !state.audioMasterGain) return null;
        const safeVolume = clampFiniteNumber(volume, 0, 1, 0.8);
        state.audioMasterGain.gain.setValueAtTime(safeVolume, context.currentTime);
        return null;
      }

      function audioPlayTone(freqHz, durationMs, waveType, volume) {
        const context = ensureAudioContext();
        if (!context) return null;
        if (context.state === "suspended" && typeof context.resume === "function") {
          context.resume().catch(() => null);
        }
        scheduleToneAt(context.currentTime, freqHz, durationMs, waveType, volume);
        return null;
      }

      function audioPlayNoise(durationMs, volume) {
        const context = ensureAudioContext();
        if (!context || !state.audioMasterGain) return null;
        if (context.state === "suspended" && typeof context.resume === "function") {
          context.resume().catch(() => null);
        }

        const safeDurationMs = clampFiniteNumber(durationMs, 1, 10000, 120);
        const durationSec = safeDurationMs / 1000;
        const safeVolume = clampFiniteNumber(volume, 0, 1, 0.2);
        const noise = context.createBufferSource();
        noise.buffer = getNoiseBuffer(context);
        const gain = context.createGain();
        noise.connect(gain);
        gain.connect(state.audioMasterGain);
        const now = context.currentTime;
        const stopAt = scheduleEnvelope(gain, now, durationSec, safeVolume);
        registerAudioSource(noise, [gain, noise]);
        noise.start(now);
        noise.stop(stopAt);
        return null;
      }

      function audioPlaySample(url, volume, options) {
        const safeUrl = coerceToString(url || "");
        if (!safeUrl) return null;

        const context = ensureAudioContext();
        if (!context || !state.audioMasterGain) return null;
        if (context.state === "suspended" && typeof context.resume === "function") {
          context.resume().catch(() => null);
        }

        const playArgs = resolveSamplePlayArgs(volume, options);
        let entry = state.audioSampleCache.get(safeUrl);
        if (entry && entry.status === "error") {
          state.audioSampleCache.delete(safeUrl);
          entry = null;
        }

        if (entry && entry.status === "ready") {
          startSamplePlayback(context, entry.buffer, safeUrl, playArgs.volume, playArgs.loop);
          return null;
        }

        if (entry && entry.status === "loading") {
          enqueueSamplePlay(safeUrl, playArgs.volume, playArgs.loop);
          return null;
        }

        state.audioSampleCache.set(safeUrl, {
          status: "loading",
          buffer: null,
          pending: [{ volume: playArgs.volume, loop: playArgs.loop }]
        });
        beginSampleDecode(context, safeUrl);
        return null;
      }

      function audioStopSample(url) {
        const hasUrl = !(url === undefined || url === null || coerceToString(url) === "");
        const safeUrl = hasUrl ? coerceToString(url) : "";

        if (!hasUrl) {
          for (const entry of state.audioSampleCache.values()) {
            if (entry && Array.isArray(entry.pending)) {
              entry.pending.length = 0;
            }
          }
        } else {
          const entry = state.audioSampleCache.get(safeUrl);
          if (entry && Array.isArray(entry.pending)) {
            entry.pending.length = 0;
          }
        }

        const currentSources = state.audioActiveSources.slice();
        for (let i = 0; i < currentSources.length; i++) {
          const record = currentSources[i];
          if (!record || record.kind !== "sample") continue;
          if (hasUrl && record.url !== safeUrl) continue;
          if (record.source && typeof record.source.stop === "function") {
            try {
              record.source.stop();
            } catch (error) {
              // Ignore stop errors from already-finished nodes.
            }
          }
        }

        return null;
      }

      function audioStopPattern() {
        if (state.audioPatternTimer !== null) {
          clearInterval(state.audioPatternTimer);
          state.audioPatternTimer = null;
        }
        state.audioPatternState = null;
        return null;
      }

      function audioStopAll() {
        audioStopPattern();
        audioStopTrack();
        const currentSources = state.audioActiveSources.slice();
        for (let i = 0; i < currentSources.length; i++) {
          const source = currentSources[i].source;
          if (source && typeof source.stop === "function") {
            try {
              source.stop();
            } catch (error) {
              // Ignore stop errors from already-finished nodes.
            }
          }
        }
        state.audioActiveSources.length = 0;
        return null;
      }

      function clearMusicTrackElement() {
        if (!state.musicTrackAudio) return;
        try {
          state.musicTrackAudio.pause();
        } catch (error) {
          // Ignore pause errors.
        }
        state.musicTrackAudio.src = "";
        state.musicTrackAudio.load();
        state.musicTrackAudio = null;
        state.musicTrackReady = false;
        state.musicTrackPlaying = false;
      }

      function audioLoadTrack(source, options) {
        requireBrowserApi("mlRuntime.game.audioLoadTrack");
        const safeSource = coerceToString(source || "");
        const safeOptions = options && typeof options === "object" ? options : {};
        if (!safeSource) {
          state.musicTrackError = "Track source is required.";
          return null;
        }

        clearMusicTrackElement();
        state.musicTrackSource = safeSource;
        state.musicTrackError = null;

        try {
          const track = new Audio(safeSource);
          track.preload = "auto";
          state.musicTrackLoop = safeOptions.loop === undefined ? true : !!safeOptions.loop;
          state.musicTrackVolume = clampFiniteNumber(safeOptions.volume, 0, 1, state.musicTrackVolume);
          track.loop = state.musicTrackLoop;
          track.volume = state.musicTrackVolume;

          track.addEventListener("canplay", () => {
            state.musicTrackReady = true;
          });
          track.addEventListener("playing", () => {
            state.musicTrackPlaying = true;
          });
          track.addEventListener("pause", () => {
            state.musicTrackPlaying = false;
          });
          track.addEventListener("ended", () => {
            state.musicTrackPlaying = false;
          });
          track.addEventListener("error", () => {
            state.musicTrackError = "Failed to load music track.";
            state.musicTrackReady = false;
            state.musicTrackPlaying = false;
          });

          state.musicTrackAudio = track;
          if (safeOptions.autoplay) {
            audioPlayTrack();
          }
        } catch (error) {
          state.musicTrackError = error && typeof error.message === "string"
            ? error.message
            : "Failed to initialize music track.";
          return null;
        }
        return null;
      }

      function audioPlayTrack() {
        if (!state.musicTrackAudio) {
          state.musicTrackError = "No music track loaded.";
          return null;
        }
        state.musicTrackAudio.loop = state.musicTrackLoop;
        state.musicTrackAudio.volume = state.musicTrackVolume;
        const playPromise = state.musicTrackAudio.play();
        if (playPromise && typeof playPromise.then === "function") {
          playPromise
            .then(() => {
              state.musicTrackError = null;
              state.musicTrackPlaying = true;
            })
            .catch((error) => {
              state.musicTrackError = error && typeof error.message === "string"
                ? error.message
                : "Failed to play music track.";
            });
        }
        return null;
      }

      function audioStopTrack() {
        if (!state.musicTrackAudio) return null;
        try {
          state.musicTrackAudio.pause();
          state.musicTrackAudio.currentTime = 0;
          state.musicTrackPlaying = false;
        } catch (error) {
          state.musicTrackError = error && typeof error.message === "string"
            ? error.message
            : "Failed to stop music track.";
        }
        return null;
      }

      function audioSetTrackOptions(options) {
        const safeOptions = options && typeof options === "object" ? options : {};
        if (safeOptions.volume !== undefined) {
          state.musicTrackVolume = clampFiniteNumber(safeOptions.volume, 0, 1, state.musicTrackVolume);
        }
        if (safeOptions.loop !== undefined) {
          state.musicTrackLoop = !!safeOptions.loop;
        }
        if (state.musicTrackAudio) {
          state.musicTrackAudio.volume = state.musicTrackVolume;
          state.musicTrackAudio.loop = state.musicTrackLoop;
        }
        return null;
      }

      function audioTrackIsReady() {
        if (!state.musicTrackAudio) return false;
        return state.musicTrackReady || state.musicTrackAudio.readyState >= 2;
      }

      function audioGetTrackInfo() {
        return {
          ready: audioTrackIsReady(),
          source: state.musicTrackSource,
          playing: !!state.musicTrackPlaying,
          loop: !!state.musicTrackLoop,
          volume: state.musicTrackVolume,
          backendError: state.musicTrackError
        };
      }

      function normalizePatternEvents(pattern) {
        if (!pattern || typeof pattern !== "object") return null;
        const tracks = Array.isArray(pattern.tracks) ? pattern.tracks : [];
        const tempoBpm = clampFiniteNumber(pattern.tempoBpm, 30, 300, 120);
        const loop = !!pattern.loop;
        const events = [];
        let maxBeat = 0;

        for (let trackIndex = 0; trackIndex < tracks.length; trackIndex++) {
          const track = tracks[trackIndex];
          if (!Array.isArray(track)) continue;
          for (let eventIndex = 0; eventIndex < track.length; eventIndex++) {
            const event = track[eventIndex];
            if (!event || typeof event !== "object") continue;
            const atBeats = clampFiniteNumber(event.atBeats, 0, 100000, 0);
            const durBeats = clampFiniteNumber(event.durBeats, 0.01, 64, 0.25);
            const noteHz = clampFiniteNumber(event.noteHz, 20, 20000, 440);
            const waveType = coerceToString(event.waveType || "square");
            const volume = clampFiniteNumber(event.volume, 0, 1, 0.25);
            maxBeat = Math.max(maxBeat, atBeats + durBeats);
            events.push({ atBeats, durBeats, noteHz, waveType, volume });
          }
        }

        events.sort((a, b) => a.atBeats - b.atBeats);
        return {
          tempoBpm,
          loop,
          events,
          loopBeats: Math.max(maxBeat, 0.25)
        };
      }

      function audioPlayPattern(pattern) {
        const context = ensureAudioContext();
        if (!context) return null;
        if (context.state === "suspended" && typeof context.resume === "function") {
          context.resume().catch(() => null);
        }

        const normalized = normalizePatternEvents(pattern);
        if (!normalized || normalized.events.length === 0) {
          return null;
        }

        audioStopPattern();

        state.audioPatternState = {
          normalized,
          startTime: context.currentTime + 0.02,
          cycleStartTime: context.currentTime + 0.02,
          nextEventIndex: 0
        };

        const scheduleAheadSec = 0.12;
        const tickMs = 25;
        state.audioPatternTimer = setInterval(() => {
          if (!state.audioPatternState || !state.audioContext || state.audioContext.state === "closed") {
            audioStopPattern();
            return;
          }

          const patternState = state.audioPatternState;
          const patternData = patternState.normalized;
          const beatToSec = 60 / patternData.tempoBpm;
          const loopSec = patternData.loopBeats * beatToSec;
          const horizon = state.audioContext.currentTime + scheduleAheadSec;

          while (patternState.cycleStartTime <= horizon) {
            if (patternState.nextEventIndex >= patternData.events.length) {
              if (patternData.loop) {
                patternState.nextEventIndex = 0;
                patternState.cycleStartTime += loopSec;
                continue;
              }
              audioStopPattern();
              return;
            }

            const event = patternData.events[patternState.nextEventIndex];
            const eventStart = patternState.cycleStartTime + (event.atBeats * beatToSec);
            if (eventStart > horizon) {
              break;
            }

            scheduleToneAt(
              eventStart,
              event.noteHz,
              event.durBeats * beatToSec * 1000,
              event.waveType,
              event.volume
            );
            patternState.nextEventIndex += 1;
          }
        }, tickMs);

        return null;
      }

      function beginLoop(apiName, updateFn, renderFn) {
        ensureCanvasContext(apiName);
        requireBrowserApi("mlRuntime.game." + apiName);
        if (typeof window.requestAnimationFrame !== "function") {
          throw new Error("mlRuntime.game." + apiName + " requires window.requestAnimationFrame.");
        }
        if (state.running) {
          throw new Error("mlRuntime.game." + apiName + " cannot be called while a game loop is already running.");
        }
        if (typeof updateFn !== "function") {
          throw new Error("mlRuntime.game." + apiName + " requires updateFn(dtMs) to be a function.");
        }
        if (renderFn !== null && renderFn !== undefined && typeof renderFn !== "function") {
          throw new Error("mlRuntime.game." + apiName + " expected renderFn to be a function when provided.");
        }
      }

      function haltLoop() {
        state.running = false;
        state.rafId = null;
        state.lastTimestamp = null;
        state.fixedAccumulator = 0;
      }

      function start(updateFn, renderFn) {
        beginLoop("start", updateFn, renderFn);

        state.running = true;
        state.lastTimestamp = null;
        state.fixedAccumulator = 0;

        const frame = (timestamp) => {
          if (!state.running) return;

          try {
            const currentTimestamp = toFiniteNumber(timestamp, 0);
            const dtMs = state.lastTimestamp === null ? 0 : Math.max(0, currentTimestamp - state.lastTimestamp);
            state.lastTimestamp = currentTimestamp;

            beginInputFrame();
            updateFn(dtMs);
            endInputFrame();
            if (typeof renderFn === "function") {
              renderFn();
            }
          } catch (error) {
            endInputFrame();
            haltLoop();
            throw error;
          }

          if (state.running) {
            state.rafId = window.requestAnimationFrame(frame);
          }
        };

        state.rafId = window.requestAnimationFrame(frame);
        return null;
      }

      function startFixed(updateFn, renderFn, tickMs) {
        if (typeof renderFn === "number" && (tickMs === undefined || tickMs === null)) {
          tickMs = renderFn;
          renderFn = undefined;
        }

        beginLoop("startFixed", updateFn, renderFn);

        const resolvedTick = tickMs === undefined || tickMs === null
          ? 1000 / 60
          : clampFiniteNumber(tickMs, 1, 1000, 1000 / 60);

        state.running = true;
        state.lastTimestamp = null;
        state.fixedAccumulator = 0;
        state.fixedTickMs = resolvedTick;

        const maxUpdates = 5;
        const frame = (timestamp) => {
          if (!state.running) return;

          try {
            const currentTimestamp = toFiniteNumber(timestamp, 0);
            const dtMs = state.lastTimestamp === null ? 0 : Math.max(0, currentTimestamp - state.lastTimestamp);
            state.lastTimestamp = currentTimestamp;
            state.fixedAccumulator += dtMs;

            let steps = 0;
            while (state.fixedAccumulator >= state.fixedTickMs && steps < maxUpdates) {
              state.fixedAccumulator -= state.fixedTickMs;
              steps += 1;
              beginInputFrame();
              try {
                updateFn(state.fixedTickMs);
              } finally {
                endInputFrame();
              }
            }
            if (steps >= maxUpdates) {
              state.fixedAccumulator = 0;
            }

            if (typeof renderFn === "function") {
              renderFn();
            }
          } catch (error) {
            endInputFrame();
            haltLoop();
            throw error;
          }

          if (state.running) {
            state.rafId = window.requestAnimationFrame(frame);
          }
        };

        state.rafId = window.requestAnimationFrame(frame);
        return null;
      }

      function stop() {
        requireBrowserApi("mlRuntime.game.stop");
        if (!state.running) {
          throw new Error("mlRuntime.game.stop cannot be called when the game loop is not running.");
        }

        state.running = false;
        if (state.rafId !== null && typeof window.cancelAnimationFrame === "function") {
          window.cancelAnimationFrame(state.rafId);
        }
        haltLoop();
        resetKeyboardAndPointerState();
        return null;
      }

      const GAME_SAVE_PREFIX = "malda.game.";

      function getLocalStorage() {
        try {
          if (typeof window === "undefined" || !window.localStorage) return null;
          return window.localStorage;
        } catch (error) {
          return null;
        }
      }

      function serializeSaveValue(value) {
        try {
          const json = toJSON(value);
          if (typeof json === "string") return json;
        } catch (error) {
          // Fall through to string coercion.
        }
        try {
          return JSON.stringify(coerceToString(value));
        } catch (error) {
          return null;
        }
      }

      function save(key, value) {
        const name = coerceToString(key || "");
        if (!name) return null;
        const storage = getLocalStorage();
        if (!storage || typeof storage.setItem !== "function") return null;
        const json = serializeSaveValue(value);
        if (typeof json !== "string") return null;
        try {
          storage.setItem(GAME_SAVE_PREFIX + name, json);
        } catch (error) {
          // QuotaExceeded or private-mode: no-op.
        }
        return null;
      }

      function load(key) {
        const name = coerceToString(key || "");
        if (!name) return null;
        const storage = getLocalStorage();
        if (!storage || typeof storage.getItem !== "function") return null;
        try {
          const raw = storage.getItem(GAME_SAVE_PREFIX + name);
          if (raw === null || raw === undefined) return null;
          return parseJSON(raw);
        } catch (error) {
          return null;
        }
      }

      function removeSave(key) {
        const name = coerceToString(key || "");
        if (!name) return null;
        const storage = getLocalStorage();
        if (!storage || typeof storage.removeItem !== "function") return null;
        try {
          storage.removeItem(GAME_SAVE_PREFIX + name);
        } catch (error) {
          // Missing storage: no-op.
        }
        return null;
      }

      return {
        createCanvas,
        setBackground,
        clear,
        fillRect,
        fillCircle,
        drawText,
        drawLine,
        strokeRect,
        setAlpha,
        setCamera,
        getCameraX,
        getCameraY,
        loadImage,
        imageIsReady,
        drawImage,
        drawImageRect,
        createPixelBuffer,
        setPixel,
        blitPixels,
        overlapRect,
        overlapCircle,
        pointInRect,
        pointInCircle,
        sweepRect,
        isKeyDown,
        wasKeyPressed,
        wasKeyReleased,
        getMouseX,
        getMouseY,
        isMouseDown,
        getTouches,
        isGamepadConnected,
        getGamepadAxis,
        isGamepadButtonDown,
        wasGamepadButtonPressed,
        audioInit,
        audioIsReady,
        audioSetMasterVolume,
        audioPlayTone,
        audioPlayNoise,
        audioPlaySample,
        audioStopSample,
        audioStopAll,
        audioLoadTrack,
        audioPlayTrack,
        audioStopTrack,
        audioSetTrackOptions,
        audioTrackIsReady,
        audioGetTrackInfo,
        audioPlayPattern,
        audioStopPattern,
        start,
        startFixed,
        stop,
        save,
        load,
        removeSave
      };
    })(),
    three: (() => {
      const state = {
        renderer: null,
        domElement: null,
        running: false,
        rafId: null,
        lastTimestamp: null,
        clearColor: "#000000",
        keysDown: new Set(),
        mouseButtonsDown: new Set(),
        mouseX: 0,
        mouseY: 0,
        listenersAttached: false,
        listeners: null,
        textureCache: new Map(),
        modelCache: new Map()
      };

      function normalizeKey(key) {
        return coerceToString(key).toLowerCase();
      }

      function clampFiniteNumber(value, minValue, maxValue, fallback) {
        const numeric = toFiniteNumber(value, fallback);
        return Math.min(maxValue, Math.max(minValue, numeric));
      }

      function ensureThree(apiName) {
        requireBrowserApi("mlRuntime.three." + apiName);
        if (!global.THREE) {
          throw new Error("mlRuntime.three." + apiName + " requires globalThis.THREE. Load a compatible three.js browser bundle before malda-js-runtime.js and the compiled MALDA script. The repository includes Examples/Web/wwwroot/vendor/three.min.js.");
        }
        return global.THREE;
      }

      function ensureRenderer(apiName) {
        if (!state.renderer || !state.domElement) {
          throw new Error("mlRuntime.three." + apiName + " requires three.createRenderer(width, height, mountSelector?) to be called first.");
        }
        return state.renderer;
      }

      function resolveMountTarget(mountSelector, apiName) {
        let mount = document.body;
        if (mountSelector !== null && mountSelector !== undefined && coerceToString(mountSelector) !== "") {
          mount = document.querySelector(coerceToString(mountSelector));
          if (!mount) {
            throw new Error("mlRuntime.three." + apiName + " could not find mount target: " + coerceToString(mountSelector));
          }
        }
        return mount;
      }

      function clearRendererElement() {
        if (state.domElement && state.domElement.parentNode) {
          state.domElement.parentNode.removeChild(state.domElement);
        }
        if (state.renderer && typeof state.renderer.dispose === "function") {
          state.renderer.dispose();
        }
        state.renderer = null;
        state.domElement = null;
      }

      function updateMousePosition(event) {
        if (!state.domElement) return;
        const rect = state.domElement.getBoundingClientRect();
        const displayX = toFiniteNumber(event.clientX, 0) - rect.left;
        const displayY = toFiniteNumber(event.clientY, 0) - rect.top;
        const renderWidth = toFiniteNumber(state.domElement.width, rect.width);
        const renderHeight = toFiniteNumber(state.domElement.height, rect.height);
        const scaleX = rect.width > 0 ? renderWidth / rect.width : 1;
        const scaleY = rect.height > 0 ? renderHeight / rect.height : 1;
        state.mouseX = displayX * scaleX;
        state.mouseY = displayY * scaleY;
      }

      function updateMouseFromTouch(touch) {
        if (!state.domElement || !touch) return;
        const rect = state.domElement.getBoundingClientRect();
        const displayX = toFiniteNumber(touch.clientX, 0) - rect.left;
        const displayY = toFiniteNumber(touch.clientY, 0) - rect.top;
        const renderWidth = toFiniteNumber(state.domElement.width, rect.width);
        const renderHeight = toFiniteNumber(state.domElement.height, rect.height);
        const scaleX = rect.width > 0 ? renderWidth / rect.width : 1;
        const scaleY = rect.height > 0 ? renderHeight / rect.height : 1;
        state.mouseX = displayX * scaleX;
        state.mouseY = displayY * scaleY;
      }

      function attachInputListeners() {
        if (state.listenersAttached || !state.domElement) return;

        const onKeyDown = (event) => {
          state.keysDown.add(normalizeKey(event.key));
        };
        const onKeyUp = (event) => {
          state.keysDown.delete(normalizeKey(event.key));
        };
        const onWindowBlur = () => {
          state.keysDown.clear();
          state.mouseButtonsDown.clear();
        };
        const onMouseMove = (event) => {
          updateMousePosition(event);
        };
        const onMouseDown = (event) => {
          updateMousePosition(event);
          state.mouseButtonsDown.add(coerceToInt(event.button));
        };
        const onMouseUp = (event) => {
          state.mouseButtonsDown.delete(coerceToInt(event.button));
        };
        const onTouchStart = (event) => {
          if (event.cancelable) event.preventDefault();
          const touch = event.touches[0] || event.changedTouches[0];
          if (touch) {
            updateMouseFromTouch(touch);
            state.mouseButtonsDown.add(0);
          }
        };
        const onTouchMove = (event) => {
          if (event.cancelable) event.preventDefault();
          const touch = event.touches[0];
          if (touch) {
            updateMouseFromTouch(touch);
          }
        };
        const onTouchEnd = () => {
          state.mouseButtonsDown.delete(0);
        };
        const onTouchCancel = () => {
          state.mouseButtonsDown.delete(0);
        };

        window.addEventListener("keydown", onKeyDown);
        window.addEventListener("keyup", onKeyUp);
        window.addEventListener("blur", onWindowBlur);
        window.addEventListener("mousemove", onMouseMove);
        window.addEventListener("mousedown", onMouseDown);
        window.addEventListener("mouseup", onMouseUp);
        window.addEventListener("touchstart", onTouchStart, { passive: false });
        window.addEventListener("touchmove", onTouchMove, { passive: false });
        window.addEventListener("touchend", onTouchEnd, { passive: true });
        window.addEventListener("touchcancel", onTouchCancel, { passive: true });

        state.listeners = {
          onKeyDown,
          onKeyUp,
          onWindowBlur,
          onMouseMove,
          onMouseDown,
          onMouseUp,
          onTouchStart,
          onTouchMove,
          onTouchEnd,
          onTouchCancel
        };
        state.listenersAttached = true;
      }

      function detachInputListeners() {
        if (!state.listenersAttached || !state.listeners) return;

        window.removeEventListener("keydown", state.listeners.onKeyDown);
        window.removeEventListener("keyup", state.listeners.onKeyUp);
        window.removeEventListener("blur", state.listeners.onWindowBlur);
        window.removeEventListener("mousemove", state.listeners.onMouseMove);
        window.removeEventListener("mousedown", state.listeners.onMouseDown);
        window.removeEventListener("mouseup", state.listeners.onMouseUp);
        window.removeEventListener("touchstart", state.listeners.onTouchStart);
        window.removeEventListener("touchmove", state.listeners.onTouchMove);
        window.removeEventListener("touchend", state.listeners.onTouchEnd);
        window.removeEventListener("touchcancel", state.listeners.onTouchCancel);

        state.listeners = null;
        state.listenersAttached = false;
      }

      function createRenderer(width, height, mountSelector) {
        const THREE = ensureThree("createRenderer");
        if (state.running) {
          throw new Error("mlRuntime.three.createRenderer cannot be called while the render loop is running. Call three.stop() first.");
        }

        const renderWidth = Math.max(1, coerceToInt(width));
        const renderHeight = Math.max(1, coerceToInt(height));
        const mount = resolveMountTarget(mountSelector, "createRenderer");
        const renderer = new THREE.WebGLRenderer({ antialias: true });

        renderer.setSize(renderWidth, renderHeight, false);
        renderer.setClearColor(state.clearColor);
        if (typeof window.devicePixelRatio === "number" && Number.isFinite(window.devicePixelRatio)) {
          renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
        }

        detachInputListeners();
        clearRendererElement();

        const domElement = renderer.domElement;
        domElement.style.touchAction = "none";
        domElement.style.display = "block";
        domElement.style.width = renderWidth + "px";
        domElement.style.height = renderHeight + "px";

        mount.appendChild(domElement);
        state.renderer = renderer;
        state.domElement = domElement;
        state.lastTimestamp = null;
        state.keysDown.clear();
        state.mouseButtonsDown.clear();
        state.mouseX = 0;
        state.mouseY = 0;
        attachInputListeners();
        return renderer;
      }

      function setClearColor(renderer, color) {
        const targetRenderer = renderer || ensureRenderer("setClearColor");
        state.clearColor = coerceToString(color || "#000000");
        targetRenderer.setClearColor(state.clearColor);
        return null;
      }

      function createScene() {
        const THREE = ensureThree("createScene");
        return new THREE.Scene();
      }

      function createPerspectiveCamera(fovDeg, aspect, near, far) {
        const THREE = ensureThree("createPerspectiveCamera");
        return new THREE.PerspectiveCamera(
          clampFiniteNumber(fovDeg, 1, 179, 75),
          Math.max(0.0001, toFiniteNumber(aspect, 1)),
          Math.max(0.0001, toFiniteNumber(near, 0.1)),
          Math.max(0.0002, toFiniteNumber(far, 1000))
        );
      }

      function setPosition(object, x, y, z) {
        if (!object || !object.position || typeof object.position.set !== "function") {
          throw new Error("mlRuntime.three.setPosition expects an object with position.set(x, y, z).");
        }
        object.position.set(toFiniteNumber(x, 0), toFiniteNumber(y, 0), toFiniteNumber(z, 0));
        return null;
      }

      function setRotation(object, x, y, z) {
        if (!object || !object.rotation || typeof object.rotation.set !== "function") {
          throw new Error("mlRuntime.three.setRotation expects an object with rotation.set(x, y, z).");
        }
        object.rotation.set(toFiniteNumber(x, 0), toFiniteNumber(y, 0), toFiniteNumber(z, 0));
        return null;
      }

      function setScale(object, x, y, z) {
        if (!object || !object.scale || typeof object.scale.set !== "function") {
          throw new Error("mlRuntime.three.setScale expects an object with scale.set(x, y, z).");
        }
        object.scale.set(toFiniteNumber(x, 1), toFiniteNumber(y, 1), toFiniteNumber(z, 1));
        return null;
      }

      function createBoxGeometry(width, height, depth) {
        const THREE = ensureThree("createBoxGeometry");
        return new THREE.BoxGeometry(
          Math.max(0.0001, toFiniteNumber(width, 1)),
          Math.max(0.0001, toFiniteNumber(height, 1)),
          Math.max(0.0001, toFiniteNumber(depth, 1))
        );
      }

      function createPlaneGeometry(width, height) {
        const THREE = ensureThree("createPlaneGeometry");
        return new THREE.PlaneGeometry(
          Math.max(0.0001, toFiniteNumber(width, 1)),
          Math.max(0.0001, toFiniteNumber(height, 1))
        );
      }

      function createSphereGeometry(radius, widthSegments, heightSegments) {
        const THREE = ensureThree("createSphereGeometry");
        return new THREE.SphereGeometry(
          Math.max(0.0001, toFiniteNumber(radius, 0.5)),
          Math.trunc(clampFiniteNumber(widthSegments, 3, 128, 24)),
          Math.trunc(clampFiniteNumber(heightSegments, 2, 128, 16))
        );
      }

      function dirnameOfUrl(url) {
        const text = coerceToString(url);
        const cut = Math.max(text.lastIndexOf("/"), text.lastIndexOf("\\"));
        if (cut < 0) {
          return "";
        }
        return text.slice(0, cut + 1);
      }

      function joinUrl(base, relative) {
        const rel = coerceToString(relative);
        if (!rel) {
          return coerceToString(base);
        }
        if (
          rel.indexOf("data:") === 0 ||
          rel.indexOf("blob:") === 0 ||
          rel.indexOf("http://") === 0 ||
          rel.indexOf("https://") === 0 ||
          rel.indexOf("/") === 0
        ) {
          return rel;
        }
        return coerceToString(base) + rel;
      }

      function decodeDataUri(uri) {
        const text = coerceToString(uri);
        const comma = text.indexOf(",");
        if (text.indexOf("data:") !== 0 || comma < 0) {
          return null;
        }
        const meta = text.slice(0, comma);
        const payload = text.slice(comma + 1);
        try {
          if (meta.indexOf(";base64") >= 0) {
            const binary = global.atob(payload);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) {
              bytes[i] = binary.charCodeAt(i);
            }
            return bytes.buffer;
          }
          const decoded = decodeURIComponent(payload);
          const bytes = new Uint8Array(decoded.length);
          for (let i = 0; i < decoded.length; i++) {
            bytes[i] = decoded.charCodeAt(i);
          }
          return bytes.buffer;
        } catch (_error) {
          return null;
        }
      }

      function runtimeFetch(url) {
        if (typeof global.fetch === "function") {
          return global.fetch(url);
        }
        return Promise.reject(new Error("fetch unavailable"));
      }

      function applyTextureColorSpace(THREE, texture) {
        if (texture && typeof THREE.SRGBColorSpace === "string") {
          texture.colorSpace = THREE.SRGBColorSpace;
        }
      }

      function applyTextureToMaterial(material, texture) {
        if (!material || !texture) {
          return;
        }
        material.map = texture;
        material.needsUpdate = true;
      }

      function bindTextureMap(material, mapOption) {
        const record = resolveTextureHandle(mapOption);
        if (record) {
          if (record.ready && record.texture) {
            applyTextureToMaterial(material, record.texture);
          } else {
            record.pendingMaterials.push(material);
          }
          return;
        }
        if (mapOption && mapOption.isTexture) {
          applyTextureToMaterial(material, mapOption);
        }
      }

      function finishTexture(record, THREE, image) {
        const TextureCtor = THREE.Texture;
        if (typeof TextureCtor !== "function") {
          record.ready = false;
          return;
        }
        const texture = new TextureCtor(image);
        texture.needsUpdate = true;
        applyTextureColorSpace(THREE, texture);
        record.texture = texture;
        record.ready = true;
        const pending = record.pendingMaterials.splice(0, record.pendingMaterials.length);
        pending.forEach((material) => applyTextureToMaterial(material, texture));
      }

      function startHtmlImageLoad(url, onOk, onFail) {
        const ImageCtor = typeof global.Image === "function" ? global.Image : null;
        if (!ImageCtor) {
          onFail();
          return;
        }
        try {
          const img = new ImageCtor();
          img.onload = function () {
            onOk(img);
          };
          img.onerror = function () {
            onFail();
          };
          img.src = url;
        } catch (_error) {
          onFail();
        }
      }

      function resolveTextureHandle(value) {
        if (!value || typeof value !== "object") {
          return null;
        }
        return value.__maldaThreeTexture ? value : null;
      }

      function createTexture(url) {
        const THREE = ensureThree("createTexture");
        const source = coerceToString(url);
        if (source === "") {
          return {
            __maldaThreeTexture: true,
            url: "",
            ready: false,
            texture: null,
            pendingMaterials: []
          };
        }

        const cached = state.textureCache.get(source);
        if (cached) {
          return cached;
        }

        const handle = {
          __maldaThreeTexture: true,
          url: source,
          ready: false,
          texture: null,
          pendingMaterials: []
        };
        state.textureCache.set(source, handle);
        startHtmlImageLoad(
          resolveAssetUrl(source),
          (image) => finishTexture(handle, THREE, image),
          () => {
            handle.ready = false;
            handle.texture = null;
          }
        );
        return handle;
      }

      function createStandardMaterial(options) {
        const THREE = ensureThree("createStandardMaterial");
        const safeOptions = options && typeof options === "object" && !Array.isArray(options)
          ? options
          : {};
        const params = {};
        Object.keys(safeOptions).forEach((key) => {
          if (key !== "map") {
            params[key] = safeOptions[key];
          }
        });
        const material = new THREE.MeshStandardMaterial(params);
        bindTextureMap(material, safeOptions.map);
        return material;
      }

      function lookAt(object, x, y, z) {
        ensureThree("lookAt");
        if (!object || typeof object.lookAt !== "function") {
          throw new Error("mlRuntime.three.lookAt expects an object with lookAt(x, y, z).");
        }
        object.lookAt(toFiniteNumber(x, 0), toFiniteNumber(y, 0), toFiniteNumber(z, 0));
        return null;
      }

      function emptyModelGroup(THREE, url) {
        const group = new THREE.Group();
        group.__maldaThreeModel = true;
        group.url = url;
        group.ready = false;
        return group;
      }

      function modelIsReady(handle) {
        return !!(handle && handle.__maldaThreeModel && handle.ready);
      }

      const GLTF_COMPONENT_BYTES = { 5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4 };
      const GLTF_TYPE_COUNT = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT4: 16 };

      function typedArrayForComponent(componentType, buffer, byteOffset, count) {
        if (componentType === 5126) return new Float32Array(buffer, byteOffset, count);
        if (componentType === 5123) return new Uint16Array(buffer, byteOffset, count);
        if (componentType === 5125) return new Uint32Array(buffer, byteOffset, count);
        if (componentType === 5121) return new Uint8Array(buffer, byteOffset, count);
        if (componentType === 5122) return new Int16Array(buffer, byteOffset, count);
        if (componentType === 5120) return new Int8Array(buffer, byteOffset, count);
        return null;
      }

      function loadBufferResource(spec, baseUrl, binChunk) {
        if (!spec) {
          return Promise.resolve(null);
        }
        if (!spec.uri && binChunk) {
          return Promise.resolve(binChunk);
        }
        const uri = coerceToString(spec.uri);
        if (!uri) {
          return Promise.resolve(binChunk || null);
        }
        const data = decodeDataUri(uri);
        if (data) {
          return Promise.resolve(data);
        }
        return runtimeFetch(joinUrl(baseUrl, uri))
          .then((response) => {
            if (!response || !response.ok) {
              return null;
            }
            return response.arrayBuffer();
          })
          .catch(() => null);
      }

      function accessorArray(json, buffers, accessorIndex) {
        const accessor = json.accessors && json.accessors[accessorIndex];
        if (!accessor) {
          return null;
        }
        const view = json.bufferViews && json.bufferViews[accessor.bufferView];
        if (!view) {
          return null;
        }
        const buffer = buffers[view.buffer];
        if (!buffer) {
          return null;
        }
        const componentBytes = GLTF_COMPONENT_BYTES[accessor.componentType];
        const typeCount = GLTF_TYPE_COUNT[accessor.type] || 1;
        if (!componentBytes) {
          return null;
        }
        const count = accessor.count || 0;
        const byteOffset = (view.byteOffset || 0) + (accessor.byteOffset || 0);
        const stride = view.byteStride || 0;
        const tightCount = count * typeCount;
        try {
          if (!stride || stride === componentBytes * typeCount) {
            return typedArrayForComponent(accessor.componentType, buffer, byteOffset, tightCount);
          }
          const packed = typedArrayForComponent(accessor.componentType, new ArrayBuffer(tightCount * componentBytes), 0, tightCount);
          if (!packed) {
            return null;
          }
          const src = new DataView(buffer);
          for (let i = 0; i < count; i++) {
            const start = byteOffset + i * stride;
            for (let c = 0; c < typeCount; c++) {
              const offset = start + c * componentBytes;
              let value = 0;
              if (accessor.componentType === 5126) value = src.getFloat32(offset, true);
              else if (accessor.componentType === 5123) value = src.getUint16(offset, true);
              else if (accessor.componentType === 5125) value = src.getUint32(offset, true);
              else if (accessor.componentType === 5121) value = src.getUint8(offset);
              packed[i * typeCount + c] = value;
            }
          }
          return packed;
        } catch (_error) {
          return null;
        }
      }

      function loadGltfTextures(THREE, json, baseUrl) {
        const images = Array.isArray(json.images) ? json.images : [];
        return Promise.all(images.map((image) => {
          const uri = image && image.uri ? coerceToString(image.uri) : "";
          if (!uri) {
            return Promise.resolve(null);
          }
          if (uri.indexOf("data:") === 0) {
            return new Promise((resolve) => {
              startHtmlImageLoad(uri, (img) => {
                const texture = new THREE.Texture(img);
                texture.needsUpdate = true;
                applyTextureColorSpace(THREE, texture);
                resolve(texture);
              }, () => resolve(null));
            });
          }
          return new Promise((resolve) => {
            startHtmlImageLoad(joinUrl(baseUrl, uri), (img) => {
              const texture = new THREE.Texture(img);
              texture.needsUpdate = true;
              applyTextureColorSpace(THREE, texture);
              resolve(texture);
            }, () => resolve(null));
          });
        })).then((loaded) => {
          const textures = Array.isArray(json.textures) ? json.textures : [];
          return textures.map((tex) => {
            const source = tex && typeof tex.source === "number" ? tex.source : 0;
            return loaded[source] || null;
          });
        });
      }

      function materialFromGltf(THREE, json, textures, materialIndex) {
        const spec = json.materials && json.materials[materialIndex] ? json.materials[materialIndex] : {};
        const pbr = spec.pbrMetallicRoughness && typeof spec.pbrMetallicRoughness === "object"
          ? spec.pbrMetallicRoughness
          : {};
        const params = {};
        if (Array.isArray(pbr.baseColorFactor) && pbr.baseColorFactor.length >= 3) {
          params.color = pbr.baseColorFactor.slice(0, 3);
        }
        if (pbr.roughnessFactor !== undefined) {
          params.roughness = pbr.roughnessFactor;
        }
        if (pbr.metallicFactor !== undefined) {
          params.metalness = pbr.metallicFactor;
        }
        const material = new THREE.MeshStandardMaterial(params);
        if (pbr.baseColorTexture && typeof pbr.baseColorTexture.index === "number") {
          const texture = textures[pbr.baseColorTexture.index];
          if (texture) {
            applyTextureToMaterial(material, texture);
          }
        }
        return material;
      }

      function buildGltfGroup(THREE, json, buffers, textures) {
        const root = new THREE.Group();
        const nodes = Array.isArray(json.nodes) ? json.nodes : [];
        const meshes = Array.isArray(json.meshes) ? json.meshes : [];

        function primitiveMesh(primitive) {
          if (!primitive || !primitive.attributes || primitive.attributes.POSITION === undefined) {
            return null;
          }
          const positions = accessorArray(json, buffers, primitive.attributes.POSITION);
          if (!positions) {
            return null;
          }
          const geometry = new THREE.BufferGeometry();
          geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
          if (primitive.attributes.NORMAL !== undefined) {
            const normals = accessorArray(json, buffers, primitive.attributes.NORMAL);
            if (normals) {
              geometry.setAttribute("normal", new THREE.BufferAttribute(normals, 3));
            }
          }
          if (primitive.attributes.TEXCOORD_0 !== undefined) {
            const uvs = accessorArray(json, buffers, primitive.attributes.TEXCOORD_0);
            if (uvs) {
              geometry.setAttribute("uv", new THREE.BufferAttribute(uvs, 2));
            }
          }
          if (primitive.indices !== undefined && primitive.indices !== null) {
            const indices = accessorArray(json, buffers, primitive.indices);
            if (indices) {
              geometry.setIndex(new THREE.BufferAttribute(indices, 1));
            }
          }
          const material = materialFromGltf(THREE, json, textures, primitive.material);
          return new THREE.Mesh(geometry, material);
        }

        function nodeObject(node) {
          const local = new THREE.Group();
          if (node && Array.isArray(node.translation) && node.translation.length >= 3) {
            local.position.set(
              toFiniteNumber(node.translation[0], 0),
              toFiniteNumber(node.translation[1], 0),
              toFiniteNumber(node.translation[2], 0)
            );
          }
          if (node && Array.isArray(node.scale) && node.scale.length >= 3) {
            local.scale.set(
              toFiniteNumber(node.scale[0], 1),
              toFiniteNumber(node.scale[1], 1),
              toFiniteNumber(node.scale[2], 1)
            );
          }
          if (node && typeof node.mesh === "number" && meshes[node.mesh]) {
            const primitives = meshes[node.mesh].primitives || [];
            primitives.forEach((primitive) => {
              const mesh = primitiveMesh(primitive);
              if (mesh) {
                local.add(mesh);
              }
            });
          }
          if (node && Array.isArray(node.children)) {
            node.children.forEach((childIndex) => {
              if (nodes[childIndex]) {
                local.add(nodeObject(nodes[childIndex]));
              }
            });
          }
          return local;
        }

        const sceneIndex = typeof json.scene === "number" ? json.scene : 0;
        const scene = json.scenes && json.scenes[sceneIndex] ? json.scenes[sceneIndex] : null;
        const sceneNodes = scene && Array.isArray(scene.nodes) ? scene.nodes : nodes.map((_, i) => i);
        sceneNodes.forEach((index) => {
          if (nodes[index]) {
            root.add(nodeObject(nodes[index]));
          }
        });
        return root;
      }

      function parseGltfJson(THREE, json, baseUrl, binChunk) {
        const bufferSpecs = Array.isArray(json.buffers) ? json.buffers : [{}];
        return Promise.all(bufferSpecs.map((spec, index) => loadBufferResource(spec, baseUrl, index === 0 ? binChunk : null)))
          .then((buffers) => {
            if (buffers.some((item, index) => !item && bufferSpecs[index])) {
              return null;
            }
            return loadGltfTextures(THREE, json, baseUrl).then((textures) => buildGltfGroup(THREE, json, buffers, textures));
          });
      }

      function parseGlb(THREE, arrayBuffer, baseUrl) {
        const view = new DataView(arrayBuffer);
        if (view.byteLength < 12 || view.getUint32(0, true) !== 0x46546C67) {
          return Promise.resolve(null);
        }
        let offset = 12;
        let json = null;
        let binChunk = null;
        while (offset + 8 <= view.byteLength) {
          const chunkLength = view.getUint32(offset, true);
          const chunkType = view.getUint32(offset + 4, true);
          const start = offset + 8;
          const end = start + chunkLength;
          if (end > view.byteLength) {
            break;
          }
          if (chunkType === 0x4E4F534A) {
            const bytes = new Uint8Array(arrayBuffer, start, chunkLength);
            let text = "";
            for (let i = 0; i < bytes.length; i++) {
              text += String.fromCharCode(bytes[i]);
            }
            try {
              json = JSON.parse(text);
            } catch (_error) {
              return Promise.resolve(null);
            }
          } else if (chunkType === 0x004E4942) {
            binChunk = arrayBuffer.slice(start, end);
          }
          offset = end;
        }
        if (!json) {
          return Promise.resolve(null);
        }
        return parseGltfJson(THREE, json, baseUrl, binChunk);
      }

      function loadGLTF(url) {
        const THREE = ensureThree("loadGLTF");
        const source = coerceToString(url);
        if (source === "") {
          return emptyModelGroup(THREE, "");
        }
        const cached = state.modelCache.get(source);
        if (cached) {
          return cached;
        }

        const group = emptyModelGroup(THREE, source);
        state.modelCache.set(source, group);

        const resolved = resolveAssetUrl(source);
        const lower = resolved.toLowerCase();
        runtimeFetch(resolved)
          .then((response) => {
            if (!response || !response.ok) {
              return null;
            }
            const contentType = response.headers && typeof response.headers.get === "function"
              ? coerceToString(response.headers.get("content-type"))
              : "";
            if (lower.endsWith(".glb") || contentType.indexOf("gltf-binary") >= 0) {
              return response.arrayBuffer().then((buffer) => parseGlb(THREE, buffer, dirnameOfUrl(resolved)));
            }
            return response.json().then((json) => parseGltfJson(THREE, json, dirnameOfUrl(resolved), null));
          })
          .then((root) => {
            if (!root) {
              group.ready = false;
              return;
            }
            group.add(root);
            group.ready = true;
          })
          .catch(() => {
            group.ready = false;
          });

        return group;
      }

      function wrapUniformValue(THREE, value) {
        if (Array.isArray(value)) {
          const x = toFiniteNumber(value[0], 0);
          const y = toFiniteNumber(value[1], 0);
          const z = toFiniteNumber(value[2], 0);
          const w = toFiniteNumber(value[3], 0);
          if (value.length <= 1) return x;
          if (value.length === 2) return new THREE.Vector2(x, y);
          if (value.length === 3) return new THREE.Vector3(x, y, z);
          return new THREE.Vector4(x, y, z, w);
        }
        if (typeof value === "string") {
          const text = coerceToString(value).trim();
          if (text.charAt(0) === "#") {
            return new THREE.Color(text);
          }
        }
        return value;
      }

      function toShaderUniforms(THREE, uniforms) {
        const result = {};
        if (!uniforms || typeof uniforms !== "object" || Array.isArray(uniforms)) {
          return result;
        }
        Object.keys(uniforms).forEach((key) => {
          const raw = uniforms[key];
          if (raw && typeof raw === "object" && !Array.isArray(raw) && Object.prototype.hasOwnProperty.call(raw, "value")) {
            result[key] = { value: wrapUniformValue(THREE, raw.value) };
          } else {
            result[key] = { value: wrapUniformValue(THREE, raw) };
          }
        });
        return result;
      }

      function createShaderMaterial(options) {
        const THREE = ensureThree("createShaderMaterial");
        const safeOptions = options && typeof options === "object" && !Array.isArray(options) ? options : {};
        const vertexShader = coerceToString(safeOptions.vertexShader);
        const fragmentShader = coerceToString(safeOptions.fragmentShader);
        if (!vertexShader) {
          throw new Error("mlRuntime.three.createShaderMaterial requires options.vertexShader (GLSL string).");
        }
        if (!fragmentShader) {
          throw new Error("mlRuntime.three.createShaderMaterial requires options.fragmentShader (GLSL string).");
        }
        if (typeof THREE.ShaderMaterial !== "function") {
          throw new Error("mlRuntime.three.createShaderMaterial requires THREE.ShaderMaterial on the loaded three.js bundle.");
        }

        const material = new THREE.ShaderMaterial({
          vertexShader,
          fragmentShader,
          uniforms: toShaderUniforms(THREE, safeOptions.uniforms)
        });
        if (safeOptions.depthWrite === false) material.depthWrite = false;
        if (safeOptions.depthTest === false) material.depthTest = false;
        if (safeOptions.transparent === true) material.transparent = true;
        return material;
      }

      function setUniform(material, name, value) {
        const THREE = ensureThree("setUniform");
        if (!material || typeof material !== "object" || !material.uniforms || typeof material.uniforms !== "object") {
          throw new Error("mlRuntime.three.setUniform expects a shader material created by three.createShaderMaterial.");
        }
        const key = coerceToString(name);
        if (!key) {
          throw new Error("mlRuntime.three.setUniform requires a uniform name.");
        }

        const current = material.uniforms[key];
        if (current && current.value && typeof current.value.set === "function" && Array.isArray(value)) {
          const args = value.map((item) => toFiniteNumber(item, 0));
          current.value.set.apply(current.value, args);
          return null;
        }
        if (current && current.value && current.value.isColor && typeof value === "string") {
          current.value.set(coerceToString(value));
          return null;
        }

        const wrapped = wrapUniformValue(THREE, value);
        if (current) {
          current.value = wrapped;
        } else {
          material.uniforms[key] = { value: wrapped };
        }
        return null;
      }

      function createOrthographicCamera(left, right, top, bottom, near, far) {
        const THREE = ensureThree("createOrthographicCamera");
        return new THREE.OrthographicCamera(
          toFiniteNumber(left, -1),
          toFiniteNumber(right, 1),
          toFiniteNumber(top, 1),
          toFiniteNumber(bottom, -1),
          toFiniteNumber(near, 0),
          Math.max(0.0001, toFiniteNumber(far, 1))
        );
      }

      function createMesh(geometry, material) {
        const THREE = ensureThree("createMesh");
        return new THREE.Mesh(geometry, material);
      }

      function createGroup() {
        const THREE = ensureThree("createGroup");
        return new THREE.Group();
      }

      function createDirectionalLight(color, intensity) {
        const THREE = ensureThree("createDirectionalLight");
        return new THREE.DirectionalLight(color || "#ffffff", clampFiniteNumber(intensity, 0, 100, 1));
      }

      function createAmbientLight(color, intensity) {
        const THREE = ensureThree("createAmbientLight");
        return new THREE.AmbientLight(color || "#ffffff", clampFiniteNumber(intensity, 0, 100, 1));
      }

      function add(parent, child) {
        if (!parent || typeof parent.add !== "function") {
          throw new Error("mlRuntime.three.add expects a parent object with add(child).");
        }
        parent.add(child);
        return child || null;
      }

      function render(renderer, scene, camera) {
        const targetRenderer = renderer || ensureRenderer("render");
        if (!scene || typeof scene !== "object") {
          throw new Error("mlRuntime.three.render requires a scene object.");
        }
        if (!camera || typeof camera !== "object") {
          throw new Error("mlRuntime.three.render requires a camera object.");
        }
        targetRenderer.render(scene, camera);
        return null;
      }

      function setRendererSize(renderer, width, height) {
        const targetRenderer = renderer || ensureRenderer("setRendererSize");
        const renderWidth = Math.max(1, coerceToInt(width));
        const renderHeight = Math.max(1, coerceToInt(height));
        targetRenderer.setSize(renderWidth, renderHeight, false);
        if (targetRenderer.domElement) {
          targetRenderer.domElement.style.width = renderWidth + "px";
          targetRenderer.domElement.style.height = renderHeight + "px";
        }
        return null;
      }

      function setCameraAspect(camera, aspect) {
        if (!camera || typeof camera !== "object" || typeof camera.updateProjectionMatrix !== "function") {
          throw new Error("mlRuntime.three.setCameraAspect expects a camera object with updateProjectionMatrix().");
        }
        camera.aspect = Math.max(0.0001, toFiniteNumber(aspect, 1));
        camera.updateProjectionMatrix();
        return null;
      }

      function start(updateFn, renderFn) {
        ensureThree("start");
        ensureRenderer("start");
        requireBrowserApi("mlRuntime.three.start");
        if (typeof window.requestAnimationFrame !== "function") {
          throw new Error("mlRuntime.three.start requires window.requestAnimationFrame.");
        }
        if (state.running) {
          throw new Error("mlRuntime.three.start cannot be called while a render loop is already running.");
        }
        if (typeof updateFn !== "function") {
          throw new Error("mlRuntime.three.start requires updateFn(dtMs) to be a function.");
        }
        if (renderFn !== null && renderFn !== undefined && typeof renderFn !== "function") {
          throw new Error("mlRuntime.three.start expected renderFn to be a function when provided.");
        }

        state.running = true;
        state.lastTimestamp = null;

        const frame = (timestamp) => {
          if (!state.running) return;

          try {
            const currentTimestamp = toFiniteNumber(timestamp, 0);
            const dtMs = state.lastTimestamp === null ? 0 : Math.max(0, currentTimestamp - state.lastTimestamp);
            state.lastTimestamp = currentTimestamp;

            updateFn(dtMs);
            if (typeof renderFn === "function") {
              renderFn();
            }
          } catch (error) {
            state.running = false;
            state.rafId = null;
            state.lastTimestamp = null;
            throw error;
          }

          if (state.running) {
            state.rafId = window.requestAnimationFrame(frame);
          }
        };

        state.rafId = window.requestAnimationFrame(frame);
        return null;
      }

      function stop() {
        requireBrowserApi("mlRuntime.three.stop");
        if (!state.running) {
          throw new Error("mlRuntime.three.stop cannot be called when the render loop is not running.");
        }

        state.running = false;
        if (state.rafId !== null && typeof window.cancelAnimationFrame === "function") {
          window.cancelAnimationFrame(state.rafId);
        }
        state.rafId = null;
        state.lastTimestamp = null;
        state.keysDown.clear();
        state.mouseButtonsDown.clear();
        return null;
      }

      function isKeyDown(key) {
        return state.keysDown.has(normalizeKey(key));
      }

      function getMouseX() {
        return state.mouseX;
      }

      function getMouseY() {
        return state.mouseY;
      }

      function isMouseDown(button) {
        const mouseButton = button === null || button === undefined ? 0 : coerceToInt(button);
        return state.mouseButtonsDown.has(mouseButton);
      }

      return {
        createRenderer,
        setClearColor,
        setRendererSize,
        createScene,
        createPerspectiveCamera,
        setCameraAspect,
        setPosition,
        setRotation,
        setScale,
        createBoxGeometry,
        createPlaneGeometry,
        createSphereGeometry,
        createTexture,
        createStandardMaterial,
        loadGLTF,
        modelIsReady,
        lookAt,
        createShaderMaterial,
        setUniform,
        createOrthographicCamera,
        createMesh,
        createGroup,
        createDirectionalLight,
        createAmbientLight,
        add,
        render,
        start,
        stop,
        isKeyDown,
        getMouseX,
        getMouseY,
        isMouseDown
      };
    })()
  };

  global.mlRuntime = Object.assign({}, global.mlRuntime || {}, runtime);
  if (typeof global.random !== "function") {
    global.random = randomBuiltin;
  }
  if (typeof global.randomInt !== "function") {
    global.randomInt = randomIntBuiltin;
  }
  if (typeof global.randomFloat !== "function") {
    global.randomFloat = randomFloatBuiltin;
  }
  if (typeof global.int !== "function") {
    global.int = coerceToInt;
  }
  if (typeof global.float !== "function") {
    global.float = coerceToFloat;
  }
  if (typeof global.string !== "function") {
    global.string = coerceToString;
  }
  if (typeof global.length !== "function") {
    global.length = lengthBuiltin;
  }
  if (typeof global.substring !== "function") {
    global.substring = substringBuiltin;
  }
  if (typeof global.indexOf !== "function") {
    global.indexOf = indexOfBuiltin;
  }
  if (typeof global.replace !== "function") {
    global.replace = replaceBuiltin;
  }
  if (typeof global.lower !== "function") {
    global.lower = lowerBuiltin;
  }
  if (typeof global.round !== "function") {
    global.round = roundBuiltin;
  }
  if (typeof global.sin !== "function") {
    global.sin = Math.sin;
  }
  if (typeof global.cos !== "function") {
    global.cos = Math.cos;
  }
  if (typeof global.asin !== "function") {
    global.asin = Math.asin;
  }
  if (typeof global.sqrt !== "function") {
    global.sqrt = Math.sqrt;
  }

  if (typeof module !== "undefined" && module.exports) {
    module.exports = global.mlRuntime;
  }
})(typeof globalThis !== "undefined" ? globalThis : window);
