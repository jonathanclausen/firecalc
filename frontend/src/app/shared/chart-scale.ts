/** Axis helpers shared by the SVG charts. */

/** Rounds a raw step up to 1, 2, 2.5 or 5 times a power of ten (whole numbers if `integer`). */
export function niceStep(raw: number, integer = false) {
  if (raw <= 0) return 1;
  const magnitude = Math.pow(10, Math.floor(Math.log10(raw)));
  const residual = raw / magnitude;
  const allowQuarter = !integer || magnitude >= 10;
  const nice =
    residual <= 1
      ? 1
      : residual <= 2
        ? 2
        : residual <= 2.5 && allowQuarter
          ? 2.5
          : residual <= 5
            ? 5
            : 10;
  return nice * magnitude;
}
