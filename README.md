# Ksyxis Tweaks

Nowa wersja aplikacji desktopowej oparta na Tauri 2, Rust i TypeScript.

## Stack

- Tauri 2 + Rust — natywne okno, odczyt systemu i operacje Windows;
- TypeScript + Vite — lekki frontend bez ciężkiego frameworka;
- CSS — responsywny, stonowany interfejs desktopowy;
- `sysinfo` — aktualny stan CPU, pamięci, systemu i czasu pracy.

## Development

```bash
npm install
npm run tauri dev
```

Walidacja kodu:

```bash
npm run build
cargo check --manifest-path src-tauri/Cargo.toml
```

## Windows release

GitHub Actions buduje instalatory MSI/NSIS po wypchnięciu taga w formacie `v1.0.0`.
Operacje systemowe wymagają uruchomienia aplikacji z uprawnieniami administratora.

Ważne: przed użyciem operacji zmieniających usługi, BCD lub ustawienia sieciowe należy utworzyć punkt przywracania systemu i potwierdzić, że dana opcja jest potrzebna na danym komputerze.
