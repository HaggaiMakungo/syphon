import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { ResponsiveContainer, AreaChart, Area, YAxis, XAxis, CartesianGrid, Tooltip } from 'recharts';

function validSamples(value) {
  return Array.isArray(value) && value.length <= 60 && value.every(v => typeof v === 'number' && Number.isFinite(v) && v >= 0 && v <= Number.MAX_SAFE_INTEGER);
}
function App() {
  const [samples, setSamples] = useState([]);
  useEffect(() => {
    const receive = event => { const value = event.data; if (value?.type === 'speed' && validSamples(value.values)) setSamples(value.values); };
    window.chrome?.webview?.addEventListener('message', receive);
    window.chrome?.webview?.postMessage({ type: 'ready' });
    return () => window.chrome?.webview?.removeEventListener('message', receive);
  }, []);
  useEffect(() => { window.chrome?.webview?.postMessage({ type: 'rendered', count: samples.length }); }, [samples]);
  const data = samples.map((bytes, index) => ({ index, speed: bytes / 1048576 }));
  return <div className="chart" aria-label="Live transfer speed, MB per second">
    <ResponsiveContainer width="100%" height="100%" minWidth={0}>
      <AreaChart data={data} margin={{ top: 14, right: 8, bottom: 4, left: 0 }} accessibilityLayer>
        <defs>
          <linearGradient id="syphonFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stopColor="var(--ov-accent-pressed)" stopOpacity={0.46}/><stop offset="100%" stopColor="var(--ov-accent-pressed)" stopOpacity={0.015}/></linearGradient>
          <linearGradient id="syphonLine" x1="0" y1="0" x2="1" y2="0"><stop offset="0%" stopColor="var(--ov-accent-pressed)"/><stop offset="65%" stopColor="var(--ov-chart-line)"/><stop offset="100%" stopColor="var(--ov-chart-line)"/></linearGradient>
        </defs>
        <CartesianGrid vertical={false} stroke="var(--ov-border)" strokeOpacity={0.45} strokeDasharray="3 5"/>
        <XAxis hide dataKey="index"/>
        <YAxis width={34} tick={{ fill: 'var(--ov-text-secondary)', fontSize: 9 }} tickLine={false} axisLine={false} tickCount={3} domain={[0, 'auto']} tickFormatter={v => Number(v).toFixed(v < 10 ? 1 : 0)}/>
        <Tooltip cursor={{ stroke: 'var(--ov-chart-line)', strokeDasharray: '3 3' }} content={({active,payload}) => active && payload?.length ? <div className="tip">{Number(payload[0].value).toFixed(2)} MB/s</div> : null}/>
        <Area dataKey="speed" type="monotone" stroke="url(#syphonLine)" strokeWidth={2} fill="url(#syphonFill)" isAnimationActive={false} dot={false} activeDot={{ r: 3, fill: 'var(--ov-chart-line)', stroke: 'var(--ov-accent-pressed)', strokeWidth: 2 }}/>
      </AreaChart>
    </ResponsiveContainer>
    <div className="unit">MB/s</div>
    {!samples.length && <div className="empty">Speed appears when a transfer starts</div>}
  </div>;
}
createRoot(document.getElementById('root')).render(<App/>);
