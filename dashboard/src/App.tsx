import { useQuery } from "@tanstack/react-query";
import { checkHealth } from "./api";
import { useRoute } from "./router";
import Overview from "./pages/Overview";
import Runs from "./pages/Runs";
import RunDetail from "./pages/RunDetail";
import Workflows from "./pages/Workflows";
import Settings from "./pages/Settings";

const NAV = [
  { href: "#/", label: "Overview", match: ["overview"] },
  { href: "#/runs", label: "Runs", match: ["runs", "run"] },
  { href: "#/workflows", label: "Workflows", match: ["workflows"] },
  { href: "#/settings", label: "Settings", match: ["settings"] },
];

export default function App() {
  const route = useRoute();
  const health = useQuery({ queryKey: ["health"], queryFn: checkHealth, refetchInterval: 5000 });

  return (
    <div className="shell">
      <header className="topbar">
        <a className="brand" href="#/">
          <span className="logo">◆</span> Orchestra
        </a>
        <nav>
          {NAV.map((n) => (
            <a key={n.href} href={n.href} className={n.match.includes(route.name) ? "active" : ""}>
              {n.label}
            </a>
          ))}
        </nav>
        <div className="spacer" />
        <span className={`health ${health.data ? "up" : "down"}`} title="API health">
          <span className="dot" /> {health.data ? "API online" : "API unreachable"}
        </span>
      </header>

      <main>
        {route.name === "overview" && <Overview />}
        {route.name === "runs" && <Runs />}
        {route.name === "run" && <RunDetail id={route.id} />}
        {route.name === "workflows" && <Workflows />}
        {route.name === "settings" && <Settings />}
      </main>
    </div>
  );
}
