// Smoothing for tracked points and sizes, to stop the suit jittering while still keeping up when
// you move.

// One Euro filter (Casiez et al. 2012): a low-pass whose cutoff rises with speed, so a point that
// is barely moving (just landmark noise) is held steady while a fast one follows with little lag.
// minCutoff is in Hz; beta is how much the cutoff rises per unit/s of speed.
export class OneEuro {
  constructor(minCutoff, beta, dCutoff = 1) {
    this.minCutoff = minCutoff; this.beta = beta; this.dCutoff = dCutoff;
    this.x = null; this.dx = 0;
  }

  filter(x, dt) {
    if (this.x === null) { this.x = x; return x; }
    const alpha = fc => 1 / (1 + 1 / (2 * Math.PI * fc * dt));
    this.dx += ((x - this.x) / dt - this.dx) * alpha(this.dCutoff);
    this.x += (x - this.x) * alpha(this.minCutoff + this.beta * Math.abs(this.dx));
    return this.x;
  }
}

// One Euro filters for a list of points' x, y and z, with depth tuned separately (it's the
// noisiest). xy and z are [minCutoff, beta]. Filters the points in place.
export class PointsFilter {
  constructor(n, xy, z) {
    this.f = Array.from({ length: n }, () => [new OneEuro(...xy), new OneEuro(...xy), new OneEuro(...z)]);
  }

  apply(pts, dt) {
    pts.forEach((p, j) => {
      const [fx, fy, fz] = this.f[j];
      p.x = fx.filter(p.x, dt); p.y = fy.filter(p.y, dt); p.z = fz.filter(p.z, dt);
    });
    return pts;
  }
}

// Ease toward a size measured fresh (and noisily) every frame: small differences are noise, so it
// barely moves for them; once the size has clearly changed (stepping closer or further away) it
// follows within a fraction of a second.
export function steady(cur, target, dt, band = 0.04) {
  if (!cur) return target;
  const tau = Math.abs(target / cur - 1) < band ? 2 : 0.25;
  return cur + (target - cur) * (1 - Math.exp(-dt / tau));
}
