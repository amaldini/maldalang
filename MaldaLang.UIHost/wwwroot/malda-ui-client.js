(function () {
  const protocolVersion = "1.0";
  const params = new URLSearchParams(window.location.search);
  const sessionId = params.get("session") || "default";
  const appRoot = document.getElementById("app");
  const authToken = params.get("token");

  let tree = null;
  let expectedServerSequence = 1;
  let nextClientSequence = 1;
  let lastServerEnvelopeId = null;
  const pendingPatches = [];
  let patchFrameScheduled = false;
  const controlStateByPath = new Map();
  const richEventProps = new Set([
    "onClick", "onChange", "onInput", "onSubmit", "onClose", "onFocus", "onBlur",
    "onRowClick", "onSelectionChange", "onSort", "onFilter", "onPageChange", "onViewportChange",
    "onColumnResize", "onNodeSelect", "onNodeToggle", "onNodeExpand", "onNodeCollapse", "onNodeActivate", "onLoadChildren",
    "onDragStart", "onDragOver", "onDrop", "onDragEnd"
  ]);

  const wsProtocol = window.location.protocol === "https:" ? "wss" : "ws";
  const wsUrl = `${wsProtocol}://${window.location.host}/ui/ws/${encodeURIComponent(sessionId)}`;
  const headers = authToken ? { "X-Malda-UI-Auth": authToken } : undefined;
  const reconnectBaseDelayMs = 500;
  const reconnectMaxDelayMs = 10000;
  const reconnectJitterMs = 250;
  const queuedOutboundLimit = 200;
  ensureFrameworkStyles();
  let socket = null;
  let reconnectAttempts = 0;
  let reconnectTimerId = null;
  let manualDisconnect = false;
  const queuedOutboundMessages = [];
  void headers; // WebSocket browser API does not support custom headers; token query param is used for now.
  connectWebSocket();
  window.addEventListener("beforeunload", () => {
    manualDisconnect = true;
    clearReconnectTimer();
    if (socket && socket.readyState === WebSocket.OPEN) {
      socket.close(1000, "page_unload");
    }
  });

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

  function ensureFrameworkStyles() {
    if (document.getElementById("malda-ui-framework-styles")) {
      return;
    }
    const style = document.createElement("style");
    style.id = "malda-ui-framework-styles";
    style.textContent = `
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
    `;
    document.head.appendChild(style);
  }

  function applyInboundPayload(payload) {
    if (payload && payload.patches) {
      pendingPatches.push(...payload.patches);
      schedulePatchFlush();
      return;
    }
    if (payload && payload.tree) {
      tree = payload.tree;
      renderRoot();
      return;
    }
    if (payload) {
      tree = payload;
      renderRoot();
    }
  }

  // Allows host pages to bootstrap rendering directly from a fetched envelope.
  window.maldaUiApplyEnvelope = function (envelopeOrPayload) {
    if (!envelopeOrPayload) return;
    const payload = envelopeOrPayload.payload != null ? envelopeOrPayload.payload : envelopeOrPayload;
    applyInboundPayload(normalizePayload(payload));
  };

  function applyPatches(patches) {
    for (const patch of patches || []) {
      if (!patch || !patch.op) {
        continue;
      }

      if (patch.op === "ReplaceNode") {
        applyReplaceNode(patch);
      } else if (patch.op === "SetProp") {
        applySetProp(patch);
      } else if (patch.op === "RemoveProp") {
        applyRemoveProp(patch);
      } else if (patch.op === "InsertChild") {
        applyInsertChild(patch);
      } else if (patch.op === "RemoveChild") {
        applyRemoveChild(patch);
      }
    }
  }

  function applyReplaceNode(patch) {
    const path = patch.path || "/";
    if (path === "/") {
      tree = patch.value || null;
      return;
    }

    const parentPath = parentOf(path);
    const index = childIndexOf(path);
    const parentNode = getNodeAtPath(parentPath);
    if (!parentNode || !Array.isArray(parentNode.children)) {
      return;
    }

    parentNode.children[index] = patch.value || null;
  }

  function applySetProp(patch) {
    const node = getNodeAtPath(patch.path || "/");
    if (!node) return;
    node.props = node.props || {};
    node.props[patch.prop] = patch.value;
  }

  function applyRemoveProp(patch) {
    const node = getNodeAtPath(patch.path || "/");
    if (!node || !node.props) return;
    delete node.props[patch.prop];
  }

  function applyInsertChild(patch) {
    const node = getNodeAtPath(patch.path || "/");
    if (!node) return;
    node.children = node.children || [];
    const index = Number.isInteger(patch.index) ? patch.index : node.children.length;
    node.children.splice(index, 0, patch.value || null);
  }

  function applyRemoveChild(patch) {
    const node = getNodeAtPath(patch.path || "/");
    if (!node || !Array.isArray(node.children)) return;
    if (!Number.isInteger(patch.index)) return;
    node.children.splice(patch.index, 1);
  }

  function schedulePatchFlush() {
    if (patchFrameScheduled) return;
    patchFrameScheduled = true;
    requestAnimationFrame(() => {
      patchFrameScheduled = false;
      const focusPath = document.activeElement ? document.activeElement.getAttribute("data-ui-path") : null;
      const patches = pendingPatches.splice(0, pendingPatches.length);
      applyPatches(patches);
      renderRoot();
      if (focusPath) {
        const focusEl = appRoot.querySelector(`[data-ui-path="${cssEscape(focusPath)}"]`);
        if (focusEl && typeof focusEl.focus === "function") {
          focusEl.focus();
        }
      }
    });
  }

  function renderRoot() {
    appRoot.innerHTML = "";
    if (!tree) {
      appRoot.textContent = "No UI mounted yet.";
      return;
    }
    appRoot.appendChild(renderNode(tree, "/"));
  }

  function renderNode(node, path) {
    const type = node.type || "div";
    const props = node.props || {};
    const children = node.children || [];

    if (type === "text") {
      const textValue = props.value != null ? String(props.value) : "";
      return document.createTextNode(textValue);
    }

    if (type === "dataGrid") {
      return renderDataGrid(path, props);
    }
    if (type === "treeView") {
      return renderTreeView(path, props);
    }

    const tag = mapTag(type);
    const el = document.createElement(tag);
    el.setAttribute("data-ui-path", path);
    applyInputTypeDefaults(type, el, props);

    for (const [key, value] of Object.entries(props)) {
      if (richEventProps.has(key)) {
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
      } else if (value != null && (typeof value === "string" || typeof value === "number" || typeof value === "boolean")) {
        el.setAttribute(key, String(value));
      }
    }

    applyA11yDefaults(type, el, props);

    if (type === "button" || props.onClick) {
      el.addEventListener("click", () => sendEvent("click", path, collectPayload(type, el, props)));
    }
    if (
      type === "textField" || type === "slider" || type === "checkbox" || type === "select" ||
      type === "textArea" || type === "radioGroup" || type === "switch" || type === "datePicker" || type === "paginator" ||
      props.onChange
    ) {
      el.addEventListener("change", () => {
        const payload = collectPayload(type, el, props);
        sendEvent("change", path, payload);
      });
    }
    if (type === "textField" || type === "textArea" || type === "slider" || type === "datePicker" || props.onInput) {
      el.addEventListener("input", () => {
        const payload = collectPayload(type, el, props);
        sendEvent("input", path, payload);
      });
    }
    if (type === "form" || props.onSubmit) {
      el.addEventListener("submit", (evt) => {
        evt.preventDefault();
        const payload = collectPayload(type, el, props);
        sendEvent("submit", path, payload);
      });
    }
    if (props.onFocus) {
      el.addEventListener("focus", () => sendEvent("focus", path, collectPayload(type, el, props)));
    }
    if (props.onBlur) {
      el.addEventListener("blur", () => sendEvent("blur", path, collectPayload(type, el, props)));
    }

    children.forEach((child, index) => {
      el.appendChild(renderNode(child, `${path}${index}/`));
    });
    return el;
  }

  function renderDataGrid(path, props) {
    const root = document.createElement("div");
    root.setAttribute("data-ui-path", path);
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

    const header = document.createElement("div");
    header.className = "malda-data-grid-header";
    header.setAttribute("role", "row");
    columns.forEach((column, idx) => {
      const cell = document.createElement("div");
      cell.className = "malda-data-grid-header-cell";
      cell.setAttribute("role", "columnheader");
      cell.tabIndex = 0;
      const title = column && column.title != null ? String(column.title) : `Column ${idx + 1}`;
      const sortKey = column && column.key != null ? String(column.key) : title;
      cell.setAttribute("data-column-key", sortKey);
      cell.setAttribute("data-column-title", title);
      const titleEl = document.createElement("span");
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
            sendEvent("sort", path, nextSort);
            return;
          }
          state.localSort = nextSort;
          renderRoot();
        });
      }
      if (resizableColumns) {
        const handle = document.createElement("span");
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
            document.removeEventListener("mousemove", onMouseMove);
            document.removeEventListener("mouseup", onMouseUp);
            if (props.onColumnResize) {
              const resizedColumn = column && column.key != null ? String(column.key) : String(idx);
              sendEvent("columnResize", path, {
                columnKey: resizedColumn,
                width: state.columnWidths[idx],
                widths: columns.map((_, colIdx) => state.columnWidths[colIdx] || null)
              });
            }
          };
          document.addEventListener("mousemove", onMouseMove);
          document.addEventListener("mouseup", onMouseUp);
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
      const filterBox = document.createElement("input");
      filterBox.type = "text";
      filterBox.className = "malda-data-grid-filter";
      filterBox.placeholder = "Filter";
      filterBox.value = props.filter != null ? String(props.filter) : "";
      filterBox.addEventListener("input", () => {
        sendEvent("filter", path, { filter: filterBox.value });
      });
      root.appendChild(filterBox);
    }

    const viewport = document.createElement("div");
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

    const topSpacer = document.createElement("div");
    topSpacer.style.height = virtualize ? `${start * rowHeight}px` : "0px";
    viewport.appendChild(topSpacer);

    for (let rowIndex = start; rowIndex < end; rowIndex++) {
      const row = rows[rowIndex];
      const rowKey = resolveRowKey(row, rowIndex, rowKeyField);
      const rowEl = document.createElement("div");
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
        const cell = document.createElement("div");
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
        sendEvent("rowClick", path, { rowIndex, rowKey, row });
        sendEvent("selectionChange", path, { rowIndex, rowKey, selectedKeys: Array.from(nextSelected) });
      });
      if (dragEnabled) {
        rowEl.addEventListener("dragstart", (evt) => {
          state.draggedRow = { rowIndex, rowKey, row };
          if (evt.dataTransfer) {
            evt.dataTransfer.effectAllowed = "move";
            evt.dataTransfer.setData("text/plain", rowKey);
          }
          sendEvent("dragStart", path, { rowIndex, rowKey, row });
        });
        rowEl.addEventListener("dragover", (evt) => {
          evt.preventDefault();
          sendEvent("dragOver", path, {
            rowIndex,
            rowKey,
            sourceRow: state.draggedRow || null
          });
        });
        rowEl.addEventListener("drop", (evt) => {
          evt.preventDefault();
          const sourceRow = state.draggedRow || null;
          sendEvent("drop", path, { rowIndex, rowKey, row, sourceRow });
        });
        rowEl.addEventListener("dragend", () => {
          sendEvent("dragEnd", path, { rowIndex, rowKey, row });
          state.draggedRow = null;
        });
      }
      rowEl.addEventListener("keydown", (evt) => handleGridRowKeydown(evt, viewport, path, rowIndex, rowKey, totalRows));
      viewport.appendChild(rowEl);
    }
    const templateColumns = buildTemplateColumns();
    applyTemplateColumns(templateColumns, viewport, header);

    const bottomSpacer = document.createElement("div");
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
          sendEvent("viewportChange", path, { start: nextStart, end: nextEnd, totalRows });
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
        sendEvent("drop", path, { rowIndex: null, rowKey: null, sourceRow });
      });
    }

    requestAnimationFrame(() => {
      if (state.scrollTop && viewport.scrollTop !== state.scrollTop) {
        viewport.scrollTop = state.scrollTop;
      }
    });

    if (props.onPageChange && props.page != null && props.pageSize != null) {
      const pager = document.createElement("div");
      pager.className = "malda-data-grid-pager";
      const page = Number(props.page);
      const pageSize = Math.max(1, Number(props.pageSize));
      const totalItems = props.totalItems != null ? Number(props.totalItems) : rows.length;
      const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

      const prev = document.createElement("button");
      prev.type = "button";
      prev.textContent = "Prev";
      prev.disabled = page <= 1;
      prev.addEventListener("click", () => sendEvent("pageChange", path, { page: Math.max(1, page - 1), pageSize, totalItems }));

      const next = document.createElement("button");
      next.type = "button";
      next.textContent = "Next";
      next.disabled = page >= totalPages;
      next.addEventListener("click", () => sendEvent("pageChange", path, { page: Math.min(totalPages, page + 1), pageSize, totalItems }));

      const label = document.createElement("span");
      label.textContent = `Page ${page} / ${totalPages}`;
      pager.appendChild(prev);
      pager.appendChild(label);
      pager.appendChild(next);
      root.appendChild(pager);
    }

    return root;
  }

  function renderTreeView(path, props) {
    const root = document.createElement("div");
    root.setAttribute("data-ui-path", path);
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

    const list = document.createElement("ul");
    list.style.listStyle = props.showLines ? "disc" : "none";
    list.style.paddingLeft = "0.75rem";
    root.appendChild(list);
    renderTreeNodes(list, nodes, path, 1, expandedKeys, selectedKeys, selectionMode, nodeKeyField, props, state);

    root.addEventListener("keydown", (evt) => {
      const focused = document.activeElement;
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
      const li = document.createElement("li");
      li.setAttribute("role", "none");
      const item = document.createElement("div");
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
        const nested = document.createElement("ul");
        nested.style.listStyle = props.showLines ? "circle" : "none";
        nested.style.paddingLeft = "1rem";
        renderTreeNodes(nested, children, `${nodePath}`, level + 1, expandedKeys, selectedKeys, selectionMode, nodeKeyField, props, treeState);
        li.appendChild(nested);
      } else if (hasChildren && expanded && props.lazy && children.length === 0 && treeState.loadingNodeKeys.has(nodeKey)) {
        const loading = document.createElement("div");
        loading.textContent = "Loading...";
        loading.className = "malda-tree-view-loading";
        li.appendChild(loading);
      }
      container.appendChild(li);
    });
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
      if (value != null && (typeof value === "string" || typeof value === "number" || typeof value === "boolean")) {
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
      el.style.cssText = String(value);
      return;
    }

    if (typeof value !== "object" || Array.isArray(value)) {
      el.style.cssText = "";
      return;
    }

    el.style.cssText = "";
    for (const [propName, propValue] of Object.entries(value)) {
      if (propValue == null) {
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

  function sendEvent(type, targetPath, payload) {
    enqueueOrSend({
      type: "event",
      eventType: type,
      targetPath,
      payload,
      sessionId,
      version: protocolVersion,
      sequence: nextClientSequence++,
      envelopeId: cryptoRandom()
    });
  }

  function sendControlMessage(type, payload) {
    enqueueOrSend({
      type,
      payload,
      sessionId,
      version: protocolVersion,
      sequence: nextClientSequence++,
      envelopeId: cryptoRandom()
    });
  }

  function sendAck(message) {
    enqueueOrSend({
      type: "ack",
      ackSequence: message.sequence,
      sessionId,
      version: protocolVersion,
      sequence: nextClientSequence++,
      envelopeId: message.envelopeId || cryptoRandom()
    });
  }

  function requestResync() {
    sendControlMessage("resync", { reason: "client_sequence_mismatch" });
  }

  function connectWebSocket() {
    clearReconnectTimer();
    if (manualDisconnect) {
      return;
    }

    let nextSocket = null;
    try {
      nextSocket = new WebSocket(wsUrl);
    } catch (err) {
      console.error("malda-ui websocket construction failed", err);
      scheduleReconnect();
      return;
    }

    socket = nextSocket;

    nextSocket.addEventListener("open", () => {
      reconnectAttempts = 0;
      clearReconnectTimer();
      console.log("malda-ui connected", { sessionId });
      flushQueuedOutboundMessages();
      // Request the latest cached envelope so pages that mounted before websocket connect still render.
      requestResync();
    });

    nextSocket.addEventListener("message", (evt) => {
      try {
        const message = JSON.parse(evt.data);
        if (message.type === "ping") {
          sendControlMessage("pong", {});
          return;
        }

        if (message.type === "ack" || message.type === "nack") {
          return;
        }

        if (message.type === "error") {
          console.warn("malda-ui protocol error", message.error || {});
          return;
        }

        if (message.type === "mount" || message.type === "patch" || message.type === "resync") {
          if (!validateInboundEnvelope(message)) {
            requestResync();
            return;
          }

          applyInboundPayload(normalizePayload(message.payload));

          sendAck(message);
          return;
        }
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
      socket.send(JSON.stringify(message));
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

  function validateInboundEnvelope(message) {
    if (message.version && message.version !== protocolVersion) {
      console.warn("Protocol version mismatch", { expected: protocolVersion, got: message.version });
      return false;
    }

    if (typeof message.sequence !== "number") {
      return true;
    }

    if (message.sequence < expectedServerSequence) {
      return false;
    }
    if (message.sequence > expectedServerSequence) {
      return false;
    }

    expectedServerSequence++;
    lastServerEnvelopeId = message.envelopeId || lastServerEnvelopeId;
    return true;
  }

  function getNodeAtPath(path) {
    if (!path || path === "/") {
      return tree;
    }

    const segments = parsePath(path);
    let current = tree;
    for (const idx of segments) {
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

  function cryptoRandom() {
    if (window.crypto && typeof window.crypto.randomUUID === "function") {
      return window.crypto.randomUUID().replace(/-/g, "");
    }
    return String(Date.now()) + String(Math.floor(Math.random() * 1e6));
  }

  function cssEscape(value) {
    if (window.CSS && typeof window.CSS.escape === "function") {
      return window.CSS.escape(value);
    }
    return String(value).replace(/"/g, '\\"');
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
        return "div";
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

    if ((type === "textField" || type === "textArea" || type === "datePicker") && !el.hasAttribute("aria-label") && props.placeholder) {
      el.setAttribute("aria-label", String(props.placeholder));
    }
  }
})();
