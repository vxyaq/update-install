import { invoke } from "@tauri-apps/api/core";
import "./styles.css";

type SystemState = {
  hostName: string;
  osVersion: string;
  cpuName: string;
  cpuCount: number;
  memoryUsedPercent: number;
  memoryTotalGb: number;
  memoryUsedGb: number;
  uptimeSeconds: number;
};

type TweakResult = { id: string; label: string; success: boolean; message: string };

const tweakGroups = [
  {
    title: "Sieć",
    description: "Podstawowe ustawienia stosu sieciowego Windows.",
    items: [
      { id: "dns", label: "Wyczyść pamięć DNS", note: "Odświeża lokalny resolver DNS" },
      { id: "network", label: "Ustawienia TCP", note: "Przywraca zalecane autotuning TCP" },
      { id: "rss", label: "Receive Side Scaling", note: "Włącza rozkład obsługi sieci na rdzenie" }
    ]
  },
  {
    title: "Wydajność",
    description: "Profile energii i bezpieczne przygotowanie systemu.",
    items: [
      { id: "power", label: "Wysoka wydajność", note: "Aktywuje plan High performance" },
      { id: "cleanup", label: "Pliki tymczasowe", note: "Usuwa możliwe do odtworzenia pliki TEMP" },
      { id: "restore", label: "Punkt przywracania", note: "Tworzy punkt przed większymi zmianami" }
    ]
  },
  {
    title: "System",
    description: "Operacje administracyjne wymagające uprawnień Windows.",
    items: [
      { id: "game_mode", label: "Tryb gry Windows", note: "Włącza automatyczny Game Mode" },
      { id: "sysmain", label: "Usługa SysMain", note: "Wyłącza usługę na czas grania" },
      { id: "telemetry", label: "Usługa telemetryczna", note: "Wyłącza usługę DiagTrack" },
      { id: "updates", label: "Wstrzymaj Windows Update", note: "Zmienia tryb usługi na wyłączony" },
      { id: "kernel", label: "Ustawienia kernela", note: "Wyłącza dynamiczne taktowanie" },
      { id: "shader_cache", label: "Cache shaderów", note: "Czyści cache DirectX użytkownika" }
    ]
  }
];

const app = document.querySelector<HTMLDivElement>("#app")!;
const selected = new Set<string>();
let state: SystemState | null = null;
let currentSection = "overview";

app.innerHTML = `
  <div class="shell">
    <aside class="sidebar">
      <div class="brand"><span class="brand-mark">K</span><div><strong>Ksyxis</strong><small>Tweaks</small></div></div>
      <div class="nav-label">WORKSPACE</div>
      <nav>
        <button class="nav-item active" data-section="overview"><span>⌂</span> Przegląd</button>
        <button class="nav-item" data-section="tweaks"><span>◈</span> Optymalizacja</button>
        <button class="nav-item" data-section="settings"><span>⚙</span> Ustawienia</button>
      </nav>
      <div class="sidebar-footer"><span class="status-dot"></span><span>Usługa lokalna gotowa</span><small>v1.0.0</small></div>
    </aside>
    <main class="content">
      <header class="topbar"><div><p class="eyebrow">KSYXIS TWEAKS</p><h1 id="page-title">Przegląd systemu</h1></div><div class="top-actions"><button class="icon-button" id="refresh-button" title="Odśwież stan">↻</button><div class="machine-pill"><span class="status-dot"></span><span id="machine-name">Ładowanie...</span></div></div></header>
      <section id="overview" class="page-section active-section">
        <div class="hero"><div><span class="section-kicker">SYSTEM STATUS</span><h2>Przygotuj komputer do pracy</h2><p>Wybierz konkretne działania, sprawdź ich opis i zastosuj je w jednym kontrolowanym kroku.</p></div><div class="hero-state"><span class="status-dot large"></span><strong>Gotowy</strong><small>Monitorowanie aktywne</small></div></div>
        <div class="metrics" id="metrics"><div class="metric"><span>Pamięć RAM</span><strong>—</strong><small>odczyt systemu</small></div><div class="metric"><span>Procesor</span><strong>—</strong><small>odczyt systemu</small></div><div class="metric"><span>Czas pracy</span><strong>—</strong><small>od ostatniego startu</small></div></div>
        <div class="section-heading"><div><h3>Rekomendowane działania</h3><p>Każda opcja działa niezależnie. Zmiany systemowe wymagają uruchomienia jako administrator.</p></div><button class="primary-button" id="apply-button">Zastosuj zaznaczone <span>→</span></button></div>
        <div class="tweak-grid" id="tweak-grid"></div>
      </section>
      <section id="tweaks" class="page-section"><div class="empty-state"><span class="empty-icon">◈</span><h2>Centrum optymalizacji</h2><p>Wszystkie dostępne opcje są widoczne na ekranie przeglądu, wraz z opisem efektu i wynikiem działania.</p><button class="secondary-button" data-section="overview">Wróć do przeglądu</button></div></section>
      <section id="settings" class="page-section"><div class="settings-panel"><div><span class="section-kicker">PREFERENCES</span><h2>Ustawienia aplikacji</h2><p>Minimalne ustawienia interfejsu i zachowania aplikacji.</p></div><div class="setting-row"><div><strong>Odświeżanie stanu</strong><span>Automatycznie odczytuj podstawowe parametry systemu</span></div><label class="switch"><input id="auto-refresh" type="checkbox" checked><span></span></label></div><div class="setting-row"><div><strong>Tryb bezpieczny</strong><span>Pokazuj potwierdzenie przed operacjami administracyjnymi</span></div><label class="switch"><input id="safe-mode" type="checkbox" checked><span></span></label></div></div></section>
    </main>
  </div>
  <div class="toast" id="toast" role="status"><span class="toast-icon">✓</span><div><strong id="toast-title">Gotowe</strong><small id="toast-message"></small></div></div>
`;

function renderTweaks(): void {
  const grid = document.querySelector<HTMLDivElement>("#tweak-grid")!;
  grid.innerHTML = tweakGroups.map((group) => `
    <article class="tweak-card"><div class="card-heading"><div><h4>${group.title}</h4><p>${group.description}</p></div><span class="card-count">${group.items.length}</span></div>
      <div class="tweak-list">${group.items.map((item) => `<label class="tweak-row"><input type="checkbox" data-tweak="${item.id}"><span class="checkmark"></span><span class="tweak-copy"><strong>${item.label}</strong><small>${item.note}</small></span></label>`).join("")}</div>
    </article>`).join("");
  grid.querySelectorAll<HTMLInputElement>("input[data-tweak]").forEach((input) => input.addEventListener("change", () => input.checked ? selected.add(input.dataset.tweak!) : selected.delete(input.dataset.tweak!)));
}

async function loadState(): Promise<void> {
  try {
    state = await invoke<SystemState>("get_system_state");
    document.querySelector("#machine-name")!.textContent = state.hostName;
    document.querySelector<HTMLDivElement>("#metrics")!.innerHTML = `
      <div class="metric"><span>Pamięć RAM</span><strong>${state.memoryUsedPercent}%</strong><small>${state.memoryUsedGb.toFixed(1)} / ${state.memoryTotalGb.toFixed(1)} GB</small></div>
      <div class="metric"><span>Procesor</span><strong>${state.cpuCount} rdzeni</strong><small>${state.cpuName}</small></div>
      <div class="metric"><span>Czas pracy</span><strong>${formatUptime(state.uptimeSeconds)}</strong><small>${state.osVersion}</small></div>`;
  } catch (error) { showToast("Nie udało się odczytać stanu", String(error), true); }
}

function formatUptime(seconds: number): string { const days = Math.floor(seconds / 86400); const hours = Math.floor((seconds % 86400) / 3600); return days ? `${days} d ${hours} h` : `${hours} h`; }

async function applySelected(): Promise<void> {
  if (!selected.size) { showToast("Nic nie zaznaczono", "Wybierz co najmniej jedną opcję.", true); return; }
  const button = document.querySelector<HTMLButtonElement>("#apply-button")!;
  button.disabled = true; button.innerHTML = `<span class="spinner"></span> Wykonywanie...`;
  try {
    const results = await invoke<TweakResult[]>("apply_tweaks", { selected: [...selected] });
    const failed = results.filter((result) => !result.success);
    showToast(failed.length ? "Zakończono z ostrzeżeniami" : "Zmiany zastosowane", failed.length ? failed.map((result) => `${result.label}: ${result.message}`).join(" · ") : `${results.length} operacji wykonano pomyślnie.` , failed.length > 0);
    selected.clear(); document.querySelectorAll<HTMLInputElement>("input[data-tweak]").forEach((input) => input.checked = false);
    await loadState();
  } catch (error) { showToast("Nie udało się wykonać zmian", String(error), true); }
  finally { button.disabled = false; button.innerHTML = `Zastosuj zaznaczone <span>→</span>`; }
}

function showToast(title: string, message: string, error = false): void { const toast = document.querySelector<HTMLDivElement>("#toast")!; toast.classList.toggle("error", error); document.querySelector("#toast-title")!.textContent = title; document.querySelector("#toast-message")!.textContent = message; toast.classList.add("visible"); window.setTimeout(() => toast.classList.remove("visible"), 5200); }

function activateSection(section: string): void { currentSection = section; document.querySelectorAll<HTMLElement>(".page-section").forEach((element) => element.classList.toggle("active-section", element.id === section)); document.querySelectorAll<HTMLElement>(".nav-item").forEach((element) => element.classList.toggle("active", element.dataset.section === section)); document.querySelector("#page-title")!.textContent = section === "overview" ? "Przegląd systemu" : section === "tweaks" ? "Optymalizacja" : "Ustawienia"; }

renderTweaks(); loadState();
document.querySelector("#apply-button")!.addEventListener("click", applySelected);
document.querySelector("#refresh-button")!.addEventListener("click", loadState);
document.querySelectorAll<HTMLElement>("[data-section]").forEach((element) => element.addEventListener("click", () => activateSection(element.dataset.section!)));
