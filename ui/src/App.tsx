import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Badge, Button, Card, FluentProvider, Input, MessageBar, MessageBarBody,
  Spinner, Tab, TabList, Table, TableBody, TableCell, TableHeader,
  TableHeaderCell, TableRow, Tooltip, webDarkTheme, webLightTheme,
} from '@fluentui/react-components';
import {
  ArrowClockwiseRegular, BoxMultipleRegular, CheckmarkCircleRegular,
  CodeRegular, DesktopRegular, SearchRegular, ServerRegular,
  WeatherMoonRegular, WeatherSunnyRegular,
} from '@fluentui/react-icons';

interface Snapshot {
  status: 'ready' | 'notFound' | 'error';
  executablePath: string | null;
  operatingSystem: string;
  architecture: string;
  checkedAt: string;
  sdks: { version: string; path: string }[];
  runtimes: { name: string; version: string; path: string }[];
  error: string | null;
}

interface InstallationRow {
  id: string;
  kind: 'sdk' | 'runtime';
  name: string;
  framework: string;
  version: string;
  path: string;
}

const comparer = new Intl.Collator('en', { numeric: true });

function runtimeName(name: string) {
  if (name === 'Microsoft.NETCore.App') return '.NET Runtime';
  if (name === 'Microsoft.AspNetCore.App') return 'ASP.NET Core Runtime';
  if (name === 'Microsoft.WindowsDesktop.App') return 'Windows Desktop Runtime';
  return name;
}

export function App() {
  const [dark, setDark] = useState(() => window.matchMedia('(prefers-color-scheme: dark)').matches);
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [tab, setTab] = useState('all');
  const [sort, setSort] = useState<{ column: 'name' | 'version' | 'path'; ascending: boolean }>({ column: 'version', ascending: false });
  const request = useRef<AbortController | null>(null);

  const refresh = useCallback(async () => {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setError(null);
    try {
      const response = await fetch('/api/installation', { signal: controller.signal, cache: 'no-store' });
      const data: Snapshot = await response.json();
      if (!response.ok || data.status === 'error') throw new Error(data.error ?? 'The installation could not be read.');
      setSnapshot(data);
    } catch (failure) {
      if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : 'Could not connect to .NET Cockpit.');
    } finally {
      if (request.current === controller) setBusy(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
    return () => request.current?.abort();
  }, [refresh]);

  const rows = useMemo(() => {
    if (!snapshot) return [];
    const installations: InstallationRow[] = [
      ...snapshot.sdks.map(sdk => ({ ...sdk, id: `sdk:${sdk.version}:${sdk.path}`, kind: 'sdk' as const, name: '.NET SDK', framework: 'Software development kit' })),
      ...snapshot.runtimes.map(runtime => ({ ...runtime, id: `${runtime.name}:${runtime.version}:${runtime.path}`, kind: 'runtime' as const, name: runtimeName(runtime.name), framework: runtime.name })),
    ];
    const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return installations
      .filter(row => (tab === 'all' || (tab === 'sdks' ? row.kind === 'sdk' : row.kind === 'runtime'))
        && terms.every(term => `${row.name} ${row.framework} ${row.version} ${row.path}`.toLowerCase().includes(term)))
      .sort((a, b) => (sort.ascending ? 1 : -1) * comparer.compare(a[sort.column], b[sort.column]) || comparer.compare(a.name, b.name));
  }, [snapshot, query, tab, sort]);

  const sortBy = (column: typeof sort.column) => setSort(current => ({ column, ascending: current.column === column ? !current.ascending : true }));
  const total = snapshot ? snapshot.sdks.length + snapshot.runtimes.length : 0;

  return (
    <FluentProvider theme={dark ? webDarkTheme : webLightTheme} className="app" data-theme={dark ? 'dark' : 'light'}>
      <a className="skip-link" href="#main">Skip to content</a>
      <header className="app-header">
        <div className="brand"><span className="brand-symbol">.NET</span><strong>.NET Cockpit</strong></div>
        <div className="header-actions">
          <Badge appearance="tint" color="success" icon={<CheckmarkCircleRegular />}>Local workspace</Badge>
          <Tooltip content={dark ? 'Switch to light mode' : 'Switch to dark mode'} relationship="label">
            <Button appearance="subtle" icon={dark ? <WeatherSunnyRegular /> : <WeatherMoonRegular />} onClick={() => setDark(!dark)} aria-label={dark ? 'Switch to light mode' : 'Switch to dark mode'} />
          </Tooltip>
        </div>
      </header>
      <div className="workspace">
        <aside className="sidebar" aria-label="Workspace navigation">
          <span className="sidebar-label">ENVIRONMENT</span>
          <Button as="a" href="#main" appearance="subtle" icon={<DesktopRegular />} className="nav-current" aria-current="page">Installations</Button>
          <div className="sidebar-footer"><span className="status-dot" />Running on your machine</div>
        </aside>
        <main id="main" tabIndex={-1}>
          <div className="breadcrumb">Environment <span>/</span> Installations</div>
          <div className="page-heading">
            <div><h1>Your .NET environment</h1><p className="muted intro">Installed SDKs and runtimes, in one place.</p></div>
            <Button appearance="primary" icon={<ArrowClockwiseRegular />} onClick={() => void refresh()} disabled={busy}>{busy ? 'Checking…' : 'Refresh'}</Button>
          </div>
          <section className="summary-grid" aria-label="Installation summary">
            <Card className="summary-card"><div className="summary-top"><span>Installed SDKs</span><CodeRegular /></div><strong className="summary-number">{snapshot ? snapshot.sdks.length : '—'}</strong><span className="muted">Build and develop applications</span></Card>
            <Card className="summary-card"><div className="summary-top"><span>Installed runtimes</span><BoxMultipleRegular /></div><strong className="summary-number">{snapshot ? snapshot.runtimes.length : '—'}</strong><span className="muted">Run .NET applications</span></Card>
            <Card className="summary-card"><div className="summary-top"><span>This machine</span><DesktopRegular /></div><strong className="summary-architecture">{snapshot ? snapshot.architecture.toUpperCase() : '—'}</strong><span className="muted os-description">{snapshot?.operatingSystem ?? 'Checking your environment…'}</span></Card>
          </section>
          {error && <MessageBar intent="error"><MessageBarBody>{error} <Button appearance="transparent" onClick={() => void refresh()} disabled={busy}>Try again</Button></MessageBarBody></MessageBar>}
          <section className="inventory" aria-labelledby="inventory-title" aria-busy={busy}>
            <div className="inventory-heading"><div><h2 id="inventory-title">Installed versions</h2><p className="muted">The .NET installation available on your PATH.</p></div>{snapshot && <Badge appearance="outline">{total} installed</Badge>}</div>
            <div className="inventory-toolbar">
              <TabList selectedValue={tab} onTabSelect={(_, data) => setTab(String(data.value))} aria-label="Installation type">
                <Tab value="all">All versions</Tab><Tab value="sdks">SDKs</Tab><Tab value="runtimes">Runtimes</Tab>
              </TabList>
              <Input contentBefore={<SearchRegular />} placeholder="Filter versions, names, or paths" aria-label="Filter installed versions" value={query} onChange={(_, data) => setQuery(data.value)} className="filter-input" />
            </div>
            {!snapshot && busy ? <div className="empty-state"><Spinner label="Reading installed .NET versions…" /></div>
              : snapshot?.status === 'notFound' ? <div className="empty-state"><div className="empty-icon"><CodeRegular /></div><h3>No .NET installation found</h3><p>The dotnet command is not available on PATH.</p><p className="muted">.NET Cockpit runs independently. After installing .NET, restart Cockpit if PATH has changed.</p></div>
                : snapshot && total === 0 ? <div className="empty-state"><h3>No SDKs or runtimes found</h3><p className="muted">The dotnet host is available, but it reported no installed SDKs or runtimes.</p></div>
                  : !snapshot ? <div className="empty-state"><h3>Installation information is unavailable</h3><p className="muted">Try refreshing to check your environment again.</p></div>
                    : <div className="table-scroll" role="region" aria-label="Installed .NET versions" tabIndex={0}>
                      <Table aria-label="Installed .NET versions" sortable>
                        <TableHeader><TableRow>
                          <TableHeaderCell onClick={() => sortBy('name')} sortDirection={sort.column === 'name' ? (sort.ascending ? 'ascending' : 'descending') : undefined}>Installation</TableHeaderCell>
                          <TableHeaderCell onClick={() => sortBy('version')} sortDirection={sort.column === 'version' ? (sort.ascending ? 'ascending' : 'descending') : undefined}>Version</TableHeaderCell>
                          <TableHeaderCell onClick={() => sortBy('path')} sortDirection={sort.column === 'path' ? (sort.ascending ? 'ascending' : 'descending') : undefined}>Location</TableHeaderCell>
                        </TableRow></TableHeader>
                        <TableBody>{rows.map(row => <TableRow key={row.id}>
                          <TableCell><div className="installation-name"><span className={`installation-icon ${row.kind}`}>{row.kind === 'sdk' ? <CodeRegular /> : <ServerRegular />}</span><div><strong>{row.name}</strong><span className="muted framework-name">{row.framework}</span></div></div></TableCell>
                          <TableCell><Badge appearance="tint" color={row.kind === 'sdk' ? 'brand' : 'informative'} className="version-badge">{row.version}</Badge></TableCell>
                          <TableCell><code className="installation-path">{row.path}</code></TableCell>
                        </TableRow>)}</TableBody>
                      </Table>
                      {rows.length === 0 && <div className="empty-state"><h3>No matching versions</h3><p className="muted">Try another filter or switch to All versions.</p><Button onClick={() => { setQuery(''); setTab('all'); }}>Clear filters</Button></div>}
                    </div>}
            <div className="inventory-footer"><span role="status" aria-live="polite">{snapshot ? `${rows.length} of ${total} installations shown` : 'Waiting for installation information'}</span><span>{snapshot ? `Checked ${new Date(snapshot.checkedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}` : ''}</span></div>
          </section>
          {snapshot?.executablePath && <div className="host-info"><span className="status-dot" /><span>dotnet host</span><code>{snapshot.executablePath}</code></div>}
          <p className="scope-note muted">This view lists the SDKs and runtimes reported by the selected dotnet host. Separate installations and other architectures may not appear here.</p>
        </main>
      </div>
    </FluentProvider>
  );
}
