import { useEffect, useState } from "react";

// Tiny hash router: #/ , #/runs , #/runs/<id> , #/workflows , #/settings

export type Route =
  | { name: "overview" }
  | { name: "runs" }
  | { name: "run"; id: string }
  | { name: "workflows" }
  | { name: "settings" };

export function parseHash(hash: string): Route {
  const parts = hash.replace(/^#\/?/, "").split("/").filter(Boolean);
  switch (parts[0]) {
    case "runs":
      return parts[1] ? { name: "run", id: parts[1] } : { name: "runs" };
    case "workflows":
      return { name: "workflows" };
    case "settings":
      return { name: "settings" };
    default:
      return { name: "overview" };
  }
}

export function useRoute(): Route {
  const [route, setRoute] = useState<Route>(() => parseHash(window.location.hash));
  useEffect(() => {
    const onChange = () => setRoute(parseHash(window.location.hash));
    window.addEventListener("hashchange", onChange);
    return () => window.removeEventListener("hashchange", onChange);
  }, []);
  return route;
}

export function navigate(path: string): void {
  window.location.hash = path;
}
