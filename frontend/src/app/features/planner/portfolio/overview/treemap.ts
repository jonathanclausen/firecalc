export interface Tile<T> {
  item: T;
  /** Position and size in percent of the whole area. */
  x: number;
  y: number;
  w: number;
  h: number;
}

/**
 * Squarified treemap (Bruls et al.): lays the items out in rows that keep tiles as close to square as
 * possible, largest first, in a box of the given aspect ratio (width / height).
 */
export function squarify<T>(items: T[], size: (item: T) => number, aspect: number): Tile<T>[] {
  const list = items.filter((i) => size(i) > 0).sort((a, b) => size(b) - size(a));
  const total = list.reduce((s, i) => s + size(i), 0);
  if (total <= 0) return [];

  // Work in units where the box is aspect × 1 and areas add up to aspect.
  const scale = aspect / total;
  const tiles: Tile<T>[] = [];
  let box = { x: 0, y: 0, w: aspect, h: 1 };
  let row: T[] = [];

  const worst = (r: T[], side: number) => {
    const areas = r.map((i) => size(i) * scale);
    const sum = areas.reduce((s, a) => s + a, 0);
    const max = Math.max(...areas);
    const min = Math.min(...areas);
    return Math.max((side * side * max) / (sum * sum), (sum * sum) / (side * side * min));
  };

  const place = (r: T[]) => {
    const sum = r.reduce((s, i) => s + size(i) * scale, 0);
    if (box.w >= box.h) {
      // A column along the left edge.
      const w = sum / box.h;
      let y = box.y;
      for (const i of r) {
        const h = (size(i) * scale) / w;
        tiles.push({ item: i, x: box.x, y, w, h });
        y += h;
      }
      box = { x: box.x + w, y: box.y, w: box.w - w, h: box.h };
    } else {
      // A row along the top edge.
      const h = sum / box.w;
      let x = box.x;
      for (const i of r) {
        const w = (size(i) * scale) / h;
        tiles.push({ item: i, x, y: box.y, w, h });
        x += w;
      }
      box = { x: box.x, y: box.y + h, w: box.w, h: box.h - h };
    }
  };

  for (const item of list) {
    const side = Math.min(box.w, box.h);
    if (row.length === 0 || worst([...row, item], side) <= worst(row, side)) {
      row.push(item);
    } else {
      place(row);
      row = [item];
    }
  }
  if (row.length) place(row);

  return tiles.map((t) => ({
    ...t,
    x: (t.x / aspect) * 100,
    w: (t.w / aspect) * 100,
    y: t.y * 100,
    h: t.h * 100,
  }));
}
