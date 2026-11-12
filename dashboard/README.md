# Orchestra dashboard

React + TypeScript (Vite, TanStack Query). Pages: overview with live counts, run list with status filter and paging,
run detail with a live DAG graph (polls while a run is active) plus Cancel / Retry, workflow list with "Start run" and
a JSON workflow editor, and settings for the API URL and key.

```powershell
cd dashboard
npm install
npm run dev        # http://localhost:5173, proxies /api and /health to http://localhost:5080
npm test           # DAG layout unit tests
npm run build      # typecheck + production build into dist/
```

In dev the key `dev-admin-key` is pre-filled (see `appsettings.Development.json` in the API). Point the proxy at
another API with `ORCHESTRA_API=https://... npm run dev`, or set the base URL on the Settings page.
