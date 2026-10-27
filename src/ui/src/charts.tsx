import { useState, type MouseEvent, type ReactNode } from "react";

// ---------- chart pieces (plain HTML/SVG, no chart library) ----------

/** Categorical slots in fixed order (never cycled); see --viz-* in styles.css. */
export const SERIES = [1, 2, 3, 4, 5, 6, 7, 8].map((n) => `var(--viz-${n})`);

type Tip = { x: number; y: number; text: string } | null;

export function useTip() {
  const [tip, setTip] = useState<Tip>(null);
  const show = (text: string) => (e: MouseEvent) => setTip({ x: e.clientX, y: e.clientY, text });
  const hide = () => setTip(null);
  const node = tip && <div className="viz-tip" style={{ left: tip.x + 12, top: tip.y + 12 }} role="tooltip">{tip.text}</div>;
  return { show, hide, node };
}

export const pct = (n: number, d: number) => (d === 0 ? 0 : Math.round((n / d) * 100));
export const plural = (n: number, unit: string) => `${n} ${n === 1 ? unit : unit.endsWith("ch") ? `${unit}es` : `${unit}s`}`;

export function ChartCard({ title, note, wide, children }: { title: string; note?: string; wide?: boolean; children: ReactNode }) {
  return (
    <div className={`card viz-card${wide ? " viz-wide" : ""}`}>
      <h2>{title}</h2>
      {children}
      {note && <p className="muted small viz-note">{note}</p>}
    </div>
  );
}

export type BarRow = { key: string; label: string; value: number; critical?: boolean; onClick?: () => void };

export function BarChart({ rows, unit }: { rows: BarRow[]; unit: string }) {
  const { show, hide, node } = useTip();
  const max = Math.max(1, ...rows.map((r) => r.value));
  const total = rows.reduce((n, r) => n + r.value, 0);
  if (rows.length === 0) return <div className="empty">Nothing to show yet.</div>;
  return (
    <div className="viz-bars" onMouseLeave={hide}>
      {rows.map((r) => {
        const text = `${r.label.replace("⚠ ", "")}: ${plural(r.value, unit)} (${pct(r.value, total)}% of ${total})`;
        return (
          <div key={r.key} className={`viz-row${r.onClick ? " clickable" : ""}`} onMouseMove={show(text)}
            onClick={r.onClick} role={r.onClick ? "button" : undefined} tabIndex={r.onClick ? 0 : undefined}
            onKeyDown={r.onClick ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); r.onClick?.(); } } : undefined}
            aria-label={text}>
            <span className="viz-label" title={r.label}>{r.label}</span>
            <span className="viz-track">
              {r.value > 0 && <span className={`viz-bar${r.critical ? " critical" : ""}`} style={{ width: `${(r.value / max) * 100}%` }} />}
              <span className="viz-value">{r.value}</span>
            </span>
          </div>
        );
      })}
      {node}
    </div>
  );
}

export type Segment = { key: string; label: string; value: number; color: string };

function Legend({ segments, total }: { segments: Segment[]; total: number }) {
  return (
    <ul className="viz-legend">
      {segments.map((s) => (
        <li key={s.key}>
          <span className="viz-swatch" style={{ background: s.color }} />
          {s.label}
          <span className="viz-legend-value">{s.value} · {pct(s.value, total)}%</span>
        </li>
      ))}
    </ul>
  );
}

export function StackedBar({ segments, unit }: { segments: Segment[]; unit: string }) {
  const { show, hide, node } = useTip();
  const total = segments.reduce((n, s) => n + s.value, 0);
  if (total === 0) return <div className="empty">No matches run yet.</div>;
  return (
    <div className="stack" style={{ gap: 12 }} onMouseLeave={hide}>
      <div className="viz-stack" role="img" aria-label={segments.map((s) => `${s.label} ${s.value}`).join(", ")}>
        {segments.filter((s) => s.value > 0).map((s) => (
          <span key={s.key} style={{ flexGrow: s.value, background: s.color }}
            onMouseMove={show(`${s.label}: ${plural(s.value, unit)} (${pct(s.value, total)}%)`)} />
        ))}
      </div>
      <Legend segments={segments} total={total} />
      {node}
    </div>
  );
}

/** Part-of-whole for a handful of categories: a ring with the total in the middle and a legend beside it. */
export function DonutChart({ segments, unit, empty = "Nothing recorded yet." }: { segments: Segment[]; unit: string; empty?: string }) {
  const { show, hide, node } = useTip();
  const [active, setActive] = useState<string | null>(null);
  const total = segments.reduce((n, s) => n + s.value, 0);
  if (total === 0) return <div className="empty">{empty}</div>;

  const r = 54, c = 2 * Math.PI * r;
  const shown = segments.filter((s) => s.value > 0);
  const gap = shown.length > 1 ? 2 : 0; // 2px surface gap between slices
  let start = 0;
  return (
    <div className="viz-donut" onMouseLeave={() => { hide(); setActive(null); }}>
      <svg viewBox="0 0 140 140" role="img" aria-label={segments.map((s) => `${s.label} ${s.value}`).join(", ")}>
        <circle cx="70" cy="70" r={r} fill="none" stroke="var(--viz-track)" strokeWidth="18" />
        {shown.map((s) => {
          const len = (s.value / total) * c;
          const el = (
            <circle key={s.key} cx="70" cy="70" r={r} fill="none" stroke={s.color} strokeWidth={active === s.key ? 22 : 18}
              strokeDasharray={`${Math.max(len - gap, 0.5)} ${c}`} strokeDashoffset={-start}
              transform="rotate(-90 70 70)" opacity={active && active !== s.key ? 0.45 : 1}
              onMouseEnter={() => setActive(s.key)}
              onMouseMove={show(`${s.label}: ${plural(s.value, unit)} (${pct(s.value, total)}%)`)} />
          );
          start += len;
          return el;
        })}
        <text x="70" y="68" textAnchor="middle" className="viz-donut-total">{total}</text>
        <text x="70" y="86" textAnchor="middle" className="viz-donut-unit">{total === 1 ? unit : plural(total, unit).split(" ")[1]}</text>
      </svg>
      <Legend segments={segments} total={total} />
      {node}
    </div>
  );
}

export type LineSeries = { key: string; label: string; color: string; values: number[] };

/** Counts over time on one shared y-axis, with a crosshair tooltip per x position. */
export function LineChart({ labels, series }: { labels: string[]; series: LineSeries[] }) {
  const [hover, setHover] = useState<number | null>(null);
  const W = 960, H = 240, L = 36, R = 28, T = 12, B = 28;
  const max = Math.max(1, ...series.flatMap((s) => s.values));
  const step = max <= 5 ? 1 : Math.ceil(max / 4);
  const top = Math.ceil(max / step) * step;
  const ticks = Array.from({ length: top / step + 1 }, (_, i) => i * step);
  const x = (i: number) => L + (labels.length === 1 ? 0 : (i / (labels.length - 1)) * (W - L - R));
  const y = (v: number) => T + (1 - v / top) * (H - T - B);
  const colW = (W - L - R) / Math.max(1, labels.length - 1);

  return (
    <div className="viz-line" onMouseLeave={() => setHover(null)}>
      <ul className="viz-legend viz-legend-row">
        {series.map((s) => (
          <li key={s.key}><span className="viz-swatch" style={{ background: s.color }} />{s.label}
            <span className="viz-legend-value">{s.values.reduce((a, b) => a + b, 0)}</span></li>
        ))}
      </ul>
      <svg viewBox={`0 0 ${W} ${H}`} role="img"
        aria-label={series.map((s) => `${s.label}: ${s.values.reduce((a, b) => a + b, 0)} in ${labels.length} days`).join(", ")}>
        {ticks.map((t) => (
          <g key={t}>
            <line x1={L} x2={W - R} y1={y(t)} y2={y(t)} className="viz-grid-line" />
            <text x={L - 8} y={y(t) + 4} textAnchor="end" className="viz-axis">{t}</text>
          </g>
        ))}
        {labels.map((d, i) => (i % 2 === (labels.length - 1) % 2) && (
          <text key={d} x={x(i)} y={H - 8} textAnchor="middle" className="viz-axis">{d}</text>
        ))}
        {hover !== null && <line x1={x(hover)} x2={x(hover)} y1={T} y2={H - B} className="viz-crosshair" />}
        {series.map((s) => (
          <polyline key={s.key} fill="none" stroke={s.color} strokeWidth="2" strokeLinejoin="round" strokeLinecap="round"
            points={s.values.map((v, i) => `${x(i)},${y(v)}`).join(" ")} />
        ))}
        {hover !== null && series.map((s) => (
          <circle key={s.key} cx={x(hover)} cy={y(s.values[hover])} r="4.5" fill={s.color} stroke="var(--surface)" strokeWidth="2" />
        ))}
        {labels.map((d, i) => (
          <rect key={d} x={x(i) - colW / 2} y={T} width={colW} height={H - T - B} fill="transparent"
            onMouseEnter={() => setHover(i)} />
        ))}
      </svg>
      {hover !== null && (
        // Sits beside the crosshair (flipping left past the middle) so it never covers the points it describes.
        <div className="viz-line-tip" role="tooltip" style={{
          left: `${(x(hover) / W) * 100}%`, transform: x(hover) > W / 2 ? "translateX(calc(-100% - 12px))" : "translateX(12px)",
        }}>
          <strong>{labels[hover]}</strong>
          {series.map((s) => (
            <span key={s.key}><span className="viz-swatch" style={{ background: s.color }} />{s.label}<b>{s.values[hover]}</b></span>
          ))}
        </div>
      )}
    </div>
  );
}
