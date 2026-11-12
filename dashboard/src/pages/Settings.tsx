import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { loadSettings, saveSettings } from "../api";

export default function Settings() {
  const queryClient = useQueryClient();
  const [settings, setSettings] = useState(loadSettings);
  const [saved, setSaved] = useState(false);

  const save = () => {
    saveSettings(settings);
    void queryClient.invalidateQueries();
    setSaved(true);
  };

  return (
    <>
      <h1>Settings</h1>
      <section className="panel narrow">
        <label className="field">
          API base URL
          <input
            value={settings.baseUrl}
            placeholder="empty = same origin (dev proxy)"
            onChange={(e) => {
              setSettings({ ...settings, baseUrl: e.target.value });
              setSaved(false);
            }}
          />
        </label>
        <label className="field">
          API key
          <input
            type="password"
            value={settings.apiKey}
            autoComplete="off"
            onChange={(e) => {
              setSettings({ ...settings, apiKey: e.target.value });
              setSaved(false);
            }}
          />
        </label>
        <p className="muted">
          Stored in this browser only. An <code>admin</code> key can start, cancel and retry runs; a{" "}
          <code>reader</code> key can only view.
        </p>
        <button className="primary" onClick={save}>
          Save
        </button>
        {saved && <span className="ok-text"> Saved.</span>}
      </section>
    </>
  );
}
