use serde::Serialize;
use sysinfo::System;

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SystemState {
    pub host_name: String,
    pub os_version: String,
    pub cpu_name: String,
    pub cpu_count: usize,
    pub memory_used_percent: u64,
    pub memory_total_gb: f64,
    pub memory_used_gb: f64,
    pub uptime_seconds: u64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TweakResult {
    pub id: String,
    pub label: String,
    pub success: bool,
    pub message: String,
}

#[tauri::command]
fn get_system_state() -> SystemState {
    let mut system = System::new_all();
    system.refresh_all();

    let total = system.total_memory();
    let used = system.used_memory();
    let memory_used_percent = if total == 0 { 0 } else { used.saturating_mul(100) / total };
    let cpu_name = system
        .cpus()
        .first()
        .map(|cpu| cpu.brand().trim().to_string())
        .filter(|name| !name.is_empty())
        .unwrap_or_else(|| "Nie wykryto procesora".to_string());

    SystemState {
        host_name: System::host_name().unwrap_or_else(|| "Ten komputer".to_string()),
        os_version: System::long_os_version().unwrap_or_else(|| "Windows".to_string()),
        cpu_name,
        cpu_count: system.cpus().len(),
        memory_used_percent,
        memory_total_gb: bytes_to_gb(total),
        memory_used_gb: bytes_to_gb(used),
        uptime_seconds: System::uptime(),
    }
}

#[tauri::command]
fn apply_tweaks(selected: Vec<String>) -> Result<Vec<TweakResult>, String> {
    if selected.is_empty() {
        return Err("Wybierz co najmniej jedną opcję do zastosowania.".to_string());
    }

    let mut results = Vec::with_capacity(selected.len());
    for id in selected {
        let result = apply_tweak(&id);
        results.push(result);
    }

    Ok(results)
}

fn apply_tweak(id: &str) -> TweakResult {
    let (label, command) = match id {
        "dns" => ("Wyczyszczenie DNS", windows_command("ipconfig", &["/flushdns"])),
        "network" => (
            "Optymalizacja stosu sieciowego",
            windows_command("netsh", &["int", "tcp", "set", "global", "autotuninglevel=normal"]),
        ),
        "rss" => (
            "Włączenie RSS",
            windows_command("netsh", &["int", "tcp", "set", "global", "rss=enabled"]),
        ),
        "power" => (
            "Plan wysokiej wydajności",
            windows_command("powercfg", &["/setactive", "SCHEME_MIN"]),
        ),
        "updates" => (
            "Wstrzymanie usługi Windows Update",
            windows_command("sc.exe", &["config", "wuauserv", "start=", "disabled"]),
        ),
        "game_mode" => (
            "Tryb gry Windows",
            windows_command("reg.exe", &["add", "HKCU\\Software\\Microsoft\\GameBar", "/v", "AllowAutoGameMode", "/t", "REG_DWORD", "/d", "1", "/f"]),
        ),
        "sysmain" => (
            "Usługa SysMain",
            windows_command("sc.exe", &["config", "SysMain", "start=", "disabled"]),
        ),
        "telemetry" => (
            "Usługa telemetryczna",
            windows_command("sc.exe", &["config", "DiagTrack", "start=", "disabled"]),
        ),
        "kernel" => (
            "Ustawienia rozruchu kernela",
            windows_command("bcdedit", &["/set", "disabledynamictick", "yes"]),
        ),
        "shader_cache" => (
            "Cache shaderów DirectX",
            powershell_command("Remove-Item -Path (Join-Path $env:LOCALAPPDATA 'D3DSCache\\*') -Recurse -Force -ErrorAction SilentlyContinue"),
        ),
        "restore" => (
            "Punkt przywracania systemu",
            powershell_command("Checkpoint-Computer -Description 'Ksyxis Tweaks Backup' -RestorePointType 'MODIFY_SETTINGS'"),
        ),
        "cleanup" => ("Czyszczenie plików tymczasowych", clean_temp_files()),
        _ => ("Nieznana operacja", Err("Nieobsługiwana opcja.".to_string())),
    };

    match command {
        Ok(output) if output.status.success() => TweakResult {
            id: id.to_string(),
            label: label.to_string(),
            success: true,
            message: clean_output(&output.stdout).unwrap_or_else(|| "Zakończono pomyślnie.".to_string()),
        },
        Ok(output) => TweakResult {
            id: id.to_string(),
            label: label.to_string(),
            success: false,
            message: clean_output(&output.stderr).unwrap_or_else(|| "System odrzucił operację.".to_string()),
        },
        Err(message) => TweakResult {
            id: id.to_string(),
            label: label.to_string(),
            success: false,
            message,
        },
    }
}

fn bytes_to_gb(bytes: u64) -> f64 {
    bytes as f64 / 1_073_741_824.0
}

fn clean_output(bytes: &[u8]) -> Option<String> {
    let text = String::from_utf8_lossy(bytes).trim().to_string();
    (!text.is_empty()).then_some(text)
}

fn clean_temp_files() -> Result<std::process::Output, String> {
    #[cfg(target_os = "windows")]
    {
        return powershell_command("Get-ChildItem -Path $env:TEMP -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue");
    }

    #[cfg(not(target_os = "windows"))]
    Err("Ta operacja jest dostępna tylko na Windows.".to_string())
}

fn windows_command(program: &str, args: &[&str]) -> Result<std::process::Output, String> {
    #[cfg(target_os = "windows")]
    {
        use std::process::Command;
        Command::new(program).args(args).output().map_err(|error| error.to_string())
    }

    #[cfg(not(target_os = "windows"))]
    {
        let _ = (program, args);
        Err("Operacje systemowe są dostępne tylko na Windows.".to_string())
    }
}

fn powershell_command(script: &str) -> Result<std::process::Output, String> {
    #[cfg(target_os = "windows")]
    {
        use std::process::Command;
        Command::new("powershell.exe")
            .args(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script])
            .output()
            .map_err(|error| error.to_string())
    }

    #[cfg(not(target_os = "windows"))]
    {
        let _ = script;
        Err("Operacje systemowe są dostępne tylko na Windows.".to_string())
    }
}

pub fn run() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![get_system_state, apply_tweaks])
        .run(tauri::generate_context!())
        .expect("Błąd uruchamiania Ksyxis Tweaks");
}
