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

  function requireReceiverArray(value, methodName) {
    if (!Array.isArray(value)) {
      throw new Error(methodName + "() expects an array");
    }
    return value;
  }

  function requireCallArity(methodName, callArgs, min, max, signature) {
    if (callArgs.length < min || callArgs.length > max) {
      if (min === max && min === 0) {
        throw new Error(methodName + "() expects 0 arguments");
      }
      if (min === max) {
        throw new Error(methodName + "() expects " + min + " argument" + (min === 1 ? "" : "s") + (signature ? ": (" + signature + ")" : ""));
      }
      throw new Error(methodName + "() expects " + min + " or " + max + " arguments");
    }
  }

  function requireCallback(methodName, value) {
    if (typeof value !== "function") {
      throw new Error(methodName + "() expects a function argument");
    }
    return value;
  }

  function normalizeArrayIndex(index, length) {
    let resolved = coerceToInt(index);
    if (resolved < 0) {
      resolved = length + resolved;
    }
    return resolved;
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
    const callArgs = Array.isArray(args) ? args : [];
    switch (methodName) {
      case "pop": {
        requireCallArity("pop", callArgs, 0, 0);
        const array = requireReceiverArray(arrayValue, "pop");
        if (array.length === 0) {
          throw new Error("Cannot pop from empty array");
        }
        return array.pop();
      }
      case "shift": {
        requireCallArity("shift", callArgs, 0, 0);
        const array = requireReceiverArray(arrayValue, "shift");
        if (array.length === 0) {
          throw new Error("Cannot shift from empty array");
        }
        return array.shift();
      }
      case "concat": {
        requireCallArity("concat", callArgs, 1, 1);
        const array = requireReceiverArray(arrayValue, "concat");
        if (!Array.isArray(callArgs[0])) {
          throw new Error("concat() expects an array argument");
        }
        return array.concat(callArgs[0]);
      }
      case "popOrNull": {
        requireCallArity("popOrNull", callArgs, 0, 0);
        const array = requireReceiverArray(arrayValue, "popOrNull");
        return array.length === 0 ? null : array.pop();
      }
      case "shiftOrNull": {
        requireCallArity("shiftOrNull", callArgs, 0, 0);
        const array = requireReceiverArray(arrayValue, "shiftOrNull");
        return array.length === 0 ? null : array.shift();
      }
      case "get": {
        requireCallArity("get", callArgs, 1, 2);
        const array = requireReceiverArray(arrayValue, "get");
        const index = normalizeArrayIndex(callArgs[0], array.length);
        if (index < 0 || index >= array.length) {
          return callArgs.length === 2 ? callArgs[1] : null;
        }
        return array[index];
      }
      case "at": {
        requireCallArity("at", callArgs, 1, 1);
        const array = requireReceiverArray(arrayValue, "at");
        const index = normalizeArrayIndex(callArgs[0], array.length);
        if (index < 0 || index >= array.length) {
          return null;
        }
        return array[index];
      }
      case "reverse": {
        requireCallArity("reverse", callArgs, 0, 0);
        const array = requireReceiverArray(arrayValue, "reverse");
        array.reverse();
        return array;
      }
      case "slice": {
        requireCallArity("slice", callArgs, 1, 2);
        const array = requireReceiverArray(arrayValue, "slice");
        return callArgs.length === 1
          ? array.slice(coerceToInt(callArgs[0]))
          : array.slice(coerceToInt(callArgs[0]), coerceToInt(callArgs[1]));
      }
      case "indexOf": {
        requireCallArity("indexOf", callArgs, 1, 1);
        const array = requireReceiverArray(arrayValue, "indexOf");
        for (let i = 0; i < array.length; i++) {
          if (equals(array[i], callArgs[0])) {
            return i;
          }
        }
        return -1;
      }
      case "includes": {
        requireCallArity("includes", callArgs, 1, 1);
        const array = requireReceiverArray(arrayValue, "includes");
        for (let i = 0; i < array.length; i++) {
          if (equals(array[i], callArgs[0])) {
            return true;
          }
        }
        return false;
      }
      case "map": {
        requireCallArity("map", callArgs, 1, 1);
        const array = requireReceiverArray(arrayValue, "map");
        const mapper = requireCallback("map", callArgs[0]);
        return array.map((item) => mapper(item));
      }
      case "filter": {
        requireCallArity("filter", callArgs, 1, 1);
        const array = requireReceiverArray(arrayValue, "filter");
        const predicate = requireCallback("filter", callArgs[0]);
        return array.filter((item) => isTruthy(predicate(item)));
      }
      case "reduce": {
        requireCallArity("reduce", callArgs, 1, 2);
        const array = requireReceiverArray(arrayValue, "reduce");
        const reducer = requireCallback("reduce", callArgs[0]);
        if (callArgs.length === 1) {
          if (array.length === 0) {
            throw new Error("reduce() on empty array requires initial value");
          }
          return array.reduce((acc, item) => reducer(acc, item));
        }
        return array.reduce((acc, item) => reducer(acc, item), callArgs[1]);
      }
      case "forEach": {
        requireCallArity("forEach", callArgs, 1, 1);
        const array = requireReceiverArray(arrayValue, "forEach");
        const visitor = requireCallback("forEach", callArgs[0]);
        for (let i = 0; i < array.length; i++) {
          visitor(array[i]);
        }
        return null;
      }
      case "find": {
        requireCallArity("find", callArgs, 1, 1);
        const array = requireReceiverArray(arrayValue, "find");
        const predicate = requireCallback("find", callArgs[0]);
        for (let i = 0; i < array.length; i++) {
          if (isTruthy(predicate(array[i]))) {
            return array[i];
          }
        }
        return null;
      }
      case "findIndex": {
        requireCallArity("findIndex", callArgs, 1, 1);
        const array = requireReceiverArray(arrayValue, "findIndex");
        const predicate = requireCallback("findIndex", callArgs[0]);
        for (let i = 0; i < array.length; i++) {
          if (isTruthy(predicate(array[i]))) {
            return i;
          }
        }
        return -1;
      }
      case "some": {
        requireCallArity("some", callArgs, 1, 1);
        const array = requireReceiverArray(arrayValue, "some");
        const predicate = requireCallback("some", callArgs[0]);
        for (let i = 0; i < array.length; i++) {
          if (isTruthy(predicate(array[i]))) {
            return true;
          }
        }
        return false;
      }
      case "every": {
        requireCallArity("every", callArgs, 1, 1);
        const array = requireReceiverArray(arrayValue, "every");
        const predicate = requireCallback("every", callArgs[0]);
        for (let i = 0; i < array.length; i++) {
          if (!isTruthy(predicate(array[i]))) {
            return false;
          }
        }
        return true;
      }
      case "sum":
        requireCallArity("sum", callArgs, 0, 0);
        return mathSum(requireReceiverArray(arrayValue, "sum"));
      case "average":
        requireCallArity("average", callArgs, 0, 0);
        return mathAverage(requireReceiverArray(arrayValue, "average"));
      case "min":
        requireCallArity("min", callArgs, 0, 0);
        return mathMin(requireReceiverArray(arrayValue, "min"));
      case "max":
        requireCallArity("max", callArgs, 0, 0);
        return mathMax(requireReceiverArray(arrayValue, "max"));
      case "sort":
        return sortBuiltin(getArray(arrayValue), callArgs[0]);
      case "join":
        return joinBuiltin(getArray(arrayValue), callArgs[0]);
      case "except": {
        if (callArgs.length !== 1) {
          throw new Error("except() expects 1 argument");
        }
        if (!Array.isArray(callArgs[0])) {
          throw new Error("except() expects an array argument");
        }
        const array = getArray(arrayValue);
        const other = callArgs[0];
        return array.filter((item) => !other.some((candidate) => equals(item, candidate)));
      }
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

  function mintCap(kind, path, callee, name) {
    if (typeof path !== "string") {
      throw new Error((callee || kind) + "() path must be a string");
    }
    const fields = { kind, path };
    if (kind === "mcpCall" || (name != null && name !== undefined && name !== "")) {
      fields.name = name == null || name === undefined ? "" : String(name);
    }
    const token = markDict(fields);
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

  function capHostOnly(callee) {
    throw new Error(callee + "() is not available on the JavaScript backend");
  }

  function hasDotDotSegment(value) {
    const raw = String(value == null ? "" : value).replace(/\\/g, "/");
    const cut = raw.search(/[?#]/);
    const path = cut >= 0 ? raw.slice(0, cut) : raw;
    return path.split("/").some((part) => part === "..");
  }

  function normalizeHttpPrefix(raw, callee) {
    let url;
    try {
      url = new URL(String(raw));
    } catch {
      throw new Error(callee + "() expects an absolute http or https URL");
    }
    if (url.protocol !== "http:" && url.protocol !== "https:") {
      throw new Error(callee + "() expects an absolute http or https URL");
    }
    let path = url.pathname === "/" ? "" : url.pathname.replace(/\/+$/, "");
    return url.origin + path;
  }

  function getHttpOrigin(prefix) {
    return new URL(prefix).origin;
  }

  function joinHttpPrefix(parent, relative) {
    const rel = String(relative).replace(/\\/g, "/").trim();
    if (rel.startsWith("//")) {
      throw new Error("confine() path '" + relative + "' is not under capability origin '" + parent + "'");
    }
    if (rel.startsWith("/")) {
      const path = rel.split("?")[0].split("#")[0].replace(/\/+$/, "");
      return getHttpOrigin(parent) + path;
    }
    const relPath = rel.split("?")[0].split("#")[0].replace(/^\/+|\/+$/g, "");
    return relPath.length === 0 ? parent : parent.replace(/\/+$/, "") + "/" + relPath;
  }

  function isHttpUnder(parent, child) {
    let p;
    let c;
    try {
      p = new URL(parent);
      c = new URL(child);
    } catch {
      return false;
    }
    if (p.protocol !== c.protocol || p.host !== c.host) return false;
    const pp = p.pathname === "/" ? "" : p.pathname.replace(/\/+$/, "");
    const cp = c.pathname === "/" ? "" : c.pathname.replace(/\/+$/, "");
    if (pp.length === 0) return true;
    if (pp === cp) return true;
    return cp.startsWith(pp + "/");
  }

  function parseArgv(value, callee) {
    if (typeof value === "string") {
      return value.trim().length === 0 ? [] : value.trim().split(/\s+/);
    }
    if (Array.isArray(value)) {
      return value.map((item) => {
        if (typeof item !== "string") {
          throw new Error(callee + "() prefix entries must be strings");
        }
        return item;
      }).filter((item) => item.trim().length > 0);
    }
    throw new Error(callee + "() prefix must be a string or an array of strings");
  }

  function rejectUnsafeShellArg(arg, callee) {
    if (!arg || String(arg).trim().length === 0) {
      throw new Error(callee + "() prefix cannot contain an empty argument");
    }
    if (hasDotDotSegment(arg) || /^([A-Za-z]:|[\\/])/.test(arg)) {
      throw new Error(callee + "() argument '" + arg + "' is not allowed under a shell capability");
    }
  }

  function confineHttp(parent, relative) {
    if (hasDotDotSegment(relative)) {
      throw new Error("confine() path '" + relative + "' is not under capability origin '" + parent.path + "'");
    }
    const pathPart = String(relative).trim().split("?")[0].split("#")[0];
    let combined = parent.path;
    if (pathPart.length > 0) {
      try {
        combined = /^https?:\/\//i.test(pathPart)
          ? normalizeHttpPrefix(pathPart, "confine")
          : joinHttpPrefix(parent.path, pathPart);
      } catch (err) {
        throw new Error("confine() path '" + relative + "' is not under capability origin '" + parent.path + "'");
      }
    }
    if (!isHttpUnder(parent.path, combined)) {
      throw new Error("confine() path '" + relative + "' is not under capability origin '" + parent.path + "'");
    }
    return mintCap(parent.kind, combined, "confine");
  }

  function confineMcp(parent, tool) {
    if (typeof tool !== "string" || tool.trim().length === 0) {
      throw new Error("confine() tool name must be a non-empty string");
    }
    if (tool.indexOf("/") >= 0 || tool.indexOf("\\") >= 0 || hasDotDotSegment(tool)) {
      throw new Error("confine() tool '" + tool + "' is not under capability server '" + parent.path + "'");
    }
    if (parent.name && parent.name !== tool) {
      throw new Error("confine() tool '" + tool + "' is not under capability tool '" + parent.name + "'");
    }
    return mintCap(parent.kind, parent.path, "confine", tool);
  }

  function confineShell(parent, relative) {
    const extra = parseArgv(relative, "confine");
    extra.forEach((part) => rejectUnsafeShellArg(part, "confine"));
    const combined = parseArgv(parent.path, "confine").concat(extra);
    if (combined.length === 0) {
      throw new Error("confine() shell prefix cannot be empty");
    }
    return mintCap(parent.kind, combined.join(" "), "confine");
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
    httpGet(origin) {
      return mintCap("httpGet", normalizeHttpPrefix(origin, "httpGet"), "httpGet");
    },
    mcpCall(server, tool) {
      if (typeof server !== "string") {
        throw new Error("mcpCall() path must be a string");
      }
      return mintCap("mcpCall", server, "mcpCall", tool == null ? "" : String(tool));
    },
    shell(prefix) {
      const parts = parseArgv(prefix, "shell");
      if (parts.length === 0) {
        throw new Error("shell() prefix cannot be empty");
      }
      parts.forEach((part) => rejectUnsafeShellArg(part, "shell"));
      return mintCap("shell", parts.join(" "), "shell");
    },
    is(value, kind) {
      return isCapToken(value, kind);
    },
    confine(token, relativePath) {
      const parent = requireCapToken(token, null, "confine");
      if (parent.kind === "httpGet") {
        if (typeof relativePath !== "string") {
          throw new Error("confine() path must be a string");
        }
        return confineHttp(parent, relativePath);
      }
      if (parent.kind === "mcpCall") {
        return confineMcp(parent, relativePath);
      }
      if (parent.kind === "shell") {
        return confineShell(parent, relativePath);
      }
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
    list() { capHostIoUnavailable("list"); },
    fetch() { capHostOnly("fetch"); },
    invoke() { capHostOnly("invoke"); },
    run() { capHostOnly("run"); }
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
      case "Equals":
        return equals(pattern.value, value);
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

  function requireNonNegativeDimension(value) {
    const n = coerceToFloat(value);
    if (!Number.isFinite(n) || n !== Math.trunc(n)) {
      throw new Error("zeros() expects integer dimensions");
    }
    if (n < 0) {
      throw new Error("zeros() dimensions must be >= 0");
    }
    return n;
  }

  function zeroRow(length) {
    const row = [];
    for (let i = 0; i < length; i++) {
      row.push(0);
    }
    return row;
  }

  function mathZeros(nOrRows, cols) {
    if (arguments.length < 1 || arguments.length > 2) {
      throw new Error("zeros() expects 1-2 arguments: (n, cols?)");
    }
    const n = requireNonNegativeDimension(nOrRows);
    if (arguments.length === 1) {
      return zeroRow(n);
    }
    const columnCount = requireNonNegativeDimension(cols);
    const rows = [];
    for (let i = 0; i < n; i++) {
      rows.push(zeroRow(columnCount));
    }
    return rows;
  }

  function neuralIsNumeric(value) {
    return typeof value === "number" && Number.isFinite(value);
  }

  function neuralAsNumeric(name, value) {
    const n = coerceToFloat(value);
    if (!Number.isFinite(n)) {
      throw new Error(name + "() expects numeric values");
    }
    return n;
  }

  function neuralIsVector(value) {
    return Array.isArray(value) && (value.length === 0 || !Array.isArray(value[0]));
  }

  function neuralIsMatrix(value) {
    return Array.isArray(value) && value.length > 0 && Array.isArray(value[0]);
  }

  function neuralRequireVector(name, value, which) {
    if (!Array.isArray(value) || neuralIsMatrix(value)) {
      throw new Error(name + "() expects a numeric vector as " + which + " argument");
    }
    const vector = [];
    for (let i = 0; i < value.length; i++) {
      vector.push(neuralAsNumeric(name, value[i]));
    }
    return vector;
  }

  function neuralRequireMatrix(name, value, which) {
    if (!neuralIsMatrix(value)) {
      throw new Error(name + "() expects a 2D numeric matrix as " + which + " argument");
    }
    const matrix = [];
    let width = -1;
    for (let i = 0; i < value.length; i++) {
      if (!Array.isArray(value[i])) {
        throw new Error(name + "() expects a 2D numeric matrix as " + which + " argument");
      }
      if (width < 0) {
        width = value[i].length;
      } else if (value[i].length !== width) {
        throw new Error(name + "() matrix rows must have the same length");
      }
      const row = [];
      for (let j = 0; j < value[i].length; j++) {
        row.push(neuralAsNumeric(name, value[i][j]));
      }
      matrix.push(row);
    }
    return matrix;
  }

  function mathDot(a, b) {
    if (arguments.length !== 2) {
      throw new Error("dot() expects 2 arguments: (a, b)");
    }
    const left = neuralRequireVector("dot", a, "first");
    const right = neuralRequireVector("dot", b, "second");
    if (left.length === 0 || right.length === 0 || left.length !== right.length) {
      throw new Error("dot() expects two non-empty numeric vectors of the same length");
    }
    let sum = 0;
    for (let i = 0; i < left.length; i++) {
      sum += left[i] * right[i];
    }
    return sum;
  }

  function mathMatMul(a, b) {
    if (arguments.length !== 2) {
      throw new Error("matmul() expects 2 arguments: (a, b)");
    }
    const leftIsMatrix = neuralIsMatrix(a);
    const rightIsMatrix = neuralIsMatrix(b);
    if (!leftIsMatrix && !neuralIsVector(a)) {
      throw new Error("matmul() expects numeric vectors or 2D matrices");
    }
    if (!rightIsMatrix && !neuralIsVector(b)) {
      throw new Error("matmul() expects numeric vectors or 2D matrices");
    }
    if (leftIsMatrix && rightIsMatrix) {
      const left = neuralRequireMatrix("matmul", a, "first");
      const right = neuralRequireMatrix("matmul", b, "second");
      if (left.length === 0 || right.length === 0 || left[0].length === 0 || right[0].length === 0) {
        throw new Error("matmul() expects non-empty matrices");
      }
      if (left[0].length !== right.length) {
        throw new Error("matmul() inner dimensions must match");
      }
      const out = [];
      for (let i = 0; i < left.length; i++) {
        const row = [];
        for (let j = 0; j < right[0].length; j++) {
          let sum = 0;
          for (let k = 0; k < right.length; k++) {
            sum += left[i][k] * right[k][j];
          }
          row.push(sum);
        }
        out.push(row);
      }
      return out;
    }
    if (leftIsMatrix) {
      const left = neuralRequireMatrix("matmul", a, "first");
      const x = neuralRequireVector("matmul", b, "second");
      if (left.length === 0 || left[0].length === 0 || x.length === 0) {
        throw new Error("matmul() expects non-empty matrices");
      }
      if (left[0].length !== x.length) {
        throw new Error("matmul() inner dimensions must match");
      }
      const out = [];
      for (let i = 0; i < left.length; i++) {
        let sum = 0;
        for (let k = 0; k < x.length; k++) {
          sum += left[i][k] * x[k];
        }
        out.push(sum);
      }
      return out;
    }
    const v = neuralRequireVector("matmul", a, "first");
    const m = neuralRequireMatrix("matmul", b, "second");
    if (v.length === 0 || m.length === 0 || m[0].length === 0) {
      throw new Error("matmul() expects non-empty matrices");
    }
    if (v.length !== m.length) {
      throw new Error("matmul() inner dimensions must match");
    }
    const out = [];
    for (let j = 0; j < m[0].length; j++) {
      let sum = 0;
      for (let k = 0; k < v.length; k++) {
        sum += v[k] * m[k][j];
      }
      out.push(sum);
    }
    return out;
  }

  function mathTranspose(matrix) {
    if (arguments.length !== 1) {
      throw new Error("transpose() expects 1 argument: (matrix)");
    }
    const src = neuralRequireMatrix("transpose", matrix, "first");
    if (src.length === 0) {
      return [];
    }
    const out = [];
    for (let j = 0; j < src[0].length; j++) {
      const row = [];
      for (let i = 0; i < src.length; i++) {
        row.push(src[i][j]);
      }
      out.push(row);
    }
    return out;
  }

  function mathSigmoidScalar(x) {
    if (x < -20) {
      return 0;
    }
    if (x > 20) {
      return 1;
    }
    return 1 / (1 + Math.exp(-x));
  }

  function mathMapNumeric(name, value, fn) {
    if (neuralIsNumeric(coerceToFloat(value)) && !Array.isArray(value)) {
      return fn(neuralAsNumeric(name, value));
    }
    if (neuralIsMatrix(value)) {
      const matrix = neuralRequireMatrix(name, value, "first");
      const out = [];
      for (let i = 0; i < matrix.length; i++) {
        const row = [];
        for (let j = 0; j < matrix[i].length; j++) {
          row.push(fn(matrix[i][j]));
        }
        out.push(row);
      }
      return out;
    }
    if (Array.isArray(value)) {
      const vector = neuralRequireVector(name, value, "first");
      const out = [];
      for (let i = 0; i < vector.length; i++) {
        out.push(fn(vector[i]));
      }
      return out;
    }
    throw new Error(name + "() expects a number or a numeric array");
  }

  function mathRelu(x) {
    if (arguments.length !== 1) {
      throw new Error("relu() expects 1 argument: (x)");
    }
    return mathMapNumeric("relu", x, (n) => n > 0 ? n : 0);
  }

  function mathSigmoid(x) {
    if (arguments.length !== 1) {
      throw new Error("sigmoid() expects 1 argument: (x)");
    }
    return mathMapNumeric("sigmoid", x, mathSigmoidScalar);
  }

  function mathTanh(x) {
    if (arguments.length !== 1) {
      throw new Error("tanh() expects 1 argument: (x)");
    }
    return mathMapNumeric("tanh", x, Math.tanh);
  }

  function mathMse(pred, target) {
    if (arguments.length !== 2) {
      throw new Error("mse() expects 2 arguments: (pred, target)");
    }
    if (!Array.isArray(pred) && !Array.isArray(target)) {
      const d = neuralAsNumeric("mse", pred) - neuralAsNumeric("mse", target);
      return d * d;
    }
    const a = neuralRequireVector("mse", pred, "first");
    const b = neuralRequireVector("mse", target, "second");
    if (a.length === 0 || b.length === 0 || a.length !== b.length) {
      throw new Error("mse() expects two non-empty numeric vectors of the same length");
    }
    let sum = 0;
    for (let i = 0; i < a.length; i++) {
      const d = a[i] - b[i];
      sum += d * d;
    }
    return sum / a.length;
  }

  function cloneAnnealState(value) {
    if (Array.isArray(value)) {
      return value.map(cloneAnnealState);
    }
    if (isObject(value)) {
      const copy = {};
      const keys = Object.keys(value);
      for (let i = 0; i < keys.length; i++) {
        copy[keys[i]] = cloneAnnealState(value[keys[i]]);
      }
      return markDict(copy);
    }
    return value;
  }

  function requireAnnealFunction(value, role) {
    if (typeof value !== "function") {
      throw new Error("anneal() " + role + " must be a function");
    }
    return value;
  }

  function annealAsNumber(value, message) {
    const n = coerceToFloat(value);
    if (!Number.isFinite(n)) {
      throw new Error(message);
    }
    return n;
  }

  function mathAnneal(initial, cost, neighbor, options) {
    if (arguments.length < 3 || arguments.length > 4) {
      throw new Error("anneal() expects 3-4 arguments: (initial, cost, neighbor, options?)");
    }
    const costFn = requireAnnealFunction(cost, "cost");
    const neighborFn = requireAnnealFunction(neighbor, "neighbor");
    const opts = options && typeof options === "object" && !Array.isArray(options) ? options : {};
    let steps = 1000;
    if (Object.prototype.hasOwnProperty.call(opts, "steps")) {
      steps = coerceToInt(opts.steps);
    }
    if (steps < 0) {
      throw new Error("anneal() steps must be >= 0");
    }
    let temp = 1.0;
    if (Object.prototype.hasOwnProperty.call(opts, "temp")) {
      temp = annealAsNumber(opts.temp, "anneal() temp must be a number");
    } else if (Object.prototype.hasOwnProperty.call(opts, "temperature")) {
      temp = annealAsNumber(opts.temperature, "anneal() temp must be a number");
    }
    if (!Number.isFinite(temp) || temp < 0) {
      throw new Error("anneal() temp must be a finite number >= 0");
    }
    let coolingFactor = 0.995;
    let scheduleFn = null;
    if (typeof opts.schedule === "function") {
      scheduleFn = opts.schedule;
    }
    if (Object.prototype.hasOwnProperty.call(opts, "cooling")) {
      if (typeof opts.cooling === "function") {
        if (scheduleFn == null) scheduleFn = opts.cooling;
      } else {
        coolingFactor = annealAsNumber(opts.cooling, "anneal() cooling must be a number or a function");
      }
    }
    const maximize = Object.prototype.hasOwnProperty.call(opts, "maximize") ? isTruthy(opts.maximize) : false;
    const copy = Object.prototype.hasOwnProperty.call(opts, "copy") ? isTruthy(opts.copy) : true;
    let current = copy ? cloneAnnealState(initial) : initial;
    let currentCost = annealAsNumber(costFn(current), "anneal() cost must return a finite number");
    let best = copy ? cloneAnnealState(current) : current;
    let bestCost = currentCost;
    for (let step = 0; step < steps; step++) {
      const candidate = neighborFn(copy ? cloneAnnealState(current) : current);
      const candidateCost = annealAsNumber(costFn(candidate), "anneal() cost must return a finite number");
      const delta = maximize ? currentCost - candidateCost : candidateCost - currentCost;
      let accept = delta <= 0;
      if (!accept && temp > 0) {
        accept = nextRandomUnit() < Math.exp(-delta / temp);
      }
      if (accept) {
        current = candidate;
        currentCost = candidateCost;
        const improved = maximize ? currentCost > bestCost : currentCost < bestCost;
        if (improved) {
          best = copy ? cloneAnnealState(current) : current;
          bestCost = currentCost;
        }
      }
      if (scheduleFn) {
        temp = annealAsNumber(scheduleFn(temp), "anneal() cooling/schedule must return a finite number >= 0");
        if (temp < 0) {
          throw new Error("anneal() cooling/schedule must return a finite number >= 0");
        }
      } else {
        temp = temp * coolingFactor;
      }
    }
    return markDict({
      state: best,
      cost: bestCost,
      steps: steps
    });
  }

  const NN_ACTIVATIONS = "relu, leakyRelu, elu, gelu, silu, softplus, sigmoid, tanh, or linear";
  const NN_GELU_K = 0.7978845608028654;
  const NN_GELU_C = 0.044715;

  function nnArity(name, length, min, max, signature) {
    if (length < min || length > max) {
      const suffix = signature ? ": (" + signature + ")" : "";
      let count;
      if (min === max) {
        const plural = min === 1 ? "argument" : "arguments";
        count = min + " " + plural;
      } else {
        count = min + "-" + max + " arguments";
      }
      throw new Error(name + "() expects " + count + suffix);
    }
  }

  function nnAlpha(name, supplied, value, fallback) {
    return supplied ? neuralAsNumeric(name, value) : fallback;
  }

  function nnSoftplusScalar(x) {
    if (x > 20) return x;
    if (x < -20) return Math.exp(x);
    return Math.log(1 + Math.exp(x));
  }

  function nnGeluScalar(x) {
    const inner = NN_GELU_K * (x + NN_GELU_C * x * x * x);
    return 0.5 * x * (1 + Math.tanh(inner));
  }

  function nnDGeluScalar(x) {
    const inner = NN_GELU_K * (x + NN_GELU_C * x * x * x);
    const tanh = Math.tanh(inner);
    const dInner = NN_GELU_K * (1 + 3 * NN_GELU_C * x * x);
    return 0.5 * (1 + tanh) + 0.5 * x * (1 - tanh * tanh) * dInner;
  }

  function nnDSiluScalar(x) {
    const s = mathSigmoidScalar(x);
    return s * (1 + x * (1 - s));
  }

  function nnLeakyRelu(x, alpha) {
    nnArity("leakyRelu", arguments.length, 1, 2, "x, alpha?");
    const a = nnAlpha("leakyRelu", arguments.length >= 2, alpha, 0.01);
    return mathMapNumeric("leakyRelu", x, (n) => n > 0 ? n : a * n);
  }

  function nnElu(x, alpha) {
    nnArity("elu", arguments.length, 1, 2, "x, alpha?");
    const a = nnAlpha("elu", arguments.length >= 2, alpha, 1);
    return mathMapNumeric("elu", x, (n) => n > 0 ? n : a * (Math.exp(n) - 1));
  }

  function nnGelu(x) {
    nnArity("gelu", arguments.length, 1, 1, "x");
    return mathMapNumeric("gelu", x, nnGeluScalar);
  }

  function nnSilu(x) {
    nnArity("silu", arguments.length, 1, 1, "x");
    return mathMapNumeric("silu", x, (n) => n * mathSigmoidScalar(n));
  }

  function nnSoftplus(x) {
    nnArity("softplus", arguments.length, 1, 1, "x");
    return mathMapNumeric("softplus", x, nnSoftplusScalar);
  }

  function nnDRelu(x) {
    nnArity("dRelu", arguments.length, 1, 1, "x");
    return mathMapNumeric("dRelu", x, (n) => n > 0 ? 1 : 0);
  }

  function nnDLeakyRelu(x, alpha) {
    nnArity("dLeakyRelu", arguments.length, 1, 2, "x, alpha?");
    const a = nnAlpha("dLeakyRelu", arguments.length >= 2, alpha, 0.01);
    return mathMapNumeric("dLeakyRelu", x, (n) => n > 0 ? 1 : a);
  }

  function nnDElu(x, alpha) {
    nnArity("dElu", arguments.length, 1, 2, "x, alpha?");
    const a = nnAlpha("dElu", arguments.length >= 2, alpha, 1);
    return mathMapNumeric("dElu", x, (n) => n > 0 ? 1 : a * Math.exp(n));
  }

  function nnDGelu(x) {
    nnArity("dGelu", arguments.length, 1, 1, "x");
    return mathMapNumeric("dGelu", x, nnDGeluScalar);
  }

  function nnDSilu(x) {
    nnArity("dSilu", arguments.length, 1, 1, "x");
    return mathMapNumeric("dSilu", x, nnDSiluScalar);
  }

  function nnDSoftplus(x) {
    nnArity("dSoftplus", arguments.length, 1, 1, "x");
    return mathMapNumeric("dSoftplus", x, mathSigmoidScalar);
  }

  function nnDSigmoid(x) {
    nnArity("dSigmoid", arguments.length, 1, 1, "x");
    return mathMapNumeric("dSigmoid", x, (n) => {
      const s = mathSigmoidScalar(n);
      return s * (1 - s);
    });
  }

  function nnDTanh(x) {
    nnArity("dTanh", arguments.length, 1, 1, "x");
    return mathMapNumeric("dTanh", x, (n) => {
      const t = Math.tanh(n);
      return 1 - t * t;
    });
  }

  function nnNormalizeActivation(name, activation) {
    if (activation === undefined || activation === null || activation === "linear") return null;
    if (activation === "relu" || activation === "leakyRelu" || activation === "elu" || activation === "gelu" ||
        activation === "silu" || activation === "softplus" || activation === "sigmoid" || activation === "tanh") {
      return activation;
    }
    throw new Error(name + "() unknown activation '" + activation + "'; expected " + NN_ACTIVATIONS);
  }

  function nnApplyActivation(activation, x) {
    if (activation == null) return x;
    if (activation === "relu") return x > 0 ? x : 0;
    if (activation === "leakyRelu") return x > 0 ? x : 0.01 * x;
    if (activation === "elu") return x > 0 ? x : (Math.exp(x) - 1);
    if (activation === "gelu") return nnGeluScalar(x);
    if (activation === "silu") return x * mathSigmoidScalar(x);
    if (activation === "softplus") return nnSoftplusScalar(x);
    if (activation === "sigmoid") return mathSigmoidScalar(x);
    if (activation === "tanh") return Math.tanh(x);
    throw new Error("dense() unknown activation '" + activation + "'");
  }

  function nnApplyDerivative(activation, x) {
    if (activation == null) return 1;
    if (activation === "relu") return x > 0 ? 1 : 0;
    if (activation === "leakyRelu") return x > 0 ? 1 : 0.01;
    if (activation === "elu") return x > 0 ? 1 : Math.exp(x);
    if (activation === "gelu") return nnDGeluScalar(x);
    if (activation === "silu") return nnDSiluScalar(x);
    if (activation === "softplus") return mathSigmoidScalar(x);
    if (activation === "sigmoid") {
      const s = mathSigmoidScalar(x);
      return s * (1 - s);
    }
    if (activation === "tanh") {
      const t = Math.tanh(x);
      return 1 - t * t;
    }
    throw new Error("denseBackward() unknown activation '" + activation + "'");
  }

  function nnResolveBias(name, biasValue, outFeatures) {
    if (biasValue == null) {
      const zeros = [];
      for (let j = 0; j < outFeatures; j++) zeros.push(0);
      return zeros;
    }
    const bias = neuralRequireVector(name, biasValue, "bias");
    if (bias.length !== outFeatures) {
      throw new Error(name + "() bias length must match the output size");
    }
    return bias;
  }

  function nnDense(x, weights, bias, activation) {
    nnArity("dense", arguments.length, 2, 4, "x, weights, bias?, activation?");
    let biasValue = null;
    let activationValue = null;
    if (arguments.length >= 3) {
      if (typeof bias === "string") {
        if (arguments.length > 3) throw new Error("dense() activation is the last argument");
        activationValue = bias;
      } else {
        biasValue = bias;
        if (arguments.length === 4) {
          if (typeof activation !== "string") throw new Error("dense() activation must be a string");
          activationValue = activation;
        }
      }
    }
    const act = nnNormalizeActivation("dense", activationValue);
    const w = neuralRequireMatrix("dense", weights, "weights");
    const b = nnResolveBias("dense", biasValue, w[0].length);
    if (neuralIsMatrix(x)) {
      const batch = neuralRequireMatrix("dense", x, "x");
      if (batch[0].length !== w.length) throw new Error("dense() inner dimensions must match");
      const pre = [];
      const out = [];
      for (let row = 0; row < batch.length; row++) {
        const preRow = [];
        const outRow = [];
        for (let j = 0; j < b.length; j++) {
          let sum = b[j];
          for (let i = 0; i < w.length; i++) sum += batch[row][i] * w[i][j];
          preRow.push(sum);
          outRow.push(nnApplyActivation(act, sum));
        }
        pre.push(preRow);
        out.push(outRow);
      }
      return markDict({ pre: pre, out: out });
    }
    const vector = neuralRequireVector("dense", x, "x");
    if (vector.length === 0) throw new Error("dense() expects non-empty input");
    if (vector.length !== w.length) throw new Error("dense() inner dimensions must match");
    const pre = [];
    const out = [];
    for (let j = 0; j < b.length; j++) {
      let sum = b[j];
      for (let i = 0; i < w.length; i++) sum += vector[i] * w[i][j];
      pre.push(sum);
      out.push(nnApplyActivation(act, sum));
    }
    return markDict({ pre: pre, out: out });
  }

  function nnDenseBackward(x, weights, upstream, activation, pre) {
    nnArity("denseBackward", arguments.length, 3, 5, "x, weights, upstream, activation?, pre?");
    let activationValue = null;
    let preValue = null;
    if (arguments.length >= 4) {
      if (typeof activation !== "string") throw new Error("denseBackward() activation must be a string");
      activationValue = activation;
      if (arguments.length === 5) preValue = pre;
    }
    const act = nnNormalizeActivation("denseBackward", activationValue);
    if (act != null && preValue == null) {
      throw new Error("denseBackward() pre is required when activation is not linear");
    }
    const w = neuralRequireMatrix("denseBackward", weights, "weights");
    const outFeatures = w[0].length;
    if (neuralIsMatrix(x)) {
      const batch = neuralRequireMatrix("denseBackward", x, "x");
      if (batch[0].length !== w.length) throw new Error("denseBackward() inner dimensions must match");
      const up = neuralRequireMatrix("denseBackward", upstream, "upstream");
      if (up.length !== batch.length || up[0].length !== outFeatures) {
        throw new Error("denseBackward() upstream shape must match the layer output");
      }
      const preMatrix = preValue == null ? up : neuralRequireMatrix("denseBackward", preValue, "pre");
      if (preValue != null && (preMatrix.length !== up.length || preMatrix[0].length !== up[0].length)) {
        throw new Error("denseBackward() pre shape must match upstream");
      }
      const dZ = [];
      for (let row = 0; row < up.length; row++) {
        const dzRow = [];
        for (let j = 0; j < outFeatures; j++) {
          dzRow.push(up[row][j] * nnApplyDerivative(act, preMatrix[row][j]));
        }
        dZ.push(dzRow);
      }
      const dW = [];
      for (let i = 0; i < w.length; i++) {
        const row = [];
        for (let j = 0; j < outFeatures; j++) {
          let sum = 0;
          for (let b = 0; b < batch.length; b++) sum += batch[b][i] * dZ[b][j];
          row.push(sum);
        }
        dW.push(row);
      }
      const dBias = [];
      for (let j = 0; j < outFeatures; j++) {
        let sum = 0;
        for (let b = 0; b < batch.length; b++) sum += dZ[b][j];
        dBias.push(sum);
      }
      const dInput = [];
      for (let b = 0; b < batch.length; b++) {
        const row = [];
        for (let i = 0; i < w.length; i++) {
          let sum = 0;
          for (let j = 0; j < outFeatures; j++) sum += dZ[b][j] * w[i][j];
          row.push(sum);
        }
        dInput.push(row);
      }
      return markDict({ dInput: dInput, dWeights: dW, dBias: dBias });
    }
    const vector = neuralRequireVector("denseBackward", x, "x");
    if (vector.length === 0) throw new Error("denseBackward() expects non-empty input");
    if (vector.length !== w.length) throw new Error("denseBackward() inner dimensions must match");
    const up = neuralRequireVector("denseBackward", upstream, "upstream");
    if (up.length !== outFeatures) throw new Error("denseBackward() upstream shape must match the layer output");
    const preRow = preValue == null ? up : neuralRequireVector("denseBackward", preValue, "pre");
    if (preValue != null && preRow.length !== up.length) {
      throw new Error("denseBackward() pre shape must match upstream");
    }
    const dZ = [];
    for (let j = 0; j < outFeatures; j++) dZ.push(up[j] * nnApplyDerivative(act, preRow[j]));
    const dW = [];
    for (let i = 0; i < w.length; i++) {
      const row = [];
      for (let j = 0; j < outFeatures; j++) row.push(vector[i] * dZ[j]);
      dW.push(row);
    }
    const dInput = [];
    for (let i = 0; i < w.length; i++) {
      let sum = 0;
      for (let j = 0; j < outFeatures; j++) sum += dZ[j] * w[i][j];
      dInput.push(sum);
    }
    return markDict({ dInput: dInput, dWeights: dW, dBias: dZ });
  }

  function nnMseGrad(pred, target) {
    nnArity("mseGrad", arguments.length, 2, 2, "pred, target");
    if (!Array.isArray(pred) && !Array.isArray(target)) {
      return neuralAsNumeric("mseGrad", pred) - neuralAsNumeric("mseGrad", target);
    }
    if (neuralIsMatrix(pred) || neuralIsMatrix(target)) {
      const a = neuralRequireMatrix("mseGrad", pred, "pred");
      const b = neuralRequireMatrix("mseGrad", target, "target");
      if (a.length !== b.length || a[0].length !== b[0].length) {
        throw new Error("mseGrad() expects pred and target with the same shape");
      }
      const grad = [];
      for (let i = 0; i < a.length; i++) {
        if (a[i].length !== b[i].length) throw new Error("mseGrad() expects pred and target with the same shape");
        const row = [];
        for (let j = 0; j < a[i].length; j++) row.push(a[i][j] - b[i][j]);
        grad.push(row);
      }
      return grad;
    }
    const a = neuralRequireVector("mseGrad", pred, "pred");
    const b = neuralRequireVector("mseGrad", target, "target");
    if (a.length === 0 || a.length !== b.length) {
      throw new Error("mseGrad() expects pred and target with the same shape");
    }
    const grad = [];
    for (let i = 0; i < a.length; i++) grad.push(a[i] - b[i]);
    return grad;
  }

  function nnSoftmax(array, temperature) {
    nnArity("softmax", arguments.length, 1, 2, "array, temperature?");
    if (!Array.isArray(array) || array.length === 0 || Array.isArray(array[0])) {
      throw new Error("softmax() expects a non-empty array");
    }
    let temp = 1;
    if (arguments.length === 2) temp = neuralAsNumeric("softmax", temperature);
    if (!(temp > 0)) throw new Error("softmax() temperature must be > 0");
    let maxVal = neuralAsNumeric("softmax", array[0]) / temp;
    for (let i = 1; i < array.length; i++) {
      const scaled = neuralAsNumeric("softmax", array[i]) / temp;
      if (scaled > maxVal) maxVal = scaled;
    }
    const exps = [];
    let sumExp = 0;
    for (let i = 0; i < array.length; i++) {
      const e = Math.exp(neuralAsNumeric("softmax", array[i]) / temp - maxVal);
      exps.push(e);
      sumExp += e;
    }
    const probs = [];
    for (let i = 0; i < exps.length; i++) probs.push(exps[i] / sumExp);
    return probs;
  }

  function nnCrossEntropyFromLogits(logits, targetIndex) {
    nnArity("crossEntropyFromLogits", arguments.length, 2, 2, "logits, targetIndex");
    if (!Array.isArray(logits) || logits.length === 0) {
      throw new Error("crossEntropyFromLogits() expects non-empty logits");
    }
    const index = neuralAsNumeric("crossEntropyFromLogits", targetIndex);
    if (!Number.isInteger(index)) {
      throw new Error("crossEntropyFromLogits() expects integer targetIndex");
    }
    if (index < 0 || index >= logits.length) {
      throw new Error("crossEntropyFromLogits() targetIndex out of range");
    }
    let maxVal = neuralAsNumeric("crossEntropyFromLogits", logits[0]);
    for (let i = 1; i < logits.length; i++) {
      const v = neuralAsNumeric("crossEntropyFromLogits", logits[i]);
      if (v > maxVal) maxVal = v;
    }
    let sumExp = 0;
    for (let i = 0; i < logits.length; i++) {
      sumExp += Math.exp(neuralAsNumeric("crossEntropyFromLogits", logits[i]) - maxVal);
    }
    return maxVal + Math.log(sumExp) - neuralAsNumeric("crossEntropyFromLogits", logits[index]);
  }

  function nnSoftmaxGrad(logits, target) {
    nnArity("softmaxGrad", arguments.length, 2, 2, "logits, target");
    const probs = nnSoftmax(logits);
    if (Array.isArray(target)) {
      const t = neuralRequireVector("softmaxGrad", target, "target");
      if (t.length !== probs.length) throw new Error("softmaxGrad() target length must match logits");
      const grad = [];
      for (let i = 0; i < probs.length; i++) grad.push(probs[i] - t[i]);
      return grad;
    }
    const index = neuralAsNumeric("softmaxGrad", target);
    if (!Number.isInteger(index)) throw new Error("softmaxGrad() target must be a class index or a numeric vector");
    if (index < 0 || index >= probs.length) throw new Error("softmaxGrad() target index out of range");
    const grad = [];
    for (let i = 0; i < probs.length; i++) grad.push(i === index ? probs[i] - 1 : probs[i]);
    return grad;
  }

  function mathRsqrt(value) {
    if (arguments.length !== 1) {
      throw new Error("rsqrt() expects 1 argument");
    }
    return 1 / Math.sqrt(neuralAsNumeric("rsqrt", value));
  }

  function mathRandn(std, mean) {
    if (arguments.length > 2) {
      throw new Error("randn() expects 0-2 arguments: (std?, mean?)");
    }
    const deviation = arguments.length >= 1 ? neuralAsNumeric("randn", std) : 1;
    const center = arguments.length === 2 ? neuralAsNumeric("randn", mean) : 0;
    // Box-Muller, same shared generator as random / seed.
    const u1 = Math.max(nextRandomUnit(), 1e-12);
    const u2 = nextRandomUnit();
    const z0 = Math.sqrt(-2 * Math.log(u1)) * Math.cos(2 * Math.PI * u2);
    return center + z0 * deviation;
  }

  function mathExtremeIndex(name, array, preferGreater) {
    if (!Array.isArray(array)) {
      throw new Error(name + "() expects an array argument");
    }
    if (array.length === 0) {
      throw new Error(name + "() expects a non-empty array");
    }
    let bestIdx = 0;
    let bestVal = neuralAsNumeric(name, array[0]);
    for (let i = 1; i < array.length; i++) {
      const v = neuralAsNumeric(name, array[i]);
      if (preferGreater ? v > bestVal : v < bestVal) {
        bestVal = v;
        bestIdx = i;
      }
    }
    return bestIdx;
  }

  function mathArgmax(array) {
    if (arguments.length !== 1) {
      throw new Error("argmax() expects 1 argument: (array)");
    }
    return mathExtremeIndex("argmax", array, true);
  }

  function mathArgmin(array) {
    if (arguments.length !== 1) {
      throw new Error("argmin() expects 1 argument: (array)");
    }
    return mathExtremeIndex("argmin", array, false);
  }

  function mathLogSumExp(array) {
    if (arguments.length !== 1) {
      throw new Error("logSumExp() expects 1 argument: (array)");
    }
    if (!Array.isArray(array)) {
      throw new Error("logSumExp() expects an array argument");
    }
    if (array.length === 0) {
      throw new Error("logSumExp() expects a non-empty array");
    }
    let maxVal = neuralAsNumeric("logSumExp", array[0]);
    for (let i = 1; i < array.length; i++) {
      const v = neuralAsNumeric("logSumExp", array[i]);
      if (v > maxVal) maxVal = v;
    }
    let sumExp = 0;
    for (let i = 0; i < array.length; i++) {
      sumExp += Math.exp(neuralAsNumeric("logSumExp", array[i]) - maxVal);
    }
    return maxVal + Math.log(sumExp);
  }

  function mathRandomChoiceWeighted(weights) {
    if (arguments.length !== 1) {
      throw new Error("randomChoiceWeighted() expects 1 argument: (weights)");
    }
    if (!Array.isArray(weights)) {
      throw new Error("randomChoiceWeighted() expects an array argument");
    }
    if (weights.length === 0) {
      throw new Error("randomChoiceWeighted() expects a non-empty array");
    }
    const values = [];
    let total = 0;
    for (let i = 0; i < weights.length; i++) {
      const w = neuralAsNumeric("randomChoiceWeighted", weights[i]);
      if (w < 0) {
        throw new Error("randomChoiceWeighted() weights must be >= 0");
      }
      values.push(w);
      total += w;
    }
    if (!(total > 0)) {
      throw new Error("randomChoiceWeighted() sum of weights must be > 0");
    }
    let r = nextRandomUnit() * total;
    let cumulative = 0;
    for (let i = 0; i < values.length; i++) {
      cumulative += values[i];
      if (r <= cumulative) return i;
    }
    return values.length - 1;
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
    zeros: mathZeros,
    rsqrt: mathRsqrt,
    randn: mathRandn,
    argmax: mathArgmax,
    argmin: mathArgmin,
    logSumExp: mathLogSumExp,
    softmax: nnSoftmax,
    dot: mathDot,
    matmul: mathMatMul,
    transpose: mathTranspose,
    sigmoid: mathSigmoid,
    tanh: mathTanh,
    randomChoiceWeighted: mathRandomChoiceWeighted,
    random: randomBuiltin,
    randomInt: randomIntBuiltin,
    randomFloat: randomFloatBuiltin,
    seed: seedBuiltin,
    anneal: mathAnneal
  };

  const NN_DENSE_ACTIVATIONS = ["relu", "leakyRelu", "elu", "gelu", "silu", "softplus", "sigmoid", "tanh", "linear"];

  function nnRequirePositiveInt(name, value, which) {
    if (typeof value !== "number" || !Number.isFinite(value) || Math.floor(value) !== value) {
      throw new Error(name + "() " + which + " must be an integer");
    }
    if (value <= 0) throw new Error(name + "() " + which + " must be > 0");
    return value;
  }

  function nnRequireFinite(name, value, which) {
    const number = neuralAsNumeric(name, value);
    if (!Number.isFinite(number)) throw new Error(name + "() " + which + " must be finite");
    return number;
  }

  function nnApplySgdVector(values, grad, learningRate) {
    for (let i = 0; i < values.length; i++) {
      values[i] = values[i] - learningRate * grad[i];
    }
  }

  function nnApplySgdMatrix(weights, grad, learningRate) {
    for (let i = 0; i < weights.length; i++) {
      nnApplySgdVector(weights[i], grad[i], learningRate);
    }
  }

  function Dense(inFeatures, outFeatures, activation, scale) {
    const argc = arguments.length;
    if (argc < 2 || argc > 4) {
      throw new Error("Dense() expects 2 to 4 arguments: (inFeatures, outFeatures, activation?, scale?)");
    }
    this.inFeatures = nnRequirePositiveInt("Dense", inFeatures, "inFeatures");
    this.outFeatures = nnRequirePositiveInt("Dense", outFeatures, "outFeatures");
    this.activation = "linear";
    if (argc >= 3) {
      if (typeof activation !== "string" || NN_DENSE_ACTIVATIONS.indexOf(activation) < 0) {
        throw new Error("Dense() unknown activation '" + activation + "'");
      }
      this.activation = activation;
    }
    let width = 1 / Math.sqrt(this.inFeatures);
    if (argc === 4) {
      width = nnRequireFinite("Dense", scale, "scale");
      if (width < 0) throw new Error("Dense() scale must be >= 0");
    }
    const weights = [];
    for (let i = 0; i < this.inFeatures; i++) {
      const row = [];
      for (let j = 0; j < this.outFeatures; j++) row.push(randomFloatBuiltin(-width, width));
      weights.push(row);
    }
    const bias = [];
    for (let j = 0; j < this.outFeatures; j++) bias.push(randomFloatBuiltin(-width, width));
    this.weights = weights;
    this.bias = bias;
    this._lastInput = null;
    this._pre = null;
    this._dWeights = null;
    this._dBias = null;
  }

  Dense.prototype.forward = function (x) {
    if (arguments.length !== 1) throw new Error("Dense.forward() expects 1 argument: (x)");
    if (neuralIsMatrix(x)) throw new Error("Dense.forward() expects a numeric vector");
    const result = this.activation === "linear"
      ? nnDense(x, this.weights, this.bias)
      : nnDense(x, this.weights, this.bias, this.activation);
    this._lastInput = x;
    this._pre = result.pre;
    return result.out;
  };

  Dense.prototype.backward = function (upstream) {
    if (arguments.length !== 1) throw new Error("Dense.backward() expects 1 argument: (upstream)");
    if (this._lastInput == null || this._pre == null) {
      throw new Error("Dense.backward() requires forward() first");
    }
    const result = this.activation === "linear"
      ? nnDenseBackward(this._lastInput, this.weights, upstream)
      : nnDenseBackward(this._lastInput, this.weights, upstream, this.activation, this._pre);
    this._dWeights = result.dWeights;
    this._dBias = result.dBias;
    return result.dInput;
  };

  Dense.prototype.sgd = function (learningRate) {
    if (arguments.length !== 1) throw new Error("Dense.sgd() expects 1 argument: (lr)");
    if (this._dWeights == null || this._dBias == null) {
      throw new Error("Dense.sgd() requires backward() first");
    }
    const step = nnRequireFinite("Dense.sgd", learningRate, "lr");
    nnApplySgdMatrix(this.weights, this._dWeights, step);
    nnApplySgdVector(this.bias, this._dBias, step);
    return null;
  };

  function Sequential(layers) {
    if (arguments.length !== 1 || !Array.isArray(layers) || layers.length === 0) {
      throw new Error("Sequential() expects 1 argument: (layers)");
    }
    for (let i = 0; i < layers.length; i++) {
      if (!(layers[i] instanceof Dense)) {
        throw new Error("Sequential() expects an array of Dense layers");
      }
    }
    this.layers = layers;
  }

  Sequential.prototype.forward = function (x) {
    if (arguments.length !== 1) throw new Error("Sequential.forward() expects 1 argument: (x)");
    let current = x;
    for (let i = 0; i < this.layers.length; i++) current = this.layers[i].forward(current);
    return current;
  };

  Sequential.prototype.backward = function (upstream) {
    if (arguments.length !== 1) throw new Error("Sequential.backward() expects 1 argument: (upstream)");
    let grad = upstream;
    for (let i = this.layers.length - 1; i >= 0; i--) grad = this.layers[i].backward(grad);
    return grad;
  };

  Sequential.prototype.sgd = function (learningRate) {
    if (arguments.length !== 1) throw new Error("Sequential.sgd() expects 1 argument: (lr)");
    for (let i = 0; i < this.layers.length; i++) this.layers[i].sgd(learningRate);
    return null;
  };

  Sequential.prototype.fit = function (inputs, targets, epochs, learningRate, lossName) {
    const argc = arguments.length;
    if (argc < 4 || argc > 5) {
      throw new Error("Sequential.fit() expects 4 or 5 arguments: (inputs, targets, epochs, lr, loss?)");
    }
    if (!Array.isArray(inputs) || !Array.isArray(targets) || inputs.length === 0 || inputs.length !== targets.length) {
      throw new Error("Sequential.fit() inputs and targets must be non-empty and the same length");
    }
    const steps = nnRequirePositiveInt("Sequential.fit", epochs, "epochs");
    const rate = nnRequireFinite("Sequential.fit", learningRate, "lr");
    const loss = argc === 5 ? lossName : "mse";
    if (loss !== "mse" && loss !== "crossEntropy") {
      throw new Error("Sequential.fit() loss must be \"mse\" or \"crossEntropy\"");
    }
    let mean = 0;
    for (let epoch = 0; epoch < steps; epoch++) {
      let total = 0;
      for (let sample = 0; sample < inputs.length; sample++) {
        const output = this.forward(inputs[sample]);
        let upstream;
        if (loss === "mse") {
          let target = targets[sample];
          if (!Array.isArray(target)) {
            if (output.length !== 1) throw new Error("Sequential.fit() scalar targets require one output");
            target = [target];
          }
          total += mathMse(output, target);
          upstream = nnMseGrad(output, target);
        } else {
          total += nnCrossEntropyFromLogits(output, targets[sample]);
          upstream = nnSoftmaxGrad(output, targets[sample]);
        }
        this.backward(upstream);
        this.sgd(rate);
      }
      mean = total / inputs.length;
    }
    return mean;
  };

  function nnSequential(layers) {
    nnArity("sequential", arguments.length, 1, 1, "layers");
    if (!Array.isArray(layers) || layers.length === 0) {
      throw new Error("sequential() expects an array of [in, out, activation?, scale?]");
    }
    const built = [];
    for (let i = 0; i < layers.length; i++) {
      const row = layers[i];
      if (!Array.isArray(row) || row.length < 2 || row.length > 4) {
        throw new Error("sequential() expects an array of [in, out, activation?, scale?]");
      }
      if (row.length === 2) built.push(new Dense(row[0], row[1]));
      else if (row.length === 3) built.push(new Dense(row[0], row[1], row[2]));
      else built.push(new Dense(row[0], row[1], row[2], row[3]));
    }
    return new Sequential(built);
  }

  const nnStdLib = {
    relu: mathRelu,
    sigmoid: mathSigmoid,
    tanh: mathTanh,
    mse: mathMse,
    softmax: nnSoftmax,
    crossEntropyFromLogits: nnCrossEntropyFromLogits,
    leakyRelu: nnLeakyRelu,
    elu: nnElu,
    gelu: nnGelu,
    silu: nnSilu,
    softplus: nnSoftplus,
    dRelu: nnDRelu,
    dLeakyRelu: nnDLeakyRelu,
    dElu: nnDElu,
    dGelu: nnDGelu,
    dSilu: nnDSilu,
    dSoftplus: nnDSoftplus,
    dSigmoid: nnDSigmoid,
    dTanh: nnDTanh,
    dense: nnDense,
    denseBackward: nnDenseBackward,
    mseGrad: nnMseGrad,
    softmaxGrad: nnSoftmaxGrad,
    sequential: nnSequential
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
      // Class instances: own enumerable fields only (methods live on the prototype).
      // Matches interpreter toJSON: public instance data, no type tag.
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
      const ctors = sumTypeRegistry[trimmed];
      const tag = isVariant(value) ? value.tag : (value && value.tag);
      if (typeof tag !== "string") {
        return path + ".tag is required and must be a string constructor name.";
      }
      if (!findSumConstructor(ctors, tag)) {
        return path + ".tag '" + tag + "' is not a known constructor. Expected one of: " + sumConstructorTags(ctors).join(", ") + ".";
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

  function normalizeSumConstructors(raw) {
    if (!Array.isArray(raw)) return [];
    return raw.map(function (item) {
      if (typeof item === "string") return { tag: item, params: [] };
      const tag = coerceToString(item && item.tag);
      const params = Array.isArray(item && item.params)
        ? item.params.map(function (name) { return coerceToString(name); })
        : [];
      return { tag: tag, params: params };
    });
  }

  function findSumConstructor(ctors, tag) {
    for (let i = 0; i < ctors.length; i++) {
      if (ctors[i].tag === tag) return ctors[i];
    }
    return null;
  }

  function sumConstructorTags(ctors) {
    return ctors.map(function (ctor) { return ctor.tag; });
  }

  function objectPayloadForConstructor(value, ctor) {
    if (ctor.params.length > 0) {
      return ctor.params.map(function (name) {
        return objectHasKey(value, name) ? value[name] : null;
      });
    }
    return Object.keys(value).filter(function (key) {
      return key !== "tag";
    }).map(function (key) {
      return value[key];
    });
  }

  const schemaStdLib = {
    register(name, fields) {
      schemaRegistry[coerceToString(name)] = Array.isArray(fields) ? fields.slice() : [];
      return null;
    },
    registerSumType(name, constructors) {
      sumTypeRegistry[coerceToString(name)] = normalizeSumConstructors(constructors);
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
    },
    asVariant(typeName, value) {
      if (arguments.length !== 2) {
        throw new Error("asVariant() expects 2 arguments: (typeName, value)");
      }
      const name = coerceToString(typeName);
      if (Object.prototype.hasOwnProperty.call(schemaRegistry, name) &&
          !Object.prototype.hasOwnProperty.call(sumTypeRegistry, name)) {
        throw new Error("asVariant() expects a sum type; '" + name + "' is not a sum type.");
      }
      if (!Object.prototype.hasOwnProperty.call(sumTypeRegistry, name)) {
        throw new Error("Unknown schema '" + name + "'.");
      }
      const ctors = sumTypeRegistry[name];
      if (isVariant(value)) {
        if (!findSumConstructor(ctors, value.tag)) {
          throw new Error("asVariant() failed: $.tag '" + value.tag + "' is not a known constructor. Expected one of: " + sumConstructorTags(ctors).join(", ") + ".");
        }
        return value;
      }
      if (!isObject(value)) {
        throw new Error("asVariant() failed: $ must be a JSON object with a sum-type tag.");
      }
      const tag = value.tag;
      if (typeof tag !== "string") {
        throw new Error("asVariant() failed: $.tag is required and must be a string constructor name.");
      }
      const ctor = findSumConstructor(ctors, tag);
      if (!ctor) {
        throw new Error("asVariant() failed: $.tag '" + tag + "' is not a known constructor. Expected one of: " + sumConstructorTags(ctors).join(", ") + ".");
      }
      return variant(tag, objectPayloadForConstructor(value, ctor));
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
    nn: nnStdLib,
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
        pendingMousePressed: new Set(),
        pendingMouseReleased: new Set(),
        mouseButtonsPressed: new Set(),
        mouseButtonsReleased: new Set(),
        mouseX: 0,
        mouseY: 0,
        touches: new Map(),
        gamepadConnected: new Set(),
        gamepadButtonsDown: new Set(),
        gamepadButtonsPrev: new Set(),
        gamepadButtonsPressed: new Set(),
        gamepadButtonsReleased: new Set(),
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
        cameraZoom: 1,
        cameraStack: [],
        alpha: 1,
        blend: "alpha",
        blendOp: "source-over",
        pixelated: false,
        imageCache: new Map(),
        tintCanvas: null,
        tintContext: null
      };

      const BLEND_MODES = {
        alpha: { name: "alpha", op: "source-over" },
        "source-over": { name: "alpha", op: "source-over" },
        add: { name: "add", op: "lighter" },
        lighter: { name: "add", op: "lighter" },
        multiply: { name: "multiply", op: "multiply" },
        screen: { name: "screen", op: "screen" }
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

      function resolvePlaybackRate(value) {
        const numeric = toFiniteNumber(value, 1);
        if (!(numeric > 0)) {
          return 1;
        }
        return Math.min(4, Math.max(0.25, numeric));
      }

      function startSamplePlayback(context, buffer, url, volume, loop, pan, playbackRate) {
        if (!context || !state.audioMasterGain || !buffer) return;

        const source = context.createBufferSource();
        source.buffer = buffer;
        source.loop = !!loop;
        const rate = resolvePlaybackRate(playbackRate);
        if (source.playbackRate && typeof source.playbackRate.setValueAtTime === "function") {
          source.playbackRate.setValueAtTime(rate, context.currentTime);
        } else if (source.playbackRate) {
          source.playbackRate.value = rate;
        }
        const gain = context.createGain();
        if (gain.gain && typeof gain.gain.setValueAtTime === "function") {
          gain.gain.setValueAtTime(volume, context.currentTime);
        } else if (gain.gain) {
          gain.gain.value = volume;
        }
        source.connect(gain);
        const cleanupNodes = [gain, source];
        if (typeof context.createStereoPanner === "function") {
          const panner = context.createStereoPanner();
          const panValue = clampFiniteNumber(pan, -1, 1, 0);
          if (panner.pan && typeof panner.pan.setValueAtTime === "function") {
            panner.pan.setValueAtTime(panValue, context.currentTime);
          } else if (panner.pan) {
            panner.pan.value = panValue;
          }
          gain.connect(panner);
          panner.connect(state.audioMasterGain);
          cleanupNodes.push(panner);
        } else {
          gain.connect(state.audioMasterGain);
        }
        registerAudioSource(source, cleanupNodes, { kind: "sample", url });
        source.start(context.currentTime);
      }

      function resolveSamplePlayArgs(volume, options) {
        if (volume && typeof volume === "object" && (options === undefined || options === null)) {
          const safeOptions = volume;
          return {
            volume: clampFiniteNumber(safeOptions.volume, 0, 1, 1),
            loop: !!safeOptions.loop,
            pan: clampFiniteNumber(safeOptions.pan, -1, 1, 0),
            playbackRate: resolvePlaybackRate(safeOptions.playbackRate)
          };
        }

        const safeOptions = options && typeof options === "object" ? options : {};
        return {
          volume: clampFiniteNumber(volume, 0, 1, 1),
          loop: !!safeOptions.loop,
          pan: clampFiniteNumber(safeOptions.pan, -1, 1, 0),
          playbackRate: resolvePlaybackRate(safeOptions.playbackRate)
        };
      }

      function enqueueSamplePlay(url, volume, loop, pan, playbackRate) {
        const entry = state.audioSampleCache.get(url);
        if (!entry || !Array.isArray(entry.pending)) return;
        if (entry.pending.length >= 8) return;
        entry.pending.push({ volume, loop, pan, playbackRate });
      }

      function flushPendingSamplePlays(context, url) {
        const entry = state.audioSampleCache.get(url);
        if (!entry || entry.status !== "ready" || !entry.buffer) return;
        const pending = entry.pending.splice(0, entry.pending.length);
        for (let i = 0; i < pending.length; i++) {
          startSamplePlayback(
            context,
            entry.buffer,
            url,
            pending[i].volume,
            pending[i].loop,
            pending[i].pan,
            pending[i].playbackRate
          );
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

      function currentZoom() {
        const zoom = state.cameraZoom;
        if (!(zoom > 0) || !Number.isFinite(zoom)) {
          return 1;
        }
        return zoom;
      }

      function worldX(x) {
        return (toFiniteNumber(x, 0) - state.cameraX) * currentZoom();
      }

      function worldY(y) {
        return (toFiniteNumber(y, 0) - state.cameraY) * currentZoom();
      }

      function worldSize(value, fallback) {
        return toFiniteNumber(value, fallback) * currentZoom();
      }

      function applyDrawStyle(context) {
        context.globalAlpha = state.alpha;
        context.globalCompositeOperation = state.blendOp || "source-over";
        context.imageSmoothingEnabled = !state.pixelated;
      }

      function resolveBlendMode(mode) {
        const key = coerceToString(mode).toLowerCase();
        const resolved = BLEND_MODES[key];
        if (!resolved) {
          return BLEND_MODES.alpha;
        }
        return resolved;
      }

      function resetBlend(context) {
        state.blend = "alpha";
        state.blendOp = "source-over";
        if (context) {
          context.globalCompositeOperation = "source-over";
        }
      }

      function applyPixelFilter(context, canvas) {
        if (context) {
          context.imageSmoothingEnabled = !state.pixelated;
        }
        if (canvas && canvas.style) {
          canvas.style.imageRendering = state.pixelated ? "pixelated" : "auto";
        }
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

      function setMouseButtonDown(button, isDown) {
        const mouseButton = coerceToInt(button);
        if (isDown) {
          if (!state.mouseButtonsDown.has(mouseButton)) {
            state.pendingMousePressed.add(mouseButton);
            state.pendingMouseReleased.delete(mouseButton);
          }
          state.mouseButtonsDown.add(mouseButton);
        } else {
          if (state.mouseButtonsDown.has(mouseButton)) {
            state.pendingMouseReleased.add(mouseButton);
          }
          state.mouseButtonsDown.delete(mouseButton);
        }
      }

      function syncPrimaryTouchMouse(touchList) {
        const primary = touchList && touchList.length > 0 ? touchList[0] : null;
        if (primary) {
          updateMouseFromTouch(primary);
          setMouseButtonDown(0, true);
        } else {
          setMouseButtonDown(0, false);
        }
      }

      function resetKeyboardAndPointerState() {
        state.keysDown.clear();
        state.pendingKeyPressed.clear();
        state.pendingKeyReleased.clear();
        state.keysPressed.clear();
        state.keysReleased.clear();
        state.mouseButtonsDown.clear();
        state.pendingMousePressed.clear();
        state.pendingMouseReleased.clear();
        state.mouseButtonsPressed.clear();
        state.mouseButtonsReleased.clear();
        state.mouseX = 0;
        state.mouseY = 0;
        state.touches.clear();
        state.gamepadButtonsDown.clear();
        state.gamepadButtonsPrev.clear();
        state.gamepadButtonsPressed.clear();
        state.gamepadButtonsReleased.clear();
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

        state.mouseButtonsPressed = state.pendingMousePressed;
        state.mouseButtonsReleased = state.pendingMouseReleased;
        state.pendingMousePressed = new Set();
        state.pendingMouseReleased = new Set();

        pollGamepads();
        state.gamepadButtonsPressed.clear();
        state.gamepadButtonsReleased.clear();
        for (const key of state.gamepadButtonsDown) {
          if (!state.gamepadButtonsPrev.has(key)) {
            state.gamepadButtonsPressed.add(key);
          }
        }
        for (const key of state.gamepadButtonsPrev) {
          if (!state.gamepadButtonsDown.has(key)) {
            state.gamepadButtonsReleased.add(key);
          }
        }
        state.gamepadButtonsPrev = new Set(state.gamepadButtonsDown);
        state.inputFrameActive = true;
      }

      function endInputFrame() {
        state.keysPressed = new Set();
        state.keysReleased = new Set();
        state.mouseButtonsPressed.clear();
        state.mouseButtonsReleased.clear();
        state.gamepadButtonsPressed.clear();
        state.gamepadButtonsReleased.clear();
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
          setMouseButtonDown(event.button, true);
        };
        const onMouseUp = (event) => {
          updateMousePosition(event);
          setMouseButtonDown(event.button, false);
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
        state.cameraZoom = 1;
        state.cameraStack = [];
        state.alpha = 1;
        state.pixelated = false;
        context.globalAlpha = 1;
        resetBlend(context);
        applyPixelFilter(context, canvas);
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
        const previousComposite = context.globalCompositeOperation;
        context.globalAlpha = 1;
        context.globalCompositeOperation = "source-over";
        if (state.backgroundColor === null || state.backgroundColor === undefined) {
          context.clearRect(0, 0, state.canvas.width, state.canvas.height);
        } else {
          context.fillStyle = coerceToString(state.backgroundColor);
          context.fillRect(0, 0, state.canvas.width, state.canvas.height);
        }
        context.globalAlpha = previousAlpha;
        context.globalCompositeOperation = previousComposite;
        return null;
      }

      function fillRect(x, y, width, height, color) {
        const context = ensureCanvasContext("fillRect");
        applyDrawStyle(context);
        context.fillStyle = coerceToString(color || "#ffffff");
        context.fillRect(
          worldX(x),
          worldY(y),
          Math.max(0, worldSize(width, 0)),
          Math.max(0, worldSize(height, 0))
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
          Math.max(0, worldSize(radius, 0)),
          0,
          Math.PI * 2
        );
        context.fill();
        return null;
      }

      function strokeCircle(x, y, radius, color, lineWidth) {
        const context = ensureCanvasContext("strokeCircle");
        applyDrawStyle(context);
        context.strokeStyle = coerceToString(color || "#ffffff");
        context.lineWidth = Math.max(0, worldSize(lineWidth, 1));
        context.beginPath();
        context.arc(
          worldX(x),
          worldY(y),
          Math.max(0, worldSize(radius, 0)),
          0,
          Math.PI * 2
        );
        context.stroke();
        return null;
      }

      function getCanvasWidth() {
        ensureCanvasContext("getCanvasWidth");
        return state.canvas.width;
      }

      function getCanvasHeight() {
        ensureCanvasContext("getCanvasHeight");
        return state.canvas.height;
      }

      function setPixelated(enabled) {
        const context = ensureCanvasContext("setPixelated");
        state.pixelated = !!enabled;
        applyPixelFilter(context, state.canvas);
        return null;
      }

      function measureText(text, font) {
        const context = ensureCanvasContext("measureText");
        const previousFont = context.font;
        const resolvedFont = coerceToString(font || "16px sans-serif");
        context.font = resolvedFont;
        const label = coerceToString(text);
        let width = 0;
        let height = 0;
        try {
          if (typeof context.measureText === "function") {
            const metrics = context.measureText(label);
            width = toFiniteNumber(metrics && metrics.width, 0);
            const ascent = metrics ? toFiniteNumber(metrics.actualBoundingBoxAscent, NaN) : NaN;
            const descent = metrics ? toFiniteNumber(metrics.actualBoundingBoxDescent, NaN) : NaN;
            if (Number.isFinite(ascent) && Number.isFinite(descent)) {
              height = Math.max(0, ascent + descent);
            }
          }
        } catch (_error) {
          width = 0;
          height = 0;
        }
        if (!(height > 0)) {
          const match = resolvedFont.match(/(\d+(?:\.\d+)?)\s*px/i);
          height = match ? Math.max(0, toFiniteNumber(match[1], 16)) : 16;
        }
        context.font = previousFont;
        return { width: width, height: height };
      }

      function drawText(text, x, y, color, font) {
        const context = ensureCanvasContext("drawText");
        applyDrawStyle(context);
        context.fillStyle = coerceToString(color || "#ffffff");
        context.font = coerceToString(font || "16px sans-serif");
        const label = coerceToString(text);
        const zoom = currentZoom();
        if (zoom === 1) {
          context.fillText(label, worldX(x), worldY(y));
          return null;
        }
        context.save();
        context.translate(worldX(x), worldY(y));
        context.scale(zoom, zoom);
        context.fillText(label, 0, 0);
        context.restore();
        return null;
      }

      function setCamera(x, y) {
        ensureCanvasContext("setCamera");
        state.cameraX = toFiniteNumber(x, 0);
        state.cameraY = toFiniteNumber(y, 0);
        return null;
      }

      function followAxis(target, screen, view, world) {
        if (!(world > view)) {
          return 0;
        }
        let cam = target - screen;
        const maxCam = world - view;
        if (cam < 0) {
          cam = 0;
        }
        if (cam > maxCam) {
          cam = maxCam;
        }
        return cam;
      }

      function followCamera(targetX, targetY, viewW, viewH, worldW, worldH, options) {
        ensureCanvasContext("followCamera");
        const viewWidth = toFiniteNumber(viewW, 0);
        const viewHeight = toFiniteNumber(viewH, 0);
        const worldWidth = toFiniteNumber(worldW, 0);
        const worldHeight = toFiniteNumber(worldH, 0);
        const opts = options && typeof options === "object" ? options : {};
        const screenX = optionHas(opts, "screenX")
          ? toFiniteNumber(opts.screenX, viewWidth / 2)
          : viewWidth / 2;
        const screenY = optionHas(opts, "screenY")
          ? toFiniteNumber(opts.screenY, viewHeight / 2)
          : viewHeight / 2;
        let camX = followAxis(toFiniteNumber(targetX, 0), screenX, viewWidth, worldWidth);
        let camY = followAxis(toFiniteNumber(targetY, 0), screenY, viewHeight, worldHeight);
        if (opts.snap) {
          camX = Math.floor(camX);
          camY = Math.floor(camY);
        }
        return setCamera(camX, camY);
      }

      function getCameraX() {
        ensureCanvasContext("getCameraX");
        return state.cameraX;
      }

      function getCameraY() {
        ensureCanvasContext("getCameraY");
        return state.cameraY;
      }

      function setCameraZoom(zoom) {
        ensureCanvasContext("setCameraZoom");
        const numeric = toFiniteNumber(zoom, 1);
        if (!(numeric > 0)) {
          state.cameraZoom = 1;
        } else {
          state.cameraZoom = Math.min(100, Math.max(0.05, numeric));
        }
        return null;
      }

      function getCameraZoom() {
        ensureCanvasContext("getCameraZoom");
        return currentZoom();
      }

      function pushCamera() {
        ensureCanvasContext("pushCamera");
        state.cameraStack.push({
          x: state.cameraX,
          y: state.cameraY,
          zoom: currentZoom()
        });
        return null;
      }

      function popCamera() {
        ensureCanvasContext("popCamera");
        if (state.cameraStack.length === 0) {
          return null;
        }
        const previous = state.cameraStack.pop();
        state.cameraX = previous.x;
        state.cameraY = previous.y;
        state.cameraZoom = previous.zoom > 0 ? previous.zoom : 1;
        return null;
      }

      function screenToWorld(x, y) {
        ensureCanvasContext("screenToWorld");
        const zoom = currentZoom();
        return {
          x: toFiniteNumber(x, 0) / zoom + state.cameraX,
          y: toFiniteNumber(y, 0) / zoom + state.cameraY
        };
      }

      function worldToScreen(x, y) {
        ensureCanvasContext("worldToScreen");
        const zoom = currentZoom();
        return {
          x: (toFiniteNumber(x, 0) - state.cameraX) * zoom,
          y: (toFiniteNumber(y, 0) - state.cameraY) * zoom
        };
      }

      function setAlpha(alpha) {
        const context = ensureCanvasContext("setAlpha");
        state.alpha = clampFiniteNumber(alpha, 0, 1, 1);
        context.globalAlpha = state.alpha;
        return null;
      }

      function setBlend(mode) {
        const context = ensureCanvasContext("setBlend");
        const resolved = resolveBlendMode(mode);
        state.blend = resolved.name;
        state.blendOp = resolved.op;
        context.globalCompositeOperation = resolved.op;
        return null;
      }

      function getBlend() {
        ensureCanvasContext("getBlend");
        return state.blend || "alpha";
      }

      function drawLine(x1, y1, x2, y2, color, width) {
        const context = ensureCanvasContext("drawLine");
        applyDrawStyle(context);
        context.strokeStyle = coerceToString(color || "#ffffff");
        context.lineWidth = Math.max(0, worldSize(width, 1));
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
        context.lineWidth = Math.max(0, worldSize(lineWidth, 1));
        context.strokeRect(
          worldX(x),
          worldY(y),
          Math.max(0, worldSize(width, 0)),
          Math.max(0, worldSize(height, 0))
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

      function imageWidth(handle) {
        const record = resolveImageHandle(handle);
        if (!record || !record.ready || !record.image) {
          return 0;
        }
        return record.width;
      }

      function imageHeight(handle) {
        const record = resolveImageHandle(handle);
        if (!record || !record.ready || !record.image) {
          return 0;
        }
        return record.height;
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
          context.drawImage(record.image, worldX(x), worldY(y), worldSize(destWidth, 0), worldSize(destHeight, 0));
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
            worldSize(destWidth, 0),
            worldSize(destHeight, 0)
          );
        } catch (_error) {
          // Decode races and detached bitmaps are ignored on the hot path.
        }
        return null;
      }

      function optionHas(options, key) {
        if (!options || typeof options !== "object") {
          return false;
        }
        if (!Object.prototype.hasOwnProperty.call(options, key)) {
          return false;
        }
        const value = options[key];
        return !(value === undefined || value === null);
      }

      function optionNumber(options, key, fallback) {
        if (!optionHas(options, key)) {
          return fallback;
        }
        return toFiniteNumber(options[key], fallback);
      }

      function optionTint(options) {
        if (!optionHas(options, "tint")) {
          return null;
        }
        const color = coerceToString(options.tint);
        return color === "" ? null : color;
      }

      function ensureTintContext() {
        if (state.tintContext) {
          return state.tintContext;
        }
        if (typeof document === "undefined" || typeof document.createElement !== "function") {
          return null;
        }
        const canvas = document.createElement("canvas");
        if (!canvas || typeof canvas.getContext !== "function") {
          return null;
        }
        const context = canvas.getContext("2d");
        if (!context) {
          return null;
        }
        state.tintCanvas = canvas;
        state.tintContext = context;
        return context;
      }

      function prepareTintedImage(record, sourceX, sourceY, sourceWidth, sourceHeight, destWidth, destHeight, tint, tintFill) {
        const tintContext = ensureTintContext();
        if (!tintContext || !state.tintCanvas || !record || !record.image) {
          return null;
        }

        const pixelW = Math.max(1, Math.ceil(Math.abs(destWidth)));
        const pixelH = Math.max(1, Math.ceil(Math.abs(destHeight)));
        if (state.tintCanvas.width !== pixelW) {
          state.tintCanvas.width = pixelW;
        }
        if (state.tintCanvas.height !== pixelH) {
          state.tintCanvas.height = pixelH;
        }

        tintContext.globalAlpha = 1;
        tintContext.globalCompositeOperation = "source-over";
        tintContext.imageSmoothingEnabled = !state.pixelated;
        tintContext.clearRect(0, 0, pixelW, pixelH);
        tintContext.drawImage(record.image, sourceX, sourceY, sourceWidth, sourceHeight, 0, 0, pixelW, pixelH);
        tintContext.globalCompositeOperation = tintFill ? "source-in" : "multiply";
        tintContext.fillStyle = tint;
        tintContext.fillRect(0, 0, pixelW, pixelH);
        if (!tintFill) {
          tintContext.globalCompositeOperation = "destination-in";
          tintContext.drawImage(record.image, sourceX, sourceY, sourceWidth, sourceHeight, 0, 0, pixelW, pixelH);
        }
        tintContext.globalCompositeOperation = "source-over";
        return state.tintCanvas;
      }

      function drawImageEx(handle, x, y, options) {
        const context = ensureCanvasContext("drawImageEx");
        const record = resolveImageHandle(handle);
        if (!record || !record.ready || !record.image) {
          return null;
        }

        const opts = options && typeof options === "object" ? options : {};
        const hasSource =
          optionHas(opts, "sx") ||
          optionHas(opts, "sy") ||
          optionHas(opts, "sw") ||
          optionHas(opts, "sh");

        const sourceX = hasSource ? optionNumber(opts, "sx", 0) : 0;
        const sourceY = hasSource ? optionNumber(opts, "sy", 0) : 0;
        const sourceWidth = hasSource
          ? Math.max(0, optionNumber(opts, "sw", record.width))
          : record.width;
        const sourceHeight = hasSource
          ? Math.max(0, optionNumber(opts, "sh", record.height))
          : record.height;
        if (sourceWidth <= 0 || sourceHeight <= 0) {
          return null;
        }

        const destWidth = optionHas(opts, "w")
          ? Math.max(0, optionNumber(opts, "w", 0))
          : sourceWidth;
        const destHeight = optionHas(opts, "h")
          ? Math.max(0, optionNumber(opts, "h", 0))
          : sourceHeight;
        if (destWidth <= 0 || destHeight <= 0) {
          return null;
        }

        const originX = optionNumber(opts, "ox", 0);
        const originY = optionNumber(opts, "oy", 0);
        const angle = optionNumber(opts, "angle", 0);
        const flipX = !!opts.flipX;
        const flipY = !!opts.flipY;
        const scaleX = flipX ? -1 : 1;
        const scaleY = flipY ? -1 : 1;
        const needsRotate = angle !== 0;
        const needsScale = scaleX !== 1 || scaleY !== 1;
        const screenW = worldSize(destWidth, 0);
        const screenH = worldSize(destHeight, 0);
        const tint = optionTint(opts);
        const tintFill = !!opts.tintFill;
        let blitImage = record.image;
        let blitSx = sourceX;
        let blitSy = sourceY;
        let blitSw = sourceWidth;
        let blitSh = sourceHeight;
        if (tint) {
          const tinted = prepareTintedImage(
            record,
            sourceX,
            sourceY,
            sourceWidth,
            sourceHeight,
            screenW,
            screenH,
            tint,
            tintFill
          );
          if (tinted) {
            blitImage = tinted;
            blitSx = 0;
            blitSy = 0;
            blitSw = tinted.width;
            blitSh = tinted.height;
          }
        }

        applyDrawStyle(context);
        try {
          context.save();
          context.translate(worldX(x), worldY(y));
          if (needsRotate) {
            context.rotate(angle);
          }
          if (needsScale) {
            context.scale(scaleX, scaleY);
          }
          context.drawImage(
            blitImage,
            blitSx,
            blitSy,
            blitSw,
            blitSh,
            -worldSize(originX, 0),
            -worldSize(originY, 0),
            screenW,
            screenH
          );
          context.restore();
        } catch (_error) {
          try {
            context.restore();
          } catch (_restoreError) {
            // Decode races and detached bitmaps are ignored on the hot path.
          }
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

      function sweepRects(x, y, w, h, dx, dy, obstacles) {
        const ax = toFiniteNumber(x, 0);
        const ay = toFiniteNumber(y, 0);
        const aw = toFiniteNumber(w, 0);
        const ah = toFiniteNumber(h, 0);
        const adx = toFiniteNumber(dx, 0);
        const ady = toFiniteNumber(dy, 0);
        const endX = ax + adx;
        const endY = ay + ady;
        if (!Array.isArray(obstacles)) {
          return sweepHit(false, 1, 0, 0, endX, endY);
        }

        let best = null;
        for (let i = 0; i < obstacles.length; i++) {
          const obstacle = obstacles[i];
          if (!obstacle || typeof obstacle !== "object" || Array.isArray(obstacle)) {
            continue;
          }
          const ow = toFiniteNumber(obstacle.w, 0);
          const oh = toFiniteNumber(obstacle.h, 0);
          if (ow <= 0 || oh <= 0) {
            continue;
          }
          const next = sweepRect(ax, ay, aw, ah, adx, ady, obstacle.x, obstacle.y, ow, oh);
          if (!next.hit) {
            continue;
          }
          if (!best || next.t < best.t) {
            best = next;
          }
        }

        return best || sweepHit(false, 1, 0, 0, endX, endY);
      }

      function resolveTileGrid(cells, options) {
        const empty = optionHas(options, "empty") ? toFiniteNumber(options.empty, 0) : 0;
        const out = optionHas(options, "out") ? toFiniteNumber(options.out, empty) : empty;
        if (!Array.isArray(cells)) {
          return { nested: false, cells: [], columns: 0, rows: 0, empty: empty, out: out };
        }

        const nested = cells.length > 0 && Array.isArray(cells[0]);
        if (nested) {
          let rows = optionHas(options, "rows")
            ? Math.max(0, Math.floor(toFiniteNumber(options.rows, cells.length)))
            : cells.length;
          if (rows > cells.length) {
            rows = cells.length;
          }
          let columns = optionHas(options, "columns")
            ? Math.max(0, Math.floor(toFiniteNumber(options.columns, 0)))
            : 0;
          if (columns <= 0) {
            let widest = 0;
            for (let i = 0; i < rows; i++) {
              const line = cells[i];
              if (Array.isArray(line) && line.length > widest) {
                widest = line.length;
              }
            }
            columns = widest;
          }
          return { nested: true, cells: cells, columns: columns, rows: rows, empty: empty, out: out };
        }

        const columns = optionHas(options, "columns")
          ? Math.max(0, Math.floor(toFiniteNumber(options.columns, 0)))
          : 0;
        if (columns <= 0) {
          return { nested: false, cells: cells, columns: 0, rows: 0, empty: empty, out: out };
        }
        const rows = optionHas(options, "rows")
          ? Math.max(0, Math.floor(toFiniteNumber(options.rows, 0)))
          : Math.floor(cells.length / columns);
        return { nested: false, cells: cells, columns: columns, rows: rows, empty: empty, out: out };
      }

      function tileGridId(grid, col, row) {
        if (col < 0 || row < 0 || col >= grid.columns || row >= grid.rows) {
          return grid.out;
        }
        if (grid.nested) {
          const line = grid.cells[row];
          if (!Array.isArray(line) || col >= line.length) {
            return grid.out;
          }
          return toFiniteNumber(line[col], grid.out);
        }
        const index = (row * grid.columns) + col;
        if (index < 0 || index >= grid.cells.length) {
          return grid.out;
        }
        return toFiniteNumber(grid.cells[index], grid.out);
      }

      function resolveSolidSet(options) {
        if (!optionHas(options, "solids") || !Array.isArray(options.solids)) {
          return null;
        }
        const solidSet = new Set();
        for (let i = 0; i < options.solids.length; i++) {
          solidSet.add(toFiniteNumber(options.solids[i], 0));
        }
        return solidSet;
      }

      function tileIdIsSolid(id, grid, solidSet) {
        if (solidSet) {
          return solidSet.has(id);
        }
        return id !== grid.empty;
      }

      function tileAt(cells, col, row, options) {
        const opts = options && typeof options === "object" ? options : {};
        const grid = resolveTileGrid(cells, opts);
        const cellX = Math.floor(toFiniteNumber(col, 0));
        const cellY = Math.floor(toFiniteNumber(row, 0));
        return tileGridId(grid, cellX, cellY);
      }

      function drawTiles(handle, cells, tileW, tileH, options) {
        const context = ensureCanvasContext("drawTiles");
        const record = resolveImageHandle(handle);
        if (!record || !record.ready || !record.image) {
          return null;
        }

        const cellW = toFiniteNumber(tileW, 0);
        const cellH = toFiniteNumber(tileH, 0);
        if (cellW <= 0 || cellH <= 0) {
          return null;
        }

        const opts = options && typeof options === "object" ? options : {};
        const grid = resolveTileGrid(cells, opts);
        if (grid.columns <= 0 || grid.rows <= 0) {
          return null;
        }

        const originX = optionNumber(opts, "x", 0);
        const originY = optionNumber(opts, "y", 0);
        const sourceWidth = Math.max(0, optionNumber(opts, "srcW", cellW));
        const sourceHeight = Math.max(0, optionNumber(opts, "srcH", cellH));
        if (sourceWidth <= 0 || sourceHeight <= 0) {
          return null;
        }

        const firstId = optionHas(opts, "firstId") ? toFiniteNumber(opts.firstId, 1) : 1;
        const atlasColumns = optionHas(opts, "atlasColumns")
          ? Math.max(1, Math.floor(toFiniteNumber(opts.atlasColumns, 1)))
          : Math.max(1, Math.floor(record.width / sourceWidth));

        let colStart = 0;
        let colEnd = grid.columns - 1;
        let rowStart = 0;
        let rowEnd = grid.rows - 1;
        const canvasW = state.canvas ? state.canvas.width : 0;
        const canvasH = state.canvas ? state.canvas.height : 0;
        if (canvasW > 0 && canvasH > 0) {
          const zoom = currentZoom();
          const viewLeft = state.cameraX;
          const viewTop = state.cameraY;
          const viewRight = viewLeft + (canvasW / zoom);
          const viewBottom = viewTop + (canvasH / zoom);
          colStart = Math.floor((viewLeft - originX) / cellW) - 1;
          colEnd = Math.floor((viewRight - originX) / cellW) + 1;
          rowStart = Math.floor((viewTop - originY) / cellH) - 1;
          rowEnd = Math.floor((viewBottom - originY) / cellH) + 1;
          if (colStart < 0) colStart = 0;
          if (rowStart < 0) rowStart = 0;
          if (colEnd >= grid.columns) colEnd = grid.columns - 1;
          if (rowEnd >= grid.rows) rowEnd = grid.rows - 1;
        }
        if (colStart > colEnd || rowStart > rowEnd) {
          return null;
        }

        applyDrawStyle(context);
        for (let row = rowStart; row <= rowEnd; row++) {
          for (let col = colStart; col <= colEnd; col++) {
            const id = tileGridId(grid, col, row);
            if (id === grid.empty) {
              continue;
            }
            const atlasIndex = id - firstId;
            if (!(atlasIndex >= 0) || !Number.isFinite(atlasIndex)) {
              continue;
            }
            const index = Math.floor(atlasIndex);
            const sx = (index % atlasColumns) * sourceWidth;
            const sy = Math.floor(index / atlasColumns) * sourceHeight;
            try {
              context.drawImage(
                record.image,
                sx,
                sy,
                sourceWidth,
                sourceHeight,
                worldX(originX + (col * cellW)),
                worldY(originY + (row * cellH)),
                worldSize(cellW, 0),
                worldSize(cellH, 0)
              );
            } catch (_error) {
              // Decode races and detached bitmaps are ignored on the hot path.
            }
          }
        }
        return null;
      }

      function sweepTiles(x, y, w, h, dx, dy, cells, tileW, tileH, options) {
        const ax = toFiniteNumber(x, 0);
        const ay = toFiniteNumber(y, 0);
        const aw = toFiniteNumber(w, 0);
        const ah = toFiniteNumber(h, 0);
        const adx = toFiniteNumber(dx, 0);
        const ady = toFiniteNumber(dy, 0);
        const cellW = toFiniteNumber(tileW, 0);
        const cellH = toFiniteNumber(tileH, 0);
        const endX = ax + adx;
        const endY = ay + ady;
        if (aw <= 0 || ah <= 0 || cellW <= 0 || cellH <= 0) {
          return sweepHit(false, 1, 0, 0, endX, endY);
        }

        const opts = options && typeof options === "object" ? options : {};
        const grid = resolveTileGrid(cells, opts);
        const originX = optionNumber(opts, "x", 0);
        const originY = optionNumber(opts, "y", 0);
        const solidSet = resolveSolidSet(opts);

        const minX = Math.min(ax, endX);
        const minY = Math.min(ay, endY);
        const maxX = Math.max(ax + aw, endX + aw);
        const maxY = Math.max(ay + ah, endY + ah);
        let col0 = Math.floor((minX - originX) / cellW) - 1;
        let col1 = Math.floor((maxX - originX) / cellW) + 1;
        let row0 = Math.floor((minY - originY) / cellH) - 1;
        let row1 = Math.floor((maxY - originY) / cellH) + 1;

        const maxSpan = 1024;
        if (col1 - col0 > maxSpan) {
          col1 = col0 + maxSpan;
        }
        if (row1 - row0 > maxSpan) {
          row1 = row0 + maxSpan;
        }

        let best = null;
        for (let row = row0; row <= row1; row++) {
          for (let col = col0; col <= col1; col++) {
            const id = tileGridId(grid, col, row);
            if (!tileIdIsSolid(id, grid, solidSet)) {
              continue;
            }
            const next = sweepRect(
              ax,
              ay,
              aw,
              ah,
              adx,
              ady,
              originX + (col * cellW),
              originY + (row * cellH),
              cellW,
              cellH
            );
            if (!next.hit) {
              continue;
            }
            if (!best || next.t < best.t) {
              best = next;
            }
          }
        }

        return best || sweepHit(false, 1, 0, 0, endX, endY);
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

      function wasMousePressed(button) {
        const mouseButton = button === null || button === undefined ? 0 : coerceToInt(button);
        return state.mouseButtonsPressed.has(mouseButton);
      }

      function wasMouseReleased(button) {
        const mouseButton = button === null || button === undefined ? 0 : coerceToInt(button);
        return state.mouseButtonsReleased.has(mouseButton);
      }

      function getMouseWorldX() {
        return state.mouseX / currentZoom() + state.cameraX;
      }

      function getMouseWorldY() {
        return state.mouseY / currentZoom() + state.cameraY;
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

      function getGamepadAxis(index, axis, deadzone) {
        ensureGamepadSnapshot();
        const padIndex = index === null || index === undefined ? 0 : coerceToInt(index);
        const axisIndex = coerceToInt(axis);
        const value = state.gamepadAxes[gamepadAxisKey(padIndex, axisIndex)];
        const raw = typeof value === "number" ? value : 0;
        if (deadzone === undefined || deadzone === null) {
          return raw;
        }
        const zone = clampFiniteNumber(deadzone, 0, 1, 0);
        return Math.abs(raw) <= zone ? 0 : raw;
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

      function wasGamepadButtonReleased(index, button) {
        const padIndex = index === null || index === undefined ? 0 : coerceToInt(index);
        const buttonIndex = coerceToInt(button);
        return state.gamepadButtonsReleased.has(gamepadButtonKey(padIndex, buttonIndex));
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
          startSamplePlayback(
            context,
            entry.buffer,
            safeUrl,
            playArgs.volume,
            playArgs.loop,
            playArgs.pan,
            playArgs.playbackRate
          );
          return null;
        }

        if (entry && entry.status === "loading") {
          enqueueSamplePlay(safeUrl, playArgs.volume, playArgs.loop, playArgs.pan, playArgs.playbackRate);
          return null;
        }

        state.audioSampleCache.set(safeUrl, {
          status: "loading",
          buffer: null,
          pending: [{
            volume: playArgs.volume,
            loop: playArgs.loop,
            pan: playArgs.pan,
            playbackRate: playArgs.playbackRate
          }]
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
        strokeCircle,
        drawText,
        measureText,
        drawLine,
        strokeRect,
        setAlpha,
        setBlend,
        getBlend,
        setPixelated,
        getCanvasWidth,
        getCanvasHeight,
        setCamera,
        followCamera,
        getCameraX,
        getCameraY,
        setCameraZoom,
        getCameraZoom,
        pushCamera,
        popCamera,
        screenToWorld,
        worldToScreen,
        loadImage,
        imageIsReady,
        imageWidth,
        imageHeight,
        drawImage,
        drawImageRect,
        drawImageEx,
        drawTiles,
        createPixelBuffer,
        setPixel,
        blitPixels,
        overlapRect,
        overlapCircle,
        pointInRect,
        pointInCircle,
        sweepRect,
        sweepRects,
        tileAt,
        sweepTiles,
        isKeyDown,
        wasKeyPressed,
        wasKeyReleased,
        getMouseX,
        getMouseY,
        getMouseWorldX,
        getMouseWorldY,
        isMouseDown,
        wasMousePressed,
        wasMouseReleased,
        getTouches,
        isGamepadConnected,
        getGamepadAxis,
        isGamepadButtonDown,
        wasGamepadButtonPressed,
        wasGamepadButtonReleased,
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
        const textureHandle = resolveTextureHandle(value);
        if (textureHandle) {
          return textureHandle.texture;
        }
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

      function copyPixelsToBuffer(target, pixels, count) {
        const src = pixels && typeof pixels.length === "number" ? pixels : [];
        const n = Math.max(0, count);
        for (let i = 0; i < n; i++) {
          target[i] = toFiniteNumber(src[i], 0);
        }
      }

      function configureDataTexture(texture) {
        const THREE = ensureThree("createDataTexture");
        if (THREE.NearestFilter !== undefined) {
          texture.magFilter = THREE.NearestFilter;
          texture.minFilter = THREE.NearestFilter;
        }
        if (THREE.ClampToEdgeWrapping !== undefined) {
          texture.wrapS = THREE.ClampToEdgeWrapping;
          texture.wrapT = THREE.ClampToEdgeWrapping;
        }
        texture.generateMipmaps = false;
        if (THREE.NoColorSpace !== undefined) {
          texture.colorSpace = THREE.NoColorSpace;
        }
        texture.needsUpdate = true;
        return texture;
      }

      function createDataTexture(width, height, pixels, options) {
        const THREE = ensureThree("createDataTexture");
        if (typeof THREE.DataTexture !== "function") {
          throw new Error("mlRuntime.three.createDataTexture requires THREE.DataTexture on the loaded three.js bundle.");
        }
        const w = Math.max(1, coerceToInt(width));
        const h = Math.max(1, coerceToInt(height));
        const safeOptions = options && typeof options === "object" && !Array.isArray(options) ? options : {};
        const typeName = coerceToString(safeOptions.type).trim().toLowerCase();
        const useFloat = typeName === "" || typeName === "float" || typeName === "float32";
        const count = w * h * 4;
        let texture;
        if (useFloat) {
          if (THREE.FloatType === undefined || THREE.RGBAFormat === undefined) {
            throw new Error("mlRuntime.three.createDataTexture float textures require THREE.FloatType and THREE.RGBAFormat.");
          }
          const data = new Float32Array(count);
          copyPixelsToBuffer(data, pixels, count);
          texture = new THREE.DataTexture(data, w, h, THREE.RGBAFormat, THREE.FloatType);
        } else {
          const data = new Uint8Array(count);
          const src = pixels && typeof pixels.length === "number" ? pixels : [];
          for (let i = 0; i < count; i++) {
            const v = toFiniteNumber(src[i], 0);
            data[i] = v <= 1 && v >= 0 && src[i] !== undefined && Math.abs(v) <= 1
              ? Math.round(Math.max(0, Math.min(1, v)) * 255)
              : Math.max(0, Math.min(255, Math.round(v)));
          }
          const byteType = THREE.UnsignedByteType !== undefined ? THREE.UnsignedByteType : undefined;
          texture = byteType !== undefined
            ? new THREE.DataTexture(data, w, h, THREE.RGBAFormat, byteType)
            : new THREE.DataTexture(data, w, h, THREE.RGBAFormat);
        }
        configureDataTexture(texture);
        return {
          __maldaThreeTexture: true,
          url: "",
          ready: true,
          texture,
          pendingMaterials: [],
          width: w,
          height: h
        };
      }

      function updateDataTexture(handle, pixels) {
        ensureThree("updateDataTexture");
        const resolved = resolveTextureHandle(handle);
        if (!resolved || !resolved.texture || !resolved.texture.image || !resolved.texture.image.data) {
          throw new Error("mlRuntime.three.updateDataTexture expects a handle from three.createDataTexture or three.mandelbrotOrbit.");
        }
        const data = resolved.texture.image.data;
        copyPixelsToBuffer(data, pixels, data.length);
        resolved.texture.needsUpdate = true;
        return null;
      }

      function parseDecimalToFixed(text, fracBits) {
        const raw = coerceToString(text).trim();
        if (raw === "") {
          throw new Error("mlRuntime.three.mandelbrotOrbit requires a decimal real/imag string.");
        }
        let sign = 1n;
        let body = raw;
        if (body.charAt(0) === "-") {
          sign = -1n;
          body = body.slice(1);
        } else if (body.charAt(0) === "+") {
          body = body.slice(1);
        }
        const parts = body.split(".");
        const ip = parts[0] === "" || parts[0] === undefined ? "0" : parts[0];
        const fp = parts[1] === undefined ? "" : parts[1];
        if (!/^\d+$/.test(ip) || (fp.length > 0 && !/^\d+$/.test(fp))) {
          throw new Error("mlRuntime.three.mandelbrotOrbit real/imag must be decimal strings, for example \"-0.75\".");
        }
        const scale = 10n ** BigInt(fp.length);
        const mag = BigInt(ip) * scale + (fp.length === 0 ? 0n : BigInt(fp));
        return sign * ((mag << BigInt(fracBits)) / scale);
      }

      function fixedToFloat(value, fracBits) {
        const keep = 40;
        if (fracBits <= keep) {
          return Number(value) / Math.pow(2, fracBits);
        }
        const shift = BigInt(fracBits - keep);
        return Number(value >> shift) / Math.pow(2, keep);
      }

      // Host-side Mandelbrot reference orbit for perturbation shaders.
      // real/imag are decimal strings (MALDA IEEE doubles cannot hold a deep C).
      // Walks Z_{n+1} = Z_n^2 + C in BigInt fixed-point (256 fraction bits) and
      // packs each iterate as float RGBA (re, im, alive, 1) in a 1-row DataTexture.
      function mandelbrotOrbit(real, imag, maxIter) {
        const THREE = ensureThree("mandelbrotOrbit");
        if (typeof BigInt !== "function") {
          throw new Error("mlRuntime.three.mandelbrotOrbit requires JavaScript BigInt.");
        }
        const iters = Math.max(1, Math.min(2048, coerceToInt(maxIter)));
        const fracBits = 256;
        const cr = parseDecimalToFixed(real, fracBits);
        const ci = parseDecimalToFixed(imag, fracBits);
        const four = 4n << BigInt(fracBits);
        const pixels = new Float32Array((iters + 1) * 4);
        let zr = 0n;
        let zi = 0n;
        let escapedAt = iters + 1;
        for (let n = 0; n <= iters; n++) {
          const o = n * 4;
          pixels[o] = fixedToFloat(zr, fracBits);
          pixels[o + 1] = fixedToFloat(zi, fracBits);
          pixels[o + 2] = escapedAt <= n ? 0 : 1;
          pixels[o + 3] = 1;
          if (escapedAt <= n) {
            continue;
          }
          const zr2 = (zr * zr) >> BigInt(fracBits);
          const zi2 = (zi * zi) >> BigInt(fracBits);
          if (zr2 + zi2 > four) {
            escapedAt = n;
            pixels[o + 2] = 0;
            continue;
          }
          const zri = (zr * zi) >> BigInt(fracBits);
          const nr = zr2 - zi2 + cr;
          const ni = (zri << 1n) + ci;
          zr = nr;
          zi = ni;
        }
        const handle = createDataTexture(iters + 1, 1, pixels, { type: "float" });
        handle.escapedAt = escapedAt;
        handle.size = iters + 1;
        return handle;
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
        createDataTexture,
        updateDataTexture,
        mandelbrotOrbit,
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

  function nnLayerMatrix(rows, cols, scale) {
    const matrix = [];
    for (let i = 0; i < rows; i++) {
      const row = [];
      for (let j = 0; j < cols; j++) row.push(randomFloatBuiltin(-scale, scale));
      matrix.push(row);
    }
    return matrix;
  }

  function nnLayerZeros(rows, cols) {
    const matrix = [];
    for (let i = 0; i < rows; i++) {
      const row = [];
      for (let j = 0; j < cols; j++) row.push(0);
      matrix.push(row);
    }
    return matrix;
  }

  function nnLayerMatmul(left, right) {
    const rows = left.length;
    const inner = left[0].length;
    const cols = right[0].length;
    const result = nnLayerZeros(rows, cols);
    for (let i = 0; i < rows; i++) {
      for (let k = 0; k < inner; k++) {
        const value = left[i][k];
        for (let j = 0; j < cols; j++) result[i][j] += value * right[k][j];
      }
    }
    return result;
  }

  function nnLayerTranspose(matrix) {
    const result = nnLayerZeros(matrix[0].length, matrix.length);
    for (let i = 0; i < matrix.length; i++) {
      for (let j = 0; j < matrix[i].length; j++) result[j][i] = matrix[i][j];
    }
    return result;
  }

  function nnReadMatrix(name, value, which) {
    if (!neuralIsMatrix(value)) throw new Error(name + "() " + which + " must be a numeric matrix");
    const width = value[0].length;
    const matrix = [];
    for (let r = 0; r < value.length; r++) {
      const row = neuralRequireVector(name, value[r], which);
      if (row.length !== width) throw new Error(name + "() " + which + " rows must have the same length");
      matrix.push(row);
    }
    return matrix;
  }

  function nnApplyOwnedVector(values, grad, learningRate) {
    for (let i = 0; i < values.length; i++) values[i] = values[i] - learningRate * grad[i];
  }

  function nnApplyOwnedMatrix(values, grad, learningRate) {
    for (let i = 0; i < values.length; i++) nnApplyOwnedVector(values[i], grad[i], learningRate);
  }

  function Conv(size, scale) {
    const argc = arguments.length;
    if (argc < 1 || argc > 2) throw new Error("Conv() expects 1 or 2 arguments: (size, scale?)");
    this.size = nnRequirePositiveInt("Conv", size, "size");
    let width = 1 / this.size;
    if (argc === 2) {
      width = nnRequireFinite("Conv", scale, "scale");
      if (width < 0) throw new Error("Conv() scale must be >= 0");
    }
    this.kernel = nnLayerMatrix(this.size, this.size, width);
    this._image = null;
    this._dKernel = null;
  }

  Conv.prototype.forward = function (image) {
    if (arguments.length !== 1) throw new Error("Conv.forward() expects 1 argument: (image)");
    const pixels = nnReadMatrix("Conv.forward", image, "image");
    if (pixels.length < this.size || pixels[0].length < this.size) {
      throw new Error("Conv.forward() image must be at least size x size");
    }
    const kernel = nnReadMatrix("Conv.forward", this.kernel, "kernel");
    const outRows = pixels.length - this.size + 1;
    const outCols = pixels[0].length - this.size + 1;
    const output = nnLayerZeros(outRows, outCols);
    for (let oy = 0; oy < outRows; oy++) {
      for (let ox = 0; ox < outCols; ox++) {
        let sum = 0;
        for (let ky = 0; ky < this.size; ky++) {
          for (let kx = 0; kx < this.size; kx++) sum += pixels[oy + ky][ox + kx] * kernel[ky][kx];
        }
        output[oy][ox] = sum;
      }
    }
    this._image = pixels;
    return output;
  };

  Conv.prototype.backward = function (upstream) {
    if (arguments.length !== 1) throw new Error("Conv.backward() expects 1 argument: (upstream)");
    if (this._image == null) throw new Error("Conv.backward() requires forward() first");
    const grad = nnReadMatrix("Conv.backward", upstream, "upstream");
    const outRows = this._image.length - this.size + 1;
    const outCols = this._image[0].length - this.size + 1;
    if (grad.length !== outRows || grad[0].length !== outCols) {
      throw new Error("Conv.backward() upstream must match the forward output");
    }
    const kernel = nnReadMatrix("Conv.backward", this.kernel, "kernel");
    const dKernel = nnLayerZeros(this.size, this.size);
    const dImage = nnLayerZeros(this._image.length, this._image[0].length);
    for (let oy = 0; oy < outRows; oy++) {
      for (let ox = 0; ox < outCols; ox++) {
        const dOut = grad[oy][ox];
        for (let ky = 0; ky < this.size; ky++) {
          for (let kx = 0; kx < this.size; kx++) {
            dKernel[ky][kx] += dOut * this._image[oy + ky][ox + kx];
            dImage[oy + ky][ox + kx] += dOut * kernel[ky][kx];
          }
        }
      }
    }
    this._dKernel = dKernel;
    return dImage;
  };

  Conv.prototype.sgd = function (learningRate) {
    if (arguments.length !== 1) throw new Error("Conv.sgd() expects 1 argument: (lr)");
    if (this._dKernel == null) throw new Error("Conv.sgd() requires backward() first");
    nnApplyOwnedMatrix(this.kernel, this._dKernel, nnRequireFinite("Conv.sgd", learningRate, "lr"));
    return null;
  };

  function Embedding(rows, dim, scale) {
    const argc = arguments.length;
    if (argc < 2 || argc > 3) throw new Error("Embedding() expects 2 or 3 arguments: (rows, dim, scale?)");
    this.rows = nnRequirePositiveInt("Embedding", rows, "rows");
    this.dim = nnRequirePositiveInt("Embedding", dim, "dim");
    let width = 1 / Math.sqrt(this.dim);
    if (argc === 3) {
      width = nnRequireFinite("Embedding", scale, "scale");
      if (width < 0) throw new Error("Embedding() scale must be >= 0");
    }
    this.table = nnLayerMatrix(this.rows, this.dim, width);
    this._index = -1;
    this._dRow = null;
  }

  Embedding.prototype.forward = function (index) {
    if (arguments.length !== 1) throw new Error("Embedding.forward() expects 1 argument: (index)");
    if (typeof index !== "number" || !Number.isInteger(index)) {
      throw new Error("Embedding.forward() index must be an integer");
    }
    if (index < 0 || index >= this.rows) throw new Error("Embedding.forward() index out of range");
    this._index = index;
    return this.table[index].slice();
  };

  Embedding.prototype.backward = function (upstream) {
    if (arguments.length !== 1) throw new Error("Embedding.backward() expects 1 argument: (upstream)");
    if (this._index < 0) throw new Error("Embedding.backward() requires forward() first");
    const grad = neuralRequireVector("Embedding.backward", upstream, "upstream");
    if (grad.length !== this.dim) throw new Error("Embedding.backward() upstream length must match dim");
    this._dRow = grad;
    return null;
  };

  Embedding.prototype.sgd = function (learningRate) {
    if (arguments.length !== 1) throw new Error("Embedding.sgd() expects 1 argument: (lr)");
    if (this._dRow == null || this._index < 0) throw new Error("Embedding.sgd() requires backward() first");
    nnApplyOwnedVector(this.table[this._index], this._dRow, nnRequireFinite("Embedding.sgd", learningRate, "lr"));
    return null;
  };

  function Rnn(inputSize, hiddenSize, activation, scale) {
    const argc = arguments.length;
    if (argc < 2 || argc > 4) {
      throw new Error("Rnn() expects 2 to 4 arguments: (inputSize, hiddenSize, activation?, scale?)");
    }
    this.inputSize = nnRequirePositiveInt("Rnn", inputSize, "inputSize");
    this.hiddenSize = nnRequirePositiveInt("Rnn", hiddenSize, "hiddenSize");
    this.activation = "tanh";
    if (argc >= 3) {
      if (typeof activation !== "string" || NN_DENSE_ACTIVATIONS.indexOf(activation) < 0) {
        throw new Error("Rnn() unknown activation '" + activation + "'");
      }
      this.activation = activation;
    }
    let width = 1 / Math.sqrt(this.inputSize);
    if (argc === 4) {
      width = nnRequireFinite("Rnn", scale, "scale");
      if (width < 0) throw new Error("Rnn() scale must be >= 0");
    }
    this.weightsXh = nnLayerMatrix(this.inputSize, this.hiddenSize, width);
    this.weightsHh = nnLayerMatrix(this.hiddenSize, this.hiddenSize, width);
    const bias = [];
    for (let j = 0; j < this.hiddenSize; j++) bias.push(randomFloatBuiltin(-width, width));
    this.bias = bias;
    this._steps = null;
    this._dXh = null;
    this._dHh = null;
    this._dBias = null;
  }

  Rnn.prototype.forward = function (sequence) {
    if (arguments.length !== 1 || !Array.isArray(sequence) || sequence.length === 0) {
      throw new Error("Rnn.forward() expects 1 argument: (sequence)");
    }
    const wxh = nnReadMatrix("Rnn.forward", this.weightsXh, "weightsXh");
    const whh = nnReadMatrix("Rnn.forward", this.weightsHh, "weightsHh");
    const bias = neuralRequireVector("Rnn.forward", this.bias, "bias");
    let hidden = [];
    for (let j = 0; j < this.hiddenSize; j++) hidden.push(0);
    const steps = [];
    const outputs = [];
    const act = this.activation === "linear" ? null : this.activation;
    for (let t = 0; t < sequence.length; t++) {
      const input = neuralRequireVector("Rnn.forward", sequence[t], "sequence");
      if (input.length !== this.inputSize) throw new Error("Rnn.forward() each input length must match inputSize");
      const pre = bias.slice();
      for (let j = 0; j < this.hiddenSize; j++) {
        for (let i = 0; i < this.inputSize; i++) pre[j] += input[i] * wxh[i][j];
        for (let i = 0; i < this.hiddenSize; i++) pre[j] += hidden[i] * whh[i][j];
      }
      const next = [];
      for (let j = 0; j < this.hiddenSize; j++) next.push(nnApplyActivation(act, pre[j]));
      steps.push({ input: input, hidden: hidden.slice(), pre: pre });
      hidden = next;
      outputs.push(next);
    }
    this._steps = steps;
    return outputs;
  };

  Rnn.prototype.backward = function (upstreams) {
    if (arguments.length !== 1 || !Array.isArray(upstreams)) {
      throw new Error("Rnn.backward() expects 1 argument: (upstreams)");
    }
    if (this._steps == null) throw new Error("Rnn.backward() requires forward() first");
    if (upstreams.length !== this._steps.length) {
      throw new Error("Rnn.backward() upstreams must have one vector per step");
    }
    const wxh = nnReadMatrix("Rnn.backward", this.weightsXh, "weightsXh");
    const whh = nnReadMatrix("Rnn.backward", this.weightsHh, "weightsHh");
    const dXh = nnLayerZeros(this.inputSize, this.hiddenSize);
    const dHh = nnLayerZeros(this.hiddenSize, this.hiddenSize);
    const dBias = [];
    for (let j = 0; j < this.hiddenSize; j++) dBias.push(0);
    const dInputs = [];
    let dhNext = [];
    for (let j = 0; j < this.hiddenSize; j++) dhNext.push(0);
    const act = this.activation === "linear" ? null : this.activation;
    for (let t = this._steps.length - 1; t >= 0; t--) {
      const upstream = neuralRequireVector("Rnn.backward", upstreams[t], "upstreams");
      if (upstream.length !== this.hiddenSize) {
        throw new Error("Rnn.backward() each upstream length must match hiddenSize");
      }
      const step = this._steps[t];
      const dPre = [];
      for (let j = 0; j < this.hiddenSize; j++) {
        dPre.push((upstream[j] + dhNext[j]) * nnApplyDerivative(act, step.pre[j]));
      }
      for (let i = 0; i < this.inputSize; i++) {
        for (let j = 0; j < this.hiddenSize; j++) dXh[i][j] += step.input[i] * dPre[j];
      }
      for (let i = 0; i < this.hiddenSize; i++) {
        for (let j = 0; j < this.hiddenSize; j++) dHh[i][j] += step.hidden[i] * dPre[j];
      }
      for (let j = 0; j < this.hiddenSize; j++) dBias[j] += dPre[j];
      const dInput = [];
      for (let i = 0; i < this.inputSize; i++) {
        let sum = 0;
        for (let j = 0; j < this.hiddenSize; j++) sum += dPre[j] * wxh[i][j];
        dInput.push(sum);
      }
      dInputs[t] = dInput;
      dhNext = [];
      for (let i = 0; i < this.hiddenSize; i++) {
        let sum = 0;
        for (let j = 0; j < this.hiddenSize; j++) sum += dPre[j] * whh[i][j];
        dhNext.push(sum);
      }
    }
    this._dXh = dXh;
    this._dHh = dHh;
    this._dBias = dBias;
    return dInputs;
  };

  Rnn.prototype.sgd = function (learningRate) {
    if (arguments.length !== 1) throw new Error("Rnn.sgd() expects 1 argument: (lr)");
    if (this._dXh == null || this._dHh == null || this._dBias == null) {
      throw new Error("Rnn.sgd() requires backward() first");
    }
    const step = nnRequireFinite("Rnn.sgd", learningRate, "lr");
    nnApplyOwnedMatrix(this.weightsXh, this._dXh, step);
    nnApplyOwnedMatrix(this.weightsHh, this._dHh, step);
    nnApplyOwnedVector(this.bias, this._dBias, step);
    return null;
  };

  function LayerNorm(features) {
    if (arguments.length !== 1) throw new Error("LayerNorm() expects 1 argument: (features)");
    this.features = nnRequirePositiveInt("LayerNorm", features, "features");
    this.gamma = [];
    this.beta = [];
    for (let i = 0; i < this.features; i++) {
      this.gamma.push(1);
      this.beta.push(0);
    }
    this._xhat = null;
    this._rstd = 0;
    this._dGamma = null;
    this._dBeta = null;
  }

  LayerNorm.prototype.forward = function (x) {
    if (arguments.length !== 1) throw new Error("LayerNorm.forward() expects 1 argument: (x)");
    const input = neuralRequireVector("LayerNorm.forward", x, "x");
    if (input.length !== this.features) throw new Error("LayerNorm.forward() length must match features");
    let mean = 0;
    for (let i = 0; i < input.length; i++) mean += input[i];
    mean /= input.length;
    let variance = 0;
    for (let i = 0; i < input.length; i++) {
      const delta = input[i] - mean;
      variance += delta * delta;
    }
    variance /= input.length;
    this._rstd = 1 / Math.sqrt(variance + 0.00001);
    this._xhat = [];
    const output = [];
    for (let i = 0; i < input.length; i++) {
      const hat = (input[i] - mean) * this._rstd;
      this._xhat.push(hat);
      output.push(this.gamma[i] * hat + this.beta[i]);
    }
    return output;
  };

  LayerNorm.prototype.backward = function (upstream) {
    if (arguments.length !== 1) throw new Error("LayerNorm.backward() expects 1 argument: (upstream)");
    if (this._xhat == null) throw new Error("LayerNorm.backward() requires forward() first");
    const grad = neuralRequireVector("LayerNorm.backward", upstream, "upstream");
    if (grad.length !== this.features) throw new Error("LayerNorm.backward() upstream length must match features");
    const dGamma = [];
    const dBeta = [];
    const dxhat = [];
    for (let i = 0; i < this.features; i++) {
      dBeta.push(grad[i]);
      dGamma.push(grad[i] * this._xhat[i]);
      dxhat.push(grad[i] * this.gamma[i]);
    }
    let meanDx = 0;
    let meanDxX = 0;
    for (let i = 0; i < this.features; i++) {
      meanDx += dxhat[i];
      meanDxX += dxhat[i] * this._xhat[i];
    }
    meanDx /= this.features;
    meanDxX /= this.features;
    const dInput = [];
    for (let i = 0; i < this.features; i++) {
      dInput.push(this._rstd * (dxhat[i] - meanDx - this._xhat[i] * meanDxX));
    }
    this._dGamma = dGamma;
    this._dBeta = dBeta;
    return dInput;
  };

  LayerNorm.prototype.sgd = function (learningRate) {
    if (arguments.length !== 1) throw new Error("LayerNorm.sgd() expects 1 argument: (lr)");
    if (this._dGamma == null || this._dBeta == null) throw new Error("LayerNorm.sgd() requires backward() first");
    const step = nnRequireFinite("LayerNorm.sgd", learningRate, "lr");
    nnApplyOwnedVector(this.gamma, this._dGamma, step);
    nnApplyOwnedVector(this.beta, this._dBeta, step);
    return null;
  };

  function Attention(length, dim, scale) {
    const argc = arguments.length;
    if (argc < 2 || argc > 3) throw new Error("Attention() expects 2 or 3 arguments: (length, dim, scale?)");
    this.length = nnRequirePositiveInt("Attention", length, "length");
    this.dim = nnRequirePositiveInt("Attention", dim, "dim");
    this.scale = argc === 3 ? nnRequireFinite("Attention", scale, "scale") : 1 / Math.sqrt(this.dim);
    const width = 1 / Math.sqrt(this.dim);
    this.query = nnLayerMatrix(this.length, this.dim, width);
    this.key = nnLayerMatrix(this.length, this.dim, width);
    this._value = null;
    this._probs = null;
    this._blocked = null;
    this._dQuery = null;
    this._dKey = null;
  }

  Object.defineProperty(Attention.prototype, "probs", {
    get: function () {
      if (this._probs == null) throw new Error("Attention.probs requires forward() first");
      return this._probs.map(function (row) { return row.slice(); });
    }
  });

  Attention.prototype.forward = function (value, mask) {
    const argc = arguments.length;
    if (argc < 1 || argc > 2) throw new Error("Attention.forward() expects 1 or 2 arguments: (value, mask?)");
    const tokens = nnReadMatrix("Attention.forward", value, "value");
    if (tokens.length !== this.length || tokens[0].length !== this.dim) {
      throw new Error("Attention.forward() value must be length x dim");
    }
    let maskMatrix = null;
    if (argc === 2) {
      maskMatrix = nnReadMatrix("Attention.forward", mask, "mask");
      if (maskMatrix.length !== this.length || maskMatrix[0].length !== this.length) {
        throw new Error("Attention.forward() mask must be length x length");
      }
    }
    const query = nnReadMatrix("Attention.forward", this.query, "query");
    const key = nnReadMatrix("Attention.forward", this.key, "key");
    const scores = nnLayerMatmul(query, nnLayerTranspose(key));
    const probs = [];
    const blocked = [];
    for (let i = 0; i < this.length; i++) {
      const row = [];
      blocked[i] = [];
      for (let j = 0; j < this.length; j++) {
        blocked[i][j] = maskMatrix != null && maskMatrix[i][j] < 0.5;
        row.push(blocked[i][j] ? -1.0e9 : scores[i][j] * this.scale);
      }
      probs.push(nnSoftmax(row));
    }
    this._value = tokens;
    this._probs = probs;
    this._blocked = blocked;
    return nnLayerMatmul(probs, tokens);
  };

  Attention.prototype.backward = function (upstream) {
    if (arguments.length !== 1) throw new Error("Attention.backward() expects 1 argument: (upstream)");
    if (this._value == null || this._probs == null) throw new Error("Attention.backward() requires forward() first");
    const grad = nnReadMatrix("Attention.backward", upstream, "upstream");
    if (grad.length !== this.length || grad[0].length !== this.dim) {
      throw new Error("Attention.backward() upstream must be length x dim");
    }
    const dProbs = nnLayerMatmul(grad, nnLayerTranspose(this._value));
    const dScores = nnLayerZeros(this.length, this.length);
    for (let i = 0; i < this.length; i++) {
      let dot = 0;
      for (let j = 0; j < this.length; j++) dot += dProbs[i][j] * this._probs[i][j];
      for (let j = 0; j < this.length; j++) {
        const local = this._probs[i][j] * (dProbs[i][j] - dot);
        dScores[i][j] = this._blocked[i][j] ? 0 : local * this.scale;
      }
    }
    const query = nnReadMatrix("Attention.backward", this.query, "query");
    const key = nnReadMatrix("Attention.backward", this.key, "key");
    this._dQuery = nnLayerMatmul(dScores, key);
    this._dKey = nnLayerMatmul(nnLayerTranspose(dScores), query);
    return nnLayerMatmul(nnLayerTranspose(this._probs), grad);
  };

  Attention.prototype.sgd = function (learningRate) {
    if (arguments.length !== 1) throw new Error("Attention.sgd() expects 1 argument: (lr)");
    if (this._dQuery == null || this._dKey == null) throw new Error("Attention.sgd() requires backward() first");
    const step = nnRequireFinite("Attention.sgd", learningRate, "lr");
    nnApplyOwnedMatrix(this.query, this._dQuery, step);
    nnApplyOwnedMatrix(this.key, this._dKey, step);
    return null;
  };

  global.Dense = Dense;
  global.Sequential = Sequential;
  global.Conv = Conv;
  global.Embedding = Embedding;
  global.Rnn = Rnn;
  global.LayerNorm = LayerNorm;
  global.Attention = Attention;
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
