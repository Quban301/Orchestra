// Pure DAG layout (no React), so it can be unit tested. Tasks are placed in columns by their longest
// dependency chain, then ordered inside each column to keep edges short.

export interface DagInput {
  name: string;
  dependsOn: string[];
}

export interface PlacedNode {
  name: string;
  layer: number;
  row: number;
  x: number;
  y: number;
}

export interface PlacedEdge {
  from: string;
  to: string;
  path: string;
}

export interface DagLayout {
  nodes: PlacedNode[];
  edges: PlacedEdge[];
  width: number;
  height: number;
}

export const NODE_W = 172;
export const NODE_H = 54;
export const GAP_X = 76;
export const GAP_Y = 22;
export const PAD = 16;

/** Layer = length of the longest chain of dependencies leading to the task. Unknown dependencies are ignored. */
export function computeLayers(tasks: DagInput[]): Map<string, number> {
  const byName = new Map(tasks.map((t) => [t.name, t]));
  const memo = new Map<string, number>();
  const visiting = new Set<string>();

  const depth = (name: string): number => {
    const known = memo.get(name);
    if (known !== undefined) return known;
    if (visiting.has(name)) return 0; // cycle guard: the API rejects cycles, but the UI must never hang
    visiting.add(name);
    const deps = (byName.get(name)?.dependsOn ?? []).filter((d) => byName.has(d));
    const layer = deps.length === 0 ? 0 : 1 + Math.max(...deps.map(depth));
    visiting.delete(name);
    memo.set(name, layer);
    return layer;
  };

  for (const t of tasks) depth(t.name);
  return memo;
}

export function layoutDag(tasks: DagInput[]): DagLayout {
  if (tasks.length === 0) return { nodes: [], edges: [], width: 0, height: 0 };

  const byName = new Map(tasks.map((t) => [t.name, t]));
  const order = new Map(tasks.map((t, i) => [t.name, i]));
  const layers = computeLayers(tasks);
  const maxLayer = Math.max(...layers.values());

  const columns: string[][] = Array.from({ length: maxLayer + 1 }, () => []);
  for (const t of tasks) columns[layers.get(t.name) ?? 0].push(t.name);

  // Order each column by the average row of its dependencies (barycenter) to reduce edge crossings.
  const rows = new Map<string, number>();
  columns.forEach((names, layerIndex) => {
    const barycenter = (n: string): number => {
      const deps = (byName.get(n)?.dependsOn ?? []).filter((d) => rows.has(d));
      return deps.length ? deps.reduce((sum, d) => sum + rows.get(d)!, 0) / deps.length : order.get(n)!;
    };
    names.sort((a, b) =>
      layerIndex === 0
        ? order.get(a)! - order.get(b)!
        : barycenter(a) - barycenter(b) || order.get(a)! - order.get(b)!,
    );
    names.forEach((n, i) => rows.set(n, i));
  });

  const maxRows = Math.max(...columns.map((c) => c.length));
  const step = NODE_H + GAP_Y;

  const nodes: PlacedNode[] = [];
  columns.forEach((names, layer) => {
    const offset = ((maxRows - names.length) * step) / 2; // centre short columns vertically
    names.forEach((name, row) => {
      nodes.push({ name, layer, row, x: PAD + layer * (NODE_W + GAP_X), y: PAD + offset + row * step });
    });
  });

  const placed = new Map(nodes.map((n) => [n.name, n]));
  const edges: PlacedEdge[] = [];
  for (const t of tasks) {
    const to = placed.get(t.name)!;
    for (const dep of new Set(t.dependsOn)) {
      const from = placed.get(dep);
      if (!from) continue;
      const x1 = from.x + NODE_W;
      const y1 = from.y + NODE_H / 2;
      const x2 = to.x;
      const y2 = to.y + NODE_H / 2;
      const dx = Math.max((x2 - x1) / 2, 16);
      edges.push({ from: dep, to: t.name, path: `M ${x1} ${y1} C ${x1 + dx} ${y1}, ${x2 - dx} ${y2}, ${x2} ${y2}` });
    }
  }

  return {
    nodes,
    edges,
    width: PAD * 2 + (maxLayer + 1) * NODE_W + maxLayer * GAP_X,
    height: PAD * 2 + maxRows * step - GAP_Y,
  };
}
