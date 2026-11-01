import React, { useEffect, useState } from "react";
import { useRecoilValue } from "recoil";
import toast from "react-hot-toast";
import { canvasState, currentWorkspaceState, diagramState, diagramWorkerState } from "../state/atoms.js";

type Destination = { id: string; substance: string };

export default function SaveToWork() {
  const workspace = useRecoilValue(currentWorkspaceState);
  const diagram = useRecoilValue(diagramState);
  const worker = useRecoilValue(diagramWorkerState);
  const canvas = useRecoilValue(canvasState);
  const [destinations, setDestinations] = useState<Destination[]>([]);
  const [project, setProject] = useState("");
  const [selected, setSelected] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  useEffect(() => {
    fetch("/ank/diagrams").then(async response => {
      const data = await response.json();
      if (!response.ok) throw Error(data.error || "Не удалось подключиться к ANK DOCX");
      setDestinations(data.diagrams); setProject(data.project);
    }).catch(error => setMessage(String(error)));
  }, []);
  useEffect(() => {
    const name = workspace.files.substance.name.replaceAll("\\", "/");
    const found = destinations.filter(item => item.substance === name);
    if (found.length === 1) setSelected(found[0].id);
  }, [destinations, workspace.files.substance.name]);

  const save = async () => {
    setBusy(true); setMessage("");
    try {
      const svg = canvas.ref?.current?.querySelector("svg");
      if (!svg || !diagram.diagramId || diagram.error) throw Error("Сначала постройте рисунок кнопкой compile");
      const source = diagram.metadata.source;
      if (source.domain !== workspace.files.domain.contents || source.substance !== workspace.files.substance.contents || source.style !== workspace.files.style.contents)
        throw Error("Исходники изменены. Сначала нажмите compile");
      const response = await fetch("/ank/save", {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ id: selected, ...source, variation: diagram.metadata.variation, svg: svg.outerHTML }),
      });
      const result = await response.json();
      if (!response.ok) throw Error(result.error || "Не удалось сохранить рисунок");
      setMessage(result.message); toast.success(result.message);
    } catch (error) {
      const text = error instanceof Error ? error.message : String(error);
      setMessage(text); toast.error(text);
    } finally { setBusy(false); }
  };

  return <section style={{ padding: "16px", border: "1px solid #b8c9bd", marginBottom: "16px" }}>
    <h3>Сохранить в работу {project}</h3>
    <label>Рисунок в работе<select aria-label="Рисунок в работе" value={selected} onChange={event => setSelected(event.target.value)} style={{ display: "block", width: "100%", margin: "8px 0" }}>
      <option value="">Выберите рисунок</option>
      {destinations.map(item => <option key={item.id} value={item.id}>{item.id}</option>)}
    </select></label>
    <button onClick={save} disabled={!selected || busy || worker.compiling || worker.optimizing || worker.resampling}>
      {busy ? "Сохранение…" : "Сохранить SVG и Trio в работу"}
    </button>
    <p>Предыдущая версия останется в истории. Общие библиотеки других рисунков не изменятся.</p>
    {message && <p role="status">{message}</p>}
  </section>;
}
