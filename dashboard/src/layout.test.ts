import { describe, expect, it } from "vitest";
import { computeLayers, layoutDag, NODE_W, GAP_X } from "./layout";

const t = (name: string, ...dependsOn: string[]) => ({ name, dependsOn });

describe("computeLayers", () => {
  it("layers a diamond by longest path", () => {
    const layers = computeLayers([t("a"), t("b", "a"), t("c", "a"), t("d", "b", "c")]);
    expect(layers.get("a")).toBe(0);
    expect(layers.get("b")).toBe(1);
    expect(layers.get("c")).toBe(1);
    expect(layers.get("d")).toBe(2);
  });

  it("uses the longest chain when paths differ in length", () => {
    const layers = computeLayers([t("a"), t("b", "a"), t("c", "b"), t("d", "a", "c")]);
    expect(layers.get("d")).toBe(3);
  });

  it("ignores unknown dependencies", () => {
    expect(computeLayers([t("a", "ghost")]).get("a")).toBe(0);
  });

  it("does not hang on a cycle", () => {
    const layers = computeLayers([t("a", "b"), t("b", "a")]);
    expect(layers.size).toBe(2);
  });
});

describe("layoutDag", () => {
  it("returns an empty layout for no tasks", () => {
    expect(layoutDag([])).toEqual({ nodes: [], edges: [], width: 0, height: 0 });
  });

  it("places columns left to right and draws one edge per dependency", () => {
    const layout = layoutDag([t("a"), t("b", "a"), t("c", "a"), t("d", "b", "c")]);
    const x = (n: string) => layout.nodes.find((p) => p.name === n)!.x;

    expect(x("b")).toBe(x("a") + NODE_W + GAP_X);
    expect(x("d")).toBeGreaterThan(x("b"));
    expect(layout.edges).toHaveLength(4);
  });

  it("gives tasks in the same column distinct rows", () => {
    const layout = layoutDag([t("a"), t("b"), t("c")]);
    const ys = new Set(layout.nodes.map((n) => n.y));
    expect(ys.size).toBe(3);
  });

  it("deduplicates repeated dependencies", () => {
    expect(layoutDag([t("a"), t("b", "a", "a")]).edges).toHaveLength(1);
  });
});
