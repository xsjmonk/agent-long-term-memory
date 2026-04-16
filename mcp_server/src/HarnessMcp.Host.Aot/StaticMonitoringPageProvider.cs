using HarnessMcp.Contracts;

namespace HarnessMcp.Host.Aot;

public static class StaticMonitoringPageProvider
{
    public static string GetHtml(AppConfig config)
    {
        var maxRows = config.Monitoring.MaxRenderedRows;
        var preview = config.Monitoring.MaxPayloadPreviewChars;
        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1"/>
  <title>Harness MCP Monitor</title>
  <style>
    body { font-family: Arial, Helvetica, sans-serif; margin: 0; background: #fff; color: #111; }
    header { padding: 12px 16px; background: #f3f4f6; border-bottom: 1px solid #d1d5db; position: sticky; top: 0; z-index: 2; }
    main { padding: 12px 16px 96px; display: grid; gap: 12px; }
    section { border: 1px solid #d1d5db; border-radius: 8px; overflow: hidden; background: #fff; }
    h2 { margin: 0; padding: 8px 10px; background: #f3f4f6; font-size: 14px; border-bottom: 1px solid #e5e7eb; }
    .table-wrap { max-height: 320px; overflow: auto; }
    table { width: 100%; border-collapse: collapse; font-size: inherit; }
    th, td { border-bottom: 1px solid #e5e7eb; padding: 6px 8px; vertical-align: top; }
    th { text-align: left; background: #f9fafb; position: sticky; top: 0; }
    tr:nth-child(even) td { background: #f9fafb; }
    .muted { color: #6b7280; }
    .warn { color: #6b7280; }
    .err { color: #6b7280; }
    #status { font-size: inherit; color: #6b7280; }
  </style>
</head>
<body>
  <header style="display:flex; align-items:center; justify-content:space-between; gap: 12px;">
    <div style="display:flex; flex-direction:column; gap: 4px;">
      <div><strong>Harness MCP</strong> <span class="muted">/monitor</span></div>
      <div id="status" class="muted">loading…</div>
    </div>
    <div style="display:flex; gap: 8px; align-items:center; flex: 0 0 auto;">
      <button type="button" id="btn-font-dec" style="padding: 6px 10px; border: 1px solid #d1d5db; background: #fff; cursor: pointer;">A-</button>
      <button type="button" id="btn-font-inc" style="padding: 6px 10px; border: 1px solid #d1d5db; background: #fff; cursor: pointer;">A+</button>
    </div>
  </header>
  <script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.7/dist/browser/signalr.min.js"></script>
  <main>
    <section><h2>Server summary</h2><div class="table-wrap"><table id="tbl-summary"><tbody></tbody></table></div></section>
    <section><h2>Live logs</h2><div class="table-wrap"><table><thead><tr><th>Time</th><th>Level</th><th>Category</th><th>Message</th></tr></thead><tbody id="tbl-logs"></tbody></table></div></section>
    <section><h2>Recent MCP operations</h2><div class="table-wrap"><table><thead><tr><th>Time</th><th>Request</th><th>Tool</th><th>Task</th><th>Status</th><th>Summary</th></tr></thead><tbody id="tbl-mcp"></tbody></table></div></section>
    <section><h2>Retrieval operations</h2><div class="table-wrap"><table><thead><tr><th>Time</th><th>Request</th><th>Tool</th><th>Task</th><th>Status</th><th>Summary</th></tr></thead><tbody id="tbl-retrieval"></tbody></table></div></section>
    <section><h2>SQL / embedding timings</h2><div class="table-wrap"><table><thead><tr><th>Time</th><th>Kind</th><th>Op</th><th>Ms</th><th>Summary</th></tr></thead><tbody id="tbl-timings"></tbody></table></div></section>
    <section><h2>Output previews</h2><div class="table-wrap"><table><thead><tr><th>Time</th><th>Tool</th><th>Preview</th></tr></thead><tbody id="tbl-out"></tbody></table></div></section>
    <section><h2>Warnings / errors</h2><div class="table-wrap"><table><thead><tr><th>Time</th><th>Level</th><th>Summary</th><th>Preview</th></tr></thead><tbody id="tbl-warn"></tbody></table></div></section>
  </main>
<script>
(() => {
  const MAX_ROWS = {{maxRows}};
  const statusEl = document.getElementById('status');
  const cap = (s) => (!s ? '' : (s.length > {{preview}} ? s.slice(0, {{preview}}) + '…' : s));

  // Convert a UTC timestamp (ISO string or ms) to the user's local time.
  function formatLocalTime(v) {
    if (v === null || v === undefined) return '';
    if (typeof v === 'number') {
      const d = new Date(v);
      return Number.isNaN(d.getTime()) ? String(v) : d.toLocaleString();
    }
    if (typeof v === 'string') {
      const t = v.trim();
      if (!t) return '';

      // If it's a numeric string, treat it as epoch-millis.
      if (/^-?\d+(\.\d+)?$/.test(t)) {
        const d = new Date(Number(t));
        return Number.isNaN(d.getTime()) ? t : d.toLocaleString();
      }

      const ms = Date.parse(t);
      if (Number.isFinite(ms)) return new Date(ms).toLocaleString();
      return t;
    }

    return String(v);
  }

  function setFontSizePx(px) {
    document.body.style.fontSize = px + 'px';
  }

  function bindFontButtons() {
    const dec = document.getElementById('btn-font-dec');
    const inc = document.getElementById('btn-font-inc');
    if (!dec || !inc) return;

    // Keep within a reasonable range for readability.
    const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
    const readPx = () => {
      const s = window.getComputedStyle(document.body).fontSize;
      const n = parseFloat(s);
      return Number.isFinite(n) ? n : 14;
    };

    dec.addEventListener('click', () => setFontSizePx(clamp(readPx() - 1, 10, 20)));
    inc.addEventListener('click', () => setFontSizePx(clamp(readPx() + 1, 10, 20)));
  }

  function appendRow(tbody, cells, maxRows) {
    const tr = document.createElement('tr');
    for (const c of cells) {
      const td = document.createElement('td');
      td.textContent = c;
      tr.appendChild(td);
    }
    tbody.appendChild(tr);
    while (tbody.rows.length > maxRows) tbody.deleteRow(0);
  }

  function scrollPanelToBottom(tbody) {
    // Each panel uses `.table-wrap { overflow: auto; }`, so scroll that container—not the window.
    if (!tbody) return;
    const wrap = tbody.closest('.table-wrap');
    if (!wrap) return;
    wrap.scrollTop = wrap.scrollHeight;
  }

  function appendRowScroll(tbody, cells, maxRows) {
    appendRow(tbody, cells, maxRows);
    // Ensure scroll follows SignalR/poll updates even when user scroll position changes.
    scrollPanelToBottom(tbody);
  }

  async function loadSnapshot() {
    const r = await fetch('/monitor/snapshot');
    const s = await r.json();
    const sum = document.querySelector('#tbl-summary tbody');
    sum.innerHTML = '';
    const sm = s.server;

    const startedMs = sm.startedUtc ? Date.parse(sm.startedUtc) : 0;
    const uptimeSeconds = startedMs > 0 ? Math.max(0, Math.floor((Date.now() - startedMs) / 1000)) : 0;
    const rows = [
      ['Server', sm.serverName],
      ['Version', sm.serverVersion],
      ['Transport', sm.protocolMode],
      ['Monitoring UI', String(sm.monitoringEnabled)],
      ['Realtime', String(sm.realtimeEnabled)],
      ['Uptime s', String(uptimeSeconds)],
      ['DB configured', String(sm.databaseConfigured)],
      ['Embedding', sm.embeddingProviderSummary],
    ];
    for (const [k,v] of rows) {
      const tr = document.createElement('tr');
      tr.innerHTML = `<td>${k}</td><td>${v}</td>`;
      sum.appendChild(tr);
    }

    const fillEvents = (id, items, row) => {
      const tb = document.getElementById(id);
      tb.innerHTML = '';
      for (const it of items) appendRow(tb, row(it), MAX_ROWS);
    };

    const recentOps = s.recentOperations || [];
    const retrievalTools = new Set([
      'retrieve_memory_by_chunks',
      'merge_retrieval_results',
      'build_memory_context_pack'
    ]);
    const mcpOps = recentOps;
    const retrievalOps = recentOps.filter(x => retrievalTools.has(x.toolName));
    fillEvents('tbl-mcp', mcpOps, it => [
      formatLocalTime(it.timestampUtc),
      it.requestId || '',
      it.toolName || '',
      it.taskId || '',
      String(it.eventKind),
      cap(it.summary)
    ]);
    fillEvents('tbl-retrieval', retrievalOps, it => [
      formatLocalTime(it.timestampUtc),
      it.requestId || '',
      it.toolName || '',
      it.taskId || '',
      'retrieval',
      cap(it.summary)
    ]);

    const lg = document.getElementById('tbl-logs');
    lg.innerHTML = '';
    for (const it of (s.recentLogs||[])) {
      appendRow(lg, [formatLocalTime(it.timestampUtc), it.level || '', it.toolName || '', cap(it.summary)], MAX_ROWS);
    }

    const tg = document.getElementById('tbl-timings');
    tg.innerHTML = '';
    for (const it of (s.recentTimings||[])) appendRow(tg, [formatLocalTime(it.timestampUtc), String(it.eventKind), it.toolName || '', '0', cap(it.summary)], MAX_ROWS);

    const og = document.getElementById('tbl-out');
    og.innerHTML = '';
    for (const it of (s.recentOutputs||[])) appendRow(og, [formatLocalTime(it.timestampUtc), it.toolName || '', cap(it.payloadPreviewJson || it.summary || '')], MAX_ROWS);

    const wg = document.getElementById('tbl-warn');
    wg.innerHTML = '';
    for (const it of (s.recentWarnings||[])) appendRow(wg, [formatLocalTime(it.timestampUtc), it.level || '', cap(it.summary), cap(it.payloadPreviewJson || '')], MAX_ROWS);

    window.__lastSeq = s.lastSequence || 0;
  }

  let conn;
  async function connectSignalR(realtime) {
    if (!realtime) {
      statusEl.textContent = 'Realtime disabled; polling /monitor/events';
      setInterval(poll, 2000);
      return;
    }
    try {
      if (!window.signalR) throw new Error('signalR missing');
      conn = new signalR.HubConnectionBuilder().withUrl('/monitor/hub').withAutomaticReconnect().build();
      conn.on('monitor', (json) => {
        try {
          const e = JSON.parse(json);
          ingestEvent(e);
        } catch {}
      });
      await conn.start();
      statusEl.textContent = 'SignalR connected';
    } catch (e) {
      statusEl.textContent = 'SignalR failed; falling back to polling';
      setInterval(poll, 2000);
    }
  }

  function ingestEvent(e) {
    const kind = e.eventKind;
    const toId = (k) => {
      if (typeof k === 'number') return k;
      if (typeof k !== 'string') return -1;
      const l = k.toLowerCase();
      if (l === 'log') return 0;
      if (l === 'requeststart' || l === 'request_start') return 1;
      if (l === 'requestsuccess' || l === 'request_success') return 2;
      if (l === 'requestfailure' || l === 'request_failure') return 3;
      if (l === 'sqltiming' || l === 'sql_timing') return 4;
      if (l === 'embeddingtiming' || l === 'embedding_timing') return 5;
      if (l === 'mergetiming' || l === 'merge_timing') return 6;
      if (l === 'contextpackbuilt' || l === 'context_pack_built') return 7;
      if (l === 'warning') return 8;
      if (l === 'healthfailure' || l === 'health_failure') return 9;
      return -1;
    };
    const id = toId(kind);

    if (id === 0) appendRowScroll(document.getElementById('tbl-logs'), [formatLocalTime(e.timestampUtc), e.level || 'Info', e.toolName || 'log', cap(e.summary)], MAX_ROWS);

    if (id === 1 || id === 2 || id === 3) {
      appendRowScroll(document.getElementById('tbl-mcp'), [formatLocalTime(e.timestampUtc), e.requestId || '', e.toolName || '', e.taskId || '', String(kind), cap(e.summary)], MAX_ROWS);
      if (e.toolName === 'retrieve_memory_by_chunks' || e.toolName === 'merge_retrieval_results' || e.toolName === 'build_memory_context_pack') {
        if (id === 2) appendRowScroll(document.getElementById('tbl-retrieval'), [formatLocalTime(e.timestampUtc), e.requestId || '', e.toolName || '', e.taskId || '', 'retrieval', cap(e.summary)], MAX_ROWS);
      }
    }

    if (id === 4 || id === 5 || id === 6 || id === 7) {
      appendRowScroll(document.getElementById('tbl-timings'), [formatLocalTime(e.timestampUtc), String(kind), e.toolName || '', '0', cap(e.summary)], MAX_ROWS);
    }

    if (id === 8 || id === 3 || id === 9) {
      appendRowScroll(document.getElementById('tbl-warn'), [formatLocalTime(e.timestampUtc), e.level || 'Warning', cap(e.summary), cap(e.payloadPreviewJson || '')], MAX_ROWS);
    }

    if (id === 2 && e.payloadPreviewJson) {
      appendRowScroll(document.getElementById('tbl-out'), [formatLocalTime(e.timestampUtc), e.toolName || '', cap(e.payloadPreviewJson)], MAX_ROWS);
    }
  }

  async function poll() {
    const after = window.__lastSeq || 0;
    const r = await fetch('/monitor/events?after=' + encodeURIComponent(after) + '&take=200');
    const b = await r.json();
    window.__lastSeq = b.lastSequence || after;
    for (const e of (b.events||[])) ingestEvent(e);
  }

  (async () => {
    await loadSnapshot();
    bindFontButtons();
    setFontSizePx(14);
    const r = await fetch('/monitor/snapshot');
    const s = await r.json();
    await connectSignalR(!!s.server.realtimeEnabled);
  })();
})();
</script>
</body>
</html>
""";
    }
}
