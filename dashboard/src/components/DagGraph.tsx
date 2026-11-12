import { useMemo } from "react";
import type { TaskRunDto } from "../api";
import { layoutDag, NODE_H, NODE_W } from "../layout";

function truncate(text: string, max: number): string {
  return text.length > max ? text.slice(0, max - 1) + "…" : text;
}

export default function DagGraph({
  tasks,
  selected,
  onSelect,
}: {
  tasks: TaskRunDto[];
  selected?: string;
  onSelect: (name: string) => void;
}) {
  const layout = useMemo(
    () => layoutDag(tasks.map((t) => ({ name: t.name, dependsOn: t.dependsOn }))),
    // Re-layout only when the graph shape changes, not on every status refresh.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [tasks.map((t) => `${t.name}:${t.dependsOn.join(",")}`).join("|")],
  );
  const byName = new Map(tasks.map((t) => [t.name, t]));

  if (layout.nodes.length === 0) return <p className="muted">No tasks.</p>;

  return (
    <div className="dag-scroll">
      <svg width={layout.width} height={layout.height} role="img" aria-label="Task dependency graph">
        <defs>
          <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
            <path d="M 0 0 L 10 5 L 0 10 z" className="arrow-head" />
          </marker>
        </defs>

        {layout.edges.map((e) => (
          <path
            key={`${e.from}->${e.to}`}
            d={e.path}
            className={`edge${selected === e.to || selected === e.from ? " edge-active" : ""}`}
            markerEnd="url(#arrow)"
          />
        ))}

        {layout.nodes.map((n) => {
          const task = byName.get(n.name)!;
          return (
            <g
              key={n.name}
              transform={`translate(${n.x}, ${n.y})`}
              className={`node s-${task.status}${selected === n.name ? " node-selected" : ""}`}
              onClick={() => onSelect(n.name)}
              tabIndex={0}
              role="button"
              aria-label={`${task.name}, ${task.status}`}
              onKeyDown={(ev) => (ev.key === "Enter" || ev.key === " ") && onSelect(n.name)}
            >
              <rect width={NODE_W} height={NODE_H} rx={8} className="node-box" />
              <rect width={5} height={NODE_H} rx={2} className="node-stripe" />
              <text x={16} y={22} className="node-title">
                {truncate(task.name, 22)}
              </text>
              <text x={16} y={40} className="node-sub">
                {task.status}
                {task.attempt > 0 ? ` · attempt ${task.attempt}/${task.maxAttempts}` : ""}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}
